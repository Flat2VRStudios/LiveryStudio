using System.Numerics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using LiveryStudio.Core;

internal static class DesignPaletteChecks
{
    [SupportedOSPlatform("windows")]
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"design-colors");Directory.CreateDirectory(folder);var blank=Path.Combine(folder,"blank.tga");FinishMaps.WriteTga(new(32,32,new byte[32*32*4]),blank);
        var originalImage=Path.Combine(folder,"image.tga");FinishMaps.WriteTga(new(2,2,[255,0,0,128,0,0,255,255,255,0,0,0,255,0,0,255]),originalImage);var imageHash=WorkshopService.Hash(originalImage);
        var svgSource=Path.Combine(folder,"logo.svg");File.WriteAllText(svgSource,"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect width='100' height='100' style='fill:red;stroke:#f00;stroke-width:2'/></svg>");var svgBytes=File.ReadAllBytes(svgSource);var vector=VectorArtwork.Import(svgSource,folder,512);
        var project=new LiveryProject{Albedo=blank,Structure=blank,OuterColor="#FF0000",InnerColor="#FF0000",UseSrgbPaintColors=true};
        var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1);
        var text=new TextArtworkOptions{Text="FURY",Color="#FF0000",OutlineColor="#000000",OutlineWidth=.02f,FontName="Segoe UI"};var pattern=new PatternArtworkOptions{Kind=PatternKind.Checker,ColorA="#FF0000",ColorB="#0000FF",Resolution=512};
        var doc=LayerDocument.Create(project,Path.Combine(folder,"doc"));doc.Commit("Artwork",layers=>layers.AddRange([
            new(){Name="Paint",Kind=LayerKind.Color,Color="#FF0000",Samples=[p],Rotation=17,ScaleX=1.3f,Gloss=.3f,Mirror=true},
            new(){Name="Lettering",Kind=LayerKind.Sticker,Asset=TextArtwork.Generate(text,folder),TextArtwork=text,Samples=[p],Opacity=.7f},
            new(){Name="Checker",Kind=LayerKind.Sticker,Asset=PatternArtwork.Generate(pattern,folder),PatternArtwork=pattern,Samples=[p]},
            new(){Name="SVG",Kind=LayerKind.Sticker,Asset=vector,VectorArtwork=VectorArtwork.ReadMetadata(vector),Samples=[p]},
            new(){Name="Image",Kind=LayerKind.Sticker,Asset=originalImage,Samples=[p]},
            new(){Name="Locked paint",Kind=LayerKind.Color,Color="#FF0000",Samples=[p],Locked=true}
        ]));
        var initial=JsonSerializer.Serialize(doc,LayerDocument.Json);var uses=DesignPalette.Uses(doc,project);
        check(uses.Count(u=>u.Hex=="#FF0000")==8,"Design color catalog finds paint, text, pattern, SVG and exact image colors");
        check(ArtworkColors.SvgHex("rgb(50%, 50%, 50%)")=="#808080"&&ArtworkColors.SvgHex("#f00")=="#FF0000"&&ArtworkColors.SvgHex("red")=="#FF0000","SVG color catalog normalizes percentages, short hex and named colors");
        check(ArtworkColors.SvgHex("url(#g)")==null&&ArtworkColors.SvgHex("currentColor")==null,"SVG color catalog preserves references and inherited colors");
        var linked=DesignPalette.Link(doc,project,"#ff0000","Racing red");var role=linked.Document.History[linked.Document.Cursor].Palette.Single();
        check(linked.LinkedFields==7&&linked.ChangedLayers==5&&linked.Document.Version==6,"One named color links every unlocked matching field and declares format 6");
        check(JsonSerializer.Serialize(doc,LayerDocument.Json)==initial,"Preparing a color link leaves every source checkpoint untouched");
        check(linked.Document.Layers.Single(l=>l.Locked).ColorLinks.Count==0,"Linking a color leaves locked artwork independent");
        check(linked.Document.Layers.Select(l=>l.Asset).SequenceEqual(doc.Layers.Select(l=>l.Asset))&&linked.Project.OuterColor==project.OuterColor,"Linking colors changes metadata without regenerating pixels or paint");
        var changed=DesignPalette.Change(linked.Document,linked.Project,role.Id,"#21DCC1",folder);var changedRole=changed.Document.History[changed.Document.Cursor].Palette.Single();
        check(changed.ChangedLayers==5&&changed.Project.OuterColor=="#21DCC1"&&changed.Project.InnerColor=="#21DCC1","A shared color updates both paint faces and all five linked artwork types");
        var colored=changed.Document.Layers;
        check(colored[0].Color=="#21DCC1"&&colored[1].TextArtwork!.Color=="#21DCC1"&&colored[2].PatternArtwork!.ColorA=="#21DCC1"&&colored[3].VectorArtwork!.Recolors["#FF0000"]=="#21DCC1","Structured artwork keeps editable recolor recipes");
        var bitmap=FinishMaps.Read(colored[4].Asset);
        check(bitmap.Pixels[0]==33&&bitmap.Pixels[1]==220&&bitmap.Pixels[2]==193&&bitmap.Pixels[3]==128&&bitmap.Pixels[4]==0&&bitmap.Pixels[6]==255&&bitmap.Pixels[11]==0,"Exact image recoloring preserves partial alpha, transparency and unrelated pixels");
        var svg=FinishMaps.Read(colored[3].Asset);var middle=(256*512+256)*4;
        check(svg.Pixels[middle]==33&&svg.Pixels[middle+1]==220&&svg.Pixels[middle+2]==193,"SVG inline styles actually render the shared replacement color");
        check(File.ReadAllBytes(svgSource).SequenceEqual(svgBytes)&&WorkshopService.Hash(originalImage)==imageHash,"Shared recoloring preserves original SVG and raster source bytes");
        for(var i=0;i<doc.Layers.Count;i++)
        {
            var a=doc.Layers[i];var b=colored[i];check(a.Samples.SequenceEqual(b.Samples)&&a.Rotation==b.Rotation&&a.ScaleX==b.ScaleX&&a.Mirror==b.Mirror&&a.Opacity==b.Opacity&&a.Gloss==b.Gloss&&a.Id==b.Id,"Shared recolor preserves placement and finish: "+a.Name);
        }
        check(colored[5].Color=="#FF0000","Locked independent artwork keeps its pigment after a shared update");
        var saved=JsonSerializer.Serialize(changed.Document,LayerDocument.Json);var cursor=changed.Document.Cursor;
        changed.Document.Undo();var undoPaint=DesignPalette.ResolvePaint(changed.Document,changed.Project);
        check(undoPaint.OuterColor=="#FF0000"&&undoPaint.InnerColor=="#FF0000"&&changed.Document.Layers[3].Asset==linked.Document.Layers[3].Asset,"One undo restores paint and every artwork raster together");
        changed.Document.Redo();check(changed.Document.Cursor==cursor&&JsonSerializer.Serialize(changed.Document,LayerDocument.Json)==saved,"Redo restores the exact shared-color state and cached assets");
        var renamed=DesignPalette.Change(changed.Document,changed.Project,role.Id,changedRole.Hex,folder,name:"Turquoise team");
        check(renamed.Document.History[renamed.Document.Cursor].Palette.Single().Name=="Turquoise team"&&renamed.Document.Layers.Select(l=>l.Asset).SequenceEqual(changed.Document.Layers.Select(l=>l.Asset)),"Renaming a shared color preserves all pixels and source assets");
        var unlocked=DesignPalette.Unlink(changed.Document,changed.Project,role.Id);check(unlocked.Document.Layers.All(l=>l.ColorLinks.Count==0)&&unlocked.Document.History[unlocked.Document.Cursor].Palette.Count==0&&unlocked.Document.Layers.Select(l=>l.Asset).SequenceEqual(changed.Document.Layers.Select(l=>l.Asset)),"Unlinking fields preserves the visible design and its generated assets");
        var manual=JsonSerializer.Deserialize<LayerDocument>(saved,LayerDocument.Json)!;manual.Commit("Independent text color",layers=>layers[1].TextArtwork=layers[1].TextArtwork! with {Color="#FFFFFF"});
        check(!manual.Layers[1].ColorLinks.ContainsKey("text.fill")&&manual.Layers[0].ColorLinks.Count==1,"Editing one linked field detaches only that field");
        DesignPalette.SyncManualPaint(manual,changed.Project with {OuterColor="#FFFFFF"});
        check(manual.History[manual.Cursor].PaintColors!.EdgeLink is null&&manual.History[manual.Cursor].PaintColors!.FaceLink is not null,"Direct paint edits detach the changed face and preserve its partner link");
        var manualValid=JsonSerializer.Serialize(manual,LayerDocument.Json);reject(()=>DesignPalette.SyncManualPaint(manual,changed.Project with {OuterColor="#12"}),"Partial paint input is rejected before changing shared history");check(JsonSerializer.Serialize(manual,LayerDocument.Json)==manualValid,"Invalid paint input preserves every shared-color checkpoint");
        var beforeManual=manual.Cursor;manual.Undo();check(DesignPalette.ResolvePaint(manual,changed.Project with {OuterColor="#FFFFFF"}).OuterColor=="#21DCC1"&&manual.Cursor==beforeManual-1,"Direct paint changes participate in shared-color undo history");
        var selected=DesignPalette.Link(doc,project,"#FF0000","Selected ink",new HashSet<Guid>{doc.Layers[1].Id});
        check(selected.LinkedFields==1&&selected.Document.History[selected.Document.Cursor].PaintColors!.EdgeLink is null&&selected.Document.Layers[0].ColorLinks.Count==0,"Selection-only links exclude base paint and unselected layers");
        var locked=JsonSerializer.Deserialize<LayerDocument>(saved,LayerDocument.Json)!;locked.Commit("Lock linked paint",layers=>layers[0].Locked=true);var lockedBefore=JsonSerializer.Serialize(locked,LayerDocument.Json);
        reject(()=>DesignPalette.Change(locked,changed.Project,role.Id,"#FFFFFF",folder),"Shared color updates require unlocking dependent layers");check(JsonSerializer.Serialize(locked,LayerDocument.Json)==lockedBefore,"Rejected locked updates preserve the entire source document");
        using var canceled=new CancellationTokenSource();canceled.Cancel();reject(()=>DesignPalette.Change(changed.Document,changed.Project,role.Id,"#FFFFFF",folder,canceled.Token),"Shared artwork regeneration honors cancellation");
        check(JsonSerializer.Serialize(changed.Document,LayerDocument.Json)==saved,"Canceled recoloring preserves every checkpoint and active layer");
        var preview=LayerBrowser.Inspection(changed.Document,[colored[3].Id]);preview.Validate();check(preview.History[0].Palette.Count==1&&DesignPalette.ResolvePaint(preview,project).OuterColor=="#21DCC1","Temporary layer inspection preserves shared palette and current paint state");
        var newStroke=new PaintLayer{Kind=LayerKind.Color,Color="#21DCC1",Samples=[p]};DesignPalette.AttachUniqueLinks(newStroke,changed.Document.History[changed.Document.Cursor].Palette);check(newStroke.ColorLinks["paint"].Id==role.Id,"New matching brush strokes join a unique shared color");
        var group=ReusableArtwork.Save("Shared artwork",changed.Document.Layers.Take(5).ToArray(),folder);var groupLayers=ReusableArtwork.Open(group,Path.Combine(folder,"group"));check(DesignPalette.LinkedColors(groupLayers).Single().Hex=="#21DCC1","Reusable groups retain their self-contained shared color definitions");
        var placed=ReusableArtwork.Place(groupLayers,p,false);check(DesignPalette.LinkedColors(placed).Single().Id!=role.Id&&DesignPalette.LinkedColors(placed).Single().Hex=="#21DCC1","Placing saved artwork forks its color IDs without changing its palette");
        var editor=Path.Combine(folder,"palette-editor.json");changed.Document.Save(editor);var archive=Path.Combine(folder,"palette.flproject");ProjectBundle.Save(changed.Project with {EditorFile=editor},archive);var restoredProject=ProjectBundle.Open(archive,Path.Combine(folder,"portable"));var restored=LayerDocument.Read(restoredProject.EditorFile);
        File.Delete(originalImage);File.Delete(svgSource);
        check(File.Exists(restored.Layers[4].RasterArtwork!.SourceFile)&&File.ReadAllBytes(restored.Layers[3].VectorArtwork!.SourceFile).SequenceEqual(svgBytes),"Portable shared-color designs retain original raster and SVG sources independently");
        var alternate=DesignPalette.Change(restored,restoredProject,role.Id,"#FFCC33",folder);check(alternate.Project.OuterColor=="#FFCC33"&&FinishMaps.Read(alternate.Document.Layers[4].Asset).Pixels[0]==255,"Portable shared palettes regenerate after external sources are removed");
        var legacy=project with {UseSrgbPaintColors=false,OuterColor="#040f2a",InnerColor="#123456"};var legacyDoc=LayerDocument.Create(legacy,Path.Combine(folder,"legacy"));var legacyHex=DesignColor.PaintPickerHex(legacy,legacy.OuterColor);var legacyLink=DesignPalette.Link(legacyDoc,legacy,legacyHex,"Legacy paint");var legacyRole=legacyLink.Document.History[legacyLink.Document.Cursor].Palette.Single();var legacyChange=DesignPalette.Change(legacyLink.Document,legacy,legacyRole.Id,"#CC5500",folder);legacyChange.Document.Undo();var legacyUndo=DesignPalette.ResolvePaint(legacyChange.Document,legacyChange.Project);
        check(legacyUndo.OuterColor=="#040f2a"&&legacyUndo.InnerColor=="#123456"&&!legacyUndo.UseSrgbPaintColors,"Legacy paint undo preserves exact raw fields and color interpretation");
        var oldFormat=JsonSerializer.Deserialize<LayerDocument>(saved,LayerDocument.Json)!;oldFormat.Version=5;reject(()=>oldFormat.Validate(),"Older editor formats reject shared-color metadata");
    }
}
