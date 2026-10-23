using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;

internal static class ArtworkPointChecks
{
    public static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        float[] Quad(float x=0, float normal=1)
        {
            var values=new List<float>();
            foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1)})
                values.AddRange([p.X+x,p.Y,0,0,0,normal,(p.X+1)/2,(1-p.Y)/2,1]);
            return values.ToArray();
        }
        var mesh=new PaintMesh([..Quad(),..Quad(5)]);
        var plane=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,2);
        var a=new PaintLayer{Name="Bottom logo",Kind=LayerKind.Sticker,Asset="solid",Samples=[plane]};
        var b=a with {Id=Guid.NewGuid(),Name="Number panel"};
        var c=a with {Id=Guid.NewGuid(),Name="Top lettering",Asset="half"};
        var solid=new PaintImage(2,1,[0,255,0,255,0,255,0,255]);
        var half=new PaintImage(2,1,[255,0,0,0,255,0,0,255]);
        PaintImage Image(string name)=>name=="half"?half:solid;
        var hit=mesh.Raycast(new(.5f,0,2),-Vector3.UnitZ)!.Value;
        IReadOnlyList<DecalSelection> All(params PaintLayer[] layers)=>DecalEditing.PickAll(mesh,layers,hit,64,64,Image);
        var stack=new[]{a,b,c};var before=JsonSerializer.Serialize(stack,LayerDocument.Json);
        var found=All(stack);
        check(found.Select(s=>s.LayerId).SequenceEqual(new[]{c.Id,b.Id,a.Id}),"Point chooser finds opaque covered decals in exact top-to-bottom layer order");
        check(found[0]==DecalEditing.Pick(mesh,stack,hit,64,64,Image),"Chooser's first editable result is exactly the ordinary direct pick");
        check(JsonSerializer.Serialize(stack,LayerDocument.Json)==before,"Listing overlapping decals leaves every source layer and projection unchanged");
        var reads=new List<string>();
        var top=DecalEditing.Pick(mesh,stack,hit,64,64,name=>{reads.Add(name);return Image(name);});
        check(top?.LayerId==c.Id&&reads.SequenceEqual(new[]{"half"}),"Ordinary picking stops after its first hit without decoding covered sources");
        var left=mesh.Raycast(new(-.5f,0,2),-Vector3.UnitZ)!.Value;
        check(DecalEditing.PickAll(mesh,stack,left,64,64,Image).Select(s=>s.LayerId).SequenceEqual(new[]{b.Id,a.Id}),"Chooser excludes transparent texels rather than listing decal bounding boxes");
        foreach(var invalid in new[]{c with {Visible=false},c with {Opacity=0},c with {Opacity=.001f},c with {Kind=LayerKind.Color},c with {Kind=LayerKind.Surface},c with {Triangles=[3]},c with {FlipHorizontal=true}})
            check(All(a,invalid).Select(s=>s.LayerId).SequenceEqual(new[]{a.Id}),"Chooser obeys visibility, coverage, type, panel and flipped-alpha restrictions: "+invalid.Kind+invalid.Opacity+invalid.FlipHorizontal+invalid.Triangles.Length);
        var locked=c with {Locked=true};
        check(All(a,locked).Single().LayerId==a.Id,"Default point queries keep ordinary locked-layer protection");
        check(DecalEditing.PickAll(mesh,[a,locked],hit,64,64,Image,includeLocked:true).Select(s=>s.LayerId).SequenceEqual(new[]{c.Id,a.Id}),"Explicit chooser queries can identify a locked decal without unlocking it");
        var shared=mesh.Raycast(new(5.5f,0,2),-Vector3.UnitZ)!.Value;
        check(DecalEditing.PickAll(mesh,stack,shared,64,64,Image).All(s=>s.SharedUv),"All covered candidates identify artwork seen through shared game UVs");
        var placements=c with {Samples=[plane,plane,plane],Mirror=true};var samples=All(a,placements);
        check(samples.Count==2&&samples[0].LayerId==c.Id&&samples[0].Sample==2,"Multiple samples, shared triangles and paired passes produce one chooser row per layer");
        check(samples[0]==DecalEditing.Pick(mesh,[a,placements],hit,64,64,Image),"Chooser retains the ordinary sample and paired-side editing frame");
        float[] Side(float x,float nx)
        {
            var values=new List<float>();foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1)})
                values.AddRange([x,p.Y,p.X,nx,0,0,(p.X+1)/2,(1-p.Y)/2,1]);return values.ToArray();
        }
        var sides=new PaintMesh([..Side(1,1),..Side(-1,-1)]);var opposite=sides.Raycast(new(-3,0,.4f),Vector3.UnitX)!.Value;
        var paired=a with {Mirror=true,Samples=[new(Vector3.UnitX,Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY,2,2)]};
        var pairedPick=DecalEditing.PickAll(sides,[paired],opposite,64,64,Image).Single();
        check(pairedPick.Mirrored&&!pairedPick.SharedUv,"A genuine opposite-side-only hit retains the mirrored editing frame in the chooser");
        check(pairedPick==DecalEditing.Pick(sides,[paired],opposite,64,64,Image),"Opposite-side chooser and ordinary picking select the same paired sample");
        var decoded=0;var many=Enumerable.Range(0,128).Select(i=>a with {Id=Guid.NewGuid()}).ToArray();
        check(DecalEditing.PickAll(mesh,many,hit,64,64,_=>{decoded++;return solid;}).Count==128&&decoded==1,"A covered stack shares decoded source pixels within its bounded query cache");
        var irrelevant=a with {Samples=[plane with {Center=new(20,0,0)}]};
        check(DecalEditing.PickAll(mesh,[irrelevant],hit,64,64,_=>throw new InvalidOperationException("Off-point artwork decoded")).Count==0,"Geometry rejection avoids decoding unrelated full-resolution artwork");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        reject(()=>DecalEditing.PickAll(mesh,stack,hit,64,64,Image,cancellation:canceled.Token),"Canceled point queries stop before reading any artwork");
        using var during=new CancellationTokenSource();
        reject(()=>DecalEditing.PickAll(mesh,stack,hit,64,64,_=>{during.Cancel();return solid;},cancellation:during.Token),"Point-query cancellation also interrupts in-flight image preparation");
        foreach(var mask in new[]{DecalMask.Box,DecalMask.Oval})
        {
            var masked=c with {Effects=new(){Mask=mask,Width=.2f,Height=.2f,Feather=0}};
            check(All(a,masked).Count==1&&All(a,masked)[0].LayerId==a.Id,"Source-space stencil holes exclude covered candidates: "+mask);
        }
        var empty=mesh.Raycast(new(.5f,0,-2),Vector3.UnitZ)!.Value;
        var back=new PaintMesh(Quad(normal:-1));
        check(DecalEditing.PickAll(back,stack,empty,64,64,Image).Count==0,"Chooser respects authored facing limits even when a ray hits the panel");
        var tiny=c with {Asset="tiny",Opacity=.1f};
        check(DecalEditing.PickAll(mesh,[a,tiny],hit,64,64,name=>name=="tiny"?new(1,1,[255,255,255,1]):solid).Count==1,"Chooser uses the same combined source-alpha and layer-opacity threshold as ordinary picking");
        check(All().Count==0,"An empty artwork stack produces an empty chooser result");
        reject(()=>DecalEditing.PickAll(mesh,stack,hit,0,64,Image),"Chooser rejects invalid rendered texture dimensions");
        var group=Guid.NewGuid();var grouped=new[]{a with {GroupId=group},b with {GroupId=group},c};var groupedHits=All(grouped);
        check(groupedHits.Count==3&&LayerSets.ExpandGroups(grouped,[groupedHits[1].LayerId]).ToHashSet().SetEquals(new[]{a.Id,b.Id}),"Chooser keeps individual rows while whole-group selection expands the actual chosen member");
        var doc=new LayerDocument();doc.Commit("Fixture",layers=>layers.AddRange(stack));var original=JsonSerializer.Serialize(doc,LayerDocument.Json);
        _=DecalEditing.PickAll(mesh,doc.Layers,hit,64,64,Image,true);
        check(JsonSerializer.Serialize(doc,LayerDocument.Json)==original,"Choosing candidates adds no checkpoint and preserves complete history");
    }
}
