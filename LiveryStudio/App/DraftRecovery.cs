using System.Text.Json;
using LiveryStudio.Core;

namespace LiveryStudio;

internal static class DraftRecovery
{
    internal static string Save(LiveryProject draft,string workspace)
    {
        var root=Path.Combine(workspace,"recovery");Directory.CreateDirectory(root);
        var stem=Path.Combine(root,Guid.NewGuid().ToString("N"));
        try{ProjectBundle.Save(draft,stem+".flproject");return stem+".flproject";}
        catch(Exception error) when(error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            // An unfinished form still deserves recovery. Copy every available
            // resource; missing references and unfinished field text stay honest.
            draft=CopyAvailable(draft,stem+".assets");
            var file=stem+".draft.json";AtomicFiles.Write(file,JsonSerializer.SerializeToUtf8Bytes(draft,Contract.Json));return file;
        }
    }
    internal static bool IsIncomplete(string file)=>file.EndsWith(".draft.json",StringComparison.OrdinalIgnoreCase);
    internal static LiveryProject Open(string file,string workspace)
    {
        if(!IsIncomplete(file))return ProjectBundle.Open(file,workspace);
        var draft=LiveryProject.Read(file);
        return CopyAvailable(draft,Path.Combine(workspace,"projects",Guid.NewGuid().ToString("N")));
    }
    private static LiveryProject CopyAvailable(LiveryProject source,string root)
    {
        Directory.CreateDirectory(root);var copied=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string Include(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return path;
            var original=Path.GetFullPath(path);if(copied.TryGetValue(original,out var previous))return previous;
            var file=Path.Combine(root,copied.Count.ToString("0000")+Path.GetExtension(path));File.Copy(original,file);copied.Add(original,file);return file;
        }
        var draft=source with{Albedo=Include(source.Albedo),Structure=Include(source.Structure),Banner=Include(source.Banner),EditorFile=Include(source.EditorFile)};
        if(File.Exists(draft.EditorFile))
        {
            LayerDocument? document=null;
            try
            {
                if(new FileInfo(draft.EditorFile).Length<=32*1024*1024)
                {
                    document=JsonSerializer.Deserialize<LayerDocument>(File.ReadAllText(draft.EditorFile),LayerDocument.Json);
                    document?.Validate(files:false);
                }
            }
            catch(Exception e) when(e is JsonException or InvalidDataException or ArgumentException or NullReferenceException){document=null;}
            // Preserve malformed editor bytes too; they remain an incomplete draft,
            // and are never used to overwrite the source editor or its maps.
            if(document is not null)
            {
                document.BaseAlbedo=Include(document.BaseAlbedo);document.BaseStructure=Include(document.BaseStructure);
                foreach(var state in document.History)foreach(var layer in state.Layers)
                {
                    layer.Asset=Include(layer.Asset);
                    if(layer.VectorArtwork is {} vector)layer.VectorArtwork=vector with{SourceFile=Include(vector.SourceFile)};
                    if(layer.RasterArtwork is {} raster)layer.RasterArtwork=raster with{SourceFile=Include(raster.SourceFile)};
                    if(layer.Effects is {} effects)layer.Effects=effects with{StencilFile=Include(effects.StencilFile)};
                }
                AtomicFiles.Write(draft.EditorFile,JsonSerializer.SerializeToUtf8Bytes(document,LayerDocument.Json));
            }
        }
        return draft;
    }
}
