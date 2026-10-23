using System.Text.Json;

namespace LiveryStudio.Core;

public sealed record SavedColor(string Hex,string Name);
public sealed class ColorLibraryState
{
    public int Version {get;set;}=1;
    public List<string> Recent {get;set;}=[];
    public List<SavedColor> Favorites {get;set;}=[];
    public void Validate()
    {
        if(Version!=1||Recent is null||Favorites is null||Recent.Count>24||Favorites.Count>32)throw new InvalidDataException("Unsupported color library.");
        foreach(var color in Recent)_=ColorLibrary.Normalize(color);
        foreach(var color in Favorites){if(color is null)throw new InvalidDataException("Invalid favorite color.");_=ColorLibrary.Normalize(color.Hex);if(color.Name is null||color.Name.Length>48||color.Name.Any(char.IsControl))throw new InvalidDataException("Invalid favorite color name.");}
        if(Recent.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Recent.Count||Favorites.Select(c=>c.Hex).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Favorites.Count)throw new InvalidDataException("Duplicate saved colors.");
    }
}
public static class ColorLibrary
{
    public static string Normalize(string hex){_=LiveryProject.ColorVector(hex);return hex.ToUpperInvariant();}
    private static string FilePath(string workspace)=>Path.Combine(workspace,"colors.json");
    public static ColorLibraryState Read(string workspace)
    {
        var path=FilePath(workspace);if(!File.Exists(path))return new();if(new FileInfo(path).Length>32*1024)throw new InvalidDataException("Color library is too large.");
        var state=JsonSerializer.Deserialize<ColorLibraryState>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("Invalid color library.");state.Validate();state.Recent=state.Recent.Select(Normalize).ToList();state.Favorites=state.Favorites.Select(c=>c with {Hex=Normalize(c.Hex)}).ToList();return state;
    }
    private static FileStream Lock(string workspace)
    {
        var path=Path.Combine(workspace,".colors.lock");for(var attempt=0;;attempt++){try{return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException) when(attempt<99){Thread.Sleep(20);}}
    }
    private static void Change(string workspace,Action<ColorLibraryState> edit)
    {
        Directory.CreateDirectory(workspace);using var guard=Lock(workspace);var state=Read(workspace);edit(state);state.Validate();AtomicFiles.Write(FilePath(workspace),JsonSerializer.SerializeToUtf8Bytes(state,Contract.Json),keepPrevious:true);
    }
    public static void Use(string workspace,params string[] colors)
    {
        var normalized=colors.Select(Normalize).ToArray();Change(workspace,state=>{foreach(var color in normalized){state.Recent.Remove(color);state.Recent.Insert(0,color);}if(state.Recent.Count>24)state.Recent.RemoveRange(24,state.Recent.Count-24);});
    }
    public static void Favorite(string workspace,string hex,string name)
    {
        hex=Normalize(hex);name=name.Trim();if(name.Length==0)name=hex;if(name.Length>48||name.Any(char.IsControl))throw new ArgumentException("Use a color name of at most 48 characters.");
        Change(workspace,state=>{var existing=state.Favorites.FindIndex(c=>c.Hex==hex);if(existing>=0)state.Favorites[existing]=new(hex,name);else{if(state.Favorites.Count==32)throw new InvalidOperationException("You have 32 favorites. Remove one before saving another.");state.Favorites.Add(new(hex,name));}});
    }
    public static void RemoveFavorite(string workspace,string hex){hex=Normalize(hex);Change(workspace,state=>state.Favorites.RemoveAll(c=>c.Hex==hex));}
}
