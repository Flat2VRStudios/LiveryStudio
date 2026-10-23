using System.Numerics;
using LiveryStudio.Core;

internal static class CreativeChecks
{
	public static void Run(Action<bool,string> check,Action<Action,string> reject)
	{
		var p=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,1);
		var layer=new PaintLayer{Kind=LayerKind.Sticker,Scale=2,ScaleX=1.5f,ScaleY=.5f,FlipHorizontal=true,FlipVertical=true,WrapAngle=55,WrapDepth=.08f,Samples=[p]};
		var changed=layer.Transform(p);
		check(changed.Width==6&&changed.Height==1,"Decal width and height stretch independently while preserving legacy uniform scale");
		check(changed.Map(new(-1.5f,.25f,0),Vector3.UnitZ,out var uv)&&Vector2.Distance(uv,new(.75f,.75f))<.0001f,"Flipped artwork maps both axes inside the transformed footprint");
		check(!changed.Map(new(0,0,.6f),Vector3.UnitZ,out _),"Flat-logo projection rejects nearby surfaces beyond its depth limit");
		check((changed with {DepthRange=1}).Map(new(0,0,.6f),Vector3.UnitZ,out _),"A deeper wrap includes the same nearby surface");
		var bentNormal=Vector3.Normalize(new Vector3(1,0,.12f));
		check(!p.Map(Vector3.Zero,bentNormal,out _)&&(p with {MinimumFacing=.05f}).Map(Vector3.Zero,bentNormal,out _),"Wrap angle explicitly controls continuation around a curved panel");
		var camera=SurfaceProjection.At(new(Vector3.Zero,Vector3.Normalize(new Vector3(1,0,1)),Vector2.Zero,0,1),Vector3.UnitX,1,1,0,Vector3.UnitZ);
		check(camera.Normal==Vector3.UnitZ&&camera.Right==Vector3.UnitX,"Camera alignment keeps straight artwork in the camera projection plane");
		var legacy=new PaintLayer{Samples=[p]}.Transform(p);
		check(legacy.Width==p.Width&&legacy.Height==p.Height&&Math.Abs(legacy.MinimumFacing-.25f)<.00001f&&legacy.DepthRange==.35f,"Legacy layers retain their original projection range and dimensions");
		var oldDocument=new LayerDocument{Version=1};oldDocument.Validate(files:false);oldDocument.Commit("Added flexible layer",layers=>layers.Add(layer));check(oldDocument.Version==3,"Opening legacy history remains supported and new edits declare the richer document format");
        reject(()=>new LayerDocument{Version=10}.Validate(files:false),"Reject unknown future editor formats instead of silently dropping their features");
		reject(()=>(layer with {ScaleX=float.NaN}).Validate(),"Reject invalid independent decal stretch");
		reject(()=>(layer with {WrapDepth=-1}).Validate(),"Reject invalid wrap depth");
		reject(()=>(layer with {WrapAngle=180}).Validate(),"Reject projection angles that would paint through the opposite side");
	}
}
