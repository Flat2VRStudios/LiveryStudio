using System.Numerics;
using LiveryStudio.Core;

internal static class MeshQueryChecks
{
    private static SurfaceHit? Scan(PaintMesh mesh,Vector3 origin,Vector3 direction,bool paintOnly=true)
    {
        if(direction.LengthSquared()<1e-12f)return null;direction=Vector3.Normalize(direction);SurfaceHit? result=null;var distance=float.MaxValue;var triangle=-1;
        for(var i=0;i<mesh.Vertices.Length;i+=3)
        {
            var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];var e1=b.Position-a.Position;var e2=c.Position-a.Position;var p=Vector3.Cross(direction,e2);var det=Vector3.Dot(e1,p);if(MathF.Abs(det)<1e-8f)continue;
            var inv=1/det;var delta=origin-a.Position;var u=Vector3.Dot(delta,p)*inv;if(u<0||u>1)continue;var q=Vector3.Cross(delta,e1);var v=Vector3.Dot(direction,q)*inv;if(v<0||u+v>1)continue;
            var t=Vector3.Dot(e2,q)*inv;if(t<=.0001f||t>=distance)continue;distance=t;triangle=i;var normal=Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v);if(Vector3.Dot(normal,direction)>0)normal=-normal;
            result=new(origin+direction*t,normal,a.UV*(1-u-v)+b.UV*u+c.UV*v,i/3,t);
        }
        return triangle>=0&&(!paintOnly||mesh.Vertices[triangle].Kind==1)?result:null;
    }
    private static int[] ScanUv(PaintMesh mesh,Vector2 point,IEnumerable<int> triangles)=>triangles.Where(triangle=>
    {
        var i=triangle*3;return mesh.Vertices[i].Kind==1&&MeshEditing.Barycentric(mesh.Vertices[i].UV,mesh.Vertices[i+1].UV,mesh.Vertices[i+2].UV,point,out _,out _);
    }).ToArray();
    private static float[] Triangle(float x,float y,float z,float kind,float u,float v,float scale=1)=>[
        x,y,z,0,0,1,u,v,kind, x+1,y,z,0,0,1,u+scale,v,kind, x,y+1,z,0,0,1,u,v+scale,kind];
    public static void Run(string repo,Action<bool,string> check)
    {
        var occluded=new PaintMesh([..Triangle(0,0,0,1,0,0),..Triangle(0,0,1,2,0,0)]);var origin=new Vector3(.2f,.2f,3);var direction=-Vector3.UnitZ;
        check(occluded.Raycast(origin,direction) is null&&occluded.Raycast(origin,direction,false)?.Triangle==1,"Indexed rays preserve glass and wheel occlusion of paintable panels");
        var tied=new PaintMesh([..Triangle(0,0,0,1,0,0),..Triangle(0,0,0,1,0,0)]);
        check(tied.Raycast(origin,direction)?.Triangle==0,"Coincident indexed ray hits retain the first triangle in mesh order");
        check(tied.Raycast(Vector3.Zero,Vector3.Zero) is null&&tied.Raycast(new(2,2,0),Vector3.UnitX) is null&&tied.Raycast(new(.2f,.2f,-1),Vector3.UnitZ)?.Triangle==0,"Indexed ray queries retain zero-direction, parallel and reverse-side behavior");
        var values=new List<float>();for(var y=0;y<16;y++)for(var x=0;x<16;x++)values.AddRange(Triangle(x*3,y*3,0,1,x/16f,y/16f,1/16f));var grid=new PaintMesh(values.ToArray());
        check(grid.RayTriangles(new(.2f,.2f,3),direction).Count<16&&grid.UvTriangles(new(.01f,.01f)).Count<16,"Spatial lookup rejects unrelated world and UV triangles before exact intersection");
        var random=new Random(56274);var rayMatch=true;var uvMatch=true;
        for(var i=0;i<1000;i++)
        {
            var at=new Vector3((float)random.NextDouble()*50-1,(float)random.NextDouble()*50-1,i%2==0?3:-3);var ray=i%2==0?-Vector3.UnitZ:Vector3.UnitZ;
            rayMatch&=grid.Raycast(at,ray)==Scan(grid,at,ray);
            var point=new Vector2((float)random.NextDouble()*1.2f-.1f,(float)random.NextDouble()*1.2f-.1f);
            uvMatch&=ScanUv(grid,point,grid.UvTriangles(point)).SequenceEqual(ScanUv(grid,point,Enumerable.Range(0,grid.Vertices.Length/3)));
        }
        check(rayMatch,"One thousand indexed grid rays exactly match the original full scan");check(uvMatch,"One thousand indexed UV queries preserve every original triangle and its order");
        var edges=new[]{new Vector2(-.000009f,.2f),new Vector2(.2f,-.000009f),new Vector2(.5f,.500009f),new Vector2(1.000009f,0),new Vector2(0,0),new Vector2(1,0)};
        check(edges.All(point=>ScanUv(tied,point,tied.UvTriangles(point)).SequenceEqual(ScanUv(tied,point,[0,1]))),"UV bounds preserve barycentric tolerance just outside triangle edges and corners");
        check(ScanUv(tied,new(.2f),tied.UvTriangles(new(.2f))).SequenceEqual(new[]{0,1})&&MeshEditing.SharedUvCount(tied,new(new(.2f,.2f,0),Vector3.UnitZ,new(.2f),0,1))==0,"Coincident UV triangles retain ordering without becoming distinct shared surface locations");
        var shared=new PaintMesh([..Triangle(0,0,0,1,0,0),..Triangle(3,0,0,1,0,0)]);
        check(MeshEditing.SharedUvCount(shared,new(new(.2f,.2f,0),Vector3.UnitZ,new(.2f),0,1))==1,"UV indexing retains genuinely shared locations on separate body panels");
        var noPaint=new PaintMesh(Triangle(0,0,0,2,0,0));check(noPaint.UvTriangles(new(.2f)).Count==0&&noPaint.Raycast(origin,direction,false) is not null,"Cars without paint candidates retain non-paint ray intersections");
        var concurrent=true;Parallel.For(0,256,i=>{var at=new Vector3(i%16*3+.2f,i/16*3+.2f,3);if(grid.Raycast(at,direction)!=Scan(grid,at,direction))concurrent=false;});
        check(concurrent,"Concurrent queries share immutable geometry without changing ray results");
        var movedValues=(float[])grid.NativeVertices.Clone();for(var i=0;i<movedValues.Length;i+=9)movedValues[i]+=100;var moved=new PaintMesh(movedValues);
        check(grid.Raycast(new(.2f,.2f,3),direction) is not null&&moved.Raycast(new(.2f,.2f,3),direction) is null&&moved.Raycast(new(100.2f,.2f,3),direction) is not null,"Rebuilt car geometry receives an independent index without changing the original mesh");
        var models=Path.Combine(repo,"LiveryStudio/artifacts/models");
        if(!Directory.Exists(models))return;
        foreach(var car in Contract.Cars)
        {
            var mesh=PaintMesh.Read(Path.Combine(models,car.Index.ToString("00")+".flsm"));var center=(mesh.Minimum+mesh.Maximum)*.5f;var matches=true;
            for(var i=0;i<16;i++)
            {
                var angle=i*MathF.Tau/16;var from=center+Vector3.Normalize(new(MathF.Cos(angle),i%3==0?1:.2f,MathF.Sin(angle)))*mesh.Radius*3;var ray=center-from;
                matches&=mesh.Raycast(from,ray,i%2==0)==Scan(mesh,from,ray,i%2==0);
                var vertex=mesh.Vertices[(i*1877)%mesh.Vertices.Length];matches&=ScanUv(mesh,vertex.UV,mesh.UvTriangles(vertex.UV)).SequenceEqual(ScanUv(mesh,vertex.UV,Enumerable.Range(0,mesh.Vertices.Length/3)));
            }
            check(matches,"Real car indexed ray and UV results exactly match full scans: "+car.Name);
        }
    }
}
