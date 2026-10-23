using System.Numerics;
using System.Text.Json;
using System.IO.Compression;
using LiveryStudio.Core;

internal static class EditorChecks
{
	public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
	{
		var folder=Path.Combine(temp,"editor");Directory.CreateDirectory(folder);
		var zero=new PaintImage(64,64,new byte[64*64*4]);var surface=new PaintImage(64,64,Enumerable.Range(0,64*64).SelectMany(_=>new byte[]{40,90,23,45}).ToArray());
		var colorFile=Path.Combine(folder,"original.tga");var surfaceFile=Path.Combine(folder,"surface.tga");var stickerFile=Path.Combine(folder,"red.tga");FinishMaps.WriteTga(zero,colorFile);FinishMaps.WriteTga(surface,surfaceFile);FinishMaps.WriteTga(new(1,1,[255,0,0,255]),stickerFile);
		var project=new LiveryProject{CarIndex=0,Albedo=colorFile,Structure=surfaceFile,InnerColor="#00FF00",OuterColor="#00FF00",UseSrgbPaintColors=true,InnerGloss=.8,Metalness=.7};
		float[] quad=[-1,-1,0,0,0,1,0,1,1,1,-1,0,0,0,1,1,1,1,1,1,0,0,0,1,1,0,1,-1,-1,0,0,0,1,0,1,1,1,1,0,0,0,1,1,0,1,-1,1,0,0,0,1,0,0,1];
		var mesh=new PaintMesh(quad);var hit=mesh.Raycast(new(0,0,2),-Vector3.UnitZ)!.Value;var projection=SurfaceProjection.At(hit,Vector3.UnitX,1,1,0);
		var doc=LayerDocument.Create(project,Path.Combine(folder,"document"));doc.SeamPadding=0;var layer=new PaintLayer{Name="Red badge",Kind=LayerKind.Sticker,Asset=stickerFile,Samples=[projection]};doc.Commit("Added badge",l=>l.Add(layer));
		var center=(32*64+32)*4;var maps=doc.Compose(mesh,project);check(maps.Albedo.Pixels[center]==255&&maps.Albedo.Pixels[center+1]==0,"Editable decal composes onto the original UV atlas");
		check(FinishMaps.Read(colorFile).Pixels.SequenceEqual(zero.Pixels),"Layer compositing preserves original RGBA source bytes");
		check(doc.Undo()&&doc.Compose(mesh,project).Albedo.Pixels.SequenceEqual(zero.Pixels),"Document undo removes a layer without changing its source texture");check(doc.Redo()&&doc.Layers.Count==1,"Document redo restores the retained decal object");
		doc.Commit("Hide badge",l=>l[0].Visible=false);check(doc.Compose(mesh,project).Albedo.Pixels.SequenceEqual(zero.Pixels),"Hiding a layer restores the paint mask without destructive editing");doc.Undo();
		doc.Commit("Opacity",l=>l[0].Opacity=.5f);maps=doc.Compose(mesh,project);check(maps.Albedo.Pixels[center]==128&&maps.Albedo.Pixels[center+1] is >=127 and <=128,"Editable layer opacity blends artwork with base paint");doc.Undo();
		var transformed=layer with {Scale=2,Rotation=90,OffsetRight=.2f,OffsetUp=.1f};var placed=transformed.Transform(projection);check(Vector3.Distance(placed.Center,new(.2f,.1f,0))<.001f&&Math.Abs(placed.Width-2)<.001f&&Vector3.Distance(placed.Right,Vector3.UnitY)<.001f,"Layer transforms retain world-space scale, rotation and position");
		var reflected=PaintLayer.Reflected(projection with {Center=new(1,0,0),Normal=Vector3.UnitX,Right=Vector3.UnitZ,Up=Vector3.UnitY});check(reflected.Center.X==-1&&reflected.Normal.X==-1&&reflected.Up==Vector3.UnitY,"Symmetry reflects placement across the car while keeping artwork upright");
		doc.Commit("Material layer",l=>l.Add(new(){Name="Matte panel",Kind=LayerKind.Surface,Gloss=.15f,Metalness=.9f,Samples=[projection],Hardness=1}));maps=doc.Compose(mesh,project);
		check(maps.Structure.Pixels[center]==38&&maps.Structure.Pixels[center+1]==230&&maps.Structure.Pixels[center+2]==23&&maps.Structure.Pixels[center+3]==45,"Surface layers set gloss and metalness while preserving blue masks and alpha");
		check(maps.Albedo.Pixels[center]==255&&maps.Albedo.Pixels[center+1]==0,"A material brush preserves existing decal RGB");
		var path=Path.Combine(folder,"document","editor.json");doc.Save(path);var restored=LayerDocument.Read(path);check(restored.Cursor==doc.Cursor&&restored.Layers[0].Samples[0].Normal==Vector3.UnitZ,"Editor serialization restores vector geometry and history cursor");
		check(restored.Undo()&&restored.Redo()&&restored.HasMaterial,"Undo and redo survive restarting the editor");
		doc.Commit("Rename",l=>l[0].Name="Badge renamed");doc.Save(path);File.WriteAllText(path,"{truncated");check(LayerDocument.Read(path).Layers[0].Name=="Red badge","A truncated editor save recovers the last complete checkpoint");doc.Save(path);
		doc.Commit("Flexible decal",layers=>{layers[0].ScaleX=1.5f;layers[0].ScaleY=.75f;layers[0].FlipHorizontal=true;layers[0].WrapAngle=55;layers[0].WrapDepth=.08f;layers[0].AlignWithCamera=true;layers[0].TextArtwork=new(){Text="TEAM FURY",FontName="Segoe UI",OutlineWidth=.04f};});doc.Save(path);
		project.EditorFile=path;var portable=Path.Combine(folder,"Design.flproject");ProjectBundle.Save(project,portable);File.Delete(colorFile);File.Delete(surfaceFile);File.Delete(stickerFile);
		var imported=ProjectBundle.Open(portable,Path.Combine(folder,"portable"));var importedDoc=LayerDocument.Read(imported.EditorFile);check(importedDoc.Compose(mesh,imported).Albedo.Pixels[center]==255,"Portable projects recover retained layers after original external files disappear");
		check(importedDoc.Layers[0].ScaleX==1.5f&&importedDoc.Layers[0].ScaleY==.75f&&importedDoc.Layers[0].FlipHorizontal&&importedDoc.Layers[0].WrapAngle==55&&importedDoc.Layers[0].WrapDepth==.08f&&importedDoc.Layers[0].AlignWithCamera,"Portable projects preserve stretch, artwork flip, projection alignment and wrap settings");
		check(importedDoc.Layers[0].TextArtwork is {Text:"TEAM FURY",FontName:"Segoe UI",OutlineWidth:.04f},"Portable projects retain editable text, font and outline settings independently of the installed font");
		check(importedDoc.BaseAlbedo.StartsWith(Path.Combine(folder,"portable"))&&importedDoc.Layers.All(l=>l.Asset.Length==0||l.Asset.StartsWith(Path.Combine(folder,"portable"))),"Portable editor assets resolve only inside the fresh project folder");
		var malicious=Path.Combine(folder,"unsafe.flproject");using(var zip=ZipFile.Open(malicious,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open());writer.Write("unsafe");}reject(()=>ProjectBundle.Open(malicious,Path.Combine(folder,"unsafe-output")),"Reject project archives with path traversal");
		reject(()=>(layer with {Scale=float.NaN}).Validate(),"Reject nonfinite layer transforms");reject(()=>(layer with {Samples=[projection with {Normal=Vector3.Zero}]}).Validate(),"Reject invalid projection normals");
		var shared=new PaintMesh([..quad,..quad.Select((v,i)=>i%9==2?v+1:v)]);check(MeshEditing.SharedUvCount(shared,hit)==1,"Shared-UV diagnostics identify a second physical surface using the same texture region");
		check(MeshEditing.Panel(mesh,hit).Length==2,"Surface isolation follows connected facing triangles");var painted=new TexturePainter(zero);painted.Stamp(mesh,projection,new(1,1,[255,0,0,255]),Vector3.UnitX,1,Vector3.UnitY,new HashSet<int>{0});check(painted.Image.Pixels[(45*64+20)*4]==255&&painted.Image.Pixels[(20*64+20)*4]==0,"A triangle mask restricts rasterization to the selected surface");
		var padding=new PaintImage(3,1,[0,0,0,0,255,10,20,255,0,0,0,0]);MeshEditing.Bleed(padding,[false,true,false],1);check(padding.Pixels.SequenceEqual(new byte[]{255,10,20,255,255,10,20,255,255,10,20,255}),"Seam padding extends border pixels and leaves mapped texels intact");
		foreach(var shape in new[]{"Circle","Ring","Chevron","Racing stripe","Number"}){var generated=ArtworkLibrary.Shape(shape,"27","#21DCC1");check(generated.Pixels.Where((_,i)=>i%4==3).Any(a=>a==255)&&generated.Pixels[3]==0,"Reusable generated artwork has transparent edges: "+shape);}
		reject(()=>ArtworkLibrary.Shape("Number","oops","#FFFFFF"),"Racing-number generation rejects invalid digits");
	}
}
