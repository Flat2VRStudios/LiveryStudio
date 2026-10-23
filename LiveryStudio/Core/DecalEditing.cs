using System.Numerics;

namespace LiveryStudio.Core;

public sealed record DecalSelection(Guid LayerId,int Sample,bool Mirrored,bool SharedUv);
public static class DecalEditing
{
	// Pick the texel that is actually displayed. Another panel can share its UVs,
	// and transparent pixels in a top decal must allow selection of the layer below.
	public static DecalSelection? Pick(PaintMesh mesh,IReadOnlyList<PaintLayer> layers,SurfaceHit hit,int width,int height,Func<string,PaintImage> image)
		=>Candidates(mesh,layers,hit,width,height,image,false,default).FirstOrDefault();
	// One result per layer, topmost first, including covered artwork with actual
	// source coverage at this texel. Preserve the same sample/paired/UV choice as Pick.
	public static IReadOnlyList<DecalSelection> PickAll(PaintMesh mesh,IReadOnlyList<PaintLayer> layers,SurfaceHit hit,int width,int height,Func<string,PaintImage> image,bool includeLocked=false,CancellationToken cancellation=default)
	{
		var cache=new DecalImageCache(cancellation,read:image);
		return Candidates(mesh,layers,hit,width,height,path=>cache.Get(path),includeLocked,cancellation).ToArray();
	}
	private static IEnumerable<DecalSelection> Candidates(PaintMesh mesh,IReadOnlyList<PaintLayer> layers,SurfaceHit hit,int width,int height,Func<string,PaintImage> image,bool includeLocked,CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();
		if(width<1||height<1)throw new ArgumentOutOfRangeException(nameof(width),"Use the displayed texture dimensions when picking artwork.");
		var uv=new Vector2((Math.Clamp((int)(hit.UV.X*width),0,width-1)+.5f)/width,(Math.Clamp((int)(hit.UV.Y*height),0,height-1)+.5f)/height);
		var points=new List<(Vector3 Position,Vector3 Normal,int Triangle)>();
        foreach(var triangle in mesh.UvTriangles(uv))
		{
			cancellation.ThrowIfCancellationRequested();
            var i=triangle*3;
			var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];if(a.Kind!=1||!MeshEditing.Barycentric(a.UV,b.UV,c.UV,uv,out var u,out var v))continue;
			points.Add((a.Position*(1-u-v)+b.Position*u+c.Position*v,Vector3.Normalize(a.Normal*(1-u-v)+b.Normal*u+c.Normal*v),i/3));
		}
		points=points.OrderBy(p=>Vector3.DistanceSquared(p.Position,hit.Position)).ToList();
		var stencils=new Dictionary<DecalEffectsOptions,DecalEffects.PreparedMask>();
		long stencilBytes=0;
		foreach(var layer in layers.Reverse().Where(l=>l.Kind==LayerKind.Sticker&&l.Visible&&(includeLocked||!l.Locked)&&l.Opacity>.001f))
		{
			cancellation.ThrowIfCancellationRequested();if(Find(layer) is {} selected)yield return selected;
		}
		DecalSelection? Find(PaintLayer layer)
		{
			PaintImage? artwork=null;
			for(var sample=layer.Samples.Count-1;sample>=0;sample--)
			{
				cancellation.ThrowIfCancellationRequested();
				var projection=layer.Transform(layer.Samples[sample]);
				foreach(var point in points)
				{
					if(layer.Triangles.Length>0&&!layer.Triangles.Contains(point.Triangle))continue;
					for(var mirror=0;mirror<(layer.Mirror?2:1);mirror++)
					{
						var p=mirror==0?projection:PaintLayer.Reflected(projection);if(!p.Map(point.Position,point.Normal,out var at))continue;
						if(artwork is null){artwork=image(layer.Asset);artwork.Validate();}
						var x=Math.Clamp((int)(at.X*artwork.Width),0,artwork.Width-1);var y=Math.Clamp((int)(at.Y*artwork.Height),0,artwork.Height-1);
						var alpha=artwork.Pixels[(y*artwork.Width+x)*4+3];
						if(alpha>0&&layer.Effects is {Mask:not DecalMask.None} effect)
						{
							if(!stencils.TryGetValue(effect,out var prepared))
							{
								prepared=new(effect,image,cancellation);
								if(stencils.Count>=32||stencilBytes+prepared.RetainedBytes>64*1024*1024){stencils.Clear();stencilBytes=0;}
								if(prepared.RetainedBytes<=64*1024*1024){stencils.Add(effect,prepared);stencilBytes+=prepared.RetainedBytes;}
							}
							alpha=prepared.Alpha(alpha,new((x+.5f)/artwork.Width,(y+.5f)/artwork.Height));
						}
						if(alpha/255f*layer.Opacity>.001f)return new(layer.Id,sample,mirror==1,Vector3.DistanceSquared(point.Position,hit.Position)>.0025f);
					}
				}
			}
			return null;
		}
	}
	public static SurfaceProjection DisplayProjection(PaintLayer layer,DecalSelection selection)
	{
		var p=layer.Transform(layer.Samples[selection.Sample]);return selection.Mirrored?PaintLayer.Reflected(p):p;
	}
	public static PaintLayer Move(PaintLayer layer,DecalSelection selection,SurfaceHit hit,Vector3 eye)
	{
		var original=layer.Samples[selection.Sample];var basis=selection.Mirrored?PaintLayer.Reflected(original):original;
		var next=SurfaceProjection.At(hit,basis.Right,original.Width,original.Width/original.Height,0,layer.AlignWithCamera?Vector3.Normalize(eye-hit.Position):null);
		// Preserve the source's upright direction, including legacy custom bases.
		if(Vector3.Dot(next.Up,basis.Up)<0)next=next with {Up=-next.Up};
		if(selection.Mirrored)next=PaintLayer.Reflected(next);
		var samples=layer.Samples.ToList();var delta=next.Center-original.Center;
		for(var i=0;i<samples.Count;i++)samples[i]=samples[i] with {Center=samples[i].Center+delta};samples[selection.Sample]=next;
		return layer with {Samples=samples,OffsetRight=0,OffsetUp=0};
	}
	public static bool Project(Vector3 point,Matrix4x4 matrix,float width,float height,out Vector2 screen)
	{
		var p=Vector4.Transform(new Vector4(point,1),matrix);screen=default;if(p.W<=.00001f||p.Z<0||p.Z>p.W)return false;
		screen=new((p.X/p.W+1)*width*.5f,(1-p.Y/p.W)*height*.5f);return float.IsFinite(screen.X)&&float.IsFinite(screen.Y);
	}
	public static bool PlanePoint(Vector2 screen,Matrix4x4 matrix,float width,float height,SurfaceProjection plane,out Vector3 point)
	{
		point=default;if(width<=0||height<=0||!Matrix4x4.Invert(matrix,out var inverse))return false;
		var x=screen.X/width*2-1;var y=1-screen.Y/height*2;var a=Vector4.Transform(new Vector4(x,y,0,1),inverse);var b=Vector4.Transform(new Vector4(x,y,1,1),inverse);
		if(Math.Abs(a.W)<1e-8f||Math.Abs(b.W)<1e-8f)return false;var origin=new Vector3(a.X,a.Y,a.Z)/a.W;var ray=new Vector3(b.X,b.Y,b.Z)/b.W;ray-=origin;
		var denominator=Vector3.Dot(ray,plane.Normal);if(Math.Abs(denominator)<1e-8f)return false;var t=Vector3.Dot(plane.Center-origin,plane.Normal)/denominator;if(t<0)return false;point=origin+ray*t;return float.IsFinite(point.X)&&float.IsFinite(point.Y)&&float.IsFinite(point.Z);
	}
	public static float RotationDelta(Vector3 start,Vector3 end,SurfaceProjection plane,bool mirrored,bool snap)
	{
		float Angle(Vector3 p)=>MathF.Atan2(Vector3.Dot(p-plane.Center,plane.Up),Vector3.Dot(p-plane.Center,plane.Right));
		var delta=MathF.IEEERemainder((Angle(end)-Angle(start))*180/MathF.PI,360)*(mirrored?-1:1);return snap?MathF.Round(delta/15)*15:delta;
	}
	public static float Resize(float scale,float startRadius,float endRadius,bool snap)
	{
		if(!float.IsFinite(startRadius)||!float.IsFinite(endRadius)||startRadius<.00001f)return scale;var next=Math.Clamp(scale*endRadius/startRadius,.02f,20);return snap?Math.Clamp(MathF.Round(next/.05f)*.05f,.02f,20):next;
	}
	public static PaintLayer Nudge(PaintLayer layer,DecalSelection selection,float right,float up)
	{
		var p=DisplayProjection(layer,selection);var delta=p.Right*right+p.Up*up;if(selection.Mirrored)delta.X=-delta.X;var basis=layer.Samples[selection.Sample];
		return layer with {OffsetRight=layer.OffsetRight+Vector3.Dot(delta,basis.Right),OffsetUp=layer.OffsetUp+Vector3.Dot(delta,basis.Up)};
	}
	public static PaintLayer Turn(PaintLayer layer,DecalSelection selection,float degrees)=>layer with {Rotation=MathF.IEEERemainder(layer.Rotation+degrees*(selection.Mirrored?-1:1),360)};
}
