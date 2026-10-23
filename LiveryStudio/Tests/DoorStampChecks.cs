using System.Numerics;
using LiveryStudio.Core;

internal static class DoorStampChecks
{
    public static void Run(Action<bool,string> check)
    {
        CheckOutsideOnly(check);
        // Independent door UV islands, with different diagonals and a slightly
        // asymmetric right door. Exact reflected triangle centers never match.
        var values=new List<float>();
        for(var side=0;side<2;side++)
        {
            var corners=new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
            foreach(var i in side==0?new[]{0,1,2,0,2,3}:new[]{0,1,3,1,2,3})
            {
                var p=corners[i];var sign=side==0?-1:1;
                values.AddRange(new[]{sign*(side==0?1:1.003f),p.Y,p.X,(float)sign,0,0,side*.5f+(side==0?p.X+1:1-p.X)*.25f,(1-p.Y)*.5f,1});
            }
        }
        var mesh=new PaintMesh(values.ToArray());
        var paired=MeshEditing.PairedPanels(mesh,[0,1]).ToHashSet();
        check(paired.SetEquals([0,1,2,3]),"Both-door isolation maps asymmetric panels with different triangulation");
        var glyph=new PaintImage(2,2,[255,0,0,255,0,0,255,255,0,255,0,255,255,255,0,255]);
        foreach(var side in new[]{-1,1})
        {
            var painter=new TexturePainter(new(128,64,new byte[128*64*4]));
            var hit=mesh.Raycast(new(side*3,0,0),new(-side,0,0))!.Value;
            var projection=SurfaceProjection.At(hit,new(0,0,-side),1,1,0);
            var reflected=PaintLayer.Reflected(projection);
            painter.Begin();
            var first=painter.Stamp(mesh,projection,glyph,Vector3.One,1,Vector3.Zero,paired);
            var second=painter.Stamp(mesh,reflected,glyph,Vector3.One,1,Vector3.Zero,paired);
            check(first>0&&second>0,$"Stamping from {(side<0?"left":"right")} paints both door UV islands");
            byte[] Pixel(int x,int y)=>painter.Image.Pixels.Skip((y*128+x)*4).Take(4).ToArray();
            check(Pixel(24,24).SequenceEqual(Pixel(88,24))&&Pixel(40,24).SequenceEqual(Pixel(104,24))&&!Pixel(24,24).SequenceEqual(Pixel(40,24)),"Paired doors keep asymmetric lettering upright and readable from either starting side");
            var expected=painter.Image.Pixels.ToArray();painter.Commit();
            check(painter.Undo()&&painter.Image.Pixels.All(b=>b==0)&&painter.Redo()&&painter.Image.Pixels.SequenceEqual(expected),"One undo and redo restore the complete two-door stamp");
        }
    }

    private static void CheckOutsideOnly(Action<bool,string> check)
    {
        var values=new List<float>();
        void Panel(float x,float z,float u0,float u1)
        {
            foreach(var p in new[]{new Vector2(-.4f,-.4f),new(.4f,-.4f),new(.4f,.4f),new(-.4f,-.4f),new(.4f,.4f),new(-.4f,.4f)})
                values.AddRange(new[]{x,p.Y,p.X+z,1f,0,0,float.Lerp(u0,u1,(p.X+.4f)/.8f),(.4f-p.Y)/.8f,1});
        }
        Panel(1,0,0,.5f);Panel(.85f,0,.5f,1);Panel(1,-2,.5f,1);
        var mesh=new PaintMesh(values.ToArray());var projection=new SurfaceProjection(new(1,0,0),Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY,.45f,.45f);
        var loose=new TexturePainter(new(128,64,new byte[128*64*4]));loose.Stamp(mesh,projection,null,Vector3.UnitX,1,Vector3.Zero);
        var safe=new TexturePainter(new(128,64,new byte[128*64*4]));safe.Stamp(mesh,SimplePlacement.OnSurface(new()).Transform(projection),null,Vector3.UnitX,1,Vector3.Zero);
        bool Painted(TexturePainter p,int from,int to)=>Enumerable.Range(0,64).Any(y=>Enumerable.Range(from,to-from).Any(x=>p.Image.Pixels[(y*128+x)*4+3]>0));
        check(Painted(loose,64,128),"Wide paint projection reproduces inner-door UV spill onto a distant panel");
        check(Painted(safe,0,64)&&!Painted(safe,64,128),"Simple surface placement paints the outer door without reaching the shared inner skin");
        var exterior=SimplePlacement.Exterior(mesh,Vector3.UnitX);check(exterior.Contains(0)&&!exterior.Contains(2)&&exterior.Contains(4),"Exterior placement excludes the occluded inner door while retaining visible body panels");
    }

    public static void CheckMesh(PaintMesh mesh,string name,Action<bool,string> check)
    {
        var middle=(mesh.Minimum+mesh.Maximum)*.5f;
        foreach(var side in new[]{-1,1})
        {
            SurfaceHit? door=null;
            foreach(var y in new[]{.4f,.3f,.5f,.2f,.6f})
            foreach(var z in new[]{0f,.1f,-.1f,.2f,-.2f})
            {
                var origin=new Vector3(side*(mesh.Radius+2),float.Lerp(mesh.Minimum.Y,mesh.Maximum.Y,y),middle.Z+z*(mesh.Maximum.Z-mesh.Minimum.Z));
                var candidate=mesh.Raycast(origin,new(-side,0,0));
                if(candidate is {} hit&&hit.Position.X*side>0&&hit.Normal.X*side>.7f){door=hit;break;}
            }
            check(door is not null,"Actual paintable door target is reachable: "+name+(side<0?" left":" right"));
            var projection=SurfaceProjection.At(door!.Value,new(0,0,-side),.5f,1,0);
            var painter=new TexturePainter(new(512,512,new byte[512*512*4]));
            var count=painter.Stamp(mesh,projection,new(1,1,[240,80,30,255]),Vector3.One,1,Vector3.Zero);
            check(count>0,"Independent door sticker reaches actual body UVs: "+name+(side<0?" left":" right"));
        }
    }
}
