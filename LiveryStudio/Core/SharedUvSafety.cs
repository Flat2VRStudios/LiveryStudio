using System.Numerics;

namespace LiveryStudio.Core;

// A panel mask cannot isolate two physical panels using the same texels.
// Protect whole triangles with overlapping UV interiors, including overlaps
// away from the cursor center. Adjacent UV edges and coincident geometry are safe.
internal static class SharedUvSafety
{
    public static IReadOnlySet<int> Unshared(PaintMesh mesh,IReadOnlySet<int> exterior,CancellationToken cancellation)
    {
        var unsafeTriangles=new HashSet<int>();
        foreach(var triangle in exterior)
        {
            cancellation.ThrowIfCancellationRequested();var a=mesh.Vertices[triangle*3];var b=mesh.Vertices[triangle*3+1];var c=mesh.Vertices[triangle*3+2];
            var minimum=Vector2.Min(a.UV,Vector2.Min(b.UV,c.UV));var maximum=Vector2.Max(a.UV,Vector2.Max(b.UV,c.UV));
            foreach(var other in mesh.UvTriangles(minimum,maximum))
            {
                if(other<=triangle||!exterior.Contains(other))continue;
                var x=mesh.Vertices[other*3];var y=mesh.Vertices[other*3+1];var z=mesh.Vertices[other*3+2];
                var overlap=Intersection([a.UV,b.UV,c.UV],[x.UV,y.UV,z.UV]);if(overlap.Count<3)continue;
                var area=0f;for(var i=0;i<overlap.Count;i++)area+=Cross(overlap[i],overlap[(i+1)%overlap.Count]);if(MathF.Abs(area)<1e-10f)continue;
                if(overlap.Any(uv=>Vector3.DistanceSquared(Position(a,b,c,uv),Position(x,y,z,uv))>.0025f)){unsafeTriangles.Add(triangle);unsafeTriangles.Add(other);}
            }
        }
        return exterior.Except(unsafeTriangles).ToHashSet();
    }
    private static Vector3 Position(SurfaceVertex a,SurfaceVertex b,SurfaceVertex c,Vector2 uv)
    {MeshEditing.Barycentric(a.UV,b.UV,c.UV,uv,out var u,out var v);return a.Position*(1-u-v)+b.Position*u+c.Position*v;}
    private static float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;
    private static List<Vector2> Intersection(List<Vector2> subject,Vector2[] clip)
    {
        var orientation=MathF.Sign(Cross(clip[1]-clip[0],clip[2]-clip[0]));if(orientation==0)return [];
        for(var i=0;i<3&&subject.Count>0;i++)
        {
            var edge=clip[(i+1)%3]-clip[i];var output=new List<Vector2>();var previous=subject[^1];var previousDistance=Cross(edge,previous-clip[i])*orientation;
            foreach(var current in subject)
            {
                var distance=Cross(edge,current-clip[i])*orientation;
                if((distance>=0)!=(previousDistance>=0))output.Add(Vector2.Lerp(previous,current,previousDistance/(previousDistance-distance)));
                if(distance>=0)output.Add(current);previous=current;previousDistance=distance;
            }
            subject=output;
        }
        return subject;
    }
}
