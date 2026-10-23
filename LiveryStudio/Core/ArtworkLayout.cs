using System.Numerics;

namespace LiveryStudio.Core;

public enum LayoutOperation { Left, CenterHorizontal, Right, Top, CenterVertical, Bottom, HorizontalCenters, VerticalCenters, HorizontalGaps, VerticalGaps, HorizontalSpacing, VerticalSpacing }
public static class ArtworkLayout
{
	private sealed record Unit(PaintLayer[] Layers,Vector2 Min,Vector2 Max)
	{
		public Vector2 Center=>(Min+Max)*.5f;
		public Vector2 Size=>Max-Min;
	}
	public static int ObjectCount(IReadOnlyList<PaintLayer> layers,bool keepGroups)=>layers.Select(l=>keepGroups&&l.GroupId is {} group?(true,group):(false,l.Id)).Distinct().Count();
	private static List<Unit> Units(IReadOnlyList<PaintLayer> layers,SurfaceProjection frame,bool keepGroups)
	{
		return layers.GroupBy(l=>keepGroups&&l.GroupId is {} group?(true,group):(false,l.Id)).Select(group=>
		{
			var min=new Vector2(float.MaxValue);var max=new Vector2(float.MinValue);
			foreach(var l in group)foreach(var sample in l.Samples)foreach(var corner in new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)})
			{
				var delta=LayerSets.Corner(l.Transform(sample),corner)-frame.Center;var point=new Vector2(Vector3.Dot(delta,frame.Right),Vector3.Dot(delta,frame.Up));min=Vector2.Min(min,point);max=Vector2.Max(max,point);
			}
			return new Unit(group.ToArray(),min,max);
		}).ToList();
	}
	public static List<PaintLayer> Arrange(IReadOnlyList<PaintLayer> layers,LayoutOperation operation,bool keepGroups=true,float gap=.03f,Guid? reference=null)
	{
		LayerSets.RequireEditable(layers);if(!Enum.IsDefined(operation)||!float.IsFinite(gap)||gap<0||gap>10)throw new ArgumentException("Choose a valid layout action and a spacing between 0 and 10 metres.");
		if(reference is {} key&&!layers.Any(l=>l.Id==key))throw new ArgumentException("The alignment anchor is not part of this selection.");
		var frame=LayerSets.Frame(layers);var units=Units(layers,frame,keepGroups);var distribution=operation is LayoutOperation.HorizontalCenters or LayoutOperation.VerticalCenters or LayoutOperation.HorizontalGaps or LayoutOperation.VerticalGaps;
		if(units.Count<(distribution?3:2))throw new InvalidOperationException(distribution?"Select at least three layout objects to distribute.":"Select at least two layout objects. Uncheck 'Keep each group together' to arrange its members.");
		var vertical=operation is LayoutOperation.Top or LayoutOperation.CenterVertical or LayoutOperation.Bottom or LayoutOperation.VerticalCenters or LayoutOperation.VerticalGaps or LayoutOperation.VerticalSpacing;
		float Axis(Vector2 v)=>vertical?v.Y:v.X;
		var moves=new Dictionary<Guid,Vector3>();
		void Move(Unit unit,float amount){var delta=(vertical?frame.Up:frame.Right)*amount;foreach(var layer in unit.Layers)moves[layer.Id]=delta;}
		if(operation is LayoutOperation.Left or LayoutOperation.CenterHorizontal or LayoutOperation.Right or LayoutOperation.Top or LayoutOperation.CenterVertical or LayoutOperation.Bottom)
		{
			var anchor=reference is {} id?units.FirstOrDefault(u=>u.Layers.Any(l=>l.Id==id)):null;
			var min=anchor?.Min??units.Select(u=>u.Min).Aggregate(Vector2.Min);var max=anchor?.Max??units.Select(u=>u.Max).Aggregate(Vector2.Max);
			var start=operation is LayoutOperation.Left or LayoutOperation.Bottom;var end=operation is LayoutOperation.Right or LayoutOperation.Top;
			var target=Axis(start?min:end?max:(min+max)*.5f);
			foreach(var unit in units)Move(unit,target-Axis(start?unit.Min:end?unit.Max:unit.Center));
		}
		else
		{
			var ordered=units.OrderBy(u=>Axis(u.Center)).ToArray();
			if(operation is LayoutOperation.HorizontalCenters or LayoutOperation.VerticalCenters)
			{
				var first=Axis(ordered[0].Center);var step=(Axis(ordered[^1].Center)-first)/(ordered.Length-1);
				for(var i=0;i<ordered.Length;i++)Move(ordered[i],first+step*i-Axis(ordered[i].Center));
			}
			else
			{
				var spacing=operation is LayoutOperation.HorizontalSpacing or LayoutOperation.VerticalSpacing?gap:(Axis(ordered[^1].Max)-Axis(ordered[0].Min)-ordered.Sum(u=>Axis(u.Size)))/(ordered.Length-1);
				var cursor=Axis(ordered[0].Min);foreach(var unit in ordered){Move(unit,cursor-Axis(unit.Min));cursor+=Axis(unit.Size)+spacing;}
			}
		}
		return layers.Select(l=>{var copy=LayerSets.Canonical(l);copy.Samples=copy.Samples.Select(p=>p with {Center=p.Center+moves[l.Id]}).ToList();return copy;}).ToList();
	}
}
