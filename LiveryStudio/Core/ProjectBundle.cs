using System.IO.Compression;
using System.Text.Json;

namespace LiveryStudio.Core;

public static class ProjectBundle
{
	public static void Save(LiveryProject source,string file)
	{
		source.Validate();var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
		try
		{
			using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))
			{
				var resources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
				string Include(string path)
				{
					if(path.Length==0)return "";var absolute=Path.GetFullPath(path);if(resources.TryGetValue(absolute,out var stored))return stored;
					if(!File.Exists(absolute)||new FileInfo(absolute).Length>300*1024*1024)throw new InvalidDataException("Missing or oversized project asset: "+Path.GetFileName(path));
					stored="assets/"+resources.Count.ToString("0000")+Path.GetExtension(path).ToLowerInvariant();zip.CreateEntryFromFile(absolute,stored,CompressionLevel.Optimal);resources.Add(absolute,stored);return stored;
				}
				var project=source with {Albedo=Include(source.Albedo),Structure=Include(source.Structure),Banner=Include(source.Banner)};
				if(source.EditorFile.Length>0)
				{
					var document=LayerDocument.Read(source.EditorFile);document.BaseAlbedo=Include(document.BaseAlbedo);document.BaseStructure=Include(document.BaseStructure);
					foreach(var state in document.History)foreach(var layer in state.Layers){if(layer.Asset.Length>0)layer.Asset=Include(layer.Asset);if(layer.VectorArtwork is {} vector)layer.VectorArtwork=vector with {SourceFile=Include(vector.SourceFile)};if(layer.RasterArtwork is {} raster)layer.RasterArtwork=raster with {SourceFile=Include(raster.SourceFile)};if(layer.Effects is {StencilFile.Length:>0} effects)layer.Effects=effects with {StencilFile=Include(effects.StencilFile)};}
					project.EditorFile="editor.json";using var writer=new StreamWriter(zip.CreateEntry("editor.json").Open());writer.Write(JsonSerializer.Serialize(document,LayerDocument.Json));
				}
				using var projectWriter=new StreamWriter(zip.CreateEntry("project.json").Open());projectWriter.Write(JsonSerializer.Serialize(project,Contract.Json));
			}
			File.Move(temp,file,true);
		}
		finally{if(File.Exists(temp))File.Delete(temp);}
	}
	public static LiveryProject Open(string file,string workspace)
	{
		using var zip=ZipFile.OpenRead(file);if(zip.Entries.Count>1500||zip.Entries.Sum(e=>e.Length)>1024L*1024*1024)throw new InvalidDataException("This project archive is too large.");
		var root=Path.GetFullPath(Path.Combine(workspace,"projects",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
		var entries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string Resolve(string name)
		{
			if(string.IsNullOrWhiteSpace(name)||name.Contains(':')||name.Contains('\\')||name.StartsWith('/')||name.Split('/').Any(p=>p is ".." or "."))throw new InvalidDataException("Unsafe project asset path.");
			var path=Path.GetFullPath(Path.Combine(root,name));if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe project asset path.");return path;
		}
		try
		{
			foreach(var entry in zip.Entries)
			{
				if(entry.FullName.EndsWith('/'))continue;if(entry.Length>300*1024*1024||!entries.Add(entry.FullName))throw new InvalidDataException("Duplicate or oversized project asset.");
				var path=Resolve(entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,false);
			}
			var project=LiveryProject.Read(Resolve("project.json"));project.Albedo=Resolve(project.Albedo);project.Structure=Resolve(project.Structure);if(project.Banner.Length>0)project.Banner=Resolve(project.Banner);
			if(project.EditorFile.Length>0)
			{
				var path=Resolve(project.EditorFile);if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Editor history is too large.");
				var document=JsonSerializer.Deserialize<LayerDocument>(File.ReadAllText(path),LayerDocument.Json)??throw new InvalidDataException("Invalid editor document.");
				document.BaseAlbedo=Resolve(document.BaseAlbedo);document.BaseStructure=Resolve(document.BaseStructure);
				foreach(var state in document.History)foreach(var layer in state.Layers){if(layer.Asset.Length>0)layer.Asset=Resolve(layer.Asset);if(layer.VectorArtwork is {} vector)layer.VectorArtwork=vector with {SourceFile=Resolve(vector.SourceFile)};if(layer.RasterArtwork is {} raster)layer.RasterArtwork=raster with {SourceFile=Resolve(raster.SourceFile)};if(layer.Effects is {StencilFile.Length:>0} effects)layer.Effects=effects with {StencilFile=Resolve(effects.StencilFile)};}
				document.Save(path);project.EditorFile=path;
			}
			project.Validate();return project;
		}
		catch
		{
			// Only remove this operation's fresh, validated destination on an incomplete import.
			if(root.StartsWith(Path.GetFullPath(workspace)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);throw;
		}
	}
}
