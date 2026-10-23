using System.Numerics;
using LiveryStudio.Core;

internal static class LayoutChecks
{
	private static (Vector2 Min,Vector2 Max) Bounds(IEnumerable<PaintLayer> layers)
	{
		var min=new Vector2(float.MaxValue);var max=new Vector2(float.MinValue);
		foreach(var l in layers)foreach(var sample in l.Samples)foreach(var corner in new[]{new Vector2(-.5f,-.5f),new Vector2(.5f,-.5f),new Vector2(.5f,.5f),new Vector2(-.5f,.5f)}){var p=LayerSets.Corner(l.Transform(sample),corner);min=Vector2.Min(min,new(p.X,p.Y));max=Vector2.Max(max,new(p.X,p.Y));}return(min,max);
	}
	public static void Run(Action<bool,string> check,Action<Action,string> reject)
	{
		PaintLayer Rect(string name,float x,float y,float width,float height)=>new(){Name=name,Kind=LayerKind.Sticker,Asset=name+".tga",Samples=[new(new(x,y,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,width,height)]};
		var left=Rect("Left",-.7f,-.1f,.5f,.3f);var middle=Rect("Center",.05f,.32f,.3f,.8f) with {Rotation=20,TextArtwork=new(){Text="TEAM"}};var right=Rect("Right",.9f,-.23f,.2f,.3f);right.Samples.Add(right.Samples[0] with {Center=new(1.1f,-.4f,0)});var source=new[]{left,middle,right};
		foreach(var operation in new[]{LayoutOperation.Left,LayoutOperation.CenterHorizontal,LayoutOperation.Right,LayoutOperation.Top,LayoutOperation.CenterVertical,LayoutOperation.Bottom})
		{
			var arranged=ArtworkLayout.Arrange(source,operation,false);var bounds=arranged.Select(l=>Bounds([l])).ToArray();
			float Value((Vector2 Min,Vector2 Max) b)=>operation switch {LayoutOperation.Left=>b.Min.X,LayoutOperation.Right=>b.Max.X,LayoutOperation.Top=>b.Max.Y,LayoutOperation.Bottom=>b.Min.Y,LayoutOperation.CenterHorizontal=>(b.Min.X+b.Max.X)*.5f,_=>(b.Min.Y+b.Max.Y)*.5f};
			check(bounds.All(b=>Math.Abs(Value(b)-Value(bounds[0]))<.0001f),"Layout aligns actual transformed bounds: "+operation);
		}
		var anchored=ArtworkLayout.Arrange(source,LayoutOperation.Left,false,reference:middle.Id);check(Vector3.Distance(anchored[1].Samples[0].Center,middle.Transform(middle.Samples[0]).Center)<.0001f&&Math.Abs(Bounds([anchored[0]]).Min.X-Bounds([middle]).Min.X)<.0001f,"Aligning to an active object keeps that object anchored");
		var gaps=ArtworkLayout.Arrange(source,LayoutOperation.HorizontalGaps,false);var g=gaps.Select(l=>Bounds([l])).OrderBy(b=>b.Min.X).ToArray();check(Math.Abs((g[1].Min.X-g[0].Max.X)-(g[2].Min.X-g[1].Max.X))<.0001f&&Math.Abs(g[0].Min.X-Bounds([left]).Min.X)<.0001f&&Math.Abs(g[2].Max.X-Bounds([right]).Max.X)<.0001f,"Equal horizontal gaps account for unequal widths and retain outer endpoints");
		var centers=ArtworkLayout.Arrange(source,LayoutOperation.VerticalCenters,false);var y=centers.Select(l=>Bounds([l])).Select(b=>(b.Min.Y+b.Max.Y)*.5f).Order().ToArray();check(Math.Abs(y[1]-y[0]-(y[2]-y[1]))<.0001f,"Equal vertical centers distribute multi-stamp and rotated artwork");
		var fixedGap=ArtworkLayout.Arrange(source,LayoutOperation.HorizontalSpacing,false,.07f).Select(l=>Bounds([l])).OrderBy(b=>b.Min.X).ToArray();check(Math.Abs(fixedGap[1].Min.X-fixedGap[0].Max.X-.07f)<.0001f&&Math.Abs(fixedGap[2].Min.X-fixedGap[1].Max.X-.07f)<.0001f,"Fixed spacing uses the requested physical gap between actual artwork edges");
		check(left.Samples[0].Center.X==-.7f&&middle.Rotation==20&&gaps[1].TextArtwork?.Text=="TEAM"&&gaps[1].Id==middle.Id,"Layout preserves editable metadata and does not mutate its source layers");
		var group=Guid.NewGuid();var mate=Rect("Group mate",-.45f,.2f,.12f,.1f);left.GroupId=mate.GroupId=group;left.GroupName=mate.GroupName="Sponsor";var grouped=new[]{left,mate,middle,right};
		var together=ArtworkLayout.Arrange(grouped,LayoutOperation.Right,true);var oldDistance=Vector3.Distance(left.Samples[0].Center,mate.Samples[0].Center);check(ArtworkLayout.ObjectCount(grouped,true)==3&&Math.Abs(Vector3.Distance(together[0].Samples[0].Center,together[1].Samples[0].Center)-oldDistance)<.0001f,"Arranging whole groups preserves the internal spacing of each logo");
		var separate=ArtworkLayout.Arrange([left,mate],LayoutOperation.CenterHorizontal,false);check(Math.Abs((Bounds([separate[0]]).Min.X+Bounds([separate[0]]).Max.X)-(Bounds([separate[1]]).Min.X+Bounds([separate[1]]).Max.X))<.0001f,"A group's members can be aligned independently when requested");
		reject(()=>ArtworkLayout.Arrange([left,mate],LayoutOperation.Left,true),"A single compound group cannot be silently broken by whole-group alignment");
		reject(()=>ArtworkLayout.Arrange([left,middle],LayoutOperation.HorizontalGaps,false),"Even distribution requires three layout objects");
		reject(()=>ArtworkLayout.Arrange([left with {Locked=true},middle],LayoutOperation.Left,false),"Layout rejects locked artwork before changing its geometry");
		reject(()=>ArtworkLayout.Arrange(source,LayoutOperation.HorizontalSpacing,false,float.NaN),"Layout rejects non-finite spacing");
		reject(()=>ArtworkLayout.Arrange(source,LayoutOperation.Left,false,reference:Guid.NewGuid()),"Layout rejects an anchor outside the current selection");
		var mirrored=LayerSets.Display(source,true);var flipped=ArtworkLayout.Arrange(mirrored,LayoutOperation.Left,false);check(flipped.Select(l=>Bounds([l]).Min.X).Max()-flipped.Select(l=>Bounds([l]).Min.X).Min()<.0001f,"Alignment also operates in the displayed mirrored projection plane");
	}
}
