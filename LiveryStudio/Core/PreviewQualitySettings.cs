using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveryStudio.Core;

public enum PreviewQuality { Low, Balanced, High }
public enum PreviewFiltering { Standard=1, Sharp=4, Sharper=8, Maximum=16 }
public readonly record struct PreviewRenderSize(uint Width,uint Height)
{
    public long Pixels=>(long)Width*Height;
}

// Preview preferences are local to the app. They never change project textures,
// paint placement, portable projects or the resolution of exported photographs.
public sealed record PreviewQualitySettings
{
    public int Version {get;init;}=1;
    public PreviewQuality Quality {get;init;}=PreviewQuality.Balanced;
    public PreviewFiltering Filtering {get;init;}=PreviewFiltering.Sharp;
    [JsonIgnore] public double ResolutionScale=>Quality switch {PreviewQuality.Low=>.75,PreviewQuality.High=>1.25,_=>1};
    public const uint MaximumEdge=3840;
    public const long MaximumPixels=3840L*2160;

    public static PreviewQualitySettings Preset(PreviewQuality quality)=>new()
    {
        Quality=quality,
        Filtering=quality switch {PreviewQuality.Low=>PreviewFiltering.Standard,PreviewQuality.High=>PreviewFiltering.Sharper,_=>PreviewFiltering.Sharp}
    };

    public void Validate()
    {
        if(Version!=1||!Enum.IsDefined(Quality)||!Enum.IsDefined(Filtering))throw new InvalidDataException("Unsupported preview quality settings.");
    }

    public PreviewRenderSize RenderSize(double width,double height,double compositionScaleX,double compositionScaleY)
    {
        Validate();
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width),"The preview size must be positive and finite.");
        if(!double.IsFinite(compositionScaleX)||!double.IsFinite(compositionScaleY)||compositionScaleX<=0||compositionScaleY<=0)throw new ArgumentOutOfRangeException(nameof(compositionScaleX),"The display scale must be positive and finite.");
        // CompositionScale includes Windows DPI and any XAML scale transform.
        // The old viewport used DIPs directly, which was blurry on scaled displays.
        var w=width*compositionScaleX*ResolutionScale;var h=height*compositionScaleY*ResolutionScale;
        if(!double.IsFinite(w)||!double.IsFinite(h))throw new ArgumentOutOfRangeException(nameof(width),"The preview size is too large.");
        var fit=Math.Min(1,Math.Min(MaximumEdge/Math.Max(w,h),Math.Sqrt(MaximumPixels/w/h)));
        return new((uint)Math.Clamp(Math.Floor(w*fit),1,MaximumEdge),(uint)Math.Clamp(Math.Floor(h*fit),1,MaximumEdge));
    }

    public static string FilePath(string workspace)=>Path.Combine(workspace,"preview-quality.json");
    public static PreviewQualitySettings Read(string workspace)
    {
        var file=FilePath(workspace);if(!File.Exists(file))return new();
        if(new FileInfo(file).Length>16*1024)throw new InvalidDataException("The preview settings file is too large.");
        var settings=JsonSerializer.Deserialize<PreviewQualitySettings>(File.ReadAllBytes(file),Contract.Json)??throw new InvalidDataException("Empty preview settings.");settings.Validate();return settings;
    }
    public void Save(string workspace)
    {
        Validate();AtomicFiles.Write(FilePath(workspace),JsonSerializer.SerializeToUtf8Bytes(this,Contract.Json));
    }
}
