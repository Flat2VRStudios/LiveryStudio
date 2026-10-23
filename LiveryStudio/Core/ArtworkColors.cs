using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LiveryStudio.Core;

public sealed record RasterArtworkOptions(string SourceFile)
{
    public Dictionary<string,string> Recolors {get;set;}=[];
    public ImagePreparationOptions? Preparation {get;set;}
    public void Validate(){if(string.IsNullOrWhiteSpace(SourceFile))throw new InvalidDataException("Edited images need their original source.");ArtworkColors.ValidateMap(Recolors);Preparation?.Validate();}
}
public static class ArtworkColors
{
    public static void ValidateMap(Dictionary<string,string> colors)
    {
        if(colors is null||colors.Count>256)throw new InvalidDataException("Artwork supports at most 256 color replacements.");
        foreach(var (source,target) in colors){if(source!=ColorLibrary.Normalize(source)||target!=ColorLibrary.Normalize(target))throw new InvalidDataException("Artwork replacement colors must use uppercase six-digit hex values.");}
    }
    public static string? SvgHex(string value)
    {
        value=value.Trim();if(Regex.IsMatch(value,@"^#[0-9a-fA-F]{6}$"))return value.ToUpperInvariant();
        if(Regex.IsMatch(value,@"^#[0-9a-fA-F]{3}$"))return "#"+string.Concat(value[1..].Select(c=>new string(char.ToUpperInvariant(c),2)));
        var rgb=Regex.Match(value,@"^rgb\(\s*([^,]+),\s*([^,]+),\s*([^,]+)\s*\)$",RegexOptions.IgnoreCase);
        if(rgb.Success)
        {
            var bytes=new byte[3];for(var i=0;i<3;i++){var part=rgb.Groups[i+1].Value.Trim();var percent=part.EndsWith('%');if(!double.TryParse(percent?part[..^1]:part,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number))return null;bytes[i]=(byte)Math.Clamp(Math.Round(percent?number*255/100:number,MidpointRounding.AwayFromZero),0,255);}
            return $"#{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}";
        }
        if(!Regex.IsMatch(value,@"^[A-Za-z]+$"))return null;var color=Color.FromName(value);return color.IsKnownColor&&color.A==255?$"#{color.R:X2}{color.G:X2}{color.B:X2}":null;
    }
    private static readonly HashSet<string> SvgProperties=["fill","stroke","color","stop-color"];
    private static void SvgColors(XDocument doc,Action<XAttribute,string> attribute,Func<string,string,string>? style)
    {
        foreach(var node in doc.Descendants())foreach(var attr in node.Attributes().ToArray())
        {
            if(attr.Name.Namespace!=XNamespace.None)continue;
            if(SvgProperties.Contains(attr.Name.LocalName)&&SvgHex(attr.Value) is {} hex)attribute(attr,hex);
            else if(attr.Name.LocalName=="style")
            {
                var entries=attr.Value.Split(';',StringSplitOptions.RemoveEmptyEntries);for(var i=0;i<entries.Length;i++){var pair=entries[i].Split(':',2);if(pair.Length==2&&SvgProperties.Contains(pair[0].Trim())&&SvgHex(pair[1]) is {} color){attribute(new XAttribute(pair[0].Trim(),pair[1]),color);if(style is not null)entries[i]=pair[0]+":"+style(pair[1],color);}}
                if(style is not null)attr.Value=string.Join(';',entries);
            }
        }
    }
    public static string[] SvgPalette(string source)
    {
        if(new FileInfo(source).Length>VectorArtwork.MaximumBytes)throw new InvalidDataException("SVG source is too large.");var validated=VectorArtwork.Validate(File.ReadAllBytes(source));var doc=XDocument.Parse(System.Text.Encoding.UTF8.GetString(validated.Xml));var colors=new HashSet<string>();SvgColors(doc,(_,hex)=>colors.Add(hex),null);return colors.Order().ToArray();
    }
    public static ValidatedVector Recolor(ValidatedVector source,Dictionary<string,string> map)
    {
        ValidateMap(map);if(map.Count==0)return source;var doc=XDocument.Parse(System.Text.Encoding.UTF8.GetString(source.Xml));
        SvgColors(doc,(attr,hex)=>{if(map.TryGetValue(hex,out var color))attr.Value=color;},(original,hex)=>map.GetValueOrDefault(hex,original));
        return source with {Xml=System.Text.Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting))};
    }
    public static string[] RasterPalette(string source,int maximum=16)
    {
        return RasterPalette(FinishMaps.Read(source),maximum);
    }
    public static string[] RasterPalette(RasterArtworkOptions source,int maximum=16)
    {
        source.Validate();var image=FinishMaps.Read(source.SourceFile);return RasterPalette(source.Preparation is {} preparation?ImagePreparation.Render(image,preparation):image,maximum);
    }
    private static string[] RasterPalette(PaintImage image,int maximum)
    {
        var counts=new Dictionary<int,int>();var step=Math.Max(1,(image.Width*image.Height+262143)/262144)*4;for(var i=0;i<image.Pixels.Length;i+=step){if(image.Pixels[i+3]==0)continue;var color=(image.Pixels[i]<<16)|(image.Pixels[i+1]<<8)|image.Pixels[i+2];counts[color]=counts.GetValueOrDefault(color)+1;}
        return counts.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key).Take(maximum).Select(p=>$"#{p.Key:X6}").ToArray();
    }
    public static PaintImage Render(PaintImage original,RasterArtworkOptions options,CancellationToken cancellation=default)
    {
        options.Validate();original.Validate();cancellation.ThrowIfCancellationRequested();
        var source=options.Preparation is {} preparation?ImagePreparation.Render(original,preparation,cancellation):new(original.Width,original.Height,(byte[])original.Pixels.Clone());var pixels=source.Pixels;
        var replacements=options.Recolors.ToDictionary(p=>Convert.ToInt32(p.Key[1..],16),p=>Convert.ToInt32(p.Value[1..],16));
        for(var i=0;i<pixels.Length;i+=4){if((i&65535)==0)cancellation.ThrowIfCancellationRequested();if(pixels[i+3]==0)continue;var rgb=(pixels[i]<<16)|(pixels[i+1]<<8)|pixels[i+2];if(replacements.TryGetValue(rgb,out var color)){pixels[i]=(byte)(color>>16);pixels[i+1]=(byte)(color>>8);pixels[i+2]=(byte)color;}}
        return source;
    }
    public static string Generate(RasterArtworkOptions options,string workspace,CancellationToken cancellation=default)
    {
        options.Validate();cancellation.ThrowIfCancellationRequested();var source=Render(FinishMaps.Read(options.SourceFile,cancellation),options,cancellation);
        var recipe=JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json);var key=Convert.ToHexString(SHA256.HashData(recipe.Concat(System.Text.Encoding.UTF8.GetBytes(WorkshopService.Hash(options.SourceFile))).ToArray()))[..16];
        var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);var path=Path.Combine(folder,(options.Preparation is null?"Recolored image-":"Prepared image-")+key+".tga");var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{FinishMaps.WriteTga(source,temporary);cancellation.ThrowIfCancellationRequested();AtomicFiles.Write(path+".raster.json",recipe);File.Move(temporary,path,true);return path;}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static RasterArtworkOptions? ReadMetadata(string raster)
    {
        var path=raster+".raster.json";if(!File.Exists(path))return null;if(new FileInfo(path).Length>32768)throw new InvalidDataException("Image color metadata is too large.");var result=JsonSerializer.Deserialize<RasterArtworkOptions>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("Image color metadata is empty.");result.Validate();return result;
    }
}
