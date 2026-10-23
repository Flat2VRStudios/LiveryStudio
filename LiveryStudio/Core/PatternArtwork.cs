using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveryStudio.Core;

public enum PatternKind { LinearGradient,RadialGradient,Checker,Stripes,Dots,CarbonWeave }
public sealed record PatternArtworkOptions
{
    public PatternKind Kind {get;set;}=PatternKind.LinearGradient;
    public string ColorA {get;set;}="#21DCC1";
    public string ColorB {get;set;}="#0D141E";
    public float AlphaA {get;set;}=1;
    public float AlphaB {get;set;}=1;
    public float Angle {get;set;}
    public int Repeats {get;set;}=12;
    public float Fill {get;set;}=.5f;
    public float Phase {get;set;}
    public float Transition {get;set;}=.5f;
    public float CenterX {get;set;}=.5f;
    public float CenterY {get;set;}=.5f;
    public float Radius {get;set;}=.6f;
    public float EdgeFade {get;set;}
    public float Aspect {get;set;}=2;
    public int Resolution {get;set;}=1024;
    public void Validate()
    {
        _=LiveryProject.ColorVector(ColorA);_=LiveryProject.ColorVector(ColorB);
        if(!Enum.IsDefined(Kind)||Repeats<2||Repeats>64||Resolution is not (512 or 1024 or 2048))throw new ArgumentException("Choose a supported pattern, 2–64 repeats, and a supported resolution.");
        if(new[]{AlphaA,AlphaB,Angle,Fill,Phase,Transition,CenterX,CenterY,Radius,EdgeFade,Aspect}.Any(v=>!float.IsFinite(v))||AlphaA<0||AlphaA>1||AlphaB<0||AlphaB>1||AlphaA+AlphaB==0||Angle<-180||Angle>180||Fill<.05f||Fill>.95f||Phase<0||Phase>1||Transition<.05f||Transition>.95f||CenterX<0||CenterX>1||CenterY<0||CenterY>1||Radius<.05f||Radius>2||EdgeFade<0||EdgeFade>.5f||Aspect<.25f||Aspect>4)throw new ArgumentException("Pattern settings are outside their supported range.");
    }
}
public static class PatternArtwork
{
    public static string Name(PatternKind kind)=>kind switch {PatternKind.LinearGradient=>"Linear gradient",PatternKind.RadialGradient=>"Radial gradient",PatternKind.CarbonWeave=>"Carbon weave",_=>kind.ToString()};
    public static PaintImage Render(PatternArtworkOptions options,int? previewResolution=null,CancellationToken cancellation=default)
    {
        options=options with {};options.Validate();var size=previewResolution??options.Resolution;if(size<32||size>2048)throw new ArgumentOutOfRangeException(nameof(previewResolution));
        var width=options.Aspect>=1?size:Math.Max(1,(int)MathF.Round(size*options.Aspect));var height=options.Aspect>=1?Math.Max(1,(int)MathF.Round(size/options.Aspect)):size;
        var aspect=(float)width/height;var pixels=new byte[width*height*4];var first=PaintFinish.ToLinear(PaintFinish.Color(options.ColorA));var second=PaintFinish.ToLinear(PaintFinish.Color(options.ColorB));
        var angle=options.Angle*MathF.PI/180;var cosine=MathF.Cos(angle);var sine=MathF.Sin(angle);float[] offsets=[.25f,.75f];
        static float Fract(float value)=>value-MathF.Floor(value);
        static float Smooth(float value){value=Math.Clamp(value,0,1);return value*value*(3-2*value);}
        float Transition(float value)=>value<options.Transition?value*.5f/options.Transition:.5f+(value-options.Transition)*.5f/(1-options.Transition);
        (Vector3 Color,float Alpha) Sample(float u,float v)
        {
            var x=(u-.5f)*aspect;var y=v-.5f;var rx=x*cosine+y*sine;var ry=-x*sine+y*cosine;var px=rx*options.Repeats+options.Phase;var py=ry*options.Repeats+options.Phase;var fx=Fract(px);var fy=Fract(py);
            var blend=options.Kind switch
            {
                PatternKind.LinearGradient=>Smooth(Transition(rx/(Math.Abs(cosine)*aspect+Math.Abs(sine))+.5f)),
                PatternKind.RadialGradient=>Smooth(Transition(MathF.Sqrt(MathF.Pow((u-options.CenterX)*aspect,2)+MathF.Pow(v-options.CenterY,2))/options.Radius)),
                PatternKind.Checker=>(((int)MathF.Floor(px)+(int)MathF.Floor(py))&1)==0?0f:1f,
                PatternKind.Stripes=>fx<options.Fill?0f:1f,
                PatternKind.Dots=>(fx-.5f)*(fx-.5f)+(fy-.5f)*(fy-.5f)<options.Fill*.25f?0f:1f,
                PatternKind.CarbonWeave=>Weave(px,py,fx,fy),
                _=>0
            };
            var alpha=float.Lerp(options.AlphaA,options.AlphaB,blend);var premultiplied=first*(options.AlphaA*(1-blend))+second*(options.AlphaB*blend);
            var edge=options.EdgeFade==0?1:Smooth(Math.Min(Math.Min(u*aspect,(1-u)*aspect),Math.Min(v,1-v))/options.EdgeFade);
            return(premultiplied*edge,alpha*edge);
        }
        static float Weave(float x,float y,float fx,float fy)
        {
            var warp=(((int)MathF.Floor(x)+(int)MathF.Floor(y))&3)<2;var across=warp?fx:fy;var ridge=MathF.Pow(Math.Max(0,MathF.Sin(MathF.PI*across)),.65f);var fiber=.055f*MathF.Cos((warp?x:y)*MathF.PI*10);return Math.Clamp((warp?.13f:.23f)+ridge*.58f+fiber,0,1);
        }
        for(var y=0;y<height;y++)
        {
            if((y&7)==0)cancellation.ThrowIfCancellationRequested();
            for(var x=0;x<width;x++)
            {
                var color=Vector3.Zero;var alpha=0f;foreach(var dx in offsets)foreach(var dy in offsets){var sample=Sample((x+dx)/width,(y+dy)/height);color+=sample.Color;alpha+=sample.Alpha;}
                var at=(y*width+x)*4;if(alpha>1e-7f){color=PaintFinish.ToSrgb(color/alpha);pixels[at]=(byte)Math.Clamp(MathF.Round(color.X*255),0,255);pixels[at+1]=(byte)Math.Clamp(MathF.Round(color.Y*255),0,255);pixels[at+2]=(byte)Math.Clamp(MathF.Round(color.Z*255),0,255);}pixels[at+3]=(byte)Math.Clamp(MathF.Round(alpha*.25f*255),0,255);
            }
        }
        return new(width,height,pixels);
    }
    public static string Generate(PatternArtworkOptions options,string workspace,CancellationToken cancellation=default)
    {
        options=options with {};var image=Render(options,cancellation:cancellation);var json=JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json);var key=Convert.ToHexString(SHA256.HashData(json))[..16];var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);var path=Path.Combine(folder,Name(options.Kind)+"-"+key+".tga");var temp=Path.Combine(folder,".pattern-"+Guid.NewGuid().ToString("N")+".tmp");
        try{FinishMaps.WriteTga(image,temp);var hash=WorkshopService.Hash(temp);if(File.Exists(path)&&WorkshopService.Hash(path)!=hash)path=Path.Combine(folder,Name(options.Kind)+"-"+key+"-"+Guid.NewGuid().ToString("N")[..8]+".tga");if(!File.Exists(path))File.Move(temp,path);AtomicFiles.Write(path+".pattern.json",json);return path;}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static PatternArtworkOptions? ReadMetadata(string image)
    {
        var path=image+".pattern.json";if(!File.Exists(path))return null;if(new FileInfo(path).Length>16*1024)throw new InvalidDataException("Pattern metadata is too large.");var options=JsonSerializer.Deserialize<PatternArtworkOptions>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("Invalid pattern metadata.");options.Validate();return options;
    }
}
