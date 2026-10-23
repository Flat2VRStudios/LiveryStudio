using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class SimpleStudioChecks
{
    public static void Run(string repo,string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"simple-studio");Directory.CreateDirectory(folder);var color=Path.Combine(folder,"base.tga");var surface=Path.Combine(folder,"surface.tga");var logo=Path.Combine(folder,"logo.tga");
        FinishMaps.WriteTga(new(64,64,Enumerable.Repeat((byte)100,64*64*4).ToArray()),color);FinishMaps.WriteTga(new(64,64,new byte[64*64*4]),surface);FinishMaps.WriteTga(new(1,1,[15,45,255,255]),logo);
        var project=new LiveryProject{Albedo=color,Structure=surface,UseSrgbPaintColors=true};var document=new LayerDocument{BaseAlbedo=color,BaseStructure=surface};DesignPalette.EnsureProjectHistory(document,project);
        var finish=FinishPreset.All.Single(f=>f.Name=="Chrome").Apply(project) with {OuterColor="#654321",InnerColor="#123456"};var before=document.History.Count;DesignPalette.SyncManualPaint(document,finish);
        check(document.History.Count==before+1&&DesignPalette.ResolvePaint(document,project)==finish,"One history step captures body colors and the complete finish together");
        document.Undo();check(DesignPalette.ResolvePaint(document,finish)==project,"Undo restores both paint colors and every finish value");document.Redo();check(DesignPalette.ResolvePaint(document,project)==finish,"Redo restores the combined finish and color change");
        var prior=JsonSerializer.Serialize(document,LayerDocument.Json);reject(()=>DesignPalette.SyncManualPaint(document,finish with {ClearCoat=double.NaN}),"Reject invalid finish before changing undo history");check(prior==JsonSerializer.Serialize(document,LayerDocument.Json),"Invalid finish input preserves all checkpoints");
        var mesh=PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models/00.flsm"));
        foreach(var kind in Enum.GetValues<StarterKind>())
        {
            var source=JsonSerializer.Serialize(document,LayerDocument.Json);var options=DesignStarters.Defaults(kind);var edit=SimpleDesigns.Look(document,finish,mesh,options,folder);
            check(JsonSerializer.Serialize(document,LayerDocument.Json)==source,"Previewing "+kind+" leaves the active design and history untouched");
            check(edit.Document.Cursor==document.Cursor+1&&edit.Document.History[edit.Document.Cursor].CleanPaint,"Keeping "+kind+" is one undoable look with a clean paint foundation");
            var expected=edit.Document.Compose(mesh,edit.Project);edit.Document.Undo();var restored=edit.Document.Compose(mesh,edit.Project);var original=document.Compose(mesh,finish);
            check(restored.Albedo.Pixels.SequenceEqual(original.Albedo.Pixels)&&restored.Structure.Pixels.SequenceEqual(original.Structure.Pixels)&&DesignPalette.ResolvePaint(edit.Document,edit.Project)==finish,"Undo restores complete original artwork and finish after "+kind);
            edit.Document.Redo();check(edit.Document.Compose(mesh,edit.Project).Albedo.Pixels.SequenceEqual(expected.Albedo.Pixels),"Redo restores the exact "+kind+" look");
        }
        var look=SimpleDesigns.Look(document,finish,mesh,DesignStarters.Defaults(StarterKind.Circuit),folder);var personal=new PaintLayer{Name="Personal logo",Asset=logo,Samples=[new(new(1,0,0),Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY,.2f,.2f)]};var brush=personal with {Id=Guid.NewGuid(),Name="Hand paint",Kind=LayerKind.Color,Asset="",Color="#AD19CC"};
        look.Document.Commit("Personal artwork",layers=>{layers.Add(personal);layers.Add(brush);});var count=look.Document.History.Count;
        var recolored=SimpleDesigns.Palette(look.Document,look.Project,new[]{"#192B42","#FFAA00","#FFF3ED"},folder);
        check(recolored.Document.History.Count==count+1,"Changing all design palette roles creates one undo step");
        check(recolored.Document.Layers.Single(l=>l.Id==personal.Id).Asset==logo&&recolored.Document.Layers.Single(l=>l.Id==brush.Id).Color==brush.Color,"Palette changes preserve personal images and unlinked hand-painted details");
        check(recolored.Document.Layers.Where(l=>l.TextArtwork is not null).All(l=>l.TextArtwork!.Color=="#18202B"),"Palette swaps choose readable number lettering automatically");
        var number=SimpleDesigns.Number(recolored.Document,recolored.Project,mesh,"37","MY TEAM",folder);check(number.Document.History.Count==recolored.Document.History.Count+1&&number.Document.Layers.Count==recolored.Document.Layers.Count&&number.Document.Layers.Count(l=>l.Name=="Race number 37"&&l.Mirror)==1,"A starter's existing number and name update on both doors in one undo step without duplicate badges");
        reject(()=>SimpleDesigns.Number(recolored.Document,recolored.Project,mesh,"ABC","",folder),"Reject invalid racing numbers without changing the design");
        var map=number.Document.Compose(mesh,number.Project);var editor=Path.Combine(folder,"editor.json");number.Document.Save(editor);var painted=Path.Combine(folder,"painted.tga");var finished=Path.Combine(folder,"finished.tga");FinishMaps.WriteTga(map.Albedo,painted);FinishMaps.WriteTga(map.Structure,finished);
        var portable=Path.Combine(folder,"Simple.flproject");ProjectBundle.Save(number.Project with {Albedo=painted,Structure=finished,EditorFile=editor},portable);var opened=ProjectBundle.Open(portable,Path.Combine(folder,"opened"));var restoredDocument=LayerDocument.Read(opened.EditorFile);
        check(restoredDocument.Version==9&&restoredDocument.CanUndo&&restoredDocument.Compose(mesh,opened).Albedo.Pixels.SequenceEqual(map.Albedo.Pixels),"Portable projects retain format 9, clean paint, finish history, editable numbers and exact map pixels");
        foreach(var name in FunArtwork.Shapes.Keys){var vector=VectorArtwork.Validate(System.Text.Encoding.UTF8.GetBytes(FunArtwork.Svg(name,"#EF3946")));var image=VectorArtwork.Render(vector,512);check(image.Pixels.Where((_,i)=>i%4==3).Any(a=>a>0)&&image.Pixels.Where((_,i)=>i%4==3).Any(a=>a==0),"Built-in "+name+" sticker renders visible art and transparent space");}
        CheckEraser(folder,check);
        SharedUvChecks.Run(repo,folder,check);
    }
    private static void CheckEraser(string folder,Action<bool,string> check)
    {
        var mesh=new PaintMesh([-.5f,.5f,0,0,0,1,0,0,1,.5f,.5f,0,0,0,1,1,0,1,-.5f,-.5f,0,0,0,1,0,1,1,.5f,.5f,0,0,0,1,1,0,1,.5f,-.5f,0,0,0,1,1,1,1,-.5f,-.5f,0,0,0,1,0,1,1]);
        var blank=Path.Combine(folder,"erase-base.tga");FinishMaps.WriteTga(new(64,64,new byte[64*64*4]),blank);var logo=Path.Combine(folder,"logo.tga");var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.9f,.9f);var dab=p with {Width=.5f,Height=.5f};
        var paint=new PaintLayer{Kind=LayerKind.Color,Color="#FF0000",Samples=[p]};var sticker=new PaintLayer{Asset=logo,Samples=[p with {Center=new(.12f,0,0),Width=.2f,Height=.2f}]};var project=new LiveryProject{Albedo=blank,Structure=blank};var document=new LayerDocument{Version=9,BaseAlbedo=blank,BaseStructure=blank};DesignPalette.EnsureProjectHistory(document,project);document.Commit("Paint and sticker",layers=>layers.AddRange([sticker,paint]));
        var before=document.Compose(mesh,project);var under=document.Snapshot("Under paint",[sticker]).Compose(mesh,project);var live=new TexturePainter(before.Albedo);live.Stamp(mesh,dab,null,Vector3.One,1,Vector3.Zero,hardness:1,restore:under.Albedo);
        document.Commit("Erase paint",layers=>layers.Single(l=>l.Id==paint.Id).Erasures.Add(dab));var erased=document.Compose(mesh,project);
        if(!erased.Albedo.Pixels.SequenceEqual(live.Image.Pixels)){var differences=Enumerable.Range(0,live.Image.Pixels.Length).Where(i=>live.Image.Pixels[i]!=erased.Albedo.Pixels[i]).ToArray();Console.WriteLine(JsonSerializer.Serialize(new{EraserDifferences=differences.Length,First=differences.Take(12).Select(i=>new{Index=i,Live=live.Image.Pixels[i],Replay=erased.Albedo.Pixels[i],Before=before.Albedo.Pixels[i],Under=under.Albedo.Pixels[i]})}));}
        check(erased.Albedo.Pixels.SequenceEqual(live.Image.Pixels),"Live eraser pixels exactly match recomposition, including restored alpha and underlying stickers");
        var middle=(32*64+32)*4;check(erased.Albedo.Pixels[middle+3]==0,"Erasing paint restores the original alpha-zero car paint mask");
        document.Undo();check(document.Compose(mesh,project).Albedo.Pixels.SequenceEqual(before.Albedo.Pixels),"One Undo restores erased paint byte for byte");document.Redo();check(document.Compose(mesh,project).Albedo.Pixels.SequenceEqual(erased.Albedo.Pixels),"Redo restores the same erased paint and preserved artwork");
        var file=Path.Combine(folder,"erased.json");document.Save(file);check(LayerDocument.Read(file).Compose(mesh,project).Albedo.Pixels.SequenceEqual(erased.Albedo.Pixels),"Erased paint survives saving and reopening with its underlying design intact");
    }
}
