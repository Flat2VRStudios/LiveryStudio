using LiveryStudio.Core;

namespace LiveryStudio;

internal sealed record TextureImport(string? Albedo=null,string? Structure=null)
{
    internal static TextureImport FromDrop(IEnumerable<string> paths,string? target)
    {
        var files=paths.ToArray();
        if(files.Length==0||files.Any(f=>!Supported(f)))throw new InvalidDataException("Choose PNG or TGA texture maps. To place artwork as a sticker, use the 3D paint studio.");
        if(target is "color" or "surface")
        {
            if(files.Length!=1)throw new InvalidDataException("Drop one map onto its color or surface tile.");
            return target=="surface"?new(Structure:files[0]):new(Albedo:files[0]);
        }
        var surface=files.Where(IsSurface).ToArray();var color=files.Where(f=>!IsSurface(f)).ToArray();
        if(color.Length>1||surface.Length>1)throw new InvalidDataException("Drop each map onto its color or surface tile so it goes in the right place.");
        return new(color.SingleOrDefault(),surface.SingleOrDefault());
    }

    internal void Validate()
    {
        foreach(var file in new[]{Albedo,Structure}.Where(f=>f is not null))
        {
            if(!Supported(file!))throw new InvalidDataException("Texture maps must be PNG or TGA images.");
            // Decode before replacing the draft, so a corrupt image cannot discard editable work.
            FinishMaps.Read(file!).Validate();
        }
    }

    private static bool Supported(string file)=>Path.GetExtension(file).Equals(".png",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(file).Equals(".tga",StringComparison.OrdinalIgnoreCase);
    private static bool IsSurface(string file)
    {
        var name=Path.GetFileNameWithoutExtension(file);
        return name.Equals("surface",StringComparison.OrdinalIgnoreCase)||name.EndsWith("_surface",StringComparison.OrdinalIgnoreCase)||name.Equals("structure",StringComparison.OrdinalIgnoreCase)||name.EndsWith("_structure",StringComparison.OrdinalIgnoreCase)||name.Equals("IOR",StringComparison.OrdinalIgnoreCase)||name.EndsWith("_IOR",StringComparison.OrdinalIgnoreCase);
    }
}
