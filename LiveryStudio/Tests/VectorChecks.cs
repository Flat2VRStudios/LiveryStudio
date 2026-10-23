using System.Numerics;
using System.Text;
using LiveryStudio.Core;

internal static class VectorChecks
{
    public static void Run(string repo,string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        byte[] Xml(string content)=>Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 50'>"+content+"</svg>");
        var red=VectorArtwork.Validate(Xml("<rect x='25' y='10' width='50' height='30' fill='#ff0000' opacity='0.5'/>"));
        var image=VectorArtwork.Render(red,512);var middle=(image.Height/2*image.Width+image.Width/2)*4;
        check(image.Width==512&&image.Height==256,"SVG rasterization preserves canvas proportions");
        check(image.Pixels[middle]==255&&image.Pixels[middle+1]==0&&image.Pixels[middle+3] is >=126 and <=129&&image.Pixels[3]==0,"SVG pixels preserve straight-alpha color and transparent margins");
        var gradient=VectorArtwork.Render(VectorArtwork.Validate(Xml("<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='c'><circle cx='50' cy='25' r='20'/></clipPath></defs><rect width='100' height='50' fill='url(#g)' clip-path='url(#c)'/>")),512);
        var left=(128*512+180)*4;var right=(128*512+330)*4;
        check(gradient.Pixels[left]>gradient.Pixels[left+2]&&gradient.Pixels[right]<gradient.Pixels[right+2]&&gradient.Pixels[3]==0,"SVG gradients and clipping render real geometry");
        var units=VectorArtwork.Validate(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='2in' height='25.4mm'><circle cx='96' cy='48' r='30'/></svg>"));
        check(Math.Abs(units.Width-192)<.01&&Math.Abs(units.Height-96)<.01,"SVG absolute canvas units normalize to a scalable viewBox");
        var use=VectorArtwork.Render(VectorArtwork.Validate(Xml("<defs><rect id='r' width='20' height='20'/></defs><use href='#r' x='20' y='10' fill='#00ff00'/>")),512);
        check(use.Pixels[(80*512+150)*4+1]==255,"SVG2 local use references normalize for the Windows renderer");
        foreach(var (body,name) in new (string,string)[]{
            ("<script>alert(1)</script>","scripts"),("<rect onclick='x()'/>","event handlers"),("<use href='https://example.com/logo.svg#x'/>","remote artwork"),
            ("<use href='file:///C:/private.svg'/>","local linked files"),("<rect fill='url(https://example.com/a)'/>","remote paints"),
            ("<rect style='fill:url(https://example.com/a)'/>","remote inline CSS"),("<rect style='fill:var(--color)'/>","CSS variables"),
            ("<style>rect{fill:red}</style>","stylesheets"),("<text>Logo</text>","unconverted text"),("<image href='data:image/png;base64,AA=='/>","embedded bitmaps"),
            ("<filter id='f'/>","unsupported filters"),("<mask id='m'/>","unsupported masks"),("<rect fill='url(#missing)'/>","missing references"),
            ("<g id='a'><use href='#a'/></g>","circular use"),("<defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/></defs>","circular gradients"),
            ("<rect id='a'/><circle id='a'/>","duplicate IDs"),("<?xml-stylesheet href='private.css'?>", "processing instructions")})
            reject(()=>VectorArtwork.Validate(Xml(body)),"SVG validation rejects "+name);
        reject(()=>VectorArtwork.Validate(Encoding.UTF8.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///C:/private'>]><svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1 1'><title>&x;</title></svg>")),"SVG validation prohibits DTDs and external entities");
        reject(()=>VectorArtwork.Validate(Xml(string.Concat(Enumerable.Repeat("<g>",65))+"<rect/>"+string.Concat(Enumerable.Repeat("</g>",65)))),"SVG validation bounds group depth");
        reject(()=>VectorArtwork.Validate(Xml(string.Concat(Enumerable.Repeat("<rect/>",5001)))),"SVG validation bounds element count");
        var expansions="<defs><g id='a0'><rect width='1' height='1'/></g>";
        for(var i=1;i<6;i++)expansions+=$"<g id='a{i}'>"+string.Concat(Enumerable.Repeat($"<use href='#a{i-1}'/>",10))+"</g>";
        expansions+="</defs><use href='#a5'/>";reject(()=>VectorArtwork.Validate(Xml(expansions)),"SVG validation bounds repeated reference expansion before rendering");
        reject(()=>VectorArtwork.Validate(new byte[VectorArtwork.MaximumBytes+1]),"SVG validation bounds file size");
        reject(()=>VectorArtwork.Render(red,8192),"SVG texture resolution is bounded");
        reject(()=>VectorArtwork.Render(VectorArtwork.Validate(Xml("<rect width='100' height='50' fill='none'/>"))),"Invisible SVG artwork gives an actionable import error");
        var folder=Path.Combine(temp,"vectors");Directory.CreateDirectory(folder);var external=Path.Combine(folder,"External badge.svg");var bytes=File.ReadAllBytes(Path.Combine(repo,"examples/VectorGarageBadge.svg"));File.WriteAllBytes(external,bytes);
        var raster=VectorArtwork.Import(external,folder,1024);var metadata=VectorArtwork.ReadMetadata(raster)!;File.Delete(external);
        check(File.ReadAllBytes(metadata.SourceFile).SequenceEqual(bytes)&&FinishMaps.Read(raster).Width==1024,"SVG import retains original bytes after the external file is removed");
        var blank=Path.Combine(folder,"blank.tga");FinishMaps.WriteTga(new(32,32,new byte[32*32*4]),blank);var editor=Path.Combine(folder,"editor.json");
        var projection=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,.625f);
        var layer=new PaintLayer{Name="Vector badge",Kind=LayerKind.Sticker,Asset=raster,VectorArtwork=metadata,Samples=[projection]};
        var doc=new LayerDocument{BaseAlbedo=blank,BaseStructure=blank};doc.Commit("SVG badge",layers=>layers.Add(layer));doc.Save(editor);
        check(doc.Version==5&&LayerBrowser.Type(doc.Layers[0])=="SVG decal","SVG layers advertise their type and upgrade document format");
        var project=new LiveryProject{Albedo=blank,Structure=blank,EditorFile=editor};var archive=Path.Combine(folder,"Vector.flproject");ProjectBundle.Save(project,archive);var loaded=ProjectBundle.Open(archive,Path.Combine(folder,"opened"));var restored=LayerDocument.Read(loaded.EditorFile);
        File.Delete(metadata.SourceFile);File.Delete(raster);
        check(File.ReadAllBytes(restored.Layers[0].VectorArtwork!.SourceFile).SequenceEqual(bytes)&&File.Exists(restored.Layers[0].Asset),"Portable SVG projects resolve retained source and raster independently of original workspace");
        var rebuilt=VectorArtwork.Rebuild(restored.Layers[0].VectorArtwork! with {RasterSize=512},Path.Combine(folder,"rebuilt"));
        check(FinishMaps.Read(rebuilt).Width==512&&VectorArtwork.ReadMetadata(rebuilt)!.SourceFile==restored.Layers[0].VectorArtwork!.SourceFile,"Portable original SVG re-renders at another resolution");
        var group=ReusableArtwork.Save("Vector badge",restored.Layers,Path.Combine(folder,"groups"));var groupLayers=ReusableArtwork.Open(group,Path.Combine(folder,"group-open"));
        check(File.ReadAllBytes(groupLayers[0].VectorArtwork!.SourceFile).SequenceEqual(bytes),"Reusable artwork groups retain original SVG source");
        restored.Version=4;reject(()=>restored.Validate(),"Old document versions reject retained vector metadata");
    }
}
