using System.Numerics;
using System.Runtime.CompilerServices;

namespace LiveryStudio.Core;

public static class SimplePlacement
{
    // A wide volume can reach an inner door skin. On stock cars that skin may
    // share UVs with a distant exterior panel. Keep casual paint on the outside.
    public const float Depth=.06f;
    public const float Angle=55;
    public static PaintLayer OnSurface(PaintLayer layer)=>layer with {WrapDepth=Depth,WrapAngle=Angle,ExteriorOnly=true,ProtectSharedUvs=true};
    private static readonly ConditionalWeakTable<PaintMesh,Dictionary<int,IReadOnlySet<int>>> shells=new();
    private static readonly ConditionalWeakTable<PaintMesh,IReadOnlySet<int>> unshared=new();
    private static readonly ConditionalWeakTable<PaintMesh,Dictionary<IReadOnlySet<int>,IReadOnlySet<int>>> protectedShells=new();
    public static IReadOnlySet<int> UnsharedExterior(PaintMesh mesh,CancellationToken cancellation=default)=>unshared.GetValue(mesh,model=>
    {
        var exterior=new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ}.SelectMany(n=>Exterior(model,n,cancellation:cancellation)).ToHashSet();
        return SharedUvSafety.Unshared(model,exterior,cancellation);
    });
    public static IReadOnlySet<int>? Mask(PaintMesh mesh,PaintLayer layer,Vector3 normal,bool includeMirror=false,CancellationToken cancellation=default)
    {
        IReadOnlySet<int>? result=layer.ExteriorOnly?Exterior(mesh,normal,includeMirror&&layer.Mirror,cancellation):null;
        if(layer.ProtectSharedUvs)
        {
            var safe=UnsharedExterior(mesh,cancellation);
            if(result is null)result=safe;
            else{var cache=protectedShells.GetValue(mesh,_=>[]);lock(cache){if(!cache.TryGetValue(result,out var protectedShell)){protectedShell=result.Intersect(safe).ToHashSet();cache[result]=protectedShell;}result=protectedShell;}}
        }
        if(layer.Triangles.Length>0){var selected=layer.Triangles.ToHashSet();result=result is null?selected:result.Intersect(selected).ToHashSet();}
        return result;
    }
    public static IReadOnlySet<int> Exterior(PaintMesh mesh,Vector3 normal,bool mirror=false,CancellationToken cancellation=default)
    {
        var axis=Math.Abs(normal.Y)>Math.Abs(normal.X)&&Math.Abs(normal.Y)>Math.Abs(normal.Z)?1:Math.Abs(normal.X)>=Math.Abs(normal.Z)?0:2;var sign=(axis==0?normal.X:axis==1?normal.Y:normal.Z)<0?-1:1;var key=(axis+1)*sign;
        if(mirror&&axis==0)key=4;var cache=shells.GetValue(mesh,_=>[]);
        lock(cache)
        {
            cancellation.ThrowIfCancellationRequested();if(cache.TryGetValue(key,out var found))return found;
            if(key==4){var paired=Exterior(mesh,Vector3.UnitX,cancellation:cancellation).Concat(Exterior(mesh,-Vector3.UnitX,cancellation:cancellation)).ToHashSet();cache[key]=paired;return paired;}
            var direction=axis==0?Vector3.UnitX*sign:axis==1?Vector3.UnitY*sign:Vector3.UnitZ*sign;var result=new HashSet<int>();
            for(var i=0;i<mesh.Vertices.Length;i+=3)
            {
                cancellation.ThrowIfCancellationRequested();var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];if(a.Kind!=1||Vector3.Dot(a.Normal+b.Normal+c.Normal,direction)<.15f)continue;
                var center=(a.Position+b.Position+c.Position)/3;var hit=mesh.Raycast(center+direction*(mesh.Radius*3+1),-direction,paintOnly:false);
                if(hit is {} h&&Vector3.DistanceSquared(center,h.Position)<.0016f)result.Add(i/3);
            }
            cache[key]=result;return result;
        }
    }
}
