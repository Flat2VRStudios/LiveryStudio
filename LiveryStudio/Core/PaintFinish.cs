using System.Numerics;

namespace LiveryStudio.Core;

public readonly record struct PaintSurface(Vector3 Color, float Gloss, float Metalness, float ClearCoat);

public static class PaintFinish
{
	public static Vector3 Color(string hex) => new(Convert.ToByte(hex.Substring(1,2),16)/255f,Convert.ToByte(hex.Substring(3,2),16)/255f,Convert.ToByte(hex.Substring(5,2),16)/255f);
	public static float ToLinear(float c) => c<=.04045f?c/12.92f:MathF.Pow((c+.055f)/1.055f,2.4f);
	public static Vector3 ToLinear(Vector3 c) => new(ToLinear(c.X),ToLinear(c.Y),ToLinear(c.Z));
	public static float ToSrgb(float c) => c<=.0031308f?c*12.92f:1.055f*MathF.Pow(c,1/2.4f)-.055f;
	public static Vector3 ToSrgb(Vector3 c) => new(ToSrgb(c.X),ToSrgb(c.Y),ToSrgb(c.Z));
	// HDR.cg applies gamma 2.2 after exposure and tone mapping. Paint uniforms are linear.
	public static Vector3 ToDisplay(Vector3 linear) => new(MathF.Pow(Math.Clamp(linear.X,0,1),1/2.2f),MathF.Pow(Math.Clamp(linear.Y,0,1),1/2.2f),MathF.Pow(Math.Clamp(linear.Z,0,1),1/2.2f));
	public static Vector3 PaintColor(LiveryProject project,string hex) => project.UseSrgbPaintColors?ToLinear(Color(hex)):Color(hex);
	public static PaintSurface Sample(LiveryProject project,Vector4 albedo,Vector3 structure,float facing)
	{
		facing=Math.Clamp(facing,0,1);var mask=Math.Clamp(albedo.W,0,1);
		var paint=Vector3.Lerp(PaintColor(project,project.OuterColor),PaintColor(project,project.InnerColor),facing);
		var gloss=project.OuterGloss+(project.InnerGloss-project.OuterGloss)*facing;
		var mapGloss=(project.OverrideArtworkFinish?(float)project.ArtworkGloss:structure.X)*(1-structure.Z);
		var mapMetal=(project.OverrideArtworkFinish?(float)project.ArtworkMetalness:structure.Y)*(1-structure.Z);
		return new(Vector3.Lerp(paint,ToLinear(new Vector3(albedo.X,albedo.Y,albedo.Z)),mask),
			(float)gloss+(mapGloss-(float)gloss)*mask,(float)project.Metalness+(mapMetal-(float)project.Metalness)*mask,(float)project.ClearCoat*(1-mask));
	}
}

public sealed record FinishPreset(string Name,string Description,double OuterGloss,double InnerGloss,double Metalness,double ClearCoat)
{
	public LiveryProject Apply(LiveryProject p) => p with {OuterGloss=OuterGloss,InnerGloss=InnerGloss,Metalness=Metalness,ClearCoat=ClearCoat};
	public static IReadOnlyList<FinishPreset> All { get; } = [
		new("Matte","Soft highlights, no metallic reflection or clear coat.",.06,.08,0,0),
		new("Satin","A gentle sheen with broad highlights.",.3,.35,0,.12),
		new("Gloss","Polished paint beneath a reflective clear coat.",.82,.86,0,.9),
		new("Metallic","Colored metal reflections beneath a polished finish.",.76,.82,.9,.65),
		new("Chrome","Sharp, tinted metal reflections. Try a pale paint color.",.98,.98,1,0),
		new("Candy","A glossy metallic base with a strong clear coat.",.9,.94,.65,1),
		new("Pearl / two-tone","Different edge and face colors shift as you orbit. Choose two colors below.",.86,.8,.7,.9)
	];
}
