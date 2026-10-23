using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class ArtworkRepeatChecks
{
    public static void Run(string temp,string repo,Action<bool,string> check,Action<Action,string> reject)
    {
        var root=Path.Combine(temp,"repeat-artwork");Directory.CreateDirectory(root);
        var asset=Path.Combine(root,"badge.tga");var surface=Path.Combine(root,"surface.tga");
        FinishMaps.WriteTga(new(32,32,Enumerable.Range(0,4096).Select(i=>(byte)(i%4==3?255:30)).ToArray()),asset);
        FinishMaps.WriteTga(new(128,128,Enumerable.Range(0,65536).Select(i=>(byte)(i%4==3?255:30)).ToArray()),surface);
        var color=Path.Combine(root,"base.tga");FinishMaps.WriteTga(new(128,128,Enumerable.Range(0,65536).Select(i=>(byte)(i%4==3?0:12)).ToArray()),color);
        var project=new LiveryProject{CarIndex=0,Albedo=color,Structure=surface};var group=Guid.NewGuid();var role=new SharedDesignColor(Guid.NewGuid(),"Accent","#21DCC1");
        var source=new PaintLayer{Kind=LayerKind.Sticker,Name="Sponsor",Asset=asset,TextArtwork=new(){Text="37",Color=role.Hex},ColorLinks=new(){["text.fill"]=role},Effects=new(){Mask=DecalMask.Oval},Opacity=.6f,Gloss=.2f,Metalness=.7f,ScaleX=1.1f,ScaleY=.8f,Rotation=12,OffsetRight=.02f,GroupId=group,GroupName="Badge",Triangles=[0,1],Samples=[new(new(-.2f,0,0),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,.2f,.12f)]};
        var patch=new PaintLayer{Kind=LayerKind.Surface,Name="Matte patch",Gloss=.1f,Metalness=.2f,GroupId=group,GroupName="Badge",Samples=[new(new(.08f,0,0),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,.12f,.1f)]};
        string Json(object value)=>JsonSerializer.Serialize(value,LayerDocument.Json);
        LayerDocument Document(params PaintLayer[] layers){var d=LayerDocument.Create(project,Path.Combine(root,Guid.NewGuid().ToString("N")));d.SeamPadding=0;d.Commit("Source",all=>all.AddRange(layers));return d;}
        bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.0001f;
        foreach(var mode in Enum.GetValues<RepeatArrangement>())foreach(var centered in new[]{true,false})foreach(var mirrored in new[]{false,true})
        {
            var doc=Document(source,patch);var before=Json(doc);var selected=doc.Layers.ToArray();var frame=LayerSets.Frame(LayerSets.Display(selected,mirrored));
            var options=new ArtworkRepeatOptions{Arrangement=mode,Count=6,Columns=3,Rows=2,Gap=.04f,RowGap=.07f,Radius=.4f,CenterPattern=centered};
            var plan=ArtworkRepeats.Create(doc,selected.Select(l=>l.Id),options,mirrored);check(Json(doc)==before,"Repeat preview leaves source and full history unchanged: "+mode+centered+mirrored);
            plan.Apply(doc);check(doc.Layers.Count==12&&doc.Cursor==2&&plan.CopyIds.Count==10&&plan.PatternIds.Count==12,"Repeat creates all members in one checkpoint: "+mode+centered+mirrored);
            var copies=doc.Layers.Chunk(2).Select(l=>LayerSets.Display(l,mirrored)).ToArray();var frames=copies.Select(l=>LayerSets.Frame(l)).ToArray();
            check(doc.Layers.Select(l=>l.Id).Distinct().Count()==12&&copies.Select(l=>l[0].GroupId).Distinct().Count()==6&&copies.All(l=>l[0].GroupId==l[1].GroupId),"Each repetition has independent layer and group identities: "+mode+centered+mirrored);
            if(centered)check(Near(frames.Select(p=>p.Center).Aggregate(Vector3.Zero,(a,b)=>a+b)/frames.Length,frame.Center),"Centered repeat preserves the pattern's overall center: "+mode+mirrored);
            else check(Json(doc.Layers.Take(2).ToArray())==Json(selected),"First-anchored repeat preserves original layers exactly: "+mode+mirrored);
            if(mode==RepeatArrangement.Line)check(Near(frames[1].Center-frames[0].Center,frame.Right*(frame.Width+.04f)),"Line spacing includes actual rotated group bounds and requested gap: "+centered+mirrored);
            if(mode==RepeatArrangement.Grid)check(Near(frames[1].Center-frames[0].Center,frame.Right*(frame.Width+.04f))&&Near(frames[3].Center-frames[0].Center,-frame.Up*(frame.Height+.07f)),"Grid rows and columns retain independent physical gaps: "+centered+mirrored);
            if(mode==RepeatArrangement.Circle)
            {
                var center=centered?frame.Center:frame.Center-frame.Right*.4f;
                check(frames.All(p=>Math.Abs(Vector3.Distance(p.Center,center)-.4f)<.0001f)&&frames.Select(p=>p.Center).Distinct().Count()==6,"Circle uses a fixed radius and no duplicate final copy: "+centered+mirrored);
            }
            check(doc.Layers.Where(l=>l.Kind==LayerKind.Sticker).All(l=>l.TextArtwork==source.TextArtwork&&Json(l.Effects!)==Json(source.Effects!)&&l.Asset==asset&&l.ColorLinks["text.fill"]==role&&l.Opacity==.6f&&l.Gloss==.2f&&l.Metalness==.7f&&l.Triangles.SequenceEqual(new[]{0,1})),"Copies retain recipes masks pigment links finish and restrictions: "+mode+centered+mirrored);
            var after=Json(doc.Layers);check(doc.Undo()&&Json(doc.Layers)==Json(selected)&&doc.Redo()&&Json(doc.Layers)==after,"Repeat Undo/Redo restores exact editable state: "+mode+centered+mirrored);
            reject(()=>plan.Apply(doc),"Applied repeat plans cannot add a second copy set");
        }
        var d=Document(source);var ids=new[]{source.Id};
        var stale=ArtworkRepeats.Create(d,ids,new());d.Layers[0].Opacity=.4f;reject(()=>stale.Apply(d),"Repeat plans reject in-place changes even without a new checkpoint");
        d=Document(source);stale=ArtworkRepeats.Create(d,ids,new());d.SeamPadding++;reject(()=>stale.Apply(d),"Repeat plans reject changed texture settings");
        d=Document(source);stale=ArtworkRepeats.Create(d,ids,new());reject(()=>stale.Apply(Document(source)),"Repeat plans cannot apply to a different project instance");
        d=Document(source);var anchored=new ArtworkRepeatOptions{Count=3,CenterPattern=false,Direction=90,KeepPanels=false};var anchoredPlan=ArtworkRepeats.Create(d,ids,anchored);anchoredPlan.Apply(d);
        check(d.Layers.Skip(1).All(l=>l.Triangles.Length==0)&&d.Layers[0].Triangles.Length==2,"Releasing repeat panel restrictions preserves an anchored original");
        check(Math.Abs(Vector3.Dot(d.Layers[1].Samples[0].Center-d.Layers[0].Transform(d.Layers[0].Samples[0]).Center,LayerSets.Frame([source]).Right))<.0001f,"Vertical line direction advances along the selection's up axis");
        var prior=Json(d.History[1]);var sibling=Json(d.Layers[2]);d.Layers[1].ColorLinks.Clear();d.Layers[1].Samples.Clear();
        check(Json(d.History[1])==prior&&Json(d.Layers[2])==sibling,"Mutating one applied copy cannot change a sibling or retained source checkpoint");
        foreach(var rotate in new[]{false,true})
        {
            var circleSource=source with {ScaleX=1,ScaleY=1,Scale=1,Rotation=0,OffsetRight=0,OffsetUp=0};d=Document(circleSource);var circleFrame=LayerSets.Frame(d.Layers);
            var circle=ArtworkRepeats.Create(d,ids,new(){Arrangement=RepeatArrangement.Circle,Count=4,Radius=.3f,RotateAroundCircle=rotate});circle.Apply(d);
            var atQuarter=d.Layers[1].Transform(d.Layers[1].Samples[0]);
            check(Near(atQuarter.Right,rotate?circleFrame.Up:circleFrame.Right)&&Near(atQuarter.Up,rotate?-circleFrame.Right:circleFrame.Up),"Circle orientation control changes actual mapped artwork axes: "+rotate);
        }
        foreach(var mirrorView in new[]{false,true})
        {
            d=Document(source with {Mirror=true},patch);var originalPair=Json(d.Layers);var originalPairMaps=d.Compose(Roof(),project);
            var paired=ArtworkRepeats.Create(d,[source.Id,patch.Id],new(){Count=3,Gap=.02f},mirrorView);paired.Apply(d);var pairedMaps=d.Compose(Roof(),project);
            check(d.Layers.Where(l=>l.Kind==LayerKind.Sticker).All(l=>l.Mirror)&&d.Layers.Where(l=>l.IsMaterial).All(l=>!l.Mirror),"Repeat preserves actual mixed paired and independent layer flags: "+mirrorView);
            check(!pairedMaps.Albedo.Pixels.SequenceEqual(originalPairMaps.Albedo.Pixels)&&d.Undo()&&Json(d.Layers)==originalPair&&d.Compose(Roof(),project).Albedo.Pixels.SequenceEqual(originalPairMaps.Albedo.Pixels)&&d.Redo()&&d.Compose(Roof(),project).Structure.Pixels.SequenceEqual(pairedMaps.Structure.Pixels),"Paired repeats compose both sides and Undo/Redo exact fixture maps: "+mirrorView);
        }
        foreach(var centered in new[]{true,false})
        {
            d=Document(source,patch);var originalFrame=LayerSets.Frame(d.Layers);var scaled=ArtworkRepeats.Create(d,[source.Id,patch.Id],new(){Count=3,Scale=.33f,Gap=.03f,CenterPattern=centered});scaled.Apply(d);
            var firstFrame=LayerSets.Frame(d.Layers.Take(2).ToArray());var secondFrame=LayerSets.Frame(d.Layers.Skip(2).Take(2).ToArray());
            check(Math.Abs(firstFrame.Width-originalFrame.Width*.33f)<.0001f&&Math.Abs(firstFrame.Height-originalFrame.Height*.33f)<.0001f&&Near(secondFrame.Center-firstFrame.Center,originalFrame.Right*(originalFrame.Width*.33f+.03f)),"Copy size scales every member and recomputes physical gap: "+centered);
            if(!centered)check(Near(firstFrame.Center,originalFrame.Center),"Resizing an anchored pattern keeps its first copy's center in place");
        }
        var offsetSource=source with {Scale=1,ScaleX=1,ScaleY=1,Rotation=0,OffsetRight=0,OffsetUp=0,Effects=null,ColorLinks=[],TextArtwork=null,Samples=[new(new(0,.05f,0),Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ,.2f,.12f)]};
        var contact=Document(offsetSource);var contactPlan=ArtworkRepeats.Create(contact,ids,new(){Scale=.33f,Count=3},mesh:Roof());contactPlan.Apply(contact);
        check(contact.Layers.All(l=>Math.Abs(l.Samples[0].Center.Y)<.0001f),"Shrinking repeats reanchors offset projection planes onto paintable body geometry");
        check(contact.Compose(Roof(),project).Albedo.Pixels.Where((_,i)=>i%4==3).Any(a=>a>0),"Surface-anchored small repeats remain present in composed texture pixels");
        var glassValues=(float[])Roof().NativeVertices.Clone();for(var v=8;v<glassValues.Length;v+=9)glassValues[v]=0;
        var blocked=Document(offsetSource);var blockedPlan=ArtworkRepeats.Create(blocked,ids,new(){Scale=.33f,Count=3},mesh:new(glassValues));blockedPlan.Apply(blocked);
        check(blocked.Layers.All(l=>Math.Abs(l.Samples[0].Center.Y-.05f)<.0001f),"Repeat anchoring rejects non-paintable occluders instead of snapping through glass");
        var reversed=(float[])Roof().NativeVertices.Clone();for(var v=4;v<reversed.Length;v+=9)reversed[v]=-1;
        var backFacing=Document(offsetSource);var backPlan=ArtworkRepeats.Create(backFacing,ids,new(){Scale=.33f,Count=3},mesh:new(reversed));backPlan.Apply(backFacing);
        check(backFacing.Layers.All(l=>Math.Abs(l.Samples[0].Center.Y-.05f)<.0001f),"Repeat anchoring tests authored face normals rather than ray-facing flipped normals");
        var sideValues=new List<float>();foreach(var (x,nx) in new[]{(-1f,-1f),(1.08f,1f)})foreach(var v in new[]{(-1f,-1f),(1f,-1f),(1f,1f),(-1f,-1f),(1f,1f),(-1f,1f)})sideValues.AddRange([x,v.Item1,v.Item2,nx,0,0,(v.Item2+1)/2,(v.Item1+1)/2,1]);
        var pairSource=offsetSource with {Mirror=true,Triangles=[],Samples=[new(new(-1.02f,0,0),-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY,.2f,.12f)]};var asymmetrical=Document(pairSource);
        var asymmetricalPlan=ArtworkRepeats.Create(asymmetrical,ids,new(){Scale=.33f,Count=2},mirrored:true,mesh:new(sideValues.ToArray()));asymmetricalPlan.Apply(asymmetrical);
        check(asymmetrical.Layers.All(l=>{var p=l.Transform(l.Samples[0]);var other=PaintLayer.Reflected(p);return p.Map(new(-1,p.Center.Y,p.Center.Z),-Vector3.UnitX,out _)&&other.Map(new(1.08f,other.Center.Y,other.Center.Z),Vector3.UnitX,out _);}),"Asymmetric paired repeats anchor both actual sides while editing the reflected side");
        foreach(var options in new ArtworkRepeatOptions[]{new(){Count=1},new(){Columns=32,Rows=32,Arrangement=RepeatArrangement.Grid},new(){Gap=float.NaN},new(){Radius=0},new(){Direction=float.PositiveInfinity},new(){Arrangement=(RepeatArrangement)99}})reject(()=>ArtworkRepeats.Create(Document(source),ids,options),"Invalid repeat options are rejected before mutation");
        reject(()=>ArtworkRepeats.Create(Document(source with {Locked=true}),ids,new()),"Locked artwork cannot be repeated accidentally");
        reject(()=>ArtworkRepeats.Create(Document(source with {Visible=false}),ids,new()),"Hidden artwork explains why it cannot be previewed");
        reject(()=>ArtworkRepeats.Create(Document(source),[Guid.NewGuid()],new()),"Missing repeat source IDs are rejected");
        var crowded=Document(Enumerable.Range(0,512).Select(_=>patch with {Id=Guid.NewGuid()}).ToArray());var crowdedBefore=Json(crowded);
        reject(()=>ArtworkRepeats.Create(crowded,[crowded.Layers[0].Id],new()),"Repeat enforces the document layer budget before generating copies");check(Json(crowded)==crowdedBefore,"Rejected layer-budget repeats preserve history");
        var heavy=source with {Samples=Enumerable.Repeat(source.Samples[0],20000).ToList()};var heavyDoc=Document(heavy);
        reject(()=>ArtworkRepeats.Create(heavyDoc,ids,new(){Count=64}),"Repeat bounds brush expansion before cloning a large selection");
        using var canceled=new CancellationTokenSource();canceled.Cancel();reject(()=>ArtworkRepeats.Create(Document(source),ids,new(),cancellation:canceled.Token),"Canceled repeat preparation creates no plan");
        d=Document(source,patch);var baseMaps=d.Compose(Roof(),project);var repeat=ArtworkRepeats.Create(d,[source.Id,patch.Id],new(){Arrangement=RepeatArrangement.Grid,Columns=2,Rows=2,Gap=.03f,RowGap=.04f});repeat.Apply(d);var repeatedMaps=d.Compose(Roof(),project);
        check(!baseMaps.Albedo.Pixels.SequenceEqual(repeatedMaps.Albedo.Pixels)&&!baseMaps.Structure.Pixels.SequenceEqual(repeatedMaps.Structure.Pixels),"Actual repeat composition paints new color and material pixels");
        var editor=Path.Combine(root,"editor.json");d.Save(editor);var bundle=Path.Combine(root,"repeat.flproject");ProjectBundle.Save(project with {EditorFile=editor},bundle);var reopened=ProjectBundle.Open(bundle,Path.Combine(root,"reopened"));var loaded=LayerDocument.Read(reopened.EditorFile);var maps=loaded.Compose(Roof(),reopened);
        check(loaded.Layers.Count==8&&maps.Albedo.Pixels.SequenceEqual(repeatedMaps.Albedo.Pixels)&&maps.Structure.Pixels.SequenceEqual(repeatedMaps.Structure.Pixels)&&loaded.Undo()&&loaded.Layers.Count==2,"Portable saving retains repeated artwork exact maps and one-step recovery");
        if(OperatingSystem.IsWindows())
        {
            var actualProject=ProjectBundle.Open(Path.Combine(repo,"examples/OppositeSideStudioDemo.flproject"),Path.Combine(root,"real-car"));var actual=LayerDocument.Read(actualProject.EditorFile);
            var model=PaintMesh.Read(Path.Combine(repo,"LiveryStudio/artifacts/models/00.flsm"));var badge=actual.Layers.Last(l=>l.Name.Contains("VectorGarageBadge"));
            foreach(var arrangement in Enum.GetValues<RepeatArrangement>())
            {
                var snapshot=JsonSerializer.Deserialize<LayerDocument>(Json(actual),LayerDocument.Json)!;var realPlan=ArtworkRepeats.Create(snapshot,[badge.Id],new(){Arrangement=arrangement,Count=arrangement==RepeatArrangement.Circle?6:3,Scale=.33f,Radius=.3f},mesh:model);realPlan.Apply(snapshot);
                using var view=new StudioPhotography(Path.Combine(repo,"LiveryStudio/artifacts/photo-runtime"),model,snapshot,actualProject,new());
                var picture=view.Capture(StudioCamera.Fit(StudioCamera.Geometry(model),0,MathF.PI/2,16f/9),1280,720);FinishMaps.WriteTga(picture,Path.Combine(temp,"repeat-real-"+arrangement+".tga"));
                check(snapshot.Layers.Count==realPlan.TotalLayers&&picture.Pixels.Where((_,i)=>i%4!=3).Distinct().Count()>100,"Actual Grosse pointe repeat plan and GPU render: "+arrangement);
            }
        }
    }
    private static PaintMesh Roof()=>new([-1,0,-1,0,1,0,0,1,1,1,0,-1,0,1,0,1,1,1,1,0,1,0,1,0,1,0,1,-1,0,-1,0,1,0,0,1,1,1,0,1,0,1,0,1,0,1,-1,0,1,0,1,0,0,0,1]);
}
