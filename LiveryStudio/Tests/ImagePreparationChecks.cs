using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class ImagePreparationChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"image-preparation");Directory.CreateDirectory(folder);
        PaintImage Image(int width,int height,Func<int,int,(byte R,byte G,byte B,byte A)> pixel)
        {
            var bytes=new byte[width*height*4];for(var y=0;y<height;y++)for(var x=0;x<width;x++){var c=pixel(x,y);var i=(y*width+x)*4;bytes[i]=c.R;bytes[i+1]=c.G;bytes[i+2]=c.B;bytes[i+3]=c.A;}return new(width,height,bytes);
        }
        var original=Image(5,5,(x,y)=>x is 0 or 4||y is 0 or 4||(x==2&&y==2)?((byte)255,(byte)255,(byte)255,(byte)255):((byte)255,(byte)0,(byte)0,(byte)255));
        var originalBytes=original.Pixels.ToArray();var edges=new ImagePreparationOptions{RemoveBackground=true,Tolerance=0,Softness=0};
        var removed=ImagePreparation.Render(original,edges);
        check(removed.Pixels[3]==0&&removed.Pixels[(2*5+2)*4+3]==255,"Edge-connected background removal preserves enclosed white details");
        check(removed.Pixels[(1*5+1)*4+3]==255&&removed.Pixels[(1*5+1)*4]==255,"Background removal retains opaque foreground pigment");
        var all=ImagePreparation.Render(original,edges with {Scope=BackgroundRemovalScope.Everywhere});
        check(all.Pixels[(2*5+2)*4+3]==0,"Everywhere removal also clears matching enclosed holes");
        check(original.Pixels.SequenceEqual(originalBytes)&&removed.Pixels.Where((_,i)=>i%4!=3).SequenceEqual(original.Pixels.Where((_,i)=>i%4!=3)),"Preparation changes copied coverage without mutating original RGB or alpha");
        var identity=ImagePreparation.Render(original,new());
        check(!ReferenceEquals(identity.Pixels,original.Pixels)&&identity.Pixels.SequenceEqual(originalBytes),"Disabled preparation returns an independent exact pixel copy");
        var diagonal=Image(3,3,(x,y)=>x==y?((byte)255,(byte)255,(byte)255,(byte)255):((byte)0,(byte)0,(byte)0,(byte)255));
        check(ImagePreparation.Render(diagonal,edges).Pixels[(1*3+1)*4+3]==255,"Edge removal uses four-connected regions instead of leaking through diagonal contact");
        var softened=Image(3,1,(x,_)=>x switch {0=>((byte)255,(byte)255,(byte)255,(byte)128),1=>((byte)220,(byte)220,(byte)220,(byte)128),_=>((byte)0,(byte)0,(byte)0,(byte)0)});
        var soft=ImagePreparation.Render(softened,edges with {Softness=.3f,Scope=BackgroundRemovalScope.Everywhere});
        check(soft.Pixels[3]==0&&soft.Pixels[7]==MathF.Round(128*(35/255f/.3f))&&soft.Pixels[11]==0,"Softness multiplies original alpha and never reveals already transparent pixels");
        var colored=Image(1,1,(_,_)=>((byte)239,(byte)113,(byte)37,(byte)255));
        check(ImagePreparation.Render(colored,edges with {BackgroundColor="#EF7125"}).Pixels[3]==0,"Zero-tolerance keys match exact byte colors without float conversion drift");
        var threshold=Image(2,1,(x,_)=>x==0?((byte)250,(byte)250,(byte)250,(byte)255):((byte)240,(byte)240,(byte)240,(byte)255));
        var tolerance=ImagePreparation.Render(threshold,edges with {Tolerance=6/255f});
        check(tolerance.Pixels[3]==0&&tolerance.Pixels[7]==255,"Hard tolerance removes only colors inside its stated range");
        var cropOptions=new ImagePreparationOptions{CropLeft=.2f,CropRight=.2f,CropTop=.2f,CropBottom=.2f};
        var cropped=ImagePreparation.Render(original,cropOptions);
        check(cropped.Width==3&&cropped.Height==3&&cropped.Pixels.SequenceEqual(Enumerable.Range(1,3).SelectMany(y=>original.Pixels.Skip((y*5+1)*4).Take(12))),"Cropping uses original pixel boundaries and preserves retained pixels exactly");
        check(ImagePreparation.Render(Image(1,1,(_,_)=>((byte)1,(byte)2,(byte)3,(byte)4)),cropOptions).Pixels.SequenceEqual(new byte[]{1,2,3,4}),"Fractional crops keep at least one pixel at tiny dimensions");
        check(ImagePreparation.GuessBackground(original)=="#FFFFFF"&&ImagePreparation.GuessBackground(Image(2,2,(_,_)=>((byte)99,(byte)77,(byte)22,(byte)0)))=="#FFFFFF","Background suggestion uses visible edge colors and handles completely transparent edges");
        var transparentBridge=Image(5,1,(x,_)=>x==0?((byte)0,(byte)0,(byte)0,(byte)0):((byte)255,(byte)255,(byte)255,(byte)255));
        check(ImagePreparation.Render(transparentBridge,edges).Pixels.Where((_,i)=>i%4==3).All(a=>a==0),"Transparent padding connects matching background regions without exposing hidden RGB");
        var random=new Random(8943);var agrees=true;
        for(var trial=0;trial<50;trial++)
        {
            var input=Image(19,13,(_,_)=>random.Next(3)==0?((byte)255,(byte)255,(byte)255,(byte)255):((byte)20,(byte)80,(byte)140,(byte)255));
            var expected=input.Pixels.ToArray();var seen=new HashSet<int>();var queue=new Queue<int>();
            void Seed(int x,int y){var i=y*input.Width+x;if(expected[i*4]==255&&seen.Add(i))queue.Enqueue(i);}
            for(var x=0;x<input.Width;x++){Seed(x,0);Seed(x,input.Height-1);}for(var y=0;y<input.Height;y++){Seed(0,y);Seed(input.Width-1,y);}
            while(queue.TryDequeue(out var i)){expected[i*4+3]=0;var x=i%input.Width;var y=i/input.Width;if(x>0)Seed(x-1,y);if(x+1<input.Width)Seed(x+1,y);if(y>0)Seed(x,y-1);if(y+1<input.Height)Seed(x,y+1);}
            agrees&=ImagePreparation.Render(input,edges).Pixels.SequenceEqual(expected);
        }
        check(agrees,"Scanline removal matches an independent pixel-queue flood across fifty irregular images");
        foreach(var invalid in new[]{edges with {Tolerance=float.NaN},edges with {Softness=-1},edges with {Scope=(BackgroundRemovalScope)22},edges with {CropLeft=.5f,CropRight=.5f},edges with {CropBottom=float.PositiveInfinity},edges with {CropTop=-.1f}})
            reject(()=>ImagePreparation.Render(original,invalid),"Invalid preparation settings fail before producing pixels");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();reject(()=>ImagePreparation.Render(original,edges,cancellation.Token),"Canceled image preparation stops before allocating output");

        var source=Path.Combine(folder,"original.tga");FinishMaps.WriteTga(original,source);var sourceHash=WorkshopService.Hash(source);
        var recipe=new RasterArtworkOptions(source){Preparation=edges,Recolors=new(){["#FF0000"]="#21DCC1"}};
        var generated=ArtworkColors.Generate(recipe,folder);var generatedImage=FinishMaps.Read(generated);
        check(generatedImage.Pixels[3]==0&&generatedImage.Pixels[(1*5+1)*4]==33,"Removal precedes retained pigment replacements in the generated decal");
        check(ArtworkColors.ReadMetadata(generated)?.Preparation==edges&&WorkshopService.Hash(source)==sourceHash,"Generated image metadata retains editable preparation and unchanged original bytes");
        var croppedRecipe=recipe with {Preparation=cropOptions};var cropAsset=ArtworkColors.Generate(croppedRecipe,folder);
        check(FinishMaps.Read(cropAsset).Width==3&&FinishMaps.Read(cropAsset).Height==3,"Generated cropped files publish the actual cropped dimensions");
        check(!ArtworkColors.RasterPalette(recipe with {Preparation=edges with {Scope=BackgroundRemovalScope.Everywhere}}).Contains("#FFFFFF"),"Prepared palette discovery excludes removed original colors");
        var baseFile=Path.Combine(folder,"blank.tga");FinishMaps.WriteTga(new(16,16,new byte[16*16*4]),baseFile);
        var project=new LiveryProject{Name="Prepared image",Albedo=baseFile,Structure=baseFile};
        var doc=LayerDocument.Create(project,Path.Combine(folder,"work"));var plane=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,.5f);
        var role=new SharedDesignColor(Guid.NewGuid(),"Sponsor ink","#21DCC1");
        var layer=new PaintLayer{Kind=LayerKind.Sticker,Asset=source,Name="Retained badge",Samples=[plane],Mirror=true,GroupId=Guid.NewGuid(),GroupName="Badge",Opacity=.8f,Rotation=23,ScaleX=1.4f,Gloss=.2f,Metalness=.7f,Effects=new(){Mask=DecalMask.Box,Width=.7f,Height=.8f}};
        doc.Commit("Original badge",layers=>layers.Add(layer));var before=JsonSerializer.Serialize(doc,LayerDocument.Json);
        ImagePreparation.Apply(doc,layer.Id,generated);var prepared=doc.Layers[0];
        check(doc.Version==8&&doc.Cursor==2&&prepared.Id==layer.Id&&prepared.Name==layer.Name&&prepared.GroupId==layer.GroupId,"Image preparation declares format eight and makes one checkpoint without replacing the layer identity");
        check(prepared.Samples.SequenceEqual(layer.Samples)&&prepared.Effects==layer.Effects&&prepared.Rotation==23&&prepared.ScaleX==1.4f&&prepared.Gloss==.2f&&prepared.Metalness==.7f&&prepared.Mirror,"Preparation keeps decal placement, paired sides, normalized masks and finish");
        var fitted=ImagePreparation.Create(prepared,cropAsset,ArtworkReplacementFit.PreserveProportions);
        check(fitted.Samples.All(p=>Math.Abs(p.Width/p.Height-1)<.0001f)&&fitted.Effects==layer.Effects,"Crop proportion-fit stays inside the old decal box with masks normalized to that resulting box");
        check(doc.Undo()&&JsonSerializer.Serialize(doc.Layers,LayerDocument.Json)==JsonSerializer.Serialize(new[]{layer},LayerDocument.Json)&&doc.Redo(),"Undo restores the full original image layer and redo restores preparation");
        var invalidDoc=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(doc,LayerDocument.Json),LayerDocument.Json)!;invalidDoc.Version=7;
        reject(()=>invalidDoc.Validate(),"Older declared formats cannot silently hide prepared-image recipes");
        var locked=prepared with {Locked=true};reject(()=>ImagePreparation.Create(locked,cropAsset),"Locked image layers reject preparation changes");
        reject(()=>ImagePreparation.Create(layer with {TextArtwork=new()},generated),"Generated text stays in its own text editor");
        var linked=DesignPalette.Link(doc,project,"#21DCC1","Sponsor ink");var linkedLayer=linked.Document.Layers[0];
        check(linked.Document.Version==8&&linkedLayer.ColorLinks.Count==1,"Linking a prepared pigment preserves format eight and its recipe");
        if(OperatingSystem.IsWindows())
        {
            var changed=DesignPalette.Change(linked.Document,linked.Project,linkedLayer.ColorLinks.Values.Single().Id,"#234567",folder);
            var changedImage=FinishMaps.Read(changed.Document.Layers[0].Asset);
            check(changed.Document.Version==8&&changedImage.Pixels[3]==0&&changedImage.Pixels[(1*5+1)*4]==0x23,"Shared pigment editing regenerates preparation from original pixels without restoring the removed background");
            var retainedLinks=ImagePreparation.Create(changed.Document.Layers[0],ArtworkColors.Generate(changed.Document.Layers[0].RasterArtwork! with {Preparation=cropOptions},folder));
            check(retainedLinks.ColorLinks.Keys.SequenceEqual(changed.Document.Layers[0].ColorLinks.Keys),"Preparing linked image pigments keeps the existing shared-color fields");
        }
        var editor=Path.Combine(folder,"work","editor.json");doc.Save(editor);project.EditorFile=editor;
        var portable=Path.Combine(folder,"prepared.flproject");ProjectBundle.Save(project,portable);var reopened=ProjectBundle.Open(portable,Path.Combine(folder,"reopened"));var restored=LayerDocument.Read(reopened.EditorFile);
        check(restored.Version==8&&restored.History.Count==doc.History.Count&&restored.Layers[0].RasterArtwork!.Preparation==edges&&WorkshopService.Hash(restored.Layers[0].RasterArtwork!.SourceFile)==sourceHash,"Portable projects retain complete prepared-image history and independently recovered original source");
        var reGenerated=ArtworkColors.Generate(restored.Layers[0].RasterArtwork!,Path.Combine(folder,"regenerated"));
        check(FinishMaps.Read(reGenerated).Pixels.SequenceEqual(generatedImage.Pixels),"Reopened prepared images regenerate identical pixels from their portable original and recipe");
        var group=ReusableArtwork.Save("Prepared badge",doc.Layers,folder);var groupLayers=ReusableArtwork.Open(group,Path.Combine(folder,"group"));
        check(groupLayers[0].RasterArtwork?.Preparation==edges&&FinishMaps.Read(groupLayers[0].Asset).Pixels.SequenceEqual(generatedImage.Pixels),"Reusable artwork groups retain prepared pixels and editable original-image settings");
        var imported=ArtworkReplacement.Import(generated,Path.Combine(folder,"imported"));var importedRecipe=ArtworkColors.ReadMetadata(imported)!;
        check(importedRecipe.Preparation==edges&&importedRecipe.SourceFile!=source&&WorkshopService.Hash(importedRecipe.SourceFile)==sourceHash,"Recipe-aware PNG/TGA imports retain preparation with an independent original");
        var unchanged=JsonSerializer.Serialize(doc,LayerDocument.Json);reject(()=>ArtworkColors.Generate(recipe,Path.Combine(folder,"cancel"),cancellation.Token),"Canceled generation does not publish prepared artwork");
        check(JsonSerializer.Serialize(doc,LayerDocument.Json)==unchanged&&WorkshopService.Hash(source)==sourceHash,"Canceled preparation preserves design history and original artwork");
    }
}
