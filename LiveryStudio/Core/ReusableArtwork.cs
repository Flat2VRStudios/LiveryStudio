using System.Numerics;

namespace LiveryStudio.Core;
public sealed record ReusableGroup(string Name,string Path);
public static class ReusableArtwork
{
	public static string Folder(string workspace)=>Path.Combine(workspace,"artwork-groups");
	public static IReadOnlyList<ReusableGroup> List(string workspace)
	{
		var folder=Folder(workspace);Directory.CreateDirectory(folder);return Directory.EnumerateFiles(folder,"*.flart").OrderByDescending(File.GetLastWriteTimeUtc).Select(p=>{var name=Path.GetFileNameWithoutExtension(p);var suffix=name.LastIndexOf("--",StringComparison.Ordinal);if(suffix>0&&name.Length-suffix==10&&name[(suffix+2)..].All(Uri.IsHexDigit))name=name[..suffix];return new ReusableGroup(name,p);}).ToArray();
	}
	public static string Save(string name,IReadOnlyList<PaintLayer> layers,string workspace)
	{
		name=name.Trim();if(name.Length<1||name.Length>80||layers.Count<1||layers.Count>128)throw new ArgumentException("Name your artwork and select 1–128 layers.");
		var frame=LayerSets.Frame(layers);var plane=new SurfaceProjection(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,frame.Width,frame.Height);
		var normalized=LayerSets.Transform(layers.Select(l=>l with {Locked=false,Triangles=[]}).ToArray(),frame,plane,planar:true);var groupId=Guid.NewGuid();foreach(var layer in normalized){layer.GroupId=groupId;layer.GroupName=name;}
		var parent=Path.GetFullPath(Path.Combine(workspace,"artwork-group-work"));Directory.CreateDirectory(parent);var root=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
		try
		{
			var blank=Path.Combine(root,"blank.tga");FinishMaps.WriteTga(new(64,64,new byte[64*64*4]),blank);var editor=Path.Combine(root,"editor.json");
			var document=new LayerDocument{BaseAlbedo=blank,BaseStructure=blank,History=[new("Reusable artwork",DateTimeOffset.UtcNow,normalized){Palette=DesignPalette.LinkedColors(normalized).ToList()}]};document.Save(editor);
			var fileName=string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));var folder=Folder(workspace);Directory.CreateDirectory(folder);var destination=Path.Combine(folder,fileName+"--"+Guid.NewGuid().ToString("N")[..8]+".flart");
			ProjectBundle.Save(new(){Name=name,Albedo=blank,Structure=blank,EditorFile=editor},destination);return destination;
		}
		finally{if(Path.GetFullPath(root).StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
	}
	public static List<PaintLayer> Open(string path,string workspace)
	{
		var project=ProjectBundle.Open(path,workspace);var doc=LayerDocument.Read(project.EditorFile);if(doc.Layers.Count<1||doc.Layers.Count>128)throw new InvalidDataException("Reusable artwork supports 1–128 layers.");return doc.Layers;
	}
	public static string Import(string path,string workspace)
	{
		var layers=Open(path,workspace);var name=layers[0].GroupName.Length>0?layers[0].GroupName:Path.GetFileNameWithoutExtension(path);return Save(name,layers,workspace);
	}
	public static PaintImage PreviewFile(string path,int size=128)
	{
		// A shelf thumbnail must not create a recoverable project or retain unpacked assets.
		var root=Path.Combine(Path.GetTempPath(),"LiveryStudioArtworkPreview",Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try{return Preview(Open(path,root),size);}
		finally{Directory.Delete(root,true);}
	}
	public static List<PaintLayer> Place(IReadOnlyList<PaintLayer> source,SurfaceProjection target,bool mirror,float opacity=1,bool flipHorizontal=false,bool flipVertical=false)
	{
		if(!float.IsFinite(opacity)||opacity<0||opacity>1)throw new ArgumentException("Choose an opacity between zero and one.");
		var copies=LayerSets.Duplicate(source);var roleIds=DesignPalette.LinkedColors(copies).ToDictionary(c=>c.Id,_=>Guid.NewGuid());foreach(var layer in copies)foreach(var (key,role) in layer.ColorLinks.ToArray())layer.ColorLinks[key]=role with {Id=roleIds[role.Id]};var layers=LayerSets.Fit(copies,target,flipHorizontal,flipVertical);var group=Guid.NewGuid();
		foreach(var layer in layers){layer.GroupId=group;layer.GroupName=source[0].GroupName.Length>0?source[0].GroupName:"Reusable artwork";layer.Triangles=[];layer.Mirror=mirror;layer.Opacity*=opacity;}return layers;
	}
	public static PaintImage Preview(IReadOnlyList<PaintLayer> layers,int size=512)
	{
		if(size<32||size>2048)throw new ArgumentOutOfRangeException(nameof(size));var frame=LayerSets.Frame(layers);var ratio=frame.Width/frame.Height;
		var width=ratio>=1?size:Math.Max(1,(int)MathF.Round(size*ratio));var height=ratio>=1?Math.Max(1,(int)MathF.Round(size/ratio)):size;
		var pixels=new byte[width*height*4];var assets=new DecalImageCache();
		foreach(var layer in layers.Where(l=>l.Visible&&!l.IsMaterial))
		{
			var ink=PaintFinish.Color(layer.Color);var image=layer.Kind==LayerKind.Sticker?assets.Get(layer.Asset,layer.Effects):null;
			foreach(var sample in layer.Samples)
			{
				var p=layer.Transform(sample);
				for(var y=0;y<height;y++)for(var x=0;x<width;x++)
				{
					var point=frame.Center+frame.Right*((x+.5f)/width-.5f)*frame.Width+frame.Up*(.5f-(y+.5f)/height)*frame.Height;
					if(!p.Map(point,frame.Normal,out var uv))continue;var color=ink;var alpha=layer.Opacity;
					if(image is not null){var k=(Math.Clamp((int)(uv.Y*image.Height),0,image.Height-1)*image.Width+Math.Clamp((int)(uv.X*image.Width),0,image.Width-1))*4;alpha*=image.Pixels[k+3]/255f;color=new(image.Pixels[k]/255f,image.Pixels[k+1]/255f,image.Pixels[k+2]/255f);}
					else {var radius=((uv-new Vector2(.5f))*2).Length();if(radius>1)continue;alpha*=Math.Clamp((1-radius)/MathF.Max(.02f,1-layer.Hardness),0,1);}
					if(alpha<=.001f)continue;var at=(y*width+x)*4;var oldAlpha=pixels[at+3]/255f;var outAlpha=alpha+oldAlpha*(1-alpha);var old=new Vector3(pixels[at]/255f,pixels[at+1]/255f,pixels[at+2]/255f);var blended=DecalEffects.Mix(old,color,layer.Effects?.Blend??DecalBlend.Normal);var rgb=(color*alpha*(1-oldAlpha)+blended*alpha*oldAlpha+old*oldAlpha*(1-alpha))/outAlpha;
					pixels[at]=(byte)MathF.Round(rgb.X*255);pixels[at+1]=(byte)MathF.Round(rgb.Y*255);pixels[at+2]=(byte)MathF.Round(rgb.Z*255);pixels[at+3]=(byte)MathF.Round(outAlpha*255);
				}
			}
		}
		return new(width,height,pixels);
	}
}
