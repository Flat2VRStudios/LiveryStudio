using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class StarterChecks
{
    public static void CheckMesh(PaintMesh mesh,string name,Action<bool,string> check)
    {
        var layouts=DesignStarters.Styles.Select(s=>DesignStarters.Layout(mesh,DesignStarters.Defaults(s.Kind))).ToArray();
        check(layouts.All(l=>l.Layers.Count>=5&&l.Layers.Any(p=>p.TextArtwork?.Text=="27")&&l.Layers.All(p=>p.Samples.All(s=>Vector3.Dot(Vector3.Cross(s.Normal,s.Right),s.Up)>.99f))),"All six starters place readable, editable number and accent layers on real car: "+name);
        check(layouts.All(l=>l.Layers.Where(p=>p.TextArtwork is not null).All(p=>p.Mirror&&p.GroupId.HasValue)&&l.Layers.Any(p=>p.PatternArtwork is not null)),"Starter badges are paired groups and accents retain their editable pattern recipes: "+name);
    }
    public static void Run(string repo,string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var defaults=DesignStarters.Defaults(StarterKind.Circuit);var original=new LiveryProject{UseSrgbPaintColors=false,OuterColor="#111111",InnerColor="#222222",Banner="old.png",EditorFile="old.json"};var painted=DesignStarters.Paint(original,defaults);
        check(painted.UseSrgbPaintColors&&painted.Name=="Circuit stripes 27"&&painted.OuterColor==defaults.BaseColor&&painted.InnerColor==defaults.BaseColor&&painted.EditorFile==""&&painted.Banner==""&&original.EditorFile=="old.json","Starting a design resets its own references and leaves the original project untouched");
        var twoTone=DesignStarters.Paint(original,DesignStarters.Defaults(StarterKind.TwoTone));check(twoTone.OuterColor!=twoTone.InnerColor&&twoTone.Metalness>.5,"Two-tone starter retains separate face and edge paint rather than baking a flat color");
        check(DesignStarters.Paint(original,defaults with {IncludeNumber=false,Number=""}).Name=="Circuit stripes","A starter without badges needs no unused racing number");
        reject(()=>(defaults with {Number="2x"}).Validate(),"Reject a non-numeric racing number");reject(()=>(defaults with {Number="1000"}).Validate(),"Reject an oversized racing number");reject(()=>(defaults with {Team="bad\nname"}).Validate(),"Reject control characters in a team name");reject(()=>(defaults with {Kind=(StarterKind)99}).Validate(),"Reject an unsupported starter style");reject(()=>(defaults with {AccentColor="red"}).Validate(),"Reject invalid starter palette colors");
        var modelPath=Path.Combine(repo,"LiveryStudio/artifacts/models/00.flsm");if(!OperatingSystem.IsWindows()||!File.Exists(modelPath))return;var mesh=PaintMesh.Read(modelPath);
        var without=DesignStarters.Layout(mesh,defaults with {IncludeNumber=false});check(without.Layers.All(l=>l.TextArtwork is null&&!l.GroupId.HasValue),"Optional number badge removes the panel, number and team together");
        var stockPixels=new byte[128*128*4];for(var i=0;i<stockPixels.Length;i+=4){stockPixels[i]=240;stockPixels[i+3]=255;}var blank=new PaintImage(128,128,stockPixels);var baseFile=Path.Combine(temp,"starter-base.tga");FinishMaps.WriteTga(blank,baseFile);var source=original with {Albedo=baseFile,Structure=baseFile};var before=File.ReadAllBytes(baseFile);var design=DesignStarters.Create(source,mesh,defaults with {Number="007",Team="TEST TEAM"},temp);var document=LayerDocument.Read(design.Project.EditorFile);
        check(File.ReadAllBytes(baseFile).SequenceEqual(before)&&design.Project.Albedo!=baseFile&&document.BaseAlbedo!=baseFile,"Starter creation preserves input textures and writes independent editable originals");
        check(FinishMaps.Read(document.BaseAlbedo).Pixels.All(p=>p==0)&&File.ReadAllBytes(document.BaseStructure).SequenceEqual(before),"Starter removes stock paint from its new foundation while preserving the original surface map");
        check(document.Version==4&&document.Cursor==1&&document.Layers.Count==5&&document.Layers.Single(l=>l.TextArtwork?.Text=="007").TextArtwork!.Text=="007","Starter preserves leading-zero numbers and creates one named artwork checkpoint");
        check(FinishMaps.Read(design.Project.Albedo).Pixels.Where((_,i)=>i%4==3).Any(a=>a>0),"Starter composition produces real marked texture pixels on the selected mesh");
        var expected=document.Layers.Select(l=>JsonSerializer.Serialize(l,LayerDocument.Json)).ToArray();check(document.Undo()&&document.Layers.Count==0&&document.Redo()&&document.Layers.Select(l=>JsonSerializer.Serialize(l,LayerDocument.Json)).SequenceEqual(expected),"One undo/redo restores every starter layer and its exact projection metadata");
        var bundle=Path.Combine(Path.GetDirectoryName(design.Project.EditorFile)!,"Starter.flproject");var reopened=ProjectBundle.Open(bundle,Path.Combine(temp,"starter-reopened"));var restored=LayerDocument.Read(reopened.EditorFile);
        check(restored.Layers.Count==document.Layers.Count&&restored.Layers.Count(l=>l.PatternArtwork is not null)==3&&restored.Layers.Count(l=>l.TextArtwork is not null)==2&&restored.Layers.All(l=>File.Exists(l.Asset)),"Portable starter restores all generated artwork, editable recipes and paired badge groups");
        check(restored.Compose(mesh,reopened).Albedo.Pixels.SequenceEqual(document.Compose(mesh,design.Project).Albedo.Pixels),"Restored starter composes identical texture pixels from its retained source");
        check(document.Layers.Where(l=>l.TextArtwork is not null).All(l=>{var im=FinishMaps.Read(l.Asset);var p=l.Samples[0];return Math.Abs(p.Width/p.Height-(float)im.Width/im.Height)<.0001f;}),"Racing numbers and team text preserve their actual font aspect ratio");
        using var canceled=new CancellationTokenSource();canceled.Cancel();reject(()=>DesignStarters.Create(source,mesh,defaults,temp,canceled.Token),"Canceled starter creation leaves the original design intact");
    }
}
