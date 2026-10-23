using System.Numerics;

namespace LiveryStudio.Core;
public static class MeshEditing
{
    public static int[] PairedPanels(PaintMesh mesh,int[] selected)
        =>selected.Length==0?[]:selected.Union(OppositeSideArtwork.ReflectPanels(mesh,selected)).Order().ToArray();
	public static int[] Panel(PaintMesh mesh,SurfaceHit hit)
	{
		var candidates=new Dictionary<(int,int,int),List<int>>();var neighbors=new Dictionary<int,HashSet<int>>();
		static (int,int,int) Key(Vector3 p)=>((int)MathF.Round(p.X*10000),(int)MathF.Round(p.Y*10000),(int)MathF.Round(p.Z*10000));
		for(var i=0;i<mesh.Vertices.Length;i+=3)
		{
			if(mesh.Vertices[i].Kind!=1)continue;var normal=Vector3.Normalize(mesh.Vertices[i].Normal+mesh.Vertices[i+1].Normal+mesh.Vertices[i+2].Normal);
			if(Vector3.Dot(normal,hit.Normal)<.6f)continue;var tri=i/3;neighbors[tri]=[];
			for(var v=0;v<3;v++){var key=Key(mesh.Vertices[i+v].Position);if(!candidates.TryGetValue(key,out var list))candidates[key]=list=[];foreach(var other in list){neighbors[tri].Add(other);neighbors[other].Add(tri);}list.Add(tri);}
		}
		if(!neighbors.ContainsKey(hit.Triangle))return [hit.Triangle];var found=new HashSet<int>{hit.Triangle};var queue=new Queue<int>();queue.Enqueue(hit.Triangle);
		while(queue.TryDequeue(out var tri))foreach(var next in neighbors[tri])if(found.Add(next))queue.Enqueue(next);
		return found.Order().ToArray();
	}
	public static int SharedUvCount(PaintMesh mesh,SurfaceHit hit)
	{
		var locations=new List<Vector3>();
        foreach(var triangle in mesh.UvTriangles(hit.UV))
		{
            var i=triangle*3;
			var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];if(a.Kind!=1||!Barycentric(a.UV,b.UV,c.UV,hit.UV,out var u,out var v))continue;
			var point=a.Position*(1-u-v)+b.Position*u+c.Position*v;if(locations.All(p=>Vector3.DistanceSquared(p,point)>.0025f))locations.Add(point);
		}
		return Math.Max(0,locations.Count-1);
	}
	internal static bool Barycentric(Vector2 a,Vector2 b,Vector2 c,Vector2 p,out float u,out float v)
	{
		var e1=b-a;var e2=c-a;var d=p-a;var det=e1.X*e2.Y-e1.Y*e2.X;u=v=0;if(MathF.Abs(det)<1e-9)return false;
		u=(d.X*e2.Y-d.Y*e2.X)/det;v=(e1.X*d.Y-e1.Y*d.X)/det;return u>=-.00001f&&v>=-.00001f&&u+v<=1.00001f;
	}
	public static bool[] UvCoverage(PaintMesh mesh,int width,int height,CancellationToken cancellation=default)
	{
		cancellation.ThrowIfCancellationRequested();
		var result=new bool[checked(width*height)];
		for(var i=0;i<mesh.Vertices.Length;i+=3)
		{
			cancellation.ThrowIfCancellationRequested();
			var a=mesh.Vertices[i];var b=mesh.Vertices[i+1];var c=mesh.Vertices[i+2];if(a.Kind!=1)continue;
			var min=Vector2.Min(a.UV,Vector2.Min(b.UV,c.UV));var max=Vector2.Max(a.UV,Vector2.Max(b.UV,c.UV));
			for(var y=Math.Clamp((int)(min.Y*height),0,height-1);y<=Math.Clamp((int)(max.Y*height),0,height-1);y++)
			{
				cancellation.ThrowIfCancellationRequested();
				for(var x=Math.Clamp((int)(min.X*width),0,width-1);x<=Math.Clamp((int)(max.X*width),0,width-1);x++)if(Barycentric(a.UV,b.UV,c.UV,new((x+.5f)/width,(y+.5f)/height),out _,out _))result[y*width+x]=true;
			}
		}
		return result;
	}
	public static void Bleed(PaintImage image,bool[] coverage,int pixels,CancellationToken cancellation=default)
	{
		cancellation.ThrowIfCancellationRequested();
		if(coverage.Length!=image.Width*image.Height||pixels<0||pixels>8)throw new ArgumentException("Invalid seam padding.");var active=(bool[])coverage.Clone();
		for(var pass=0;pass<pixels;pass++)
		{
			var next=(bool[])active.Clone();
			for(var y=0;y<image.Height;y++)
			{
			cancellation.ThrowIfCancellationRequested();for(var x=0;x<image.Width;x++)
			{
				var at=y*image.Width+x;if(active[at])continue;
				var from=x>0&&active[at-1]?at-1:x+1<image.Width&&active[at+1]?at+1:y>0&&active[at-image.Width]?at-image.Width:y+1<image.Height&&active[at+image.Width]?at+image.Width:-1;
				if(from>=0){image.Pixels.AsSpan(from*4,4).CopyTo(image.Pixels.AsSpan(at*4,4));next[at]=true;}
			}
			}
			active=next;
		}
	}
}
