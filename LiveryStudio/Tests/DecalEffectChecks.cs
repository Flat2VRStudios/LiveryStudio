using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class DecalEffectChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"decal-effects");Directory.CreateDirectory(folder);
        PaintImage Solid(int size,byte r,byte g,byte b,byte a){var pixels=new byte[size*size*4];for(var k=0;k<pixels.Length;k+=4){pixels[k]=r;pixels[k+1]=g;pixels[k+2]=b;pixels[k+3]=a;}return new(size,size,pixels);}
        var source=Solid(32,200,100,50,128);var sourceBytes=(byte[])source.Pixels.Clone();
        var oval=new DecalEffectsOptions{Mask=DecalMask.Oval,Width=.8f,Height=.8f,Feather=.2f};
        var masked=DecalEffects.Apply(source,oval);var center=(16*32+16)*4;
        check(masked.Pixels[center+3]==128&&masked.Pixels[3]==0,"Oval stencil preserves center alpha and cuts outside corners");
        check(source.Pixels.SequenceEqual(sourceBytes)&&masked.Pixels.Where((_,i)=>i%4!=3).SequenceEqual(source.Pixels.Where((_,i)=>i%4!=3)),"Masking changes only copied alpha and never edits source RGB or bytes");
        var inverted=DecalEffects.Apply(source,oval with {Invert=true});
        check(inverted.Pixels[center+3]==0&&inverted.Pixels[3]==128,"Inverting a stencil reveals the original outside and hides its center");
        check(masked.Pixels.Where((_,i)=>i%4==3).Any(a=>a>0&&a<128),"Feathered stencil produces partial alpha without losing source transparency");
        var box=DecalEffects.Apply(source,new(){Mask=DecalMask.Box,Width=.5f,Height=.5f,Feather=0});
        check(box.Pixels[center+3]==128&&box.Pixels[(16*32+2)*4+3]==0,"Box stencil crops relative to original decal dimensions");
        var fade=DecalEffects.Apply(source,new(){Mask=DecalMask.LinearFade});
        check(fade.Pixels[(16*32+1)*4+3]>fade.Pixels[(16*32+30)*4+3],"Directional fade changes coverage across the original artwork");
        var rotated=DecalEffects.Apply(source,new(){Mask=DecalMask.LinearFade,Angle=90});
        check(rotated.Pixels[(1*32+16)*4+3]>rotated.Pixels[(30*32+16)*4+3],"Rotating a fade changes its direction in decal coordinates");
        var radial=DecalEffects.Apply(source,new(){Mask=DecalMask.RadialFade});
        check(radial.Pixels[center+3]>radial.Pixels[(16*32+1)*4+3]&&radial.Pixels[3]==0,"Radial fade reveals the center and fades outward");
        var shifted=DecalEffects.Apply(source,oval with {CenterX=0});
        check(shifted.Pixels[center+3]==0&&shifted.Pixels[(16*32)*4+3]>0,"Stencil offsets are independent of the decal's car placement");
        var stencilPath=Path.Combine(folder,"stencil.tga");var stencil=new PaintImage(2,2,[255,255,255,255,0,0,0,255,255,255,255,255,0,0,0,255]);FinishMaps.WriteTga(stencil,stencilPath);var stencilHash=WorkshopService.Hash(stencilPath);
        var imageOptions=new DecalEffectsOptions{Mask=DecalMask.Image,StencilFile=stencilPath};
        var image=DecalEffects.Apply(source,imageOptions);
        check(image.Pixels[(16*32)*4+3]==128&&image.Pixels[(16*32+31)*4+3]==0,"Automatic opaque image stencils use white/black brightness");
        check(image.Pixels[center+3]>0&&image.Pixels[center+3]<128,"Image stencil sampling is bilinear between its source pixels");
        var alphaOnly=DecalEffects.Apply(source,imageOptions with {Channel=StencilChannel.Alpha});
        check(alphaOnly.Pixels.Where((_,i)=>i%4==3).All(a=>a==128),"Explicit alpha stencils ignore black RGB when fully opaque");
        var alphaPath=Path.Combine(folder,"alpha-stencil.tga");FinishMaps.WriteTga(new(2,2,[0,0,0,255,255,255,255,0,0,0,0,255,255,255,255,0]),alphaPath);
        var alphaAuto=DecalEffects.Apply(source,imageOptions with {StencilFile=alphaPath});
        check(alphaAuto.Pixels[(16*32)*4+3]==128&&alphaAuto.Pixels[(16*32+31)*4+3]==0,"Automatic transparent stencils use alpha rather than black pigment");
        var outside=DecalEffects.Apply(source,imageOptions with {Width=.5f});
        check(outside.Pixels[(16*32+1)*4+3]==0,"An image stencil's outside area is hidden without edge smearing");
        check(WorkshopService.Hash(stencilPath)==stencilHash,"Stencil rendering leaves the retained original image untouched");
        var zero=Solid(4,70,80,90,0);check(DecalEffects.Apply(zero,oval).Pixels.SequenceEqual(zero.Pixels),"Already transparent source pixels remain byte-identical");
        check(ReferenceEquals(DecalEffects.Apply(source,new()),source),"Neutral effects preserve the original image without a new allocation");
        foreach(var bad in new[]{oval with {Width=0},oval with {Angle=float.NaN},oval with {Feather=2},oval with {CenterX=float.PositiveInfinity},oval with {Mask=(DecalMask)90}})reject(()=>DecalEffects.Apply(source,bad),"Reject invalid stencil dimensions, enum or non-finite settings");
        reject(()=>DecalEffects.Apply(source,new(){Mask=DecalMask.Image}),"Image stencils require a retained source");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();reject(()=>DecalEffects.Apply(source,oval,cancel.Token),"Canceled stencil preparation leaves the source pixels untouched");}
        var below=new Vector3(.25f,.6f,.8f);var above=new Vector3(.8f,.4f,.1f);
        check(DecalEffects.Mix(below,above,DecalBlend.Normal)==above,"Normal blending keeps the decal pigment");
        check(Vector3.Distance(DecalEffects.Mix(below,above,DecalBlend.Multiply),new(.2f,.24f,.08f))<.00001f,"Multiply blends actual pigment channels");
        check(Vector3.Distance(DecalEffects.Mix(below,above,DecalBlend.Screen),new(.85f,.76f,.82f))<.00001f,"Screen blends actual pigment channels");
        check(Vector3.Distance(DecalEffects.Mix(below,above,DecalBlend.Overlay),new(.4f,.52f,.64f))<.00001f,"Overlay switches around the underlying midpoint");
        reject(()=>DecalEffects.Mix(below,above,(DecalBlend)99),"Reject unknown blend modes");
        var values=new List<float>();void Vertex(float x,float y,float u,float v)=>values.AddRange([x,y,0,0,0,1,u,v,1]);Vertex(-.5f,.5f,0,0);Vertex(.5f,.5f,1,0);Vertex(-.5f,-.5f,0,1);Vertex(.5f,.5f,1,0);Vertex(.5f,-.5f,1,1);Vertex(-.5f,-.5f,0,1);var mesh=new PaintMesh(values.ToArray());
        var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1);
        var baseFile=Path.Combine(folder,"base.tga");var surfaceFile=Path.Combine(folder,"surface.tga");var decalFile=Path.Combine(folder,"decal.tga");FinishMaps.WriteTga(Solid(32,80,100,120,255),baseFile);FinishMaps.WriteTga(Solid(32,100,0,0,255),surfaceFile);FinishMaps.WriteTga(Solid(32,200,120,60,255),decalFile);var decalHash=WorkshopService.Hash(decalFile);
        var project=new LiveryProject{Albedo=baseFile,Structure=surfaceFile,OverrideArtworkFinish=false,UseSrgbPaintColors=true};var doc=LayerDocument.Create(project,Path.Combine(folder,"doc"));doc.SeamPadding=0;
        doc.Commit("Decal",layers=>layers.Add(new(){Name="Masked badge",Asset=decalFile,Kind=LayerKind.Sticker,Samples=[p]}));var normal=doc.Compose(mesh,project);
        var before=JsonSerializer.Serialize(doc.Layers[0],LayerDocument.Json);doc.Commit("Oval and multiply",layers=>layers[0].Effects=oval with {Blend=DecalBlend.Multiply});var result=doc.Compose(mesh,project);
        check(doc.Version==7&&result.Albedo.Pixels[3]==255&&result.Albedo.Pixels[0]==80,"Effect documents declare format 7 and masked-out pixels reveal unchanged underlying artwork");
        check(result.Albedo.Pixels[center]<normal.Albedo.Pixels[center]&&result.Structure.Pixels.SequenceEqual(normal.Structure.Pixels),"Blending changes composed color without changing the structure map");
        check(doc.Layers[0].Samples[0]==p&&doc.Layers[0].Asset==decalFile&&WorkshopService.Hash(decalFile)==decalHash,"Effects preserve source asset, placement and source file bytes");
        check(doc.Undo()&&JsonSerializer.Serialize(doc.Layers[0],LayerDocument.Json)==before,"One undo restores the complete prior decal metadata");
        check(doc.Redo()&&doc.Layers[0].Effects?.Mask==DecalMask.Oval,"Redo restores the editable stencil and blend settings");
        var old=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(doc,LayerDocument.Json),LayerDocument.Json)!;old.Version=6;reject(()=>old.Validate(),"Older formats cannot silently drop decal effects");
        var role=new SharedDesignColor(Guid.NewGuid(),"Pigment","#21DCC1");var state=doc.History[doc.Cursor];doc.History[doc.Cursor]=state with {Palette=[role],PaintColors=new(project.OuterColor,project.InnerColor,true)};
        var snapshot=doc.Snapshot("Drag preview",doc.Layers);snapshot.Validate();var originalState=JsonSerializer.Serialize(doc,LayerDocument.Json);snapshot.Layers[0].Name="Temporary";snapshot.History[0].Palette.Clear();
        check(snapshot.Version==7&&snapshot.History[0].PaintColors is not null&&JsonSerializer.Serialize(doc,LayerDocument.Json)==originalState,"Drag snapshots retain newer format and paint metadata while remaining independent");
        var centerHit=new SurfaceHit(Vector3.Zero,Vector3.UnitZ,new(.5f,.5f),0,1);var cornerHit=new SurfaceHit(new(-.47f,.47f,0),Vector3.UnitZ,new(.03f,.03f),0,1);
        var under=new PaintLayer{Name="Under",Kind=LayerKind.Sticker,Asset=decalFile,Samples=[p]};
        check(DecalEditing.Pick(mesh,[under,doc.Layers[0]],centerHit,32,32,FinishMaps.Read)?.LayerId==doc.Layers[0].Id,"Selection picks the visible center of a masked decal");
        check(DecalEditing.Pick(mesh,[under,doc.Layers[0]],cornerHit,32,32,FinishMaps.Read)?.LayerId==under.Id,"Masked-out decal pixels allow selection of the artwork underneath");
        var largeSource=Solid(1024,200,120,60,255);var candidates=Enumerable.Range(0,128).Select(i=>new PaintLayer{Name="Candidate "+i,Kind=LayerKind.Sticker,Asset="cached image",Effects=oval,Samples=[p]}).ToArray();
        _=DecalEditing.Pick(mesh,candidates,cornerHit,32,32,_=>largeSource);var allocationBefore=GC.GetAllocatedBytesForCurrentThread();var picked=DecalEditing.Pick(mesh,candidates,cornerHit,32,32,_=>largeSource);var allocated=GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
        check(picked is null&&allocated<256*1024,"Picking across 128 masked 1024px candidates uses bounded texel sampling ("+allocated+" allocated bytes)");
        var transparentStencil=FinishMaps.Read(alphaPath);var stencilReads=0;PaintImage CachedImage(string path){if(path==alphaPath){stencilReads++;return transparentStencil;}return largeSource;}
        var imageCandidates=candidates.Select(l=>l with {Effects=new(){Mask=DecalMask.Image,StencilFile=alphaPath}}).ToArray();var rightHit=new SurfaceHit(new(.47f,-.47f,0),Vector3.UnitZ,new(.97f,.97f),1,1);
        check(DecalEditing.Pick(mesh,imageCandidates,rightHit,32,32,CachedImage) is null&&stencilReads==1,"A shared image stencil is prepared once while picking through 128 candidates");
        var tinyAlpha=new PaintImage(1,1,[255,255,255,1]);var quantizedLayer=new PaintLayer{Kind=LayerKind.Sticker,Asset="fractional",Effects=new(){Mask=DecalMask.LinearFade},Samples=[p]};
        check(DecalEffects.Apply(tinyAlpha,quantizedLayer.Effects).Pixels[3]==0&&DecalEditing.Pick(mesh,[quantizedLayer],centerHit,32,32,_=>tinyAlpha) is null,"Picking uses baked alpha quantization so a rounded-to-zero texel remains unselectable");
        doc.Commit("Image stencil",layers=>layers[0].Effects=imageOptions with {Blend=DecalBlend.Screen});var editor=Path.Combine(folder,"editor.json");doc.Save(editor);project.EditorFile=editor;var portable=Path.Combine(folder,"stencil.flproject");ProjectBundle.Save(project,portable);File.Delete(stencilPath);var recovered=ProjectBundle.Open(portable,Path.Combine(folder,"recovered"));var recoveredDoc=LayerDocument.Read(recovered.EditorFile);
        check(recoveredDoc.Layers[0].Effects is {Mask:DecalMask.Image,Blend:DecalBlend.Screen} recoveredEffect&&WorkshopService.Hash(recoveredEffect.StencilFile)==stencilHash,"Portable projects retain image stencil bytes and resolve their editable recipe after originals disappear");
        check(recoveredDoc.Compose(mesh,recovered).Albedo.Pixels.Length==32*32*4,"Recovered image stencils compose through the normal livery pipeline");
        var reusable=ReusableArtwork.Save("Stencil badge",recoveredDoc.Layers,folder);var group=ReusableArtwork.Open(reusable,Path.Combine(folder,"group"));
        check(group[0].Effects?.Mask==DecalMask.Image&&File.Exists(group[0].Effects!.StencilFile),"Reusable artwork groups retain their image stencils and effects");
        check(ReusableArtwork.Preview(group,64).Pixels.Where((_,i)=>i%4==3).Any(a=>a==0),"Reusable previews render stencil transparency");
        var placed=ReusableArtwork.Place(group,p,true);check(placed[0].Effects==group[0].Effects&&placed[0].Mirror,"Placed paired groups preserve effects in decal coordinates");
        recoveredDoc.Commit("Lock",layers=>layers[0].Locked=true);reject(()=>recoveredDoc.Commit("Alter effects",layers=>layers[0].Effects=oval),"Locked artwork rejects stencil and blend changes");
    }
}
