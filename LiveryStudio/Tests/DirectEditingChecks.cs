using System.Numerics;
using LiveryStudio.Core;

internal static class DirectEditingChecks
{
	public static void Run(Action<bool,string> check)
	{
		float[] Quad(Vector3 center,Vector3 right,Vector3 up,Vector3 normal)
		{
			var values=new List<float>();foreach(var corner in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,-1),new Vector2(1,1),new Vector2(-1,1)}){var p=center+right*corner.X+up*corner.Y;values.AddRange([p.X,p.Y,p.Z,normal.X,normal.Y,normal.Z,(corner.X+1)*.5f,(1-corner.Y)*.5f,1]);}return values.ToArray();
		}
		var quad=Quad(Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ);var mesh=new PaintMesh([..quad,..Quad(new(5,0,0),Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ)]);
		var hit=mesh.Raycast(new(.5f,0,2),-Vector3.UnitZ)!.Value;
		var projection=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,2);
		var lower=new PaintLayer{Kind=LayerKind.Sticker,Asset="solid",Samples=[projection]};var upper=new PaintLayer{Kind=LayerKind.Sticker,Asset="half",Samples=[projection]};
		PaintImage Image(string name)=>name=="half"?new(2,1,[255,0,0,0,255,0,0,255]):new(1,1,[0,255,0,255]);
		DecalSelection? Pick(SurfaceHit at,params PaintLayer[] layers)=>DecalEditing.Pick(mesh,layers,at,64,64,Image);
		check(Pick(hit,lower,upper)?.LayerId==upper.Id,"Direct selection picks the topmost visible artwork on the rendered UV texel");
		var transparentHit=mesh.Raycast(new(-.5f,0,2),-Vector3.UnitZ)!.Value;
		check(Pick(transparentHit,lower,upper)?.LayerId==lower.Id,"Transparent sticker pixels allow selection of artwork beneath them");
		check(Pick(hit,lower,upper with {Visible=false})?.LayerId==lower.Id&&Pick(hit,lower,upper with {Opacity=0})?.LayerId==lower.Id,"Hidden and zero-opacity artwork never intercept direct selection");
		check(Pick(hit,lower,upper with {FlipHorizontal=true})?.LayerId==lower.Id,"Direct selection respects the flipped artwork's alpha footprint");
		check(Pick(hit,lower,upper with {Triangles=[3]})?.LayerId==lower.Id,"Direct selection obeys the layer's panel isolation mask");
		var shared=mesh.Raycast(new(5.5f,0,2),-Vector3.UnitZ)!.Value;var sharedSelection=Pick(shared,lower,upper);
		check(sharedSelection?.LayerId==upper.Id&&sharedSelection.SharedUv,"Visible artwork can be selected on another panel sharing its texture coordinates");
		check(Pick(hit,lower,upper with {Kind=LayerKind.Surface})?.LayerId==lower.Id,"Material strokes do not obscure decal selection");
		var sides=new PaintMesh([..Quad(Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitY,Vector3.UnitX),..Quad(-Vector3.UnitX,-Vector3.UnitZ,-Vector3.UnitY,-Vector3.UnitX)]);
		var sideLayer=lower with {Mirror=true,Samples=[new(Vector3.UnitX,Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitY,1,1)]};var opposite=sides.Raycast(new(-3,0,0),Vector3.UnitX)!.Value;
		var mirrorSelection=DecalEditing.Pick(sides,[sideLayer],opposite,64,64,Image)!;
		check(mirrorSelection.Mirrored&&!mirrorSelection.SharedUv,"Opposite-side selection identifies the readable mirrored decal rather than a remote UV source");
		var destination=opposite with {Position=new(-1,.2f,.3f)};var moved=DecalEditing.Move(sideLayer,mirrorSelection,destination,new(-3,0,0));var displayed=DecalEditing.DisplayProjection(moved,mirrorSelection);var previous=DecalEditing.DisplayProjection(sideLayer,mirrorSelection);
		check(Vector3.Distance(displayed.Center,destination.Position)<.00001f&&Vector3.Dot(displayed.Up,previous.Up)>.999f&&Vector3.Dot(displayed.Right,previous.Right)>.999f,"Moving the mirrored decal preserves its upright and readable axes");
		check(sideLayer.Samples[0].Center==Vector3.UnitX&&moved.Samples[0].Center==new Vector3(1,.2f,.3f),"A drag produces independent source geometry and keeps the original checkpoint intact");
		var nudged=DecalEditing.DisplayProjection(DecalEditing.Nudge(moved,mirrorSelection,.01f,0),mirrorSelection);check(Vector3.Distance(nudged.Center-displayed.Center,displayed.Right*.01f)<.00001f,"Keyboard nudges follow the selected mirrored side's readable right axis");
		var turned=DecalEditing.DisplayProjection(DecalEditing.Turn(moved,mirrorSelection,15),mirrorSelection);check(Vector3.Distance(turned.Right,displayed.Right*MathF.Cos(MathF.PI/12)+displayed.Up*MathF.Sin(MathF.PI/12))<.00001f,"Keyboard rotation follows the same physical direction on the mirrored side");
		var matrix=Matrix4x4.CreateLookAt(new(0,0,5),Vector3.Zero,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.8f,1.5f,.1f,100);
		var planePoint=new Vector3(.4f,.3f,0);check(DecalEditing.Project(planePoint,matrix,900,600,out var screen)&&DecalEditing.PlanePoint(screen,matrix,900,600,projection,out var restored)&&Vector3.Distance(planePoint,restored)<.00001f,"Perspective-projected handles map back onto the decal plane accurately");
		check(!DecalEditing.Project(new(0,0,6),matrix,900,600,out _),"Selection handles behind the camera are hidden");
		check(Math.Abs(DecalEditing.RotationDelta(Vector3.UnitX,Vector3.UnitY,projection,false,false)-90)<.001f&&Math.Abs(DecalEditing.RotationDelta(Vector3.UnitX,Vector3.UnitY,projection,true,false)+90)<.001f,"Rotation gestures preserve their physical direction on either body side");
		var angle=17*MathF.PI/180;check(DecalEditing.RotationDelta(Vector3.UnitX,new(MathF.Cos(angle),MathF.Sin(angle),0),projection,false,true)==15,"Shift rotation snaps to useful 15-degree increments");
		check(DecalEditing.Resize(1,1,0,false)==.02f&&DecalEditing.Resize(1,1,100,false)==20,"Dragging through the resize center keeps sizes positive and bounded");
		check(Math.Abs(DecalEditing.Resize(1,1,1.13f,true)-1.15f)<.0001f,"Shift resizing snaps the uniform scale without distorting the artwork");
		var doc=new LayerDocument();doc.Commit("Added",l=>l.Add(sideLayer));var count=doc.History.Count;doc.Commit("Moved",l=>l[0]=moved);check(doc.History.Count==count+1&&doc.Undo()&&doc.Layers[0].Samples[0].Center==Vector3.UnitX&&doc.Redo()&&doc.Layers[0].Samples[0].Center==moved.Samples[0].Center,"A completed direct edit is one persistent and reversible history operation");
	}
}
