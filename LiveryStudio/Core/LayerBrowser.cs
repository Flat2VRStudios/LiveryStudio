using System.Text.Json;

namespace LiveryStudio.Core;

public enum LayerFilter { All,Artwork,Paint,Materials,Groups,Hidden,Locked }
public static class LayerBrowser
{
    public static string Type(PaintLayer layer)=>layer.VectorArtwork is not null?"SVG decal":layer.TextArtwork is not null?"Text decal":layer.PatternArtwork is {} pattern?PatternArtwork.Name(pattern.Kind):layer.Kind switch{LayerKind.Sticker=>"Image decal",LayerKind.Color=>"Paint stroke",LayerKind.Gloss=>"Gloss stroke",LayerKind.Metalness=>"Metallic stroke",_=>"Surface finish"};
    public static string State(PaintLayer layer)=>string.Join(" · ",new[]{layer.Visible?"":"Hidden",layer.Locked?"Locked":"",layer.Mirror?"Paired sides":"",layer.GroupId.HasValue?layer.GroupName:""}.Where(s=>s.Length>0));
    public static PaintLayer[] Filter(IEnumerable<PaintLayer> layers,string query,LayerFilter filter)
    {
        if(!Enum.IsDefined(filter))throw new ArgumentOutOfRangeException(nameof(filter));
        var words=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        bool Matches(PaintLayer layer)
        {
            var category=filter switch{LayerFilter.Artwork=>layer.Kind==LayerKind.Sticker,LayerFilter.Paint=>layer.Kind==LayerKind.Color,LayerFilter.Materials=>layer.IsMaterial,LayerFilter.Groups=>layer.GroupId.HasValue,LayerFilter.Hidden=>!layer.Visible,LayerFilter.Locked=>layer.Locked,_=>true};
            var text=string.Join(" ",layer.Name,layer.GroupName,layer.TextArtwork?.Text??"",Type(layer),State(layer));
            return category&&words.All(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
        }
        return layers.Where(Matches).Reverse().ToArray();
    }
    public static LayerDocument Inspection(LayerDocument source,IEnumerable<Guid> selected)
    {
        var ids=selected.ToHashSet();var available=source.Layers.Where(l=>ids.Contains(l.Id)).ToArray();
        if(available.Length==0)throw new InvalidOperationException("Select artwork to inspect on the car.");
        var copy=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(source,LayerDocument.Json),LayerDocument.Json)!;
        var layers=copy.Layers.Where(l=>ids.Contains(l.Id)).Select(l=>l with {Visible=true}).ToList();
        var active=copy.History[copy.Cursor];copy.History=[new("Temporary selection preview",DateTimeOffset.UtcNow,layers){Palette=active.Palette,PaintColors=active.PaintColors}];copy.Cursor=0;return copy;
    }
    public static PaintImage Thumbnail(PaintLayer layer,int width=64,int height=48)
    {
        if(width is <8 or >256||height is <8 or >256)throw new ArgumentOutOfRangeException(nameof(width));
        var pixels=new byte[width*height*4];
        var source=layer.Kind==LayerKind.Sticker?DecalEffects.Apply(FinishMaps.Read(layer.Asset),layer.Effects):null;
        var color=PaintFinish.Color(layer.Color);var scale=source is null?1:Math.Min((double)width/source.Width,(double)height/source.Height);
        var w=source is null?width:Math.Max(1,(int)Math.Round(source.Width*scale));var h=source is null?height:Math.Max(1,(int)Math.Round(source.Height*scale));var left=(width-w)/2;var top=(height-h)/2;
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var k=(y*width+x)*4;var shade=((x/6+y/6)&1)==0?40:59;float r=shade,g=shade,b=shade;
            if(source is not null&&x>=left&&x<left+w&&y>=top&&y<top+h)
            {
                var sx=Math.Clamp((int)((x-left+.5)*source.Width/w),0,source.Width-1);var sy=Math.Clamp((int)((y-top+.5)*source.Height/h),0,source.Height-1);var s=(sy*source.Width+sx)*4;var a=source.Pixels[s+3]/255f;
                r=float.Lerp(r,source.Pixels[s],a);g=float.Lerp(g,source.Pixels[s+1],a);b=float.Lerp(b,source.Pixels[s+2],a);
            }
            else if(source is null)
            {
                // A swatch identifies a paint/material stroke; it is not a
                // miniature rendering of its projection on the body.
                var spot=MathF.Sqrt(MathF.Pow((x-width*.5f)/(width*.4f),2)+MathF.Pow((y-height*.5f)/(height*.4f),2));
                if(spot<1){var value=layer.IsMaterial?.25f+.65f*(1-spot):1;r=color.X*255*value;g=color.Y*255*value;b=color.Z*255*value;}
            }
            pixels[k]=(byte)Math.Clamp(MathF.Round(r),0,255);pixels[k+1]=(byte)Math.Clamp(MathF.Round(g),0,255);pixels[k+2]=(byte)Math.Clamp(MathF.Round(b),0,255);pixels[k+3]=255;
        }
        return new(width,height,pixels);
    }
}
