using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class LayerBrowserChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var group=Guid.NewGuid();var number=new PaintLayer{Name="Race number",Kind=LayerKind.Sticker,TextArtwork=new(){Text="007"},GroupId=group,GroupName="Team badge",Mirror=true};var team=new PaintLayer{Name="Sponsor",Kind=LayerKind.Sticker,TextArtwork=new(){Text="FLAT2VR"},GroupId=group,GroupName="Team badge",Locked=true,Visible=false};var pattern=new PaintLayer{Name="Top accent",Kind=LayerKind.Sticker,PatternArtwork=new(){Kind=PatternKind.CarbonWeave}};var color=new PaintLayer{Name="Blue stroke",Kind=LayerKind.Color,Color="#164A7B"};var finish=new PaintLayer{Name="Matte patch",Kind=LayerKind.Surface,Gloss=.15f};PaintLayer[] layers=[number,team,pattern,color,finish];
        var before=JsonSerializer.Serialize(layers,LayerDocument.Json);
        check(LayerBrowser.Filter(layers,"",LayerFilter.All).Select(l=>l.Id).SequenceEqual(layers.Reverse().Select(l=>l.Id)),"Layer browser preserves topmost-first stack order");
        check(LayerBrowser.Filter(layers,"BADGE 007",LayerFilter.All).Single()==number,"Layer search matches text and group names across case-insensitive words");
        check(LayerBrowser.Filter(layers,"carbon",LayerFilter.Artwork).Single()==pattern,"Layer search recognizes editable pattern families");
        check(LayerBrowser.Filter(layers,"flat2vr",LayerFilter.All).Single()==team,"Hidden and locked lettering remains searchable");
        check(LayerBrowser.Filter(layers,"",LayerFilter.Artwork).Length==3&&LayerBrowser.Filter(layers,"",LayerFilter.Paint).Single()==color&&LayerBrowser.Filter(layers,"",LayerFilter.Materials).Single()==finish,"Layer type filters separate decals, paint and surface strokes");
        check(LayerBrowser.Filter(layers,"",LayerFilter.Groups).Length==2&&LayerBrowser.Filter(layers,"",LayerFilter.Hidden).Single()==team&&LayerBrowser.Filter(layers,"",LayerFilter.Locked).Single()==team,"Layer state filters expose grouped, hidden and locked artwork");
        check(LayerBrowser.Filter(layers,"missing",LayerFilter.All).Length==0&&LayerBrowser.Filter(layers,"  \t ",LayerFilter.All).Length==5,"Layer search handles empty whitespace and unmatched queries");
        reject(()=>LayerBrowser.Filter(layers,"",(LayerFilter)99),"Reject an unknown layer browser filter");
        check(!before.Contains("BrowserType")&&!before.Contains("BrowserState")&&JsonSerializer.Serialize(layers,LayerDocument.Json)==before,"Browsing labels and filters change no portable layer metadata");
        check(LayerBrowser.State(team).Contains("Hidden")&&LayerBrowser.State(team).Contains("Locked")&&LayerBrowser.Type(number)=="Text decal","Layer rows describe hidden state, locks and editable text");
        var doc=new LayerDocument{Version=4,History=[new("Artwork",DateTimeOffset.UtcNow,layers.ToList())]};var documentBefore=JsonSerializer.Serialize(doc,LayerDocument.Json);var preview=LayerBrowser.Inspection(doc,[team.Id]);
        check(preview.Layers.Count==1&&preview.Layers[0].Visible&&preview.Layers[0].Locked,"Selection inspection reveals a hidden locked layer only in its independent preview");
        preview.Layers[0].TextArtwork!.Text="CHANGED";preview.Layers[0].Samples.Add(default);
        check(JsonSerializer.Serialize(doc,LayerDocument.Json)==documentBefore,"Inspection clones nested lettering and projection lists without changing source history");
        reject(()=>LayerBrowser.Inspection(doc,[]),"Empty inspection cannot silently hide the complete design");
        var folder=Path.Combine(temp,"browser");Directory.CreateDirectory(folder);var stickerFile=Path.Combine(folder,"wide.tga");FinishMaps.WriteTga(new(4,1,[255,0,0,255,0,255,0,0,0,0,255,128,255,255,255,255]),stickerFile);
        var decal=number with {TextArtwork=null,Asset=stickerFile};var thumb=LayerBrowser.Thumbnail(decal);check(thumb.Width==64&&thumb.Height==48&&thumb.Pixels.Where((_,i)=>i%4==3).All(a=>a==255),"Layer thumbnail fits its bounded canvas and displays transparency over a checkerboard");
        var middle=(24*64+24)*4;check(thumb.Pixels[middle]==thumb.Pixels[middle+1]&&thumb.Pixels[middle+1]==thumb.Pixels[middle+2],"A transparent artwork texel reveals the thumbnail background without a colored fringe");
        var chip=LayerBrowser.Thumbnail(color);var center=(24*64+32)*4;check(chip.Pixels[center]==22&&chip.Pixels[center+1]==74&&chip.Pixels[center+2]==123,"Paint stroke thumbnail preserves its exact pigment in the swatch");
        reject(()=>LayerBrowser.Thumbnail(color,4096,48),"Bound layer thumbnail dimensions");
        var blank=Path.Combine(folder,"base.tga");var surface=Path.Combine(folder,"surface.tga");FinishMaps.WriteTga(new(32,32,new byte[32*32*4]),blank);FinishMaps.WriteTga(new(32,32,Enumerable.Range(0,32*32).SelectMany(_=>new byte[]{40,90,23,45}).ToArray()),surface);
        var project=new LiveryProject{Albedo=blank,Structure=surface,UseSrgbPaintColors=true,InnerColor="#164A7B",OuterColor="#164A7B",OverrideArtworkFinish=true};
        float[] quad=[-1,-1,0,0,0,1,0,1,1,1,-1,0,0,0,1,1,1,1,1,1,0,0,0,1,1,0,1,-1,-1,0,0,0,1,0,1,1,1,1,0,0,0,1,1,0,1,-1,1,0,0,0,1,0,0,1];var mesh=new PaintMesh(quad);var hit=mesh.Raycast(new(0,0,2),-Vector3.UnitZ)!.Value;var projection=SurfaceProjection.At(hit,Vector3.UnitX,1,1,0);
        var red=new PaintLayer{Name="Hidden red",Kind=LayerKind.Color,Visible=false,Locked=true,Color="#FF0000",Samples=[projection],Hardness=1};var material=new PaintLayer{Name="Finish",Kind=LayerKind.Surface,Gloss=.12f,Metalness=.9f,Samples=[projection],Hardness=1};var source=LayerDocument.Create(project,Path.Combine(folder,"document"));source.Commit("Source layers",l=>l.AddRange([red,material]));var original=source.Compose(mesh,project);var fullBefore=JsonSerializer.Serialize(source,LayerDocument.Json);var inspected=LayerBrowser.Inspection(source,[red.Id]);var maps=inspected.Compose(mesh,project);var pixel=(16*32+16)*4;
        check(maps.Albedo.Pixels[pixel]==255&&maps.Albedo.Pixels[pixel+1]==0&&!inspected.HasMaterial,"Inspection composes the selected hidden paint and excludes unselected material layers");
        check(LayerBrowser.Inspection(source,[material.Id]).HasMaterial&&LayerBrowser.Inspection(source,[red.Id,material.Id]).Layers.Count==2,"Material inspection retains its material state and compound selections");
        var after=source.Compose(mesh,project);check(JsonSerializer.Serialize(source,LayerDocument.Json)==fullBefore&&original.Albedo.Pixels.SequenceEqual(after.Albedo.Pixels)&&original.Structure.Pixels.SequenceEqual(after.Structure.Pixels),"Temporary inspection leaves full bake pixels and every source checkpoint unchanged");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();reject(()=>inspected.Compose(mesh,project,cancellation.Token),"Selection preview composition honors cancellation");
        check(LayerBrowser.Filter(Enumerable.Range(0,512).Select(i=>new PaintLayer{Name="Layer "+i}),"511",LayerFilter.All).Length==1,"Layer browser searches the full supported 512-layer document");
    }
}
