using System.Numerics;
using LiveryStudio.Core;

internal static class GroupChecks
{
	public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
	{
		var a=new PaintLayer{Kind=LayerKind.Sticker,Name="Team",TextArtwork=new(){Text="TEAM"},Asset=Path.Combine(temp,"group-team.tga"),Samples=[new(new(-.4f,0,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.4f,.2f)],ScaleX=1.2f,Rotation=10};
		var b=new PaintLayer{Kind=LayerKind.Color,Name="Circle",Color="#FF0000",Samples=[new(new(.3f,0,0),Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,.3f,.3f)]};FinishMaps.WriteTga(new(1,1,[255,255,255,128]),a.Asset);
		var layers=new List<PaintLayer>{a,b};var ids=layers.Select(l=>l.Id).ToHashSet();LayerSets.Group(layers,ids,"Team badge");
		check(a.GroupId==b.GroupId&&a.GroupId.HasValue&&a.GroupName=="Team badge","Grouping retains individual text and shape layers under one named identity");
		check(LayerSets.ExpandGroups(layers,[a.Id]).Length==2,"Clicking a group member can select the whole group");
		var edited=LayerSets.Edit(layers,right:.2f,up:.1f,scale:2,rotation:90);var before=Vector3.Distance(a.Transform(a.Samples[0]).Center,b.Transform(b.Samples[0]).Center);var after=Vector3.Distance(edited[0].Samples[0].Center,edited[1].Samples[0].Center);
		check(Math.Abs(after-before*2)<.0001f&&edited[0].TextArtwork?.Text=="TEAM"&&edited[0].Samples[0].Width>a.Samples[0].Width,"A shared transform scales spacing and artwork together while retaining editable text");
		check(a.Samples[0].Center.X==-.4f&&a.Rotation==10&&edited[0].Rotation==0,"Group transforms create independent canonical geometry without mutating earlier source");
		var fitTarget=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,2,.4f);var fitted=LayerSets.Fit(layers,fitTarget);var fitBounds=LayerSets.Frame(fitted);check(Math.Abs(fitBounds.Width-2)<.0001f&&Math.Abs(fitBounds.Height-.4f)<.0001f,"Uneven group stretch preserves the exact width and height of rotated components");
		var sourceFrame=LayerSets.Frame(layers);var sourceCorner=LayerSets.Corner(a.Transform(a.Samples[0]),new(.2f,-.3f));var mapped=fitTarget.Center+fitTarget.Right*(Vector3.Dot(sourceCorner-sourceFrame.Center,sourceFrame.Right)*fitTarget.Width/sourceFrame.Width)+fitTarget.Up*(Vector3.Dot(sourceCorner-sourceFrame.Center,sourceFrame.Up)*fitTarget.Height/sourceFrame.Height);
		check(fitted[0].Samples[0].Map(mapped,Vector3.UnitZ,out var fitUv)&&Vector2.Distance(fitUv,new(.7f,.8f))<.0001f,"Stretched rotated artwork retains its exact original texture coordinates");
		var flipped=LayerSets.Fit(layers,fitTarget,flipHorizontal:true,flipVertical:true);var flipPoint=new Vector3(-mapped.X,-mapped.Y,0);check(flipped[0].Samples[0].Map(flipPoint,Vector3.UnitZ,out var flipUv)&&Vector2.Distance(flipUv,fitUv)<.0001f,"Whole-group flips reflect component positions and artwork consistently");
		var individual=fitted[0] with {Rotation=37};individual.Validate();var rotated=individual.Transform(individual.Samples[0]);check(Math.Abs(rotated.Right.Length()-1)<.0001f&&Math.Abs(rotated.Up.Length()-1)<.0001f&&Math.Abs(Vector3.Dot(rotated.Right,rotated.Up)-Vector3.Dot(fitted[0].Samples[0].Right,fitted[0].Samples[0].Up))<.0001f,"Individual rotation preserves the oblique projection produced by uneven group stretch");
		var mirrored=LayerSets.Display(layers,true);var mirrorFrame=LayerSets.Frame(mirrored);var movedMirrored=LayerSets.Display(LayerSets.Edit(mirrored,right:.1f,up:.2f),true);var actualDelta=movedMirrored[0].Samples[0].Center-a.Transform(a.Samples[0]).Center;var shownDelta=mirrorFrame.Right*.1f+mirrorFrame.Up*.2f;shownDelta.X=-shownDelta.X;check(Vector3.Distance(actualDelta,shownDelta)<.0001f,"Nudging a mirrored group maps the displayed direction back to every original member");
		var reversed=LayerSets.Display(LayerSets.Display(layers,true),true);check(Vector3.Distance(reversed[0].Samples[0].Center,a.Transform(a.Samples[0]).Center)<.0001f&&Vector3.Distance(reversed[0].Samples[0].Right,a.Transform(a.Samples[0]).Right)<.0001f,"Mirrored display conversion is reversible without changing component geometry");
		var c=b with {Id=Guid.NewGuid(),Name="Above"};var order=new List<PaintLayer>{a,b,c};LayerSets.Reorder(order,ids,1);check(order.Select(l=>l.Id).SequenceEqual(new[]{c.Id,a.Id,b.Id}),"Moving a selected block forward preserves its member order");LayerSets.Reorder(order,ids,-1);check(order.Select(l=>l.Id).SequenceEqual(new[]{a.Id,b.Id,c.Id}),"Moving a selected block backward restores its original order");
		var copies=LayerSets.Duplicate(layers);check(copies.All(l=>!ids.Contains(l.Id))&&copies[0].GroupId==copies[1].GroupId&&copies[0].GroupId!=a.GroupId,"Duplicating a group assigns independent layer and group identities");
		var doc=new LayerDocument();doc.Commit("Added",l=>l.AddRange(layers));doc.Commit("Lock",l=>l[0].Locked=true);var cursor=doc.Cursor;
		reject(()=>doc.Commit("Accidental move",l=>l[0].OffsetUp=1),"Locked geometry cannot be changed through any document mutation");
		reject(()=>doc.Commit("Accidental removal",l=>l.RemoveAt(0)),"Locked layers cannot be removed accidentally");check(doc.Cursor==cursor&&doc.Layers[0].OffsetUp==0,"A rejected locked edit leaves history and source intact");
		doc.Commit("Unlock",l=>l[0].Locked=false);doc.Commit("Move",l=>l[0].OffsetUp=1);check(doc.Layers[0].OffsetUp==1&&doc.Undo()&&doc.Undo()&&doc.Layers[0].Locked,"Lock state participates in persistent undo and redo");
		var library=Path.Combine(temp,"group-library");var saved=ReusableArtwork.Save("Team badge",layers,library);var preview=ReusableArtwork.Preview(layers,128);check(preview.Pixels.Where((_,i)=>i%4==3).Any(x=>x==128)&&preview.Pixels.Where((_,i)=>i%4==3).Any(x=>x==0),"Reusable artwork previews preserve partial alpha and transparent backgrounds");
		File.Delete(a.Asset);var restored=ReusableArtwork.Open(saved,Path.Combine(temp,"group-reopen"));check(restored.Count==2&&restored[0].TextArtwork?.Text=="TEAM"&&File.Exists(restored[0].Asset),"Reusable group archives restore independent editable assets after the originals are gone");
		var target=new SurfaceProjection(new(2,1,0),Vector3.UnitX,Vector3.UnitZ,-Vector3.UnitY,1,1);var placed=ReusableArtwork.Place(restored,target,mirror:true);var frame=LayerSets.Frame(placed);
		check(Math.Abs(frame.Width-1)<.0001f&&Vector3.Dot(placed[0].Samples[0].Normal,Vector3.UnitX)>.999f&&placed.All(l=>l.Mirror&&l.Triangles.Length==0),"Reusable logos scale and reproject onto another car without retaining foreign panel indices");
		check(placed[0].GroupId!=restored[0].GroupId&&placed[0].Id!=restored[0].Id,"Every reusable stamp creates an independent editable group");
		check(ReusableArtwork.List(library).Single().Path==saved,"Saved compound artwork appears in its reusable library");
		var importWorkspace=Path.Combine(temp,"group-import");var imported=ReusableArtwork.Import(saved,importWorkspace);File.Delete(saved);var importedLayers=ReusableArtwork.Open(imported,Path.Combine(temp,"group-import-open"));check(importedLayers.Count==2&&importedLayers[0].TextArtwork?.Text=="TEAM"&&FinishMaps.Read(importedLayers[0].Asset).Pixels[3]==128,"Importing portable artwork keeps independent assets after the external archive is removed");
		var dashed=ReusableArtwork.Save("TEAM--27",importedLayers,importWorkspace);check(ReusableArtwork.List(importWorkspace).First(g=>g.Path==dashed).Name=="TEAM--27","Reusable library names preserve double dashes before their unique file suffix");
		LayerSets.Ungroup(layers,ids);check(layers.All(l=>l.GroupId is null&&l.GroupName==""),"Ungrouping preserves components while clearing their shared identity");
		reject(()=>LayerSets.Edit([a with {Locked=true},b]),"An operation cannot move an unlocked member away from a locked group mate");
		new LayerDocument{Version=2}.Validate(files:false);check(new LayerDocument().Version==3,"Legacy editor documents remain readable and new group documents declare version 3");
	}
}
