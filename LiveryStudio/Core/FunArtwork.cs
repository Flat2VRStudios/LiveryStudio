using System.Text;

namespace LiveryStudio.Core;

// Original, self-contained vector artwork shipped with Livery Studio.
public static class FunArtwork
{
    public static readonly IReadOnlyDictionary<string,string> Shapes=new Dictionary<string,string>
    {
        ["Star"]="<path d='M50 5 61 35 94 36 68 56 77 89 50 70 23 89 32 56 6 36 39 35Z'/>",
        ["Lightning"]="<path d='M53 3 17 57 44 57 35 97 86 37 57 37 70 3Z'/>",
        ["Flame"]="<path d='M51 3C63 30 28 33 48 57 50 39 67 37 68 24 104 66 85 96 52 97 15 97 0 67 23 39 19 65 36 64 32 47 26 25 46 24 51 3Z'/>",
        ["Heart"]="<path d='M50 91 11 51C-13 23 23-7 50 22 77-7 113 23 89 51Z'/>",
        ["Stripes"]="<path d='M3 12H26L73 88H50ZM36 12H59L100 78V88H83ZM69 12H91L100 27V63Z'/>",
        ["Chevron"]="<path d='M4 8 52 50 4 92H35L83 50 35 8ZM51 8 99 50 51 92H70L100 65V35L70 8Z'/>",
        ["Checker"]="<path d='M0 0H25V25H0ZM50 0H75V25H50ZM25 25H50V50H25ZM75 25H100V50H75ZM0 50H25V75H0ZM50 50H75V75H50ZM25 75H50V100H25ZM75 75H100V100H75Z'/>",
        ["Wings"]="<path d='M50 42 7 12 0 18 16 37 3 31 1 40 25 59 9 53 10 65 38 82 50 92 62 82 90 65 91 53 75 59 99 40 97 31 84 37 100 18 93 12Z'/>"
    };
    public static string Svg(string name,string hex)
    {
        hex=ColorLibrary.Normalize(hex);if(!Shapes.TryGetValue(name,out var shape))throw new ArgumentException("Choose a built-in sticker.");
        return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><g fill='{hex}'>{shape}</g></svg>";
    }
    public static string Generate(string name,string hex,string workspace)
    {
        var folder=Path.Combine(workspace,"builtin-stickers");Directory.CreateDirectory(folder);var source=Path.Combine(folder,name+"-"+ColorLibrary.Normalize(hex)[1..]+".svg");
        if(!File.Exists(source))AtomicFiles.Write(source,Encoding.UTF8.GetBytes(Svg(name,hex)));
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)))[..16];var result=Path.Combine(ArtworkLibrary.Folder(workspace),Path.GetFileNameWithoutExtension(source)+"-"+key+"-512.tga");
        return File.Exists(result)?result:VectorArtwork.Import(source,workspace,512);
    }
}
