using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class ArtworkReplacementChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"replacement");Directory.CreateDirectory(folder);
        var oldAsset=Path.Combine(folder,"old.tga");var newAsset=Path.Combine(folder,"new.tga");var stencil=Path.Combine(folder,"stencil.tga");
        FinishMaps.WriteTga(new(2,1,[255,0,0,255,255,0,0,0]),oldAsset);
        FinishMaps.WriteTga(new(1,2,[0,255,0,255,0,0,255,128]),newAsset);
        FinishMaps.WriteTga(new(1,2,[255,255,255,255,255,255,255,64]),stencil);
        var oldHash=WorkshopService.Hash(oldAsset);var newHash=WorkshopService.Hash(newAsset);var stencilHash=WorkshopService.Hash(stencil);
        var source=new PaintLayer{Kind=LayerKind.Sticker,Name="Sponsor badge",Asset=oldAsset,TextArtwork=new(){Text="OLD"},GroupId=Guid.NewGuid(),GroupName="Door badge",Mirror=true,Opacity=.65f,Scale=1.3f,ScaleX=1.7f,ScaleY=.6f,Rotation=23,OffsetRight=.2f,OffsetUp=-.1f,FlipHorizontal=true,FlipVertical=true,WrapAngle=56,WrapDepth=.7f,Gloss=.24f,Metalness=.81f,Triangles=[0,1],Samples=[new(new(.2f,.1f,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.8f,.3f),new(new(.5f,.2f,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.6f,.2f)],Effects=new(){Mask=DecalMask.Image,StencilFile=stencil,Blend=DecalBlend.Screen,Feather=.2f}};
        var oldRole=new SharedDesignColor(Guid.NewGuid(),"Former sponsor","#FF0000");source.TextArtwork!.Color=oldRole.Hex;source.ColorLinks["text.fill"]=oldRole;
        var serialized=JsonSerializer.Serialize(source,LayerDocument.Json);var replacement=ArtworkReplacement.Create(source,newAsset);
        var allowed=source with {Asset=newAsset,TextArtwork=null,ColorLinks=[]};
        check(JsonSerializer.Serialize(replacement,LayerDocument.Json)==JsonSerializer.Serialize(allowed,LayerDocument.Json),"Replacing an image changes only its source/recipe/links, preserving both stamp placements, mirrors, group, effects and finish");
        check(JsonSerializer.Serialize(source,LayerDocument.Json)==serialized&&replacement.Samples!=source.Samples&&replacement.Triangles!=source.Triangles,"Replacement geometry is independent and the original layer remains unchanged");
        var fitted=ArtworkReplacement.Create(source,newAsset,ArtworkReplacementFit.PreserveProportions);
        check(fitted.Samples.Zip(source.Samples).All(pair=>{var p=source.Transform(pair.Second);var q=fitted.Transform(pair.First);return q.Center==p.Center&&q.Right==p.Right&&q.Up==p.Up&&q.Normal==p.Normal&&q.Width<=p.Width&&q.Height<=p.Height&&Math.Abs(q.Width/q.Height-.5f)<.0001f&&q.FlipHorizontal==p.FlipHorizontal&&q.FlipVertical==p.FlipVertical;}),"Proportion fit retains each transformed center, rotation and flips while fitting the new portrait image inside each existing box");
        var wide=Path.Combine(folder,"wide.tga");FinishMaps.WriteTga(new(4,1,Enumerable.Repeat((byte)255,16).ToArray()),wide);
        var wideFit=ArtworkReplacement.Create(source,wide,ArtworkReplacementFit.PreserveProportions);
        check(wideFit.Samples.Zip(source.Samples).All(pair=>{var p=source.Transform(pair.Second);var q=wideFit.Transform(pair.First);return q.Width<=p.Width+.00001f&&q.Height<=p.Height+.00001f&&Math.Abs(q.Width/q.Height-4)<.0001f;}),"Proportion fit handles a wider replacement without exceeding its current decal box");
        reject(()=>ArtworkReplacement.Create(source with {Locked=true},newAsset),"Locked decals reject replacement");
        reject(()=>ArtworkReplacement.Create(source with {Kind=LayerKind.Color,TextArtwork=null},newAsset),"Paint strokes cannot be replaced as artwork");
        reject(()=>ArtworkReplacement.Create(source,newAsset,(ArtworkReplacementFit)99),"Unknown replacement sizing is rejected");
        var baseColor=Path.Combine(folder,"base.tga");var baseSurface=Path.Combine(folder,"surface.tga");
        FinishMaps.WriteTga(new(16,16,Enumerable.Range(0,1024).Select(i=>(byte)(i%4==3?255:50)).ToArray()),baseColor);
        FinishMaps.WriteTga(new(16,16,Enumerable.Range(0,1024).Select(i=>(byte)(i%4==2?255:90)).ToArray()),baseSurface);
        var simple=source with {TextArtwork=null,ColorLinks=[],Mirror=false,Opacity=1,Scale=1,ScaleX=1,ScaleY=1,Rotation=0,OffsetRight=0,OffsetUp=0,FlipHorizontal=false,FlipVertical=false,Effects=new(){Mask=DecalMask.Image,StencilFile=stencil},Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,2)]};
        var mesh=Quad();var project=new LiveryProject{CarIndex=0,Name="Replacement source",Albedo=baseColor,Structure=baseSurface};
        var doc=LayerDocument.Create(project,Path.Combine(folder,"editor"));doc.SeamPadding=0;doc.Commit("Old badge",layers=>layers.Add(simple));
        var before=doc.Compose(mesh,project);var cursor=doc.Cursor;var oldState=JsonSerializer.Serialize(doc,LayerDocument.Json);
        ArtworkReplacement.Apply(doc,simple.Id,newAsset);
        var after=doc.Compose(mesh,project);
        check(doc.Cursor==cursor+1&&doc.Layers.Count==1&&doc.Layers[0].Id==simple.Id,"Applying replacement preserves layer identity/order and makes exactly one undo step");
        check(!before.Albedo.Pixels.SequenceEqual(after.Albedo.Pixels)&&before.Structure.Pixels.SequenceEqual(after.Structure.Pixels),"The replacement reaches actual composed color while retaining the original surface texture");
        check(doc.Undo()&&doc.Compose(mesh,project).Albedo.Pixels.SequenceEqual(before.Albedo.Pixels)&&doc.Redo()&&doc.Compose(mesh,project).Albedo.Pixels.SequenceEqual(after.Albedo.Pixels),"Undo and redo restore the exact original and replacement texture bytes");
        var invalid=Path.Combine(folder,"bad.png");File.WriteAllText(invalid,"broken image");var unchanged=JsonSerializer.Serialize(doc,LayerDocument.Json);
        reject(()=>ArtworkReplacement.Apply(doc,simple.Id,invalid),"A corrupt replacement image is rejected before changing history");
        check(JsonSerializer.Serialize(doc,LayerDocument.Json)==unchanged,"Failed replacement keeps the current design and undo history byte-identical");
        reject(()=>ArtworkReplacement.Apply(doc,Guid.NewGuid(),newAsset),"A stale replacement target cannot mutate another layer");
        var portable=Path.Combine(folder,"Replacement.flproject");var editorFile=Path.Combine(folder,"editor","editor.json");doc.Save(editorFile);project.EditorFile=editorFile;ProjectBundle.Save(project,portable);
        var reopened=ProjectBundle.Open(portable,Path.Combine(folder,"reopened"));var restored=LayerDocument.Read(reopened.EditorFile);
        check(restored.Cursor==doc.Cursor&&restored.History.Count==doc.History.Count&&restored.Layers[0].Asset!=newAsset&&File.Exists(restored.Layers[0].Effects!.StencilFile),"Portable replacement retains its complete undo history, independent replacement asset and image stencil");
        check(restored.Compose(mesh,reopened).Albedo.Pixels.SequenceEqual(after.Albedo.Pixels)&&restored.Undo()&&restored.Compose(mesh,reopened).Albedo.Pixels.SequenceEqual(before.Albedo.Pixels),"Reopened portable projects reproduce replacement and original artwork pixels");
        check(WorkshopService.Hash(oldAsset)==oldHash&&WorkshopService.Hash(newAsset)==newHash&&WorkshopService.Hash(stencil)==stencilHash,"Replacing, undoing and saving never change either artwork source or the retained stencil");
        if(OperatingSystem.IsWindows())
        {
            var text=TextArtwork.Generate(new(){Text="NEW",Color="#21DCC1"},folder);var textReplacement=ArtworkReplacement.Create(source,text);
            check(textReplacement.TextArtwork?.Text=="NEW"&&textReplacement.VectorArtwork is null&&textReplacement.PatternArtwork is null,"Replacing with retained text adopts only the new editable text recipe");
            var imports=Path.Combine(folder,"picker-imports");var importedText=ArtworkReplacement.Import(text,imports);
            check(WorkshopService.Hash(importedText)==WorkshopService.Hash(text)&&TextArtwork.ReadMetadata(importedText)?.Text=="NEW","The picker import retains selected text image bytes and its validated editable recipe");
            var nextRole=new SharedDesignColor(Guid.NewGuid(),"New sponsor","#21DCC1");var paletteDoc=new LayerDocument();paletteDoc.History[0]=paletteDoc.History[0] with {Palette=[oldRole,nextRole]};paletteDoc.Commit("Original linked badge",layers=>layers.Add(source));
            ArtworkReplacement.Apply(paletteDoc,source.Id,importedText);
            check(paletteDoc.Layers[0].ColorLinks["text.fill"].Id==nextRole.Id&&paletteDoc.Undo()&&paletteDoc.Layers[0].ColorLinks["text.fill"].Id==oldRole.Id,"Replacement detaches former artwork color links, joins matching new design colors and restores the original links on Undo");
            var svg=Path.Combine(folder,"new.svg");File.WriteAllText(svg,"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"><rect width=\"20\" height=\"10\" fill=\"#21DCC1\"/></svg>");
            var vector=VectorArtwork.Import(svg,folder,512);var vectorReplacement=ArtworkReplacement.Create(source,vector);
            check(vectorReplacement.VectorArtwork is {RasterSize:512}&&File.Exists(vectorReplacement.VectorArtwork.SourceFile)&&vectorReplacement.TextArtwork is null,"Replacing with SVG retains its original vector and clears the former text recipe");
            var importedVector=ArtworkReplacement.Import(vector,imports);var retainedVector=VectorArtwork.ReadMetadata(importedVector)!;var externalVector=VectorArtwork.ReadMetadata(vector)!;
            check(WorkshopService.Hash(importedVector)==WorkshopService.Hash(vector)&&retainedVector.SourceFile!=externalVector.SourceFile&&WorkshopService.Hash(retainedVector.SourceFile)==WorkshopService.Hash(externalVector.SourceFile),"Importing an existing generated SVG decal captures its original vector independently without re-rendering its selected pixels");
            File.WriteAllText(externalVector.SourceFile,"changed external vector");
            check(VectorArtwork.Rebuild(retainedVector,imports).Length>0,"A replacement SVG can still rebuild after its former external source changes");
            var pattern=PatternArtwork.Generate(new(),folder);var importedPattern=ArtworkReplacement.Import(pattern,imports);
            check(PatternArtwork.ReadMetadata(importedPattern)==PatternArtwork.ReadMetadata(pattern)&&WorkshopService.Hash(importedPattern)==WorkshopService.Hash(pattern),"The picker preserves generated pattern recipes and their exact selected image");
            var externalRaster=Path.Combine(folder,"external-color.tga");FinishMaps.WriteTga(new(1,1,[255,0,0,255]),externalRaster);var recolored=ArtworkColors.Generate(new(externalRaster){Recolors=new(){["#FF0000"]="#123456"}},folder);
            var importedRaster=ArtworkReplacement.Import(recolored,imports);var retainedRaster=ArtworkColors.ReadMetadata(importedRaster)!;FinishMaps.WriteTga(new(1,1,[0,0,255,255]),externalRaster);
            check(retainedRaster.SourceFile!=externalRaster&&FinishMaps.Read(retainedRaster.SourceFile).Pixels[0]==255&&retainedRaster.Recolors["#FF0000"]=="#123456"&&WorkshopService.Hash(importedRaster)==WorkshopService.Hash(recolored),"The picker captures original recolor pixels and mappings independently of later external edits");
            File.WriteAllText(pattern+".text.json",JsonSerializer.Serialize(new TextArtworkOptions{Text="Conflicting"},Contract.Json));
            reject(()=>ArtworkReplacement.Import(pattern,imports),"Conflicting replacement recipes are rejected before a managed image is published");
        }
    }

    private static PaintMesh Quad()
    {
        var values=new List<float>();foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1)})values.AddRange([p.X,p.Y,0,0,0,1,(p.X+1)/2,(1-p.Y)/2,1]);return new(values.ToArray());
    }
}
