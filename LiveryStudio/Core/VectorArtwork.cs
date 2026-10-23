using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LiveryStudio.Core;

public sealed record VectorArtworkOptions(string SourceFile, int RasterSize=2048)
{
    public Dictionary<string,string> Recolors {get;set;}=[];
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(SourceFile)||RasterSize is not (512 or 1024 or 2048 or 4096))
            throw new InvalidDataException("SVG artwork needs its original source and a resolution of 512, 1024, 2048 or 4096.");
        ArtworkColors.ValidateMap(Recolors);
    }
}
public sealed record ValidatedVector(byte[] Xml, double Width, double Height);

public static class VectorArtwork
{
    public const int MaximumBytes=2*1024*1024;
    public static string Name(VectorArtworkOptions options)=>Regex.Replace(Path.GetFileNameWithoutExtension(options.SourceFile),@"-[0-9a-fA-F]{16}$","");
    private static readonly XNamespace Svg="http://www.w3.org/2000/svg", Link="http://www.w3.org/1999/xlink";
    private static readonly HashSet<string> Presentation=["clip-path","clip-rule","color","display","fill","fill-opacity","fill-rule","opacity","overflow","stop-color","stop-opacity","stroke","stroke-dasharray","stroke-dashoffset","stroke-linecap","stroke-linejoin","stroke-miterlimit","stroke-opacity","stroke-width","visibility"];
    private static readonly Dictionary<string,string[]> Elements=new()
    {
        ["svg"]=["x","y","width","height","viewBox","preserveAspectRatio","version"],
        ["g"]=[],["defs"]=[],["title"]=[],["desc"]=[],
        ["circle"]=["cx","cy","r"],["ellipse"]=["cx","cy","rx","ry"],
        ["rect"]=["x","y","width","height","rx","ry"],["path"]=["d"],
        ["line"]=["x1","y1","x2","y2"],["polygon"]=["points"],["polyline"]=["points"],
        ["clipPath"]=["clipPathUnits"],["use"]=["x","y","width","height","href"],
        ["linearGradient"]=["x1","y1","x2","y2","gradientUnits","gradientTransform","spreadMethod","href"],
        ["radialGradient"]=["cx","cy","r","fx","fy","gradientUnits","gradientTransform","spreadMethod","href"],
        ["stop"]=["offset"]
    };
    public static ValidatedVector Validate(byte[] bytes)
    {
        if(bytes.Length<1||bytes.Length>MaximumBytes)throw new InvalidDataException("Choose an SVG smaller than 2 MB.");
        XDocument doc;
        try
        {
            using var stream=new MemoryStream(bytes);
            using var reader=XmlReader.Create(stream,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumBytes,IgnoreComments=true});
            doc=XDocument.Load(reader);
        }
        catch(XmlException x){throw new InvalidDataException("This SVG is invalid. DTDs and external entities are not supported.",x);}
        if(doc.DescendantNodes().OfType<XProcessingInstruction>().Any())throw new InvalidDataException("SVG processing instructions are not supported.");
        var root=doc.Root??throw new InvalidDataException("The SVG is empty.");
        if(root.Name!=Svg+"svg")throw new InvalidDataException("Choose a standalone SVG with the standard SVG namespace.");
        root.SetAttributeValue(XNamespace.Xmlns+"xlink",Link.NamespaceName);
        foreach(var metadata in root.Descendants(Svg+"metadata").ToArray())metadata.Remove();
        var nodes=root.DescendantsAndSelf().ToArray();
        if(nodes.Length>5000||nodes.Any(n=>n.Ancestors().Count()>64))throw new InvalidDataException("This SVG is too complex. Use at most 5,000 elements and 64 nested groups.");
        var ids=new Dictionary<string,XElement>(StringComparer.Ordinal);
        var references=new Dictionary<XElement,List<string>>();
        foreach(var node in nodes)
        {
            if(node.Name.Namespace!=Svg||!Elements.TryGetValue(node.Name.LocalName,out var attrs))
                throw new InvalidDataException(node.Name.LocalName=="text"?"Convert SVG lettering to paths before importing, or use the editor’s Text tool.":$"SVG element '{node.Name.LocalName}' is not supported. Export plain SVG paths, shapes and gradients; flatten filters, masks and embedded images.");
            if(node!=root&&node.Name.LocalName=="svg")throw new InvalidDataException("Nested SVG viewports are not supported. Export a single SVG canvas.");
            references[node]=[];
            foreach(var attr in node.Attributes().ToArray())
            {
                if(attr.IsNamespaceDeclaration)continue;
                if(attr.Name.Namespace!=XNamespace.None&&attr.Name.Namespace!=Link)
                {
                    // Authoring-tool metadata has no drawing effect.
                    if(attr.Name.Namespace==XNamespace.Xml)throw new InvalidDataException("XML base and language overrides are not supported.");
                    attr.Remove();continue;
                }
                var name=attr.Name.LocalName;
                if((attr.Name.Namespace==Link&&name!="href")||
                    !(name is "id" or "style" or "transform"||attrs.Contains(name)||Presentation.Contains(name)))
                    throw new InvalidDataException($"SVG attribute '{name}' is not supported. Export plain SVG with inline styles.");
                if(name=="id")
                {
                    if(!Regex.IsMatch(attr.Value,@"^[A-Za-z_][\w.:-]{0,127}$")||!ids.TryAdd(attr.Value,node))throw new InvalidDataException("SVG IDs must be unique, short names.");
                }
                else if(name=="href")
                {
                    var reference=attr.Value.Trim();if(!Regex.IsMatch(reference,@"^#[A-Za-z_][\w.:-]{0,127}$"))throw new InvalidDataException("SVG references must point inside the same file. Linked files and web resources are not supported.");
                    references[node].Add(reference[1..]);
                    if(attr.Name.Namespace==XNamespace.None){attr.Remove();node.SetAttributeValue(Link+"href",reference);}
                }
                else if(name=="style")
                {
                    foreach(var declaration in attr.Value.Split(';',StringSplitOptions.RemoveEmptyEntries))
                    {
                        var pair=declaration.Split(':',2);if(pair.Length!=2||!Presentation.Contains(pair[0].Trim()))throw new InvalidDataException("SVG styles must use supported inline fill, stroke and opacity settings.");
                        CheckValue(pair[1],references[node]);
                    }
                }
                else CheckValue(attr.Value,references[node]);
            }
            if(node.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value))&&node.Name.LocalName is not ("title" or "desc"))throw new InvalidDataException("Convert lettering to paths before importing SVG artwork.");
        }
        foreach(var list in references.Values)if(list.Any(id=>!ids.ContainsKey(id)))throw new InvalidDataException("The SVG contains a missing internal reference.");
        var active=new HashSet<XElement>();var costs=new Dictionary<XElement,int>();
        int Visit(XElement node)
        {
            if(costs.TryGetValue(node,out var known))return known;if(!active.Add(node))throw new InvalidDataException("The SVG contains a circular reference.");
            var cost=1;
            foreach(var child in node.Elements().Concat(references[node].Select(id=>ids[id])))
            {
                cost+=Visit(child);if(cost>50000)throw new InvalidDataException("The SVG repeats too much artwork. Simplify nested use references before importing.");
            }
            active.Remove(node);costs[node]=cost;return cost;
        }
        Visit(root);
        double width,height;
        if(root.Attribute("viewBox") is {} view)
        {
            var numbers=Regex.Split(view.Value.Trim(),@"[\s,]+").Select(v=>double.TryParse(v,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:double.NaN).ToArray();
            if(numbers.Length!=4||numbers.Any(n=>!double.IsFinite(n))||numbers[2]<=0||numbers[3]<=0)throw new InvalidDataException("SVG viewBox needs four finite numbers and a positive size.");
            width=numbers[2];height=numbers[3];
            if(root.Attribute("width") is {} canvasWidth&&root.Attribute("height") is {} canvasHeight&&!canvasWidth.Value.Contains('%')&&!canvasHeight.Value.Contains('%')){width=Length(canvasWidth.Value);height=Length(canvasHeight.Value);}
        }
        else
        {
            width=Length(root.Attribute("width")?.Value);height=Length(root.Attribute("height")?.Value);
            root.SetAttributeValue("viewBox",FormattableString.Invariant($"0 0 {width} {height}"));
        }
        if(width/height is <.01 or >100)throw new InvalidDataException("SVG canvas proportions must be between 1:100 and 100:1.");
        root.Attribute("width")?.Remove();root.Attribute("height")?.Remove();root.SetAttributeValue("width","100%");root.SetAttributeValue("height","100%");root.Attribute("version")?.Remove();
        return new(Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting)),width,height);
    }
    private static void CheckValue(string value,List<string> refs)
    {
        if(value.Contains('\\')||value.Contains("/*",StringComparison.Ordinal)||value.Contains('!')||value.Contains('@')||Regex.IsMatch(value,@"\bvar\s*\(",RegexOptions.IgnoreCase))throw new InvalidDataException("SVG styles cannot contain CSS imports, variables, escapes or special overrides.");
        var urls=Regex.Matches(value,@"url\s*\(\s*['"" ]*(#[A-Za-z_][\w.:-]{0,127})['"" ]*\s*\)",RegexOptions.IgnoreCase);
        var rest=value;foreach(Match match in urls){refs.Add(match.Groups[1].Value[1..]);rest=rest.Replace(match.Value,"");}
        if(rest.Contains("url",StringComparison.OrdinalIgnoreCase)||rest.Contains("://",StringComparison.Ordinal))throw new InvalidDataException("SVG artwork cannot load files or web resources.");
    }
    private static double Length(string? value)
    {
        var match=Regex.Match(value??"",@"^\s*([+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*(px|pt|pc|cm|mm|in)?\s*$",RegexOptions.IgnoreCase);
        if(!match.Success||!double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<=0)throw new InvalidDataException("SVG needs a viewBox or positive width and height. Relative canvas units are not supported.");
        return n*(match.Groups[2].Value.ToLowerInvariant() switch {"pt"=>96d/72,"pc"=>16,"cm"=>96d/2.54,"mm"=>96d/25.4,"in"=>96,_=>1});
    }
    [DllImport("LiveryPreview.dll",CallingConvention=CallingConvention.Cdecl)]
    private static extern int ls_svg(byte[] xml,uint length,uint width,uint height,byte[] rgba,uint capacity);
    public static PaintImage Render(ValidatedVector vector,int size=2048)
    {
        new VectorArtworkOptions("validated.svg",size).Validate();
        var scale=size/Math.Max(vector.Width,vector.Height);var width=Math.Max(1,(int)Math.Round(vector.Width*scale));var height=Math.Max(1,(int)Math.Round(vector.Height*scale));
        var rgba=new byte[checked(width*height*4)];var result=ls_svg(vector.Xml,(uint)vector.Xml.Length,(uint)width,(uint)height,rgba,(uint)rgba.Length);
        if(result<0)throw new InvalidDataException($"This SVG could not be rendered (0x{result:X8}). Export plain paths, shapes and gradients.");
        if(!rgba.Where((_,i)=>i%4==3).Any(a=>a>0))throw new InvalidDataException("This SVG has no visible artwork.");
        return new(width,height,rgba);
    }
    public static string Import(string source,string workspace,int size=2048)
    {
        if(new FileInfo(source).Length>MaximumBytes)throw new InvalidDataException("Choose an SVG smaller than 2 MB.");
        var bytes=File.ReadAllBytes(source);var vector=Validate(bytes);var image=Render(vector,size);
        var name=Path.GetFileNameWithoutExtension(source);if(name.Length>60)name=name[..60];
        var key=Convert.ToHexString(SHA256.HashData(bytes))[..16];var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);
        var original=Path.Combine(folder,name+"-"+key+".svg");var raster=Path.Combine(folder,name+"-"+key+"-"+size+".tga");
        AtomicFiles.Write(original,bytes);FinishMaps.WriteTga(image,raster);
        AtomicFiles.Write(raster+".vector.json",JsonSerializer.SerializeToUtf8Bytes(new VectorArtworkOptions(original,size),Contract.Json));return raster;
    }
    public static VectorArtworkOptions? ReadMetadata(string raster)
    {
        var path=raster+".vector.json";if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>8192)throw new InvalidDataException("SVG artwork metadata is too large.");
        var options=JsonSerializer.Deserialize<VectorArtworkOptions>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("SVG artwork metadata is empty.");options.Validate();return options;
    }
    public static string Rebuild(VectorArtworkOptions options,string workspace)
    {
        options.Validate();if(new FileInfo(options.SourceFile).Length>MaximumBytes)throw new InvalidDataException("Choose an SVG smaller than 2 MB.");
        var bytes=File.ReadAllBytes(options.SourceFile);var image=Render(ArtworkColors.Recolor(Validate(bytes),options.Recolors),options.RasterSize);var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);
        var signature=bytes.Concat(JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json)).ToArray();var path=Path.Combine(folder,"SVG-"+Convert.ToHexString(SHA256.HashData(signature))[..16]+"-"+options.RasterSize+".tga");FinishMaps.WriteTga(image,path);
        AtomicFiles.Write(path+".vector.json",JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json));return path;
    }
}
