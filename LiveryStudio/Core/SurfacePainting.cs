using System.Numerics;

namespace LiveryStudio.Core;

public readonly record struct SurfaceVertex(Vector3 Position, Vector3 Normal, Vector2 UV, float Kind);
public readonly record struct SurfaceHit(Vector3 Position, Vector3 Normal, Vector2 UV, int Triangle, float Distance);
public sealed class PaintMesh
{
    // Loaded geometry is read-only. Create a new PaintMesh for changed vertices
    // so rendering and its spatial lookup always describe the same geometry.
    public SurfaceVertex[] Vertices { get; }
    public float[] NativeVertices { get; }
    private readonly MeshQueryIndex queries;
    internal List<int> UvTriangles(Vector2 point)=>queries.Uv(point);
    internal List<int> UvTriangles(Vector2 minimum,Vector2 maximum)=>queries.Uv(minimum,maximum);
    internal List<int> RayTriangles(Vector3 origin,Vector3 direction)=>queries.Ray(origin,direction);
    public Vector3 Minimum { get; private set; } = new(float.MaxValue);
    public Vector3 Maximum { get; private set; } = new(float.MinValue);
    public float Radius => Vector3.Distance(Minimum, Maximum) * .5f;
    public PaintMesh(float[] values)
    {
        if (values.Length == 0 || values.Length % 27 != 0 || values.Length > 54_000_000 || values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid preview mesh.");
        NativeVertices=values;Vertices=new SurfaceVertex[values.Length/9];
        for(var i=0;i<Vertices.Length;i++) {var k=i*9;var v=new SurfaceVertex(new(values[k],values[k+1],values[k+2]),Vector3.Normalize(new(values[k+3],values[k+4],values[k+5])),new(values[k+6],values[k+7]),values[k+8]);Vertices[i]=v;Minimum=Vector3.Min(Minimum,v.Position);Maximum=Vector3.Max(Maximum,v.Position);}
        queries=new(Vertices);
    }
    public static PaintMesh Read(string path)
    {
        using var file=File.OpenRead(path);using var reader=new BinaryReader(file);
        if(reader.ReadUInt32()!=0x4d534c46)throw new InvalidDataException("Invalid preview mesh header.");
        var count=reader.ReadUInt32();if(count>6_000_000 || file.Length!=8L+count*36L)throw new InvalidDataException("Invalid preview mesh size.");
        var values=new float[checked((int)count*9)];for(var i=0;i<values.Length;i++)values[i]=reader.ReadSingle();return new(values);
    }
    public SurfaceHit? Raycast(Vector3 origin,Vector3 direction,bool paintOnly=true)
    {
        if(direction.LengthSquared()<1e-12f)return null;direction=Vector3.Normalize(direction);
        SurfaceHit? result=null;var distance=float.MaxValue;var triangle=-1;
        foreach(var candidate in queries.Ray(origin,direction))
        {
            var i=candidate*3;
            var a=Vertices[i];var b=Vertices[i+1];var c=Vertices[i+2];var e1=b.Position-a.Position;var e2=c.Position-a.Position;
            var p=Vector3.Cross(direction,e2);var det=Vector3.Dot(e1,p);if(MathF.Abs(det)<1e-8f)continue;
            var inv=1/det;var delta=origin-a.Position;var u=Vector3.Dot(delta,p)*inv;if(u<0 || u>1)continue;
            var q=Vector3.Cross(delta,e1);var v=Vector3.Dot(direction,q)*inv;if(v<0 || u+v>1)continue;
            var t=Vector3.Dot(e2,q)*inv;if(t<=.0001f || t>=distance)continue;
            distance=t;triangle=i;var n=Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v);if(Vector3.Dot(n,direction)>0)n=-n;
            result=new(origin+direction*t,n,a.UV*(1-u-v)+b.UV*u+c.UV*v,i/3,t);
        }
        // Non-paintable parts occlude the body; never paint through a window or wheel.
        return triangle>=0 && (!paintOnly || Vertices[triangle].Kind==1)?result:null;
    }
}

public sealed record PaintImage(int Width,int Height,byte[] Pixels)
{
    public void Validate(){if(Width<1 || Height<1 || Width>8192 || Height>8192 || Pixels.Length!=(long)Width*Height*4)throw new InvalidDataException("Images must contain RGBA pixels and be at most 8192 × 8192.");}
}

public readonly record struct SurfaceProjection(Vector3 Center,Vector3 Normal,Vector3 Right,Vector3 Up,float Width,float Height)
{
    public float MinimumFacing {get;init;}=.25f;
    public float DepthRange {get;init;}=.35f;
    public bool FlipHorizontal {get;init;}
    public bool FlipVertical {get;init;}
    public static SurfaceProjection At(SurfaceHit hit,Vector3 cameraRight,float width,float aspect,float rotation,Vector3? alignmentNormal=null)
    {
        if(alignmentNormal is {} normal)hit=hit with {Normal=Vector3.Normalize(normal)};
        var right=cameraRight-hit.Normal*Vector3.Dot(cameraRight,hit.Normal);
        if(right.LengthSquared()<1e-6f)right=Vector3.Cross(MathF.Abs(hit.Normal.Y)>.9f?Vector3.UnitZ:Vector3.UnitY,hit.Normal);
        right=Vector3.Normalize(right);var up=Vector3.Normalize(Vector3.Cross(hit.Normal,right));
        var angle=rotation*MathF.PI/180;return new(hit.Position,hit.Normal,right*MathF.Cos(angle)+up*MathF.Sin(angle),up*MathF.Cos(angle)-right*MathF.Sin(angle),width,width/aspect);
    }
    public bool Map(Vector3 point,Vector3 normal,out Vector2 uv)
    {
        var offset=point-Center;uv=new(Vector3.Dot(offset,Right)/Width+.5f,.5f-Vector3.Dot(offset,Up)/Height);
        var inside=uv.X>=0 && uv.X<=1 && uv.Y>=0 && uv.Y<=1 && MathF.Abs(Vector3.Dot(offset,Normal))<=MathF.Max(Width,Height)*DepthRange && Vector3.Dot(normal,Normal)>MinimumFacing;
        if(FlipHorizontal)uv.X=1-uv.X;if(FlipVertical)uv.Y=1-uv.Y;return inside;
    }
}

public sealed class TexturePainter
{
    public PaintImage Image { get; }
    private readonly Dictionary<int,uint> pending=[];
    private readonly Stack<Change> undo=[],redo=[];
    private long historyBytes;
    private record Change(int[] Indices,uint[] Before,uint[] After);
    public bool CanUndo => undo.Count>0; public bool CanRedo=>redo.Count>0; public bool HasPending=>pending.Count>0;
    public TexturePainter(PaintImage image){image.Validate();Image=new(image.Width,image.Height,(byte[])image.Pixels.Clone());}
    private uint Pixel(int index)=>BitConverter.ToUInt32(Image.Pixels,index*4);
    private void Pixel(int index,uint value){System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(Image.Pixels.AsSpan(index*4,4),value);}
    public void Begin(){if(pending.Count>0)Commit();}
    public int Stamp(PaintMesh mesh,SurfaceProjection projection,PaintImage? sticker,Vector3 color,float opacity,Vector3 paintColor,IReadOnlySet<int>? triangles=null,float hardness=.85f,LayerKind? material=null,bool markSurface=false,DecalBlend blend=DecalBlend.Normal,CancellationToken cancellation=default,PaintImage? erased=null,PaintImage? restore=null,bool hardEdge=false)
    {
        cancellation.ThrowIfCancellationRequested();
        foreach(var map in new[]{erased,restore})if(map is not null){map.Validate();if(map.Width!=Image.Width||map.Height!=Image.Height)throw new ArgumentException("Paint restoration maps must match the canvas size.");}
        if(!float.IsFinite(opacity) || opacity<=0)return 0;if(projection.Width<=0 || projection.Height<=0)throw new ArgumentException("Stamp size must be positive.");sticker?.Validate();opacity=Math.Clamp(opacity,0,1);var changed=0;var touched=new HashSet<int>();
        var depth=projection.DepthRange*MathF.Max(projection.Width,projection.Height);
        var radius=MathF.Sqrt((projection.Width*projection.Width+projection.Height*projection.Height)*.25f+depth*depth);
        var lower=projection.Center-new Vector3(radius);var upper=projection.Center+new Vector3(radius);
        for(var tri=0;tri<mesh.Vertices.Length;tri+=3)
        {
            cancellation.ThrowIfCancellationRequested();
            var a=mesh.Vertices[tri];var b=mesh.Vertices[tri+1];var c=mesh.Vertices[tri+2];if(a.Kind!=1 || (triangles is not null && !triangles.Contains(tri/3)))continue;
            var min=Vector3.Min(a.Position,Vector3.Min(b.Position,c.Position));var max=Vector3.Max(a.Position,Vector3.Max(b.Position,c.Position));
            if(min.X>upper.X || min.Y>upper.Y || min.Z>upper.Z || max.X<lower.X || max.Y<lower.Y || max.Z<lower.Z)continue;
            var aa=new Vector2(a.UV.X*Image.Width,a.UV.Y*Image.Height);var bb=new Vector2(b.UV.X*Image.Width,b.UV.Y*Image.Height);var cc=new Vector2(c.UV.X*Image.Width,c.UV.Y*Image.Height);
            var e1=bb-aa;var e2=cc-aa;var det=e1.X*e2.Y-e1.Y*e2.X;if(MathF.Abs(det)<.00001f)continue;
            var x0=Math.Clamp((int)MathF.Floor(MathF.Min(aa.X,MathF.Min(bb.X,cc.X))),0,Image.Width-1);var x1=Math.Clamp((int)MathF.Ceiling(MathF.Max(aa.X,MathF.Max(bb.X,cc.X))),0,Image.Width-1);
            var y0=Math.Clamp((int)MathF.Floor(MathF.Min(aa.Y,MathF.Min(bb.Y,cc.Y))),0,Image.Height-1);var y1=Math.Clamp((int)MathF.Ceiling(MathF.Max(aa.Y,MathF.Max(bb.Y,cc.Y))),0,Image.Height-1);
            for(var y=y0;y<=y1;y++)
            {
            cancellation.ThrowIfCancellationRequested();for(var x=x0;x<=x1;x++)
            {
                var d=new Vector2(x+.5f,y+.5f)-aa;var u=(d.X*e2.Y-d.Y*e2.X)/det;var v=(e1.X*d.Y-e1.Y*d.X)/det;if(u<0 || v<0 || u+v>1)continue;
                var point=a.Position*(1-u-v)+b.Position*u+c.Position*v;var normal=Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v);
                if(!projection.Map(point,normal,out var uv))continue;
                var source=color;var coverage=opacity;
                if(sticker is not null){var sx=Math.Clamp((int)(uv.X*sticker.Width),0,sticker.Width-1);var sy=Math.Clamp((int)(uv.Y*sticker.Height),0,sticker.Height-1);var s=(sy*sticker.Width+sx)*4;coverage*=sticker.Pixels[s+3]/255f;source=new(sticker.Pixels[s]/255f,sticker.Pixels[s+1]/255f,sticker.Pixels[s+2]/255f);}
                else {var circle=(uv-new Vector2(.5f))*2;var length=circle.Length();if(length>1)continue;if(!hardEdge&&restore is null)coverage*=Math.Clamp((1-length)/MathF.Max(.02f,1-hardness),0,1);}
                if(coverage<=.001f)continue;
                var index=y*Image.Width+x;var k=index*4;if(erased is not null){coverage*=1-erased.Pixels[k]/255f;if(coverage<=.001f)continue;}if(!touched.Add(index))continue;var old=Pixel(index);var mask=Image.Pixels[k+3]/255f;
                if(restore is not null){for(var channelIndex=0;channelIndex<4;channelIndex++)Image.Pixels[k+channelIndex]=(byte)Math.Clamp(MathF.Round(float.Lerp(Image.Pixels[k+channelIndex],restore.Pixels[k+channelIndex],coverage)),0,255);if(Pixel(index)!=old){pending.TryAdd(index,old);changed++;}continue;}
                if(material is {} channel){if(channel is LayerKind.Gloss or LayerKind.Surface)Image.Pixels[k]=(byte)Math.Clamp(MathF.Round(float.Lerp(Image.Pixels[k],color.X*255,coverage)),0,255);if(channel is LayerKind.Metalness or LayerKind.Surface)Image.Pixels[k+1]=(byte)Math.Clamp(MathF.Round(float.Lerp(Image.Pixels[k+1],color.Y*255,coverage)),0,255);if(Pixel(index)!=old){pending.TryAdd(index,old);changed++;}continue;}
                var rgb=new Vector3(Image.Pixels[k]/255f,Image.Pixels[k+1]/255f,Image.Pixels[k+2]/255f);rgb=Vector3.Lerp(paintColor,rgb,mask);if(!markSurface)rgb=Vector3.Lerp(rgb,DecalEffects.Mix(rgb,source,blend),coverage);
                Image.Pixels[k]=(byte)Math.Clamp(MathF.Round(rgb.X*255),0,255);Image.Pixels[k+1]=(byte)Math.Clamp(MathF.Round(rgb.Y*255),0,255);Image.Pixels[k+2]=(byte)Math.Clamp(MathF.Round(rgb.Z*255),0,255);Image.Pixels[k+3]=255;
                if(Pixel(index)!=old){pending.TryAdd(index,old);changed++;}
            }
            }
        }
        return changed;
    }
    public bool Commit()
    {
        if(pending.Count==0)return false;var ids=pending.Keys.ToArray();var change=new Change(ids,ids.Select(i=>pending[i]).ToArray(),ids.Select(Pixel).ToArray());
        // Bound memory for large 4K strokes. Retain the most recent stroke even if it is large.
        if(historyBytes+ids.Length*12L>128*1024*1024){undo.Clear();historyBytes=0;}undo.Push(change);historyBytes+=ids.Length*12L;redo.Clear();pending.Clear();return true;
    }
    public bool Undo(){if(!undo.TryPop(out var c))return false;for(var i=0;i<c.Indices.Length;i++)Pixel(c.Indices[i],c.Before[i]);redo.Push(c);historyBytes-=c.Indices.Length*12L;return true;}
    public bool Redo(){if(!redo.TryPop(out var c))return false;for(var i=0;i<c.Indices.Length;i++)Pixel(c.Indices[i],c.After[i]);undo.Push(c);historyBytes+=c.Indices.Length*12L;return true;}
}
