using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class OppositeSideChecks
{
    public static void Run(string temp,string repo,Action<bool,string> check,Action<Action,string> reject)
    {
        var folder=Path.Combine(temp,"opposite-side");Directory.CreateDirectory(folder);
        var asset=Path.Combine(folder,"letter.tga");var stencil=Path.Combine(folder,"stencil.tga");
        FinishMaps.WriteTga(new(3,2,[255,20,10,255,20,255,40,180,10,20,255,255,255,255,20,255,255,20,255,160,20,255,255,255]),asset);
        FinishMaps.WriteTga(new(2,1,[255,255,255,230,255,255,255,120]),stencil);
        var baseColor=Path.Combine(folder,"base.tga");var surface=Path.Combine(folder,"surface.tga");
        FinishMaps.WriteTga(new(64,32,Enumerable.Range(0,64*32*4).Select(i=>(byte)(i%4==3?255:40)).ToArray()),baseColor);
        FinishMaps.WriteTga(new(64,32,Enumerable.Range(0,64*32*4).Select(i=>(byte)(i%4==2?255:90)).ToArray()),surface);
        var project=new LiveryProject{CarIndex=0,Name="Opposite sides",Albedo=baseColor,Structure=surface};
        LayerDocument Document(params PaintLayer[] layers){var doc=LayerDocument.Create(project,Path.Combine(folder,Guid.NewGuid().ToString("N")));doc.SeamPadding=0;doc.Commit("Original artwork",all=>all.AddRange(layers));return doc;}
        string Json(object value)=>JsonSerializer.Serialize(value,LayerDocument.Json);
        bool Same(CompositeMaps a,CompositeMaps b)=>a.Albedo.Pixels.SequenceEqual(b.Albedo.Pixels)&&a.Structure.Pixels.SequenceEqual(b.Structure.Pixels);
        var mesh=Sides();var group=Guid.NewGuid();var role=new SharedDesignColor(Guid.NewGuid(),"Team red","#FF0000");
        var source=new PaintLayer{Kind=LayerKind.Sticker,Name="Sponsor",Asset=asset,TextArtwork=new(){Text="TEAM",Color=role.Hex},ColorLinks=new(){["text.fill"]=role},GroupId=group,GroupName="Sponsor badge",Opacity=.7f,Scale=1.2f,ScaleX=1.1f,ScaleY=.8f,OffsetRight=.1f,OffsetUp=.03f,Rotation=12,FlipVertical=true,Gloss=.23f,Metalness=.8f,Triangles=[0,1],Effects=new(){Mask=DecalMask.Image,StencilFile=stencil,Blend=DecalBlend.Screen},Samples=[new(new(-1,.1f,-.35f),-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY,.7f,.4f)]};
        var shape=source with {Id=Guid.NewGuid(),Name="Background",Kind=LayerKind.Color,TextArtwork=null,ColorLinks=[],Effects=null,Asset="",Samples=[new(new(-1,-.2f,.3f),-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY,.3f,.2f)]};
        var doc=Document(source,shape);var original=Json(doc);var maps=doc.Compose(mesh,project);
        var readable=OppositeSideArtwork.Create(doc,mesh,[source.Id,shape.Id],OppositeSideMode.Readable);
        var copies=readable.Layers.Where(l=>readable.CopyIds.Contains(l.Id)).ToArray();
        check(Json(doc)==original&&readable.Layers.Take(2).Select(Json).SequenceEqual(doc.Layers.Select(Json)),"Opposite-side planning leaves original layers and history untouched");
        var isolated=OppositeSideArtwork.Create(doc,mesh,[source.Id],OppositeSideMode.Readable);isolated.Layers[0].ColorLinks.Clear();isolated.Layers[0].Samples.Clear();
        check(Json(doc)==original,"Editing a proposed plan cannot mutate the source document or its retained recipe links");
        check(copies.Length==2&&copies.All(l=>!l.Mirror&&!l.Locked)&&copies[0].GroupId==copies[1].GroupId&&copies[0].GroupId!=group,"Opposite-side group copies get independent layer/group identities");
        check(copies[0].TextArtwork==source.TextArtwork&&Json(copies[0].Effects!)==Json(source.Effects!)&&copies[0].Opacity==source.Opacity&&copies[0].Gloss==source.Gloss&&copies[0].Metalness==source.Metalness&&copies[0].ColorLinks["text.fill"]==role,"Opposite-side copies retain editable recipe, stencil, finish, opacity and named color roles");
        check(copies.All(l=>l.Triangles.SequenceEqual(new[]{2,3})),"Panel restrictions map to the actual opposite body triangles");
        var frame=LayerSets.Frame(doc.Layers);var target=PaintLayer.Reflected(frame);
        var old=source.Transform(source.Samples[0]);var next=copies[0].Transform(copies[0].Samples[0]);
        var oldOffset=old.Center-frame.Center;var nextOffset=next.Center-target.Center;
        check(Math.Abs(Vector3.Dot(oldOffset,frame.Right)-Vector3.Dot(nextOffset,target.Right))<.0001f&&Math.Abs(Vector3.Dot(oldOffset,frame.Up)-Vector3.Dot(nextOffset,target.Up))<.0001f&&Vector3.Dot(next.Normal,Vector3.UnitX)>.99f,"Readable copying preserves the whole group's local layout and outward facing");
        var uvPoint=LayerSets.Corner(old,new(.2f,-.1f));var mappedPoint=target.Center+target.Right*Vector3.Dot(uvPoint-frame.Center,frame.Right)+target.Up*Vector3.Dot(uvPoint-frame.Center,frame.Up)+target.Normal*Vector3.Dot(uvPoint-frame.Center,frame.Normal);
        check(old.Map(uvPoint,old.Normal,out var oldUv)&&next.Map(mappedPoint,next.Normal,out var newUv)&&Vector2.Distance(oldUv,newUv)<.0001f,"Readable group copying preserves exact artwork coordinates, including rotated and stretched lettering");
        var mirror=OppositeSideArtwork.Create(doc,mesh,[source.Id,shape.Id],OppositeSideMode.Mirrored);var mirrored=mirror.Layers.First(l=>mirror.CopyIds.Contains(l.Id));var reflectedPoint=new Vector3(-uvPoint.X,uvPoint.Y,uvPoint.Z);var mirroredProjection=mirrored.Transform(mirrored.Samples[0]);
        check(mirroredProjection.Map(reflectedPoint,mirroredProjection.Normal,out var mirrorUv)&&Vector2.Distance(oldUv,mirrorUv)<.0001f&&mirroredProjection.Right.Z<0&&mirrored.FlipHorizontal!=source.FlipHorizontal,"Mirrored design maps each physical reflected point to its exact original image coordinate");
        check(Vector3.Dot(Vector3.Normalize(Vector3.Cross(mirroredProjection.Right,mirroredProjection.Up)),mirroredProjection.Normal)>.99f,"Mirrored copies retain a proper outward editing frame and expose the image flip through existing controls");
        var preview=doc.Snapshot("Preview",readable.Layers);var previewMaps=preview.Compose(mesh,project);
        check(!Same(maps,previewMaps)&&Same(maps,doc.Compose(mesh,project)),"Opposite-side preview paints the new body side without changing the original document");
        var cursor=doc.Cursor;readable.Apply(doc);check(doc.Cursor==cursor+1&&doc.Layers.Count==4&&Same(previewMaps,doc.Compose(mesh,project)),"Applying the exact opposite-side plan creates one checkpoint with the preview's actual pixels");
        readable.Layers[0].Name="Changed only in the old plan";check(doc.Layers[0].Name==source.Name,"Applied history does not share mutable layer objects with the preview plan");
        check(doc.Undo()&&Same(maps,doc.Compose(mesh,project))&&doc.Redo()&&Same(previewMaps,doc.Compose(mesh,project)),"Opposite-side Undo and Redo restore exact color and surface pixels");
        reject(()=>readable.Apply(doc),"An applied or stale other-side plan cannot overwrite newer history");
        var otherDoc=Document(source,shape);reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[source.Id],OppositeSideMode.Readable).Apply(Document(source)),"Other-side plans are bound to their source document");
        var locked=Document(source with {Locked=true});var lockedPlan=OppositeSideArtwork.Create(locked,mesh,[source.Id],OppositeSideMode.Readable);lockedPlan.Apply(locked);
        check(locked.Layers[0].Locked&&!locked.Layers[1].Locked,"A locked source can be copied without changing its locked original");
        var unmasked=OppositeSideArtwork.Create(otherDoc,mesh,[source.Id],OppositeSideMode.Readable,keepPanels:false);
        check(unmasked.Layers.Last().Triangles.Length==0&&otherDoc.Layers[0].Triangles.Length==2,"Explicitly allowing nearby panels clears only the copy's panel restriction");
        reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[],OppositeSideMode.Readable),"Empty opposite-side selections are rejected");
        reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[Guid.NewGuid()],OppositeSideMode.Readable),"Stale opposite-side selections are rejected");
        reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[source.Id],(OppositeSideMode)99),"Unknown opposite-side orientation is rejected");
        reject(()=>OppositeSideArtwork.ReflectPanels(mesh,[99]),"A foreign panel restriction is rejected instead of painting an arbitrary surface");
        check(OppositeSideArtwork.ReflectPanels(Sides(.02f,true),[0,1]).SequenceEqual(new[]{2,3}),"Panel mapping tolerates slight body asymmetry and different opposite triangulation");
        var front=source with {Rotation=0,Scale=1,ScaleX=1,ScaleY=1,OffsetRight=0,OffsetUp=0,FlipVertical=false,Effects=null,Triangles=[0,1],Samples=[new(new(-1,0,-.5f),-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY,.6f,.5f)]};
        var rear=front with {Id=Guid.NewGuid(),Name="Rear lettering",Triangles=[2,3],Samples=[front.Samples[0] with {Center=new(-1,0,.5f)}]};
        var panelDoc=Document(front,rear);var divided=DividedSides();
        var panelPlan=OppositeSideArtwork.Create(panelDoc,divided,[front.Id,rear.Id],OppositeSideMode.Readable);var panelCopies=panelPlan.Layers.Where(l=>panelPlan.CopyIds.Contains(l.Id)).ToArray();
        check(panelCopies[0].Samples[0].Center.Z>.4f&&panelCopies[0].Triangles.SequenceEqual(new[]{6,7})&&panelCopies[1].Samples[0].Center.Z<-.4f&&panelCopies[1].Triangles.SequenceEqual(new[]{4,5}),"Readable group copying transforms distinct front/rear panel restrictions with their corresponding components");
        var restricted=panelDoc.Snapshot("Restricted copy",panelPlan.Layers).Compose(divided,project);
        var unrestrictedPlan=OppositeSideArtwork.Create(panelDoc,divided,[front.Id,rear.Id],OppositeSideMode.Readable,keepPanels:false);
        check(Same(restricted,panelDoc.Snapshot("Unrestricted copy",unrestrictedPlan.Layers).Compose(divided,project)),"Separate front/rear restrictions do not clip readable grouped artwork on symmetric body geometry");
        var unchanged=Json(otherDoc);
        reject(()=>OppositeSideArtwork.Create(otherDoc,Sides(.5f),[source.Id],OppositeSideMode.Readable),"A missing opposite panel fails before the copy can silently disappear");
        check(Json(otherDoc)==unchanged&&OppositeSideArtwork.Create(otherDoc,Sides(.5f),[source.Id],OppositeSideMode.Readable,keepPanels:false).CopyIds.Length==1,"Unmatched panels leave history intact and the explicit nearby-panel option remains available");
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[source.Id],OppositeSideMode.Readable,cancellation:canceled.Token),"A canceled other-side preview stops before constructing or committing layers");
        }
        var paired=source with {Mirror=true,Triangles=[0,1,2,3]};var pairedDoc=Document(paired,shape);
        var pairedBefore=Json(pairedDoc);var pairedPixels=pairedDoc.Compose(mesh,project);
        reject(()=>OppositeSideArtwork.Create(pairedDoc,mesh,[paired.Id],OppositeSideMode.Readable),"Already paired artwork must be separated instead of double-stamping the other side");
        var split=OppositeSideArtwork.Create(pairedDoc,mesh,[paired.Id,shape.Id],OppositeSideMode.SeparatePaired);split.Apply(pairedDoc);
        check(pairedDoc.Layers.Count==3&&split.CopyIds.Length==1&&pairedDoc.Layers[0].Id==paired.Id&&pairedDoc.Layers.All(l=>!l.Mirror)&&pairedDoc.Layers[^1].Id==shape.Id,"Separating paired sides keeps the original identity, inserts its copy in paint order and leaves unpaired selection members alone");
        check(Same(pairedPixels,pairedDoc.Compose(mesh,project)),"Separating paired masked artwork preserves exact baked color and surface bytes");
        check(pairedDoc.Undo()&&Json(pairedDoc.History[pairedDoc.Cursor])==Json(JsonSerializer.Deserialize<LayerDocument>(pairedBefore,LayerDocument.Json)!.History[1])&&pairedDoc.Redo(),"Separating sides is one persistent undoable operation");
        reject(()=>OppositeSideArtwork.Create(Document(paired with {Locked=true}),mesh,[paired.Id],OppositeSideMode.SeparatePaired),"Separating a locked paired layer requires unlocking the source");
        reject(()=>OppositeSideArtwork.Create(otherDoc,mesh,[source.Id],OppositeSideMode.SeparatePaired),"A separation with no paired layers is rejected");
        var stroke=paired with {GroupId=null,GroupName="",Triangles=[],TextArtwork=null,ColorLinks=[],Rotation=0,OffsetRight=0,OffsetUp=0,Scale=1,ScaleX=1,ScaleY=1,FlipVertical=false,Opacity=.48f,Effects=new(){Blend=DecalBlend.Normal,Mask=DecalMask.Image,StencilFile=stencil},Samples=[new(new(-.2f,0,-.05f),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,1.5f,1.5f),new(new(.15f,0,.1f),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,1.2f,1.4f)]};
        var roof=Roof();var strokeDoc=Document(stroke);var strokePixels=strokeDoc.Compose(roof,project);var strokePlan=OppositeSideArtwork.Create(strokeDoc,roof,[stroke.Id],OppositeSideMode.SeparatePaired);strokePlan.Apply(strokeDoc);
        check(strokeDoc.Layers.Count==4&&strokePlan.CopyIds.Length==2&&strokeDoc.Layers[0].GroupId==strokeDoc.Layers[2].GroupId&&strokeDoc.Layers[1].GroupId==strokeDoc.Layers[3].GroupId&&strokeDoc.Layers[0].GroupId!=strokeDoc.Layers[1].GroupId,"A multi-stamp paired stroke separates into independent grouped sides with original stamp ordering");
        check(Same(strokePixels,strokeDoc.Compose(roof,project)),"Separating overlapping translucent paired stamps is byte-exact across the car centerline");
        var naive=strokeDoc.Snapshot("Incorrect regrouped order",[strokeDoc.Layers[0],strokeDoc.Layers[2],strokeDoc.Layers[1],strokeDoc.Layers[3]]);
        check(!Same(strokePixels,naive.Compose(roof,project)),"The overlap fixture detects the blending error that grouping every copied stamp later would introduce");
        var material=stroke with {Kind=LayerKind.Surface,Asset="",Effects=null,Gloss=.18f,Metalness=.81f};var materialDoc=Document(material);var materialPixels=materialDoc.Compose(roof,project);OppositeSideArtwork.Create(materialDoc,roof,[material.Id],OppositeSideMode.SeparatePaired).Apply(materialDoc);
        check(Same(materialPixels,materialDoc.Compose(roof,project)),"Paired local gloss/metal strokes separate without changing baked material output");
        var heavy=stroke with {Samples=Enumerable.Repeat(stroke.Samples[0],20000).ToList()};var heavyDoc=Document(heavy);var heavyState=Json(heavyDoc);
        reject(()=>OppositeSideArtwork.Create(heavyDoc,roof,[heavy.Id],OppositeSideMode.SeparatePaired),"Exact separation enforces the project layer limit before modifying history");check(Json(heavyDoc)==heavyState,"A rejected over-capacity separation leaves the document unchanged");
        var editor=Path.Combine(folder,"portable-editor.json");strokeDoc.Save(editor);var bundle=Path.Combine(folder,"sides.flproject");ProjectBundle.Save(project with {EditorFile=editor},bundle);
        var reopened=ProjectBundle.Open(bundle,Path.Combine(folder,"reopened"));var reopenedDoc=LayerDocument.Read(reopened.EditorFile);
        check(reopenedDoc.Layers.Count==4&&Same(strokePixels,reopenedDoc.Compose(roof,reopened))&&reopenedDoc.Undo()&&reopenedDoc.Layers.Count==1&&reopenedDoc.Layers[0].Mirror,"Portable projects retain independent side groups and the original paired stroke in Undo history");
        check(WorkshopService.Hash(asset)==WorkshopService.Hash(reopenedDoc.Layers[0].Asset)&&WorkshopService.Hash(stencil)==WorkshopService.Hash(reopenedDoc.Layers[0].Effects!.StencilFile),"Opposite-side operations and portable saving preserve original artwork and stencil bytes");
        for(var car=0;car<27;car++)
        {
            var model=PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models",car.ToString("00")+".flsm"));
            var left=Enumerable.Range(0,model.Vertices.Length/3).Select(t=>
            {
                var a=model.Vertices[t*3];var b=model.Vertices[t*3+1];var c=model.Vertices[t*3+2];
                return (Triangle:t,Center:(a.Position+b.Position+c.Position)/3,Normal:Vector3.Normalize(a.Normal+b.Normal+c.Normal),a.Kind);
            }).Where(t=>t.Kind==1&&t.Center.X<-.15f&&t.Normal.X<-.7f).OrderBy(t=>Math.Abs(t.Center.Z-(model.Minimum.Z+model.Maximum.Z)/2)+Math.Abs(t.Center.Y-(model.Minimum.Y+model.Maximum.Y)/2)).First();
            var panel=MeshEditing.Panel(model,new(left.Center,left.Normal,default,left.Triangle,0));
            var opposite=OppositeSideArtwork.ReflectPanels(model,panel);
            check(opposite.Length>0&&opposite.All(t=>model.Vertices[t*3].Kind==1)&&opposite.Any(t=>(model.Vertices[t*3].Position.X+model.Vertices[t*3+1].Position.X+model.Vertices[t*3+2].Position.X)/3>.15f),$"Opposite-side panel mapping finds real paintable right body geometry: {Contract.Cars[car].Name}");
        }
    }
    private static PaintMesh Sides(float rightOffset=0,bool alternate=false)
    {
        var values=new List<float>();
        void Vertex(float x,float y,float z,float u,float v,float nx)=>values.AddRange([x,y,z,nx,0,0,u,v,1]);
        for(var side=0;side<2;side++)
        {
            var x=side==0?-1:1+rightOffset;var nx=side==0?-1:1;var u=side*.5f;
            var points=new[]{(-1f,-1f,u,1f),(1f,-1f,u,0f),(1f,1f,u+.5f,0f),(-1f,1f,u+.5f,1f)};
            foreach(var index in alternate&&side==1?new[]{0,1,3,1,2,3}:new[]{0,1,2,0,2,3}){var p=points[index];Vertex(x,p.Item1,p.Item2,p.Item3,p.Item4,nx);}
        }
        return new(values.ToArray());
    }
    private static PaintMesh Roof()=>new([-1,0,-1,0,1,0,0,1,1,1,0,-1,0,1,0,1,1,1,1,0,1,0,1,0,1,0,1,-1,0,-1,0,1,0,0,1,1,1,0,1,0,1,0,1,0,1,-1,0,1,0,1,0,0,0,1]);
    private static PaintMesh DividedSides()
    {
        var values=new List<float>();
        for(var side=0;side<2;side++)for(var panel=0;panel<2;panel++)
        {
            var x=side==0?-1:1;var z=(float)panel-1;var u=side*.5f;var v=panel*.5f;
            var points=new[]{(y:-1f,z:z,u:u,v:v+.5f),(y:1f,z:z,u:u,v:v),(y:1f,z:z+1,u:u+.5f,v:v),(y:-1f,z:z+1,u:u+.5f,v:v+.5f)};
            foreach(var index in new[]{0,1,2,0,2,3}){var p=points[index];values.AddRange([x,p.y,p.z,side==0?-1:1,0,0,p.u,p.v,1]);}
        }
        return new(values.ToArray());
    }
}
