using System.Numerics;

namespace LiveryStudio.Core;

public static class DesignColor
{
    public static string PaintPickerHex(LiveryProject project,string field)=>project.UseSrgbPaintColors?ColorLibrary.Normalize(field):Hex(PaintFinish.ToSrgb(PaintFinish.Color(ColorLibrary.Normalize(field))));
    public static string PaintFieldHex(LiveryProject project,string picker)=>project.UseSrgbPaintColors?ColorLibrary.Normalize(picker):Hex(PaintFinish.ToLinear(PaintFinish.Color(ColorLibrary.Normalize(picker))));
    public static string Hex(Vector3 color)=>$"#{Byte(color.X):X2}{Byte(color.Y):X2}{Byte(color.Z):X2}";
    private static byte Byte(float value)=>(byte)Math.Clamp(MathF.Round(value*255),0,255);
    public static string Sample(LiveryProject project,PaintImage image,SurfaceHit hit,Vector3 eye)
    {
        image.Validate();if(!float.IsFinite(hit.UV.X)||!float.IsFinite(hit.UV.Y)||!Finite(hit.Normal)||!Finite(hit.Position)||!Finite(eye)||hit.Normal.LengthSquared()<1e-8f||(eye-hit.Position).LengthSquared()<1e-8f)throw new ArgumentException("A valid body point and viewing direction are required.");
        // Match the renderer's clamped bilinear atlas sampling, excluding its
        // lighting, reflection, normal-map, dirt, and damage simulations.
        var x=Math.Clamp(hit.UV.X,0,1)*image.Width-.5f;var y=Math.Clamp(hit.UV.Y,0,1)*image.Height-.5f;var x0=(int)MathF.Floor(x);var y0=(int)MathF.Floor(y);var fx=x-x0;var fy=y-y0;
        Vector4 Pixel(int px,int py){var at=(Math.Clamp(py,0,image.Height-1)*image.Width+Math.Clamp(px,0,image.Width-1))*4;return new(image.Pixels[at]/255f,image.Pixels[at+1]/255f,image.Pixels[at+2]/255f,image.Pixels[at+3]/255f);}
        var rgba=Vector4.Lerp(Vector4.Lerp(Pixel(x0,y0),Pixel(x0+1,y0),fx),Vector4.Lerp(Pixel(x0,y0+1),Pixel(x0+1,y0+1),fx),fy);
        var facing=Math.Clamp(Vector3.Dot(Vector3.Normalize(hit.Normal),Vector3.Normalize(eye-hit.Position)),0,1);var surface=PaintFinish.Sample(project,rgba,Vector3.Zero,facing);return Hex(PaintFinish.ToSrgb(surface.Color));
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
