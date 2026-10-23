using System.Numerics;

namespace LiveryStudio.Core;
public static class LayerSets
{
	public static Guid[] ExpandGroups(IReadOnlyList<PaintLayer> layers,IEnumerable<Guid> ids)
	{
		var selected=ids.ToHashSet();var groups=layers.Where(l=>selected.Contains(l.Id)&&l.GroupId.HasValue).Select(l=>l.GroupId!.Value).ToHashSet();
		return layers.Where(l=>selected.Contains(l.Id)||(l.GroupId is {} group&&groups.Contains(group))).Select(l=>l.Id).ToArray();
	}
	public static void RequireEditable(IEnumerable<PaintLayer> layers)
	{
		if(layers.FirstOrDefault(l=>l.Locked) is {} locked)throw new InvalidOperationException("Unlock '"+locked.Name+"' before changing this selection.");
	}
	public static SurfaceProjection Frame(IReadOnlyList<PaintLayer> layers)
	{
		if(layers.Count<1)throw new ArgumentException("Select at least one layer.");var anchor=layers[0].Transform(layers[0].Samples[0]);anchor=anchor with {Up=Vector3.Normalize(anchor.Up-anchor.Right*Vector3.Dot(anchor.Up,anchor.Right))};var min=new Vector2(float.MaxValue);var max=new Vector2(float.MinValue);
		foreach(var layer in layers)foreach(var sample in layer.Samples)
		{
			var p=layer.Transform(sample);foreach(var corner in new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)}){var delta=Corner(p,corner)-anchor.Center;var point=new Vector2(Vector3.Dot(delta,anchor.Right),Vector3.Dot(delta,anchor.Up));min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
		}
		var midpoint=(min+max)*.5f;return anchor with {Center=anchor.Center+anchor.Right*midpoint.X+anchor.Up*midpoint.Y,Width=MathF.Max(.001f,max.X-min.X),Height=MathF.Max(.001f,max.Y-min.Y)};
	}
	public static Vector3 Corner(SurfaceProjection p,Vector2 corner)
	{
		// Projection axes can be oblique after stretching rotated artwork. Solve
		// their dual basis so bounds describe the actual mapped rectangle.
		var dot=Vector3.Dot(p.Right,p.Up);var denominator=1-dot*dot;if(denominator<.000001f)throw new InvalidDataException("Artwork projection axes are parallel.");var x=corner.X*p.Width;var y=corner.Y*p.Height;return p.Center+p.Right*((x-dot*y)/denominator)+p.Up*((y-dot*x)/denominator);
	}
	public static List<PaintLayer> Display(IReadOnlyList<PaintLayer> layers,bool mirrored)=>layers.Select(l=>{var copy=Canonical(l);if(mirrored)copy.Samples=copy.Samples.Select(PaintLayer.Reflected).ToList();return copy;}).ToList();
	public static List<PaintLayer> Fit(IReadOnlyList<PaintLayer> layers,SurfaceProjection target,bool flipHorizontal=false,bool flipVertical=false)
	{
		var source=Frame(layers);var sx=target.Width/source.Width*(flipHorizontal?-1:1);var sy=target.Height/source.Height*(flipVertical?-1:1);if(!float.IsFinite(sx)||!float.IsFinite(sy)||Math.Abs(sx)<.001f||Math.Abs(sy)<.001f)throw new ArgumentException("The artwork dimensions are too small.");
		Vector3 Axis(Vector3 v)=>target.Right*(Vector3.Dot(v,source.Right)/sx)+target.Up*(Vector3.Dot(v,source.Up)/sy);
		return layers.Select(l=>{var copy=Canonical(l);copy.Samples=copy.Samples.Select(p=>{var offset=p.Center-source.Center;var right=Axis(p.Right);var up=Axis(p.Up);if(right.LengthSquared()<.000001f||up.LengthSquared()<.000001f)throw new InvalidDataException("Artwork cannot project edge-on onto this plane.");return p with {Center=target.Center+target.Right*(Vector3.Dot(offset,source.Right)*sx)+target.Up*(Vector3.Dot(offset,source.Up)*sy),Right=Vector3.Normalize(right),Up=Vector3.Normalize(up),Normal=target.Normal,Width=p.Width/right.Length(),Height=p.Height/up.Length()};}).ToList();return copy;}).ToList();
	}
	public static PaintLayer Canonical(PaintLayer layer)=>layer with {Samples=layer.Samples.Select(layer.Transform).ToList(),Scale=1,ScaleX=1,ScaleY=1,Rotation=0,OffsetRight=0,OffsetUp=0};
	public static List<PaintLayer> Transform(IReadOnlyList<PaintLayer> layers,SurfaceProjection from,SurfaceProjection to,float scale=1,bool planar=false)
	{
		RequireEditable(layers);if(!float.IsFinite(scale)||scale<.01f||scale>100)throw new ArgumentException("The selection scale is outside its supported range.");
		Vector3 Map(Vector3 v)=>to.Right*Vector3.Dot(v,from.Right)+to.Up*Vector3.Dot(v,from.Up)+to.Normal*Vector3.Dot(v,from.Normal);
		return layers.Select(layer=>
		{
			var copy=Canonical(layer);copy.Samples=copy.Samples.Select(p=>
			{
				var offset=p.Center-from.Center;var right=Map(p.Right);var up=Map(p.Up);var normal=Map(p.Normal);
				if(planar){offset-=from.Normal*Vector3.Dot(offset,from.Normal);right-=to.Normal*Vector3.Dot(right,to.Normal);up-=to.Normal*Vector3.Dot(up,to.Normal);normal=to.Normal;}
				if(right.LengthSquared()<.00001f)right=to.Right;if(up.LengthSquared()<.00001f)up=to.Up;
				return p with {Center=to.Center+Map(offset)*scale,Right=Vector3.Normalize(right),Up=Vector3.Normalize(up),Normal=Vector3.Normalize(normal),Width=p.Width*scale,Height=p.Height*scale};
			}).ToList();return copy;
		}).ToList();
	}
	public static List<PaintLayer> Edit(IReadOnlyList<PaintLayer> layers,float right=0,float up=0,float scale=1,float rotation=0)
	{
		var frame=Frame(layers);var angle=rotation*MathF.PI/180;var to=frame with {Center=frame.Center+frame.Right*right+frame.Up*up,Right=frame.Right*MathF.Cos(angle)+frame.Up*MathF.Sin(angle),Up=frame.Up*MathF.Cos(angle)-frame.Right*MathF.Sin(angle)};
		return Transform(layers,frame,to,scale);
	}
	public static List<PaintLayer> Duplicate(IReadOnlyList<PaintLayer> layers)
	{
		var groups=new Dictionary<Guid,Guid>();return layers.Select(layer=>{var copy=Canonical(layer);copy.Id=Guid.NewGuid();copy.Name=copy.Name[..Math.Min(90,copy.Name.Length)]+" copy";copy.Locked=false;if(copy.GroupId is {} group){if(!groups.TryGetValue(group,out var next))groups[group]=next=Guid.NewGuid();copy.GroupId=next;}return copy;}).ToList();
	}
	public static void Group(List<PaintLayer> layers,IReadOnlySet<Guid> ids,string name)
	{
		var members=layers.Where(l=>ids.Contains(l.Id)).ToArray();RequireEditable(members);if(members.Length<2)throw new InvalidOperationException("Select two or more layers to group.");name=name.Trim();if(name.Length<1||name.Length>80)throw new ArgumentException("Name the group using 1–80 characters.");var id=Guid.NewGuid();foreach(var layer in members){layer.GroupId=id;layer.GroupName=name;}
	}
	public static void Ungroup(List<PaintLayer> layers,IReadOnlySet<Guid> ids){var members=layers.Where(l=>ids.Contains(l.Id)).ToArray();RequireEditable(members);foreach(var layer in members){layer.GroupId=null;layer.GroupName="";}}
	public static void Reorder(List<PaintLayer> layers,IReadOnlySet<Guid> ids,int direction)
	{
		RequireEditable(layers.Where(l=>ids.Contains(l.Id)));if(direction>0){for(var i=layers.Count-2;i>=0;i--)if(ids.Contains(layers[i].Id)&&!ids.Contains(layers[i+1].Id))(layers[i],layers[i+1])=(layers[i+1],layers[i]);}
		else{for(var i=1;i<layers.Count;i++)if(ids.Contains(layers[i].Id)&&!ids.Contains(layers[i-1].Id))(layers[i],layers[i-1])=(layers[i-1],layers[i]);}
	}
}
