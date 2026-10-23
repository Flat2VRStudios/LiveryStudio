namespace LiveryStudio.Core;

// Owned by one composition or shelf preview. Images are read-only to callers;
// the next operation reads disk again so changed artwork never becomes stale.
internal sealed class DecalImageCache
{
    private readonly record struct Key(string Path,DecalEffectsOptions? Effects);
    private sealed record Entry(Key Key,PaintImage Image);
    private readonly Dictionary<Key,LinkedListNode<Entry>> entries=[];
    private readonly LinkedList<Entry> recent=[];
    private readonly Func<string,PaintImage> read;
    private readonly long budget;
    private readonly CancellationToken cancellation;
    internal long RetainedBytes {get;private set;}
    internal DecalImageCache(CancellationToken cancellation=default,long budget=64*1024*1024,Func<string,PaintImage>? read=null)
    {
        if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));
		this.budget=budget;this.read=read??(path=>FinishMaps.Read(path,cancellation));this.cancellation=cancellation;
    }
    internal PaintImage Get(string path,DecalEffectsOptions? effects=null)
    {
        cancellation.ThrowIfCancellationRequested();effects?.Validate(files:true);
        var full=Path.GetFullPath(path);if(OperatingSystem.IsWindows())full=full.ToUpperInvariant();
        // Blending takes place on the destination, not in the masked image.
        var recipe=effects?.Mask is null or DecalMask.None?null:effects with {Blend=DecalBlend.Normal};
        var key=new Key(full,recipe);
        if(entries.TryGetValue(key,out var found)){recent.Remove(found);recent.AddLast(found);return found.Value.Image;}
        var image=recipe is null?read(path):DecalEffects.Apply(Get(path),recipe,GetSource,cancellation);
        image.Validate();cancellation.ThrowIfCancellationRequested();
        if(image.Pixels.LongLength>budget)return image;
        while(RetainedBytes+image.Pixels.LongLength>budget&&recent.First is {} oldest)
        {
            recent.RemoveFirst();entries.Remove(oldest.Value.Key);RetainedBytes-=oldest.Value.Image.Pixels.LongLength;
        }
        if(budget>0){entries.Add(key,recent.AddLast(new Entry(key,image)));RetainedBytes+=image.Pixels.LongLength;}
        return image;
    }
    private PaintImage GetSource(string path)=>Get(path);
}
