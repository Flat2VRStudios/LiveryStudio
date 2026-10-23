namespace LiveryStudio.Core;

public enum BackgroundRemovalScope { ImageEdges, Everywhere }

public sealed record ImagePreparationOptions
{
    public bool RemoveBackground {get;set;}
    public string BackgroundColor {get;set;}="#FFFFFF";
    public BackgroundRemovalScope Scope {get;set;}
    public float Tolerance {get;set;}=.04f;
    public float Softness {get;set;}=.08f;
    // Fractions removed from each edge of the retained original, before removal.
    public float CropLeft {get;set;}
    public float CropTop {get;set;}
    public float CropRight {get;set;}
    public float CropBottom {get;set;}
    public void Validate()
    {
        _=PaintFinish.Color(BackgroundColor);
        if(!Enum.IsDefined(Scope)||!float.IsFinite(Tolerance)||Tolerance<0||Tolerance>1||!float.IsFinite(Softness)||Softness<0||Softness>1)
            throw new InvalidDataException("Background tolerance and softness must be between 0 and 100%.");
        if(new[]{CropLeft,CropTop,CropRight,CropBottom}.Any(v=>!float.IsFinite(v)||v<0||v>=1)||CropLeft+CropRight>=1||CropTop+CropBottom>=1)
            throw new InvalidDataException("The crop must leave part of the original image visible.");
    }
}

public readonly record struct ImageCrop(int Left,int Top,int Width,int Height);

public static class ImagePreparation
{
    public static ImageCrop Bounds(PaintImage source,ImagePreparationOptions options)
    {
        source.Validate();options.Validate();
        var left=Math.Clamp((int)Math.Floor(source.Width*options.CropLeft),0,source.Width-1);
        var top=Math.Clamp((int)Math.Floor(source.Height*options.CropTop),0,source.Height-1);
        var right=Math.Clamp((int)Math.Ceiling(source.Width*(1d-options.CropRight)),left+1,source.Width);
        var bottom=Math.Clamp((int)Math.Ceiling(source.Height*(1d-options.CropBottom)),top+1,source.Height);
        return new(left,top,right-left,bottom-top);
    }

    public static string GuessBackground(PaintImage source)
    {
        source.Validate();var colors=new Dictionary<int,int>();
        void Add(int x,int y)
        {
            var i=(y*source.Width+x)*4;if(source.Pixels[i+3]==0)return;
            var rgb=(source.Pixels[i]<<16)|(source.Pixels[i+1]<<8)|source.Pixels[i+2];colors[rgb]=colors.GetValueOrDefault(rgb)+1;
        }
        for(var x=0;x<source.Width;x++){Add(x,0);if(source.Height>1)Add(x,source.Height-1);}
        for(var y=1;y<source.Height-1;y++){Add(0,y);if(source.Width>1)Add(source.Width-1,y);}
        return colors.Count==0?"#FFFFFF":$"#{colors.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key).First().Key:X6}";
    }

    public static PaintImage Render(PaintImage source,ImagePreparationOptions options,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();var crop=Bounds(source,options);var pixels=new byte[checked(crop.Width*crop.Height*4)];
        for(var y=0;y<crop.Height;y++){cancellation.ThrowIfCancellationRequested();Array.Copy(source.Pixels,((crop.Top+y)*source.Width+crop.Left)*4,pixels,y*crop.Width*4,crop.Width*4);}
        var result=new PaintImage(crop.Width,crop.Height,pixels);if(!options.RemoveBackground)return result;
        var key=Convert.FromHexString(ColorLibrary.Normalize(options.BackgroundColor)[1..]);
        float Weight(int pixel)
        {
            var i=pixel*4;if(pixels[i+3]==0)return 1;
            var distance=Math.Max(Math.Abs(pixels[i]-key[0]),Math.Max(Math.Abs(pixels[i+1]-key[1]),Math.Abs(pixels[i+2]-key[2])))/255f;
            return distance<=options.Tolerance?1:options.Softness>0?Math.Clamp(1-(distance-options.Tolerance)/options.Softness,0,1):0;
        }
        void Remove(int pixel){var i=pixel*4;pixels[i+3]=(byte)Math.Clamp((int)MathF.Round(pixels[i+3]*(1-Weight(pixel))),0,255);}
        if(options.Scope==BackgroundRemovalScope.Everywhere)
        {
            for(var pixel=0;pixel<pixels.Length/4;pixel++){if((pixel&4095)==0)cancellation.ThrowIfCancellationRequested();Remove(pixel);}return result;
        }
        // Mark complete horizontal runs when queued: each texel is visited once,
        // without an image-sized int queue for ordinary solid backgrounds.
        var visited=new byte[pixels.Length/4];var spans=new Stack<(int Left,int Right,int Y)>();var work=0;
        bool Candidate(int x,int y){var i=y*crop.Width+x;return visited[i]==0&&Weight(i)>0;}
        void Seed(int x,int y)
        {
            if(!Candidate(x,y))return;var left=x;var right=x;
            while(left>0&&Candidate(left-1,y))left--;
            while(right+1<crop.Width&&Candidate(right+1,y))right++;
            for(var column=left;column<=right;column++)
            {
                if((work++&4095)==0)cancellation.ThrowIfCancellationRequested();var i=y*crop.Width+column;visited[i]=1;Remove(i);
            }
            spans.Push((left,right,y));
        }
        for(var x=0;x<crop.Width;x++){Seed(x,0);if(crop.Height>1)Seed(x,crop.Height-1);}
        for(var y=1;y<crop.Height-1;y++){Seed(0,y);if(crop.Width>1)Seed(crop.Width-1,y);}
        while(spans.TryPop(out var span))
        {
            cancellation.ThrowIfCancellationRequested();
            for(var x=span.Left;x<=span.Right;x++){if(span.Y>0)Seed(x,span.Y-1);if(span.Y+1<crop.Height)Seed(x,span.Y+1);}
        }
        return result;
    }

    public static bool CanEdit(PaintLayer layer)=>layer.Kind==LayerKind.Sticker&&layer.TextArtwork is null&&layer.PatternArtwork is null&&layer.VectorArtwork is null;
    public static PaintLayer Create(PaintLayer layer,string asset,ArtworkReplacementFit fit=ArtworkReplacementFit.ExistingBox)
    {
        if(!CanEdit(layer))throw new InvalidOperationException("Select an image decal; text, patterns and SVGs have their own editors.");
        var prepared=ArtworkReplacement.Create(layer,asset,fit);
        if(prepared.RasterArtwork?.Preparation is null)throw new InvalidDataException("The prepared image needs its original and editable settings.");
        // Preparing the same retained source does not replace its shared pigments.
        if(!Path.GetFullPath(prepared.RasterArtwork.SourceFile).Equals(Path.GetFullPath(layer.RasterArtwork?.SourceFile??layer.Asset),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new InvalidDataException("The prepared image belongs to a different original.");
        return prepared with {ColorLinks=layer.ColorLinks.ToDictionary(p=>p.Key,p=>p.Value)};
    }
    public static void Apply(LayerDocument document,Guid id,string asset,ArtworkReplacementFit fit=ArtworkReplacementFit.ExistingBox)
    {
        var layer=document.Layers.SingleOrDefault(l=>l.Id==id)??throw new InvalidOperationException("The selected image is no longer in this design.");
        var prepared=Create(layer,asset,fit);document.Commit("Prepared image",layers=>layers[layers.FindIndex(l=>l.Id==id)]=prepared);
    }
}
