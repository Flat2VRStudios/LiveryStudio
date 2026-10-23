using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using LiveryStudio;
using LiveryStudio.Core;

internal static class DraftRecoveryChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        Directory.CreateDirectory(root);var workspace=Path.Combine(root,"workspace");
        var color=Path.Combine(root,"color.tga");var surface=Path.Combine(root,"surface.tga");var artwork=Path.Combine(root,"artwork.tga");
        FinishMaps.WriteTga(new(1,1,[20,40,60,255]),color);FinishMaps.WriteTga(new(1,1,[160,100,0,255]),surface);FinishMaps.WriteTga(new(1,1,[240,220,20,255]),artwork);
        var valid=new LiveryProject{Name="Recovery fixture",Albedo=color,Structure=surface,Description="Keep unfinished details · unchanged",InnerColor="#2468AC",Metalness=.37};

        foreach(var (label,incomplete) in new[]{
            ("name",valid with{Name=""}),
            ("hex",valid with{InnerColor="#12"}),
            ("maps",valid with{Albedo=Path.Combine(root,"missing.png"),Structure=""})})
        {
            var path=DraftRecovery.Save(incomplete,workspace);var restored=LiveryProject.Read(path);
            var fields=restored with{Albedo=incomplete.Albedo,Structure=incomplete.Structure,Banner=incomplete.Banner,EditorFile=incomplete.EditorFile};
            check(DraftRecovery.IsIncomplete(path)&&JsonSerializer.Serialize(fields,Contract.Json)==JsonSerializer.Serialize(incomplete,Contract.Json),$"Incomplete {label} recovery preserves every form field without requiring validation");
            check((!File.Exists(incomplete.Albedo)?restored.Albedo==incomplete.Albedo:restored.Albedo!=incomplete.Albedo&&WorkshopService.Hash(restored.Albedo)==WorkshopService.Hash(incomplete.Albedo))&&(!File.Exists(incomplete.Structure)?restored.Structure==incomplete.Structure:restored.Structure!=incomplete.Structure&&WorkshopService.Hash(restored.Structure)==WorkshopService.Hash(incomplete.Structure)),$"Incomplete {label} recovery copies available map bytes and retains missing references");
        }
        check(DraftRecovery.IsIncomplete("PREVIOUS.DRAFT.JSON")&&!DraftRecovery.IsIncomplete("previous.flproject"),"Recovery format detection supports case-insensitive raw snapshots and distinguishes portable projects");

        var editor=Path.Combine(root,"editor.json");var document=new LayerDocument{BaseAlbedo=color,BaseStructure=surface};
        document.Commit("Placed decal",layers=>layers.Add(new(){Name="Saved decal",Kind=LayerKind.Sticker,Asset=artwork,Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1)]}));document.Save(editor);
        var unfinished=valid with{Name="",EditorFile=editor};var originalEditorHash=WorkshopService.Hash(editor);
        var rawPath=DraftRecovery.Save(unfinished,workspace);var raw=LiveryProject.Read(rawPath);
        string Normalize(LayerDocument d)=>JsonSerializer.Serialize(d,LayerDocument.Json).Replace(JsonSerializer.Serialize(d.BaseAlbedo),"\"COLOR\"").Replace(JsonSerializer.Serialize(d.BaseStructure),"\"SURFACE\"").Replace(JsonSerializer.Serialize(d.Layers[0].Asset),"\"ARTWORK\"");
        var rawEditorHash=WorkshopService.Hash(raw.EditorFile);
        check(DraftRecovery.IsIncomplete(rawPath)&&raw.EditorFile!=editor&&File.Exists(raw.EditorFile)&&Normalize(LayerDocument.Read(raw.EditorFile))==Normalize(document),"Incomplete editable draft snapshots every editor field while rebasing resource paths");
        check(unfinished.EditorFile==editor&&unfinished.Name==""&&WorkshopService.Hash(editor)==originalEditorHash&&raw.Albedo!=color&&raw.Structure!=surface&&WorkshopService.Hash(raw.Albedo)==WorkshopService.Hash(color)&&WorkshopService.Hash(raw.Structure)==WorkshopService.Hash(surface),"Raw recovery does not mutate the caller and independently copies its output maps");
        document.Commit("Later edit",layers=>layers[0].Name="Changed after recovery");document.Save(editor);
        var rawDocument=LayerDocument.Read(raw.EditorFile);
        check(WorkshopService.Hash(raw.EditorFile)==rawEditorHash&&rawDocument.Cursor==1&&rawDocument.Layers[0].Name=="Saved decal","Later source-editor changes cannot overwrite the incomplete recovery history");
        check(rawDocument.BaseAlbedo!=color&&rawDocument.BaseStructure!=surface&&rawDocument.Layers[0].Asset!=artwork&&WorkshopService.Hash(rawDocument.BaseAlbedo)==WorkshopService.Hash(color)&&WorkshopService.Hash(rawDocument.BaseStructure)==WorkshopService.Hash(surface)&&WorkshopService.Hash(rawDocument.Layers[0].Asset)==WorkshopService.Hash(artwork),"Incomplete recovery copies retained original maps and decal bytes into its snapshot");
        var rawFirst=DraftRecovery.Open(rawPath,workspace);var rawSecond=DraftRecovery.Open(rawPath,workspace);
        check(rawFirst.Name==""&&rawSecond.Name==""&&rawFirst.EditorFile!=rawSecond.EditorFile&&rawFirst.EditorFile!=raw.EditorFile&&rawFirst.EditorFile!=editor,"Repeated incomplete recovery opens preserve invalid fields and allocate independent working editor files");
        var rawWorkingDocument=LayerDocument.Read(rawFirst.EditorFile);rawWorkingDocument.Commit("Edited restored raw draft",layers=>layers[0].Name="Only first raw copy");rawWorkingDocument.Save(rawFirst.EditorFile);
        check(WorkshopService.Hash(raw.EditorFile)==rawEditorHash&&LayerDocument.Read(rawSecond.EditorFile).Layers[0].Name=="Saved decal"&&LayerDocument.Read(editor).Layers[0].Name=="Changed after recovery","Editing a restored incomplete draft cannot mutate its saved recovery, another opened copy or original editor");
        var rawThird=DraftRecovery.Open(rawPath,workspace);
        check(LayerDocument.Read(rawThird.EditorFile).Layers[0].Name=="Saved decal"&&rawThird.Albedo!=color&&rawThird.Structure!=surface&&rawThird.Albedo!=rawFirst.Albedo&&WorkshopService.Hash(rawThird.Albedo)==WorkshopService.Hash(color),"Reopening incomplete recovery returns saved history and an independent copy of its saved maps");
        var firstMapHash=WorkshopService.Hash(rawFirst.Albedo);FinishMaps.WriteTga(new(1,1,[9,8,7,255]),rawSecond.Albedo);FinishMaps.WriteTga(new(1,1,[1,2,3,255]),rawSecond.Structure);
        check(WorkshopService.Hash(rawFirst.Albedo)==firstMapHash&&WorkshopService.Hash(raw.Albedo)==WorkshopService.Hash(color)&&WorkshopService.Hash(rawThird.Structure)==WorkshopService.Hash(surface),"Writing incomplete recovery maps leaves the snapshot, other opened copies and originals unchanged");
        var absentEditor=valid with{InnerColor="#",EditorFile=Path.Combine(root,"missing-editor.json")};var absentRecovery=DraftRecovery.Open(DraftRecovery.Save(absentEditor,workspace),workspace);
        check(absentRecovery.EditorFile==absentEditor.EditorFile&&absentRecovery.InnerColor=="#","Incomplete recovery preserves missing-editor references without substituting a stock design");
        var brokenEditor=Path.Combine(root,"broken-editor.json");File.WriteAllText(brokenEditor,"{\"History\":null}");var broken=DraftRecovery.Open(DraftRecovery.Save(valid with{Name="",EditorFile=brokenEditor},workspace),workspace);
        check(broken.EditorFile!=brokenEditor&&File.ReadAllText(broken.EditorFile)==File.ReadAllText(brokenEditor),"Malformed editor bytes remain recoverable without rewriting or validating the original");

        var portable=DraftRecovery.Save(valid with{EditorFile=editor},workspace);var portableHash=WorkshopService.Hash(portable);
        using(var archive=ZipFile.OpenRead(portable))check(!DraftRecovery.IsIncomplete(portable)&&archive.GetEntry("project.json") is not null&&archive.GetEntry("editor.json") is not null&&archive.Entries.Count(x=>x.FullName.StartsWith("assets/"))==3,"Valid editable recovery produces a portable project containing maps, decal and editor history");
        var first=DraftRecovery.Open(portable,workspace);var second=DraftRecovery.Open(portable,workspace);var firstDocument=LayerDocument.Read(first.EditorFile);var secondDocument=LayerDocument.Read(second.EditorFile);
        check(first.EditorFile!=second.EditorFile&&first.Albedo!=second.Albedo&&first.Structure!=second.Structure&&first.Albedo!=color&&firstDocument.Layers[0].Asset!=secondDocument.Layers[0].Asset,"Repeated portable recovery opens allocate independent editor, texture and artwork sources");
        check(firstDocument.Cursor==document.Cursor&&secondDocument.Cursor==document.Cursor&&firstDocument.Layers[0].Name=="Changed after recovery"&&WorkshopService.Hash(first.Albedo)==WorkshopService.Hash(color),"Portable recovery restores exact saved history and texture bytes");
        var secondColorHash=WorkshopService.Hash(second.Albedo);var sourceColorHash=WorkshopService.Hash(color);
        FinishMaps.WriteTga(new(1,1,[1,2,3,255]),first.Albedo);firstDocument.Commit("Edit reopened copy",layers=>layers[0].Name="Only first copy");firstDocument.Save(first.EditorFile);
        check(WorkshopService.Hash(second.Albedo)==secondColorHash&&WorkshopService.Hash(color)==sourceColorHash&&LayerDocument.Read(second.EditorFile).Layers[0].Name=="Changed after recovery"&&LayerDocument.Read(editor).Layers[0].Name=="Changed after recovery","Editing one reopened copy leaves the other copy and original sources unchanged");
        check(WorkshopService.Hash(portable)==portableHash&&DraftRecovery.Open(portable,workspace).Name==valid.Name,"Editing reopened content does not alter the reusable portable recovery archive");

        var svg=Path.Combine(root,"retained.svg");File.WriteAllText(svg,"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"><rect width=\"1\" height=\"1\" fill=\"#f0dc14\"/></svg>");
        var svgHash=WorkshopService.Hash(svg);var artworkHash=WorkshopService.Hash(artwork);var retainedEditor=Path.Combine(root,"retained-editor.json");
        var retainedDocument=new LayerDocument{BaseAlbedo=color,BaseStructure=surface};
        retainedDocument.Commit("Retained sources in future checkpoint",layers=>
        {
            layers.Add(new(){Name="Retained SVG",Kind=LayerKind.Sticker,Asset=artwork,VectorArtwork=new(svg){Recolors=new(){["#F0DC14"]="#112233"}},Effects=new(){Mask=DecalMask.Image,StencilFile=artwork,Blend=DecalBlend.Multiply,Invert=true},Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1)]});
            layers.Add(new(){Name="Retained raster",Kind=LayerKind.Sticker,Asset=artwork,RasterArtwork=new(artwork){Recolors=new(){["#F0DC14"]="#212121"}},Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1)]});
        });
        retainedDocument.Undo();retainedDocument.Save(retainedEditor);
        var retainedRecovery=DraftRecovery.Save(valid with{Name="",EditorFile=retainedEditor,Banner=artwork},workspace);var retainedSnapshot=LiveryProject.Read(retainedRecovery);var retainedSnapshotDocument=LayerDocument.Read(retainedSnapshot.EditorFile);var future=retainedSnapshotDocument.History[1].Layers;
        check(retainedSnapshotDocument.Cursor==0&&retainedSnapshotDocument.History.Count==2&&future[0].VectorArtwork!.SourceFile!=svg&&WorkshopService.Hash(future[0].VectorArtwork!.SourceFile)==svgHash&&WorkshopService.Hash(future[1].RasterArtwork!.SourceFile)==artworkHash,"Incomplete recovery preserves retained SVG and raster sources in an undone future checkpoint");
        check(future[0].Asset==future[1].Asset&&future[0].Asset==future[0].Effects!.StencilFile&&future[0].Asset==future[1].RasterArtwork!.SourceFile&&future[0].Asset==retainedSnapshot.Banner,"Repeated image, stencil, banner and retained-raster references share one independent copied asset");
        check(future[0].VectorArtwork!.Recolors["#F0DC14"]=="#112233"&&future[1].RasterArtwork!.Recolors["#F0DC14"]=="#212121"&&future[0].Effects is{Invert:true,Blend:DecalBlend.Multiply,Mask:DecalMask.Image},"Incomplete recovery retains source recolors and stencil settings alongside copied bytes");
        File.WriteAllText(svg,"Changed external source");FinishMaps.WriteTga(new(1,1,[1,3,5,255]),artwork);
        var reopenedRetained=DraftRecovery.Open(retainedRecovery,workspace);var reopenedFuture=LayerDocument.Read(reopenedRetained.EditorFile).History[1].Layers;
        check(reopenedFuture[0].VectorArtwork!.SourceFile!=future[0].VectorArtwork!.SourceFile&&WorkshopService.Hash(reopenedFuture[0].VectorArtwork!.SourceFile)==svgHash&&reopenedFuture[0].Effects!.StencilFile!=future[0].Effects!.StencilFile&&WorkshopService.Hash(reopenedFuture[0].Effects!.StencilFile)==artworkHash&&WorkshopService.Hash(reopenedFuture[1].RasterArtwork!.SourceFile)==artworkHash,"Reopening incomplete recovery uses independent saved SVG, raster and stencil bytes after external originals change");
    }
}
