using System.Numerics;
using System.Text.Json.Serialization;

namespace LiveryStudio.Core;

public enum DecalBlend { Normal, Multiply, Screen, Overlay }
public enum DecalMask { None, Oval, Box, LinearFade, RadialFade, Image }
public enum StencilChannel { Automatic, Alpha, Luminance }

public sealed record DecalEffectsOptions
{
    public DecalBlend Blend {get;init;}
    public DecalMask Mask {get;init;}
    public float CenterX {get;init;}=.5f;
    public float CenterY {get;init;}=.5f;
    public float Width {get;init;}=1;
    public float Height {get;init;}=1;
    public float Angle {get;init;}
    public float Feather {get;init;}=.05f;
    public bool Invert {get;init;}
    public string StencilFile {get;init;}="";
    public StencilChannel Channel {get;init;}
    [JsonIgnore] public bool IsNeutral => Blend==DecalBlend.Normal&&Mask==DecalMask.None;
    public void Validate(bool files=false)
    {
        if(!Enum.IsDefined(Blend)||!Enum.IsDefined(Mask)||!Enum.IsDefined(Channel)||
            !float.IsFinite(CenterX)||CenterX< -2||CenterX>3||!float.IsFinite(CenterY)||CenterY< -2||CenterY>3||
            !float.IsFinite(Width)||Width<.02f||Width>5||!float.IsFinite(Height)||Height<.02f||Height>5||
            !float.IsFinite(Angle)||Angle< -360||Angle>360||!float.IsFinite(Feather)||Feather<0||Feather>1)
            throw new InvalidDataException("Decal effect settings are outside their supported range.");
        if(Mask==DecalMask.Image&&string.IsNullOrWhiteSpace(StencilFile))throw new InvalidDataException("Choose an image stencil for this decal.");
        if(files&&StencilFile.Length>0&&!File.Exists(StencilFile))throw new FileNotFoundException("A retained image stencil is missing. Open a portable project to recover it.",StencilFile);
    }
}

public static class DecalEffects
{
    // Blend pigment values, before game lighting. Alpha remains the decal's coverage.
    public static Vector3 Mix(Vector3 below,Vector3 above,DecalBlend blend)=>blend switch
    {
        DecalBlend.Normal=>above,
        DecalBlend.Multiply=>below*above,
        DecalBlend.Screen=>Vector3.One-(Vector3.One-below)*(Vector3.One-above),
        DecalBlend.Overlay=>new(Overlay(below.X,above.X),Overlay(below.Y,above.Y),Overlay(below.Z,above.Z)),
        _=>throw new ArgumentOutOfRangeException(nameof(blend))
    };
    private static float Overlay(float below,float above)=>below<=.5f?2*below*above:1-2*(1-below)*(1-above);
    public static PaintImage Apply(PaintImage original,DecalEffectsOptions? options,CancellationToken cancellation=default)
        =>Apply(original,options,FinishMaps.Read,cancellation);
    internal static PaintImage Apply(PaintImage original,DecalEffectsOptions? options,Func<string,PaintImage> image,CancellationToken cancellation=default)
    {
        original.Validate();if(options is null)return original;options.Validate(files:true);if(options.Mask==DecalMask.None)return original;
        cancellation.ThrowIfCancellationRequested();var prepared=new PreparedMask(options,image,cancellation);var pixels=(byte[])original.Pixels.Clone();
        for(var y=0;y<original.Height;y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(var x=0;x<original.Width;x++)
            {
                var k=(y*original.Width+x)*4;if(pixels[k+3]==0)continue;
                pixels[k+3]=prepared.Alpha(pixels[k+3],new((x+.5f)/original.Width,(y+.5f)/original.Height));
            }
        }
        return new(original.Width,original.Height,pixels);
    }
    // Picking needs one source texel per projection, not a complete temporary
    // masked image for every candidate. Share this exact coverage/quantization
    // calculation with baking, and prepare each distinct stencil once per pick.
    internal sealed class PreparedMask
    {
        private readonly DecalEffectsOptions options;
        private readonly PaintImage? stencil;
        private readonly StencilChannel channel;
        private readonly float cosine,sine;
        internal long RetainedBytes=>stencil?.Pixels.LongLength??0;
        internal PreparedMask(DecalEffectsOptions options,Func<string,PaintImage> image,CancellationToken cancellation=default)
        {
            options.Validate(files:true);cancellation.ThrowIfCancellationRequested();this.options=options;channel=options.Channel;
            stencil=options.Mask==DecalMask.Image?image(options.StencilFile):null;stencil?.Validate();
            if(stencil is not null&&channel==StencilChannel.Automatic)
            {
                channel=StencilChannel.Luminance;for(var i=3;i<stencil.Pixels.Length;i+=4){if((i&262143)==3)cancellation.ThrowIfCancellationRequested();if(stencil.Pixels[i]<255){channel=StencilChannel.Alpha;break;}}
            }
            var angle=options.Angle*MathF.PI/180;cosine=MathF.Cos(angle);sine=MathF.Sin(angle);
        }
        internal byte Alpha(byte original,Vector2 uv)
        {
            if(original==0)return 0;var dx=uv.X-options.CenterX;var dy=uv.Y-options.CenterY;
            var local=new Vector2((dx*cosine+dy*sine)/options.Width+.5f,(-dx*sine+dy*cosine)/options.Height+.5f);
            var radius=((local-new Vector2(.5f))*2).Length();
            float Edge(float distance)=>options.Feather==0?distance>=0?1:0:Smooth(Math.Clamp(distance/options.Feather,0,1));
            var coverage=options.Mask switch
            {
                DecalMask.Oval=>Edge(1-radius),
                DecalMask.Box=>Edge(MathF.Min(.5f-MathF.Abs(local.X-.5f),.5f-MathF.Abs(local.Y-.5f))*2),
                DecalMask.LinearFade=>Math.Clamp(1-local.X,0,1),
                DecalMask.RadialFade=>Math.Clamp(1-radius,0,1),
                DecalMask.Image=>Stencil(stencil!,local,channel),
                _=>1
            };
            if(options.Invert)coverage=1-coverage;
            return (byte)Math.Clamp(MathF.Round(original*coverage),0,255);
        }
    }
    private static float Smooth(float value)=>value*value*(3-2*value);
    private static float Stencil(PaintImage image,Vector2 uv,StencilChannel channel)
    {
        if(uv.X<0||uv.Y<0||uv.X>1||uv.Y>1)return 0;
        var x=Math.Clamp(uv.X*image.Width-.5f,0,image.Width-1);var y=Math.Clamp(uv.Y*image.Height-.5f,0,image.Height-1);
        var x0=(int)x;var y0=(int)y;var x1=Math.Min(x0+1,image.Width-1);var y1=Math.Min(y0+1,image.Height-1);
        float At(int xx,int yy){var k=(yy*image.Width+xx)*4;var alpha=image.Pixels[k+3]/255f;return channel==StencilChannel.Alpha?alpha:(image.Pixels[k]*.2126f+image.Pixels[k+1]*.7152f+image.Pixels[k+2]*.0722f)/255f*alpha;}
        return float.Lerp(float.Lerp(At(x0,y0),At(x1,y0),x-x0),float.Lerp(At(x0,y1),At(x1,y1),x-x0),y-y0);
    }
}
