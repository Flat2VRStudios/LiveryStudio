using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class DecalMaskPaintingChecks
{
    public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"mask-painting");Directory.CreateDirectory(folder);
        var brush=new DecalMaskPainting(64,64);var center=(32*64+32)*4+3;
        check(brush.Stencil.Pixels.All(b=>b==255),"Freehand mask begins with complete original artwork coverage");
        check(brush.Dab(new(.5f),new(.2f),1,1)>0&&brush.Stencil.Pixels[center]==0&&brush.Stencil.Pixels[3]==255,"Erase changes only alpha inside its source-coordinate footprint");
        check(brush.Stencil.Pixels.Where((_,i)=>i%4!=3).All(b=>b==255),"Freehand erasing leaves the stencil's white RGB channels unchanged");
        brush.Dab(new(.5f),new(.2f),1,1,true);check(brush.Stencil.Pixels[center]==255,"Restore reaches an erased hole and reveals original coverage");
        brush.Dab(new(.5f),new(.2f),1,.5f);check(brush.Stencil.Pixels[center]==128,"Half-strength erasing retains half of the original coverage");
        brush.Dab(new(.5f),new(.2f),1,.5f,true);check(brush.Stencil.Pixels[center]==192,"Half-strength restoring blends towards original coverage");
        var soft=new DecalMaskPainting(64,64);soft.Dab(new(.5f),new(.4f),.25f,1);check(soft.Stencil.Pixels.Where((_,i)=>i%4==3).Any(b=>b>0&&b<255),"Soft erasing creates gradual feathered alpha");
        var before=(byte[])soft.Stencil.Pixels.Clone();check(soft.Dab(new(3,3),new(.1f),1,1)==0&&soft.Stencil.Pixels.SequenceEqual(before),"Off-image erasing leaves every stencil byte untouched");
        var line=new DecalMaskPainting(64,64);line.Dab(new(.1f,.5f),new(.03f,.1f),1,1);line.Line(new(.1f,.5f),new(.9f,.5f),new(.03f,.1f),1,1);
        check(Enumerable.Range(7,50).All(x=>line.Stencil.Pixels[(32*64+x)*4+3]==0),"Interpolated freehand paths remain connected between distant pointer samples");
        reject(()=>brush.Dab(new(float.NaN,.5f),new(.1f),1,1),"Freehand masks reject non-finite positions");reject(()=>brush.Dab(new(.5f),Vector2.Zero,1,1),"Freehand masks reject zero brush radii");reject(()=>brush.Dab(new(.5f),new(.1f),2,1),"Freehand masks reject invalid edge hardness");
        var fade=new DecalEffectsOptions{Mask=DecalMask.LinearFade,Angle=37,CenterX=.6f,Width=.8f,Invert=true,Blend=DecalBlend.Screen};var source=new PaintImage(64,64,Enumerable.Range(0,64*64*4).Select(i=>(byte)(i%4==3?128:90)).ToArray());var sourceBefore=(byte[])source.Pixels.Clone();
        var flattened=new DecalMaskPainting(64,64,fade);var path=flattened.Save(folder);var recipe=DecalMaskPainting.Recipe(path,fade.Blend);var old=DecalEffects.Apply(source,fade);var round=DecalEffects.Apply(source,recipe);
        check(recipe is {Mask:DecalMask.Image,Channel:StencilChannel.Alpha,CenterX:.5f,CenterY:.5f,Width:1,Height:1,Angle:0,Invert:false,Feather:0,Blend:DecalBlend.Screen},"Painted stencil resets already-flattened transforms while retaining pigment blending");
        check(old.Pixels.Where((_,i)=>i%4==3).Zip(round.Pixels.Where((_,i)=>i%4==3),(a,b)=>Math.Abs(a-b)).All(d=>d<=1)&&source.Pixels.SequenceEqual(sourceBefore),"Flattened effects preserve partial source alpha within one quantization byte and original pixels exactly");
        var firstHash=WorkshopService.Hash(path);flattened.Dab(new(.5f),new(.3f),1,1);var next=flattened.Save(folder);
        flattened.AttachArtwork(source);flattened.Dab(new(.7f,.5f),new(.15f),.5f,.7f);var liveFile=flattened.Save(folder);var bakedLive=DecalEffects.Apply(source,DecalMaskPainting.Recipe(liveFile,fade.Blend));
        check(flattened.Artwork!.Pixels.SequenceEqual(bakedLive.Pixels)&&source.Pixels.SequenceEqual(sourceBefore),"Incremental brush artwork exactly matches baked stencil pixels without changing the source");
        check(next!=path&&WorkshopService.Hash(path)==firstHash&&WorkshopService.Hash(next)!=firstHash,"Each completed stroke writes a new stencil without changing earlier history's file");
        var transparent=new PaintImage(1,1,[255,0,0,0]);var restore=new DecalMaskPainting(1,1);restore.Dab(new(.5f),new(1f),1,1);restore.Dab(new(.5f),new(1f),1,1,true);
        var restoreFile=restore.Save(folder);check(DecalEffects.Apply(transparent,DecalMaskPainting.Recipe(restoreFile,DecalBlend.Normal)).Pixels[3]==0,"Restore cannot make the original image's transparent pixels opaque");
        restore.AttachArtwork(transparent);var invisibleBefore=(byte[])restore.Stencil.Pixels.Clone();
        check(restore.Dab(new(.5f),new(1f),1,1)==0&&restore.Stencil.Pixels.SequenceEqual(invisibleBefore),"Drawing over transparent original padding creates no invisible mask edit");
        check(Path.GetDirectoryName(restoreFile)==Path.Combine(folder,"stencils"),"Saved brush stencils stay out of the artwork library folder");
        var blocked=Path.Combine(folder,"blocked-workspace");File.WriteAllText(blocked,"existing file");reject(()=>restore.Save(blocked),"Stencil write failures propagate without pretending the stroke was saved");
        var ellipse=new DecalMaskPainting(80,40);ellipse.Dab(new(.5f),new(.3f,.1f),1,1);
        check(ellipse.Stencil.Pixels[(20*80+55)*4+3]==0&&ellipse.Stencil.Pixels[(25*80+40)*4+3]==255,"Source-coordinate brush radii preserve the requested ellipse on non-square artwork");
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();reject(()=>new DecalMaskPainting(64,64,cancellation:canceled.Token),"Preparing a brush respects cancellation before allocating its stencil");}
        reject(()=>restore.AttachArtwork(new PaintImage(2,1,new byte[8])),"Brush preview rejects artwork with mismatched stencil dimensions");
        var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1);var mesh=new PaintMesh([
            -.5f,.5f,0,0,0,1,0,0,1, .5f,.5f,0,0,0,1,1,0,1, -.5f,-.5f,0,0,0,1,0,1,1,
            .5f,.5f,0,0,0,1,1,0,1, .5f,-.5f,0,0,0,1,1,1,1, -.5f,-.5f,0,0,0,1,0,1,1]);
        var hit=new SurfaceHit(new(.25f,0,0),Vector3.UnitZ,new(.75f,.5f),1,1);var layer=new PaintLayer{Kind=LayerKind.Sticker,Asset=path,Effects=recipe,Samples=[p]};
        var at=DecalMaskPainting.SourcePoint(mesh,layer,hit,64,64);
        check(DecalMaskPainting.CanPreviewSingleProjection(mesh,layer,64,64),"A single flat decal with unambiguous UVs supports incremental GPU mask preview");
        check(!DecalMaskPainting.CanPreviewSingleProjection(mesh,layer,1024,1024),"Large projection eligibility checks stop at their bounded texel budget and use exact preview");
        check(!DecalMaskPainting.CanPreviewSingleProjection(mesh,layer with {Mirror=true},64,64),"Paired masks use exact composition preview instead of overlapping GPU passes");
        var shifted=mesh.NativeVertices.Select((value,index)=>index%9==0?value+.1f:value).ToArray();var overlapping=new PaintMesh(mesh.NativeVertices.Concat(shifted).ToArray());
        check(!DecalMaskPainting.CanPreviewSingleProjection(overlapping,layer,64,64),"Different decal coordinates at a shared UV texel require exact preview");
        var sharedHit=hit with {Position=hit.Position+new Vector3(.1f,0,0),Triangle=3};
        check(DecalMaskPainting.SourcePoint(overlapping,layer,sharedHit,64,64) is {SharedUv:true} shared&&shared.UV==at!.Value.UV,"Brush source picking follows the baked first-covered triangle on shared UVs");
        var pickPixels=Enumerable.Repeat((byte)255,64*64*4).ToArray();pickPixels[(32*64+48)*4+3]=0;var pickArtwork=new PaintImage(64,64,pickPixels);
        check(DecalMaskPainting.SourcePoint(overlapping,layer,sharedHit,64,64,pickArtwork) is {} fallback&&fallback.UV.X>at!.Value.UV.X+.09f,"Brush source picking skips transparent earlier projections like the compositor");
        pickPixels[(32*64+48)*4+3]=1;
        check(DecalMaskPainting.SourcePoint(overlapping,layer with {Opacity=.1f},sharedHit,64,64,pickArtwork) is {} lowAlpha&&lowAlpha.UV.X>at!.Value.UV.X+.09f,"Brush picking matches the compositor's combined low-alpha and layer-opacity coverage threshold");
        check(DecalMaskPainting.CanPreviewSingleProjection(new PaintMesh(mesh.NativeVertices.Concat(mesh.NativeVertices).ToArray()),layer,64,64),"Identical shared projections remain safe for incremental preview");
        check(DecalMaskPainting.CanPreviewSingleProjection(overlapping,layer with {Triangles=[0,1]},64,64),"Preview eligibility respects a decal's isolated surface triangles");
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();reject(()=>DecalMaskPainting.CanPreviewSingleProjection(mesh,layer,64,64,canceled.Token),"Shared UV preview preparation can be canceled");}
        check(at is {} point&&Math.Abs(point.UV.X-.7578125)<.0001&&Math.Abs(point.UV.Y-.5078125)<.0001,"3D mask brush maps the displayed car texel into selected decal coordinates");
        var flipped=DecalMaskPainting.SourcePoint(mesh,layer with {FlipHorizontal=true},hit,64,64);check(flipped?.UV.X==1-at?.UV.X,"Source mapping respects horizontal artwork flipping");
        check(DecalMaskPainting.SourcePoint(mesh,layer with {Locked=true},hit,64,64) is null&&DecalMaskPainting.SourcePoint(mesh,layer with {Visible=false},hit,64,64) is null,"Mask brush cannot edit locked or hidden artwork");
        check(DecalMaskPainting.SourcePoint(mesh,layer with {Triangles=[0]},hit,64,64) is null,"Source mapping respects the selected decal's triangle restriction");
        var behind=layer with {Samples=[p with {Center=new(0,0,1)}]};check(DecalMaskPainting.SourcePoint(mesh,behind,hit,64,64) is null,"Source mapping rejects out-of-depth projections");
        var back=layer with {Samples=[p with {Normal=-Vector3.UnitZ}]};check(DecalMaskPainting.SourcePoint(mesh,back,hit,64,64) is null,"Source mapping rejects back-facing projections");
        var duplicate=layer with {Samples=[p,p]};check(DecalMaskPainting.SourcePoint(mesh,duplicate,hit,64,64)?.Sample==1,"Overlapping copies resolve once to the last applied source projection");
        var pairValues=mesh.NativeVertices.Select((value,index)=>index%9==0?value+2:value).Concat(mesh.NativeVertices.Select((value,index)=>index%9==0?value-2:value)).ToArray();var pairMesh=new PaintMesh(pairValues);var leftHit=hit with {Position=hit.Position-new Vector3(2,0,0),Triangle=3};var pairedLayer=layer with {Samples=[p with {Center=new(2,0,0)}],Mirror=true};
        check(DecalMaskPainting.SourcePoint(pairMesh,pairedLayer,leftHit,64,64) is {Mirrored:true,SharedUv:false},"Mask brush resolves the paired side to the same editable source stencil");
        check(DecalMaskPainting.SourcePoint(pairMesh,pairedLayer with {Mirror=false},leftHit,64,64) is {Mirrored:false,SharedUv:true},"Mask brush identifies source artwork displayed through shared car UVs");
        var baseFile=Path.Combine(folder,"base.tga");FinishMaps.WriteTga(new(64,64,new byte[64*64*4]),baseFile);var asset=Path.Combine(folder,"source.tga");FinishMaps.WriteTga(source,asset);var assetHash=WorkshopService.Hash(asset);
        var document=new LayerDocument{Version=7,BaseAlbedo=baseFile,BaseStructure=baseFile,History=[new("Decal",DateTimeOffset.UtcNow,[layer with {Asset=asset}])]};var original=JsonSerializer.Serialize(document.Layers[0],LayerDocument.Json);
        document.Commit("Erased decal",layers=>layers[0].Effects=DecalMaskPainting.Recipe(next,fade.Blend));var after=JsonSerializer.Serialize(document.Layers[0],LayerDocument.Json);
        check(JsonSerializer.Serialize(document.Layers[0] with {Effects=layer.Effects},LayerDocument.Json)==original,"Mask commits preserve source, placement, opacity and all non-effect decal metadata");
        check(document.Undo()&&JsonSerializer.Serialize(document.Layers[0],LayerDocument.Json)==original&&document.Redo()&&JsonSerializer.Serialize(document.Layers[0],LayerDocument.Json)==after,"One mask stroke is fully undoable and redoable with its original decal metadata");
        var editor=Path.Combine(folder,"editor.json");document.Save(editor);var portable=Path.Combine(folder,"painted.flproject");ProjectBundle.Save(new(){Albedo=baseFile,Structure=baseFile,EditorFile=editor},portable);File.Delete(path);File.Delete(next);var project=ProjectBundle.Open(portable,Path.Combine(folder,"recovered"));var recovered=LayerDocument.Read(project.EditorFile);
        check(recovered.History.All(h=>File.Exists(h.Layers[0].Effects!.StencilFile))&&recovered.Compose(mesh,project).Albedo.Pixels.Length==64*64*4&&WorkshopService.Hash(asset)==assetHash,"Portable history recovers both immutable brush stencils and retains source image bytes");
    }
}
