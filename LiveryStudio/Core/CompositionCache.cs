using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveryStudio.Core;

// One editor owns this bounded cache. Cached pixels are immutable; callers always
// receive their own maps, so a live brush stroke cannot corrupt an undo result.
public sealed class CompositionCache(long budget=256L*1024*1024)
{
    private sealed record Entry(string Key,object Value,long Bytes);
    private sealed class MeshIdentity { public Guid Id {get;}=Guid.NewGuid(); }
    private readonly ConditionalWeakTable<PaintMesh,MeshIdentity> meshes=new();
    private readonly Dictionary<string,LinkedListNode<Entry>> entries=[];
    private readonly LinkedList<Entry> recent=[];
    private readonly object gate=new();
    public long RetainedBytes {get;private set;}
    public long Budget {get;}=budget>=0?budget:throw new ArgumentOutOfRangeException(nameof(budget));
    public long Hits {get;private set;}
    public long Misses {get;private set;}

    public void Clear(){lock(gate){entries.Clear();recent.Clear();RetainedBytes=0;}}
    private T? Get<T>(string key) where T:class
    {
        lock(gate){if(!entries.TryGetValue(key,out var found))return null;recent.Remove(found);recent.AddLast(found);return (T)found.Value.Value;}
    }
    private void Put(string key,object value,long bytes)
    {
        lock(gate)
        {
            if(bytes>Budget||Budget==0)return;
            if(entries.Remove(key,out var previous)){recent.Remove(previous);RetainedBytes-=previous.Value.Bytes;}
            while(RetainedBytes+bytes>Budget)
            {
                // Restored map pairs take precedence over decoding accelerators.
                // Even two 4K/4K states can fit without retaining their bases.
                var victim=recent.First;while(victim is not null&&victim.Value.Key.StartsWith("maps:",StringComparison.Ordinal))victim=victim.Next;
                if(victim is null){if(!key.StartsWith("maps:",StringComparison.Ordinal))return;victim=recent.First;}
                if(victim is null)return;recent.Remove(victim);entries.Remove(victim.Value.Key);RetainedBytes-=victim.Value.Bytes;
            }
            entries.Add(key,recent.AddLast(new Entry(key,value,bytes)));RetainedBytes+=bytes;
        }
    }
    private static string FileVersion(string path)
    {
        var file=new FileInfo(Path.GetFullPath(path));if(!file.Exists)throw new FileNotFoundException("Artwork is missing: "+path,path);
        return file.FullName+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks+"|"+file.CreationTimeUtc.Ticks;
    }
    private Guid MeshId(PaintMesh mesh)=>meshes.GetValue(mesh,_=>new()).Id;
    private string StateKey(LayerDocument document,PaintMesh mesh,LiveryProject project)
    {
        var sources=new[]{document.BaseAlbedo,document.BaseStructure}.Concat(document.Layers.Where(l=>l.Visible&&l.Kind==LayerKind.Sticker).SelectMany(l=>string.IsNullOrEmpty(l.Effects?.StencilFile)?new[]{l.Asset}:new[]{l.Asset,l.Effects.StencilFile})).Distinct(StringComparer.OrdinalIgnoreCase).Select(FileVersion).ToArray();
        var state=JsonSerializer.SerializeToUtf8Bytes(new{Mesh=MeshId(mesh),document.CarIndex,document.SeamPadding,document.History[document.Cursor].CleanPaint,Layers=document.Layers,Sources=sources,project.InnerColor,project.OuterColor,project.InnerGloss,project.OuterGloss,project.Metalness,project.UseSrgbPaintColors,project.OverrideArtworkFinish,project.ArtworkGloss,project.ArtworkMetalness},LayerDocument.Json);
        return "maps:"+Convert.ToHexString(SHA256.HashData(state));
    }
    private static PaintImage Copy(PaintImage image)=>new(image.Width,image.Height,(byte[])image.Pixels.Clone());
    private static CompositeMaps Copy(CompositeMaps maps)=>new(Copy(maps.Albedo),Copy(maps.Structure));
    public bool Touch(LayerDocument document,PaintMesh mesh,LiveryProject project)
        =>Get<CompositeMaps>(StateKey(document,mesh,DesignPalette.ResolvePaint(document,project))) is not null;
    public CompositeMaps Compose(LayerDocument document,PaintMesh mesh,LiveryProject project,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();document.Validate();project=DesignPalette.ResolvePaint(document,project);
        if(document.CarIndex!=project.CarIndex)throw new InvalidDataException("These layers belong to a different car.");
        var key=StateKey(document,mesh,project);
        if(Get<CompositeMaps>(key) is {} found){cancellation.ThrowIfCancellationRequested();lock(gate)Hits++;return Copy(found);}
        lock(gate)Misses++;
        var maps=document.ComposeCore(mesh,project,cancellation,this);cancellation.ThrowIfCancellationRequested();
        Put(key,Copy(maps),maps.Albedo.Pixels.LongLength+maps.Structure.Pixels.LongLength);return maps;
    }
    internal PaintImage BaseImage(string path,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var key="base:"+FileVersion(path);if(Get<PaintImage>(key) is {} found)return found;
        var image=FinishMaps.Read(path,cancellation);cancellation.ThrowIfCancellationRequested();Put(key,image,image.Pixels.LongLength);return image;
    }
    internal bool[] Coverage(PaintMesh mesh,int width,int height,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();var key=$"coverage:{MeshId(mesh)}:{width}:{height}";if(Get<bool[]>(key) is {} found)return found;
        var coverage=MeshEditing.UvCoverage(mesh,width,height,cancellation);cancellation.ThrowIfCancellationRequested();Put(key,coverage,coverage.LongLength);return coverage;
    }
}
