using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveryStudio.Core;

public enum ArtworkCollection { All, Favorites, Recent }
public enum ArtworkCategory { All, Decals, Groups }
public sealed record BrowseArtwork(string Name,string Path,string Key,bool IsGroup,string Description,DateTime Modified);
public sealed class ArtworkBrowsingState
{
    public int Version {get;set;}=1;
    public List<string> Favorites {get;set;}=[];
    public List<string> Recent {get;set;}=[];
    public void Validate()
    {
        if(Version!=1||Favorites is null||Recent is null||Favorites.Count>512||Recent.Count>48)throw new InvalidDataException("Unsupported artwork library choices.");
        foreach(var list in new[]{Favorites,Recent})
        {
            foreach(var key in list)ArtworkBrowsing.ValidateKey(key);
            if(list.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Count)throw new InvalidDataException("Duplicate artwork library choices.");
        }
    }
}

// Browsing preferences never write artwork, project files, or undo history.
public static class ArtworkBrowsing
{
    private static string StatePath(string workspace)=>Path.Combine(workspace,"artwork-browsing.json");
    internal static void ValidateKey(string key)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>300||key.Any(char.IsControl)||key.Contains('\\')||key.Contains(':'))throw new InvalidDataException("Invalid artwork library key.");
        var parts=key.Split('/');
        if(parts.Length!=2||parts.Any(p=>p is "" or "." or ".."||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0)||
            !(parts[0]=="artwork"&&new[]{".png",".tga"}.Contains(Path.GetExtension(parts[1]).ToLowerInvariant())||parts[0]=="artwork-groups"&&Path.GetExtension(parts[1]).Equals(".flart",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid artwork library key.");
    }
    public static string Key(string workspace,string path)
    {
        var key=Path.GetRelativePath(Path.GetFullPath(workspace),Path.GetFullPath(path)).Replace('\\','/');ValidateKey(key);return key;
    }
    public static ArtworkBrowsingState Read(string workspace)
    {
        var path=StatePath(workspace);if(!File.Exists(path))return new();if(new FileInfo(path).Length>512*1024)throw new InvalidDataException("Artwork library choices are too large.");
        var state=JsonSerializer.Deserialize<ArtworkBrowsingState>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("Invalid artwork library choices.");state.Validate();return state;
    }
    private static void Change(string workspace,Action<ArtworkBrowsingState> edit)
    {
        Directory.CreateDirectory(workspace);FileStream guard;
        for(var attempt=0;;attempt++){try{guard=new(Path.Combine(workspace,".artwork-browsing.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);break;}catch(IOException) when(attempt<99){Thread.Sleep(20);}}
        using(guard){var state=Read(workspace);edit(state);state.Validate();AtomicFiles.Write(StatePath(workspace),JsonSerializer.SerializeToUtf8Bytes(state,Contract.Json),keepPrevious:true);}
    }
    public static void Favorite(string workspace,string path,bool favorite)
    {
        var key=Key(workspace,path);if(favorite&&!File.Exists(path))throw new FileNotFoundException("This artwork is no longer available.",path);
        Change(workspace,state=>{state.Favorites.RemoveAll(k=>k.Equals(key,StringComparison.OrdinalIgnoreCase));if(favorite)state.Favorites.Add(key);});
    }
    public static void Use(string workspace,string path)
    {
        var key=Key(workspace,path);if(!File.Exists(path))throw new FileNotFoundException("This artwork is no longer available.",path);
        Change(workspace,state=>{state.Recent.RemoveAll(k=>k.Equals(key,StringComparison.OrdinalIgnoreCase));state.Recent.Insert(0,key);if(state.Recent.Count>48)state.Recent.RemoveRange(48,state.Recent.Count-48);});
    }
    public static IReadOnlyList<BrowseArtwork> Catalog(string workspace)
    {
        var result=new List<BrowseArtwork>();
        foreach(var asset in ArtworkLibrary.List(workspace))
        {
            var name=Regex.Replace(asset.Name,@"-[0-9a-fA-F]{6,16}$","");var description=Path.GetExtension(asset.Path).TrimStart('.').ToUpperInvariant()+" decal";
            try
            {
                if(VectorArtwork.ReadMetadata(asset.Path) is {} vector){name=VectorArtwork.Name(vector);description="SVG decal";}
                else if(TextArtwork.ReadMetadata(asset.Path) is {} text){name=text.Text.Replace('\n',' ');description="Text decal";}
                else if(PatternArtwork.ReadMetadata(asset.Path) is {} pattern)description=PatternArtwork.Name(pattern.Kind)+" pattern";
            }
            catch(Exception x) when(x is IOException or InvalidDataException or ArgumentException or JsonException){description+=" · source details unavailable";}
            result.Add(new(name,asset.Path,Key(workspace,asset.Path),false,description,File.GetLastWriteTimeUtc(asset.Path)));
        }
        result.AddRange(ReusableArtwork.List(workspace).Select(g=>new BrowseArtwork(g.Name,g.Path,Key(workspace,g.Path),true,"Editable group",File.GetLastWriteTimeUtc(g.Path))));return result;
    }
    public static IReadOnlyList<BrowseArtwork> Query(IEnumerable<BrowseArtwork> catalog,ArtworkBrowsingState state,string search,ArtworkCollection collection,ArtworkCategory category)
    {
        state.Validate();if(!Enum.IsDefined(collection)||!Enum.IsDefined(category))throw new ArgumentException("Choose a library view.");
        var favorites=state.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);var recent=state.Recent.Select((key,index)=>(key,index)).ToDictionary(x=>x.key,x=>x.index,StringComparer.OrdinalIgnoreCase);
        var terms=search.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        var shown=catalog.Where(a=>(category==ArtworkCategory.All||a.IsGroup==(category==ArtworkCategory.Groups))&&
            (collection==ArtworkCollection.All||collection==ArtworkCollection.Favorites&&favorites.Contains(a.Key)||collection==ArtworkCollection.Recent&&recent.ContainsKey(a.Key))&&
            terms.All(t=>(a.Name+" "+a.Description).Contains(t,StringComparison.OrdinalIgnoreCase)));
        return (collection==ArtworkCollection.Recent?shown.OrderBy(a=>recent[a.Key]):shown.OrderByDescending(a=>a.Modified)).ThenBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).ThenBy(a=>a.Key,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
