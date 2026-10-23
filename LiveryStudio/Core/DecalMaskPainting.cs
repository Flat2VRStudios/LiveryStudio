using System.Numerics;

namespace LiveryStudio.Core;

public readonly record struct DecalMaskPoint(Vector2 UV,SurfaceProjection Projection,int Sample,bool Mirrored,bool SharedUv);

// A temporary brush canvas. Saving creates a new immutable stencil; the source
// artwork and stencil files referenced by existing history are never edited.
public sealed class DecalMaskPainting
{
    public PaintImage Stencil {get;}
    private PaintImage? original;
    public PaintImage? Artwork {get;private set;}
    public DecalMaskPainting(int width,int height,DecalEffectsOptions? effects=null,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(width<1||height<1||width>8192||height>8192)throw new ArgumentOutOfRangeException(nameof(width));
        var pixels=new byte[checked(width*height*4)];Array.Fill(pixels,(byte)255);
        Stencil=DecalEffects.Apply(new(width,height,pixels),effects,cancellation);
    }
    public DecalMaskPainting(PaintImage stencil)
    {
        stencil.Validate();Stencil=new(stencil.Width,stencil.Height,(byte[])stencil.Pixels.Clone());
    }
    public void AttachArtwork(PaintImage image)
    {
        image.Validate();if(image.Width!=Stencil.Width||image.Height!=Stencil.Height)throw new ArgumentException("Artwork and stencil dimensions must match.");
        original=image;Artwork=new(image.Width,image.Height,(byte[])image.Pixels.Clone());
        for(var k=3;k<Artwork.Pixels.Length;k+=4)Artwork.Pixels[k]=Alpha(image.Pixels[k],Stencil.Pixels[k]);
    }
    private static byte Alpha(byte source,byte coverage)=>(byte)Math.Clamp(MathF.Round(source*(coverage/255f)),0,255);
    public int Dab(Vector2 center,Vector2 radius,float hardness,float strength,bool restore=false)
    {
        if(!Finite(center)||!Finite(radius)||radius.X<=0||radius.Y<=0||!float.IsFinite(hardness)||hardness<0||hardness>1||!float.IsFinite(strength)||strength<0||strength>1)throw new ArgumentException("Choose finite brush dimensions and strength between zero and one.");
        if(strength==0)return 0;
        var x0=Math.Clamp((int)(Math.Clamp(center.X-radius.X,0,1)*Stencil.Width),0,Stencil.Width-1);var x1=Math.Clamp((int)MathF.Ceiling(Math.Clamp(center.X+radius.X,0,1)*Stencil.Width),0,Stencil.Width-1);
        var y0=Math.Clamp((int)(Math.Clamp(center.Y-radius.Y,0,1)*Stencil.Height),0,Stencil.Height-1);var y1=Math.Clamp((int)MathF.Ceiling(Math.Clamp(center.Y+radius.Y,0,1)*Stencil.Height),0,Stencil.Height-1);var changed=0;
        for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++)
        {
            var uv=new Vector2((x+.5f)/Stencil.Width,(y+.5f)/Stencil.Height);var distance=((uv-center)/radius).Length();if(distance>1)continue;
            var coverage=hardness==1?strength:strength*Math.Clamp((1-distance)/(1-hardness),0,1);var at=(y*Stencil.Width+x)*4+3;if(original?.Pixels[at]==0)continue;var old=Stencil.Pixels[at];
            var alpha=(byte)Math.Clamp(MathF.Round(float.Lerp(old,restore?255:0,coverage)),0,255);if(alpha!=old){Stencil.Pixels[at]=alpha;if(Artwork is not null)Artwork.Pixels[at]=Alpha(original!.Pixels[at],alpha);changed++;}
        }
        return changed;
    }
    public int Line(Vector2 start,Vector2 end,Vector2 radius,float hardness,float strength,bool restore=false)
    {
        if(!Finite(start)||!Finite(end)||!Finite(radius)||radius.X<=0||radius.Y<=0)throw new ArgumentException("Invalid brush path.");
        var length=((end-start)/radius).Length();var steps=(int)Math.Clamp(MathF.Ceiling(length/.3f),1,4096);var changed=0;
        for(var i=1;i<=steps;i++)changed+=Dab(Vector2.Lerp(start,end,(float)i/steps),radius,hardness,strength,restore);
        return changed;
    }
    public string Save(string workspace)
    {
        var folder=Path.Combine(workspace,"stencils");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"mask--"+Guid.NewGuid().ToString("N")+".tga");
        if(File.Exists(path))throw new IOException("The new stencil already exists.");FinishMaps.WriteTga(Stencil,path);return path;
    }
    public static DecalEffectsOptions Recipe(string file,DecalBlend blend)=>new(){Mask=DecalMask.Image,StencilFile=file,Channel=StencilChannel.Alpha,Blend=blend,Feather=0};
    private static bool Finite(Vector2 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y);
    // The GPU projection pass keeps the last triangle at a shared UV texel,
    // while baking keeps the first covered triangle. Use it only when every
    // overlapping triangle samples the same source coordinates.
    public static bool CanPreviewSingleProjection(PaintMesh mesh,PaintLayer layer,int width,int height,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(width<1||height<1)throw new ArgumentOutOfRangeException(nameof(width));
        if(layer.Kind!=LayerKind.Sticker||layer.Mirror||layer.Samples.Count!=1)return false;
        var projection=layer.Transform(layer.Samples[0]);var mapped=new Dictionary<int,Vector2>();
        var triangles=layer.Triangles.Length>0?layer.Triangles.ToHashSet():null;
        for(var tri=0;tri<mesh.Vertices.Length;tri+=3)
        {
            cancellation.ThrowIfCancellationRequested();
            var a=mesh.Vertices[tri];var b=mesh.Vertices[tri+1];var c=mesh.Vertices[tri+2];if(a.Kind!=1||(triangles is not null&&!triangles.Contains(tri/3)))continue;
            var min=Vector2.Min(a.UV,Vector2.Min(b.UV,c.UV));var max=Vector2.Max(a.UV,Vector2.Max(b.UV,c.UV));
            for(var y=Math.Clamp((int)MathF.Floor(min.Y*height),0,height-1);y<=Math.Clamp((int)MathF.Ceiling(max.Y*height),0,height-1);y++)
            {
                cancellation.ThrowIfCancellationRequested();
                for(var x=Math.Clamp((int)MathF.Floor(min.X*width),0,width-1);x<=Math.Clamp((int)MathF.Ceiling(max.X*width),0,width-1);x++)
                {
                    if(!MeshEditing.Barycentric(a.UV,b.UV,c.UV,new((x+.5f)/width,(y+.5f)/height),out var u,out var v))continue;
                    var position=a.Position*(1-u-v)+b.Position*u+c.Position*v;var normal=Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v);
                    if(!projection.Map(position,normal,out var uv))continue;
                    var index=y*width+x;if(mapped.TryGetValue(index,out var previous)){if(Vector2.DistanceSquared(previous,uv)>1e-10f)return false;}
                    else{if(mapped.Count>=262144)return false;mapped.Add(index,uv);} // Bound preparation memory; larger footprints use exact preview.
                }
            }
        }
        return true;
    }
    public static DecalMaskPoint? SourcePoint(PaintMesh mesh,PaintLayer layer,SurfaceHit hit,int textureWidth,int textureHeight,PaintImage? artwork=null)
    {
        if(layer.Kind!=LayerKind.Sticker||!layer.Visible||layer.Locked||layer.Opacity<=.001f||textureWidth<1||textureHeight<1)return null;
        var uv=new Vector2((Math.Clamp((int)(hit.UV.X*textureWidth),0,textureWidth-1)+.5f)/textureWidth,(Math.Clamp((int)(hit.UV.Y*textureHeight),0,textureHeight-1)+.5f)/textureHeight);
        var points=new List<(Vector3 Position,Vector3 Normal,int Triangle)>();
        foreach(var triangle in mesh.UvTriangles(uv))
        {
            var i=triangle*3;
            var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];if(a.Kind!=1||!MeshEditing.Barycentric(a.UV,b.UV,c.UV,uv,out var u,out var v))continue;
            points.Add((a.Position*(1-u-v)+b.Position*u+c.Position*v,Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v),i/3));
        }
        // Composition applies copies and paired sides in order; the last
        // covered pass wins. Within each pass the first covered mesh triangle
        // wins, even when the hit panel shares UVs with a different location.
        // Restore passes the original artwork so erased holes remain reachable.
        for(var sample=layer.Samples.Count-1;sample>=0;sample--)
        for(var mirror=(layer.Mirror?1:0);mirror>=0;mirror--)
        foreach(var point in points)
        {
            if(layer.Triangles.Length>0&&!layer.Triangles.Contains(point.Triangle))continue;
            var projection=layer.Transform(layer.Samples[sample]);
            var p=mirror==0?projection:PaintLayer.Reflected(projection);if(!p.Map(point.Position,point.Normal,out var at))continue;
            if(artwork is not null){var x=Math.Clamp((int)(at.X*artwork.Width),0,artwork.Width-1);var y=Math.Clamp((int)(at.Y*artwork.Height),0,artwork.Height-1);if(artwork.Pixels[(y*artwork.Width+x)*4+3]/255f*layer.Opacity<=.001f)continue;}
            return new(at,p,sample,mirror==1,Vector3.DistanceSquared(point.Position,hit.Position)>.0025f);
        }
        return null;
    }
}
