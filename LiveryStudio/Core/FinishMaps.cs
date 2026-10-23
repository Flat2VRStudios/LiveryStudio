using StbImageSharp;

namespace LiveryStudio.Core;

public static class FinishMaps
{
	public static PaintImage Read(string path)
		=>Read(path,default);
	public static PaintImage Read(string path,CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();
		if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase)){var t=TgaDecoder.Decode(File.ReadAllBytes(path),cancellation);return new(t.Width,t.Height,t.Pixels);}
		using var stream=File.OpenRead(path);var image=ImageResult.FromStream(stream,ColorComponents.RedGreenBlueAlpha);
		cancellation.ThrowIfCancellationRequested();
		if(image.Width>8192||image.Height>8192)throw new InvalidDataException("Choose an image of at most 8192 × 8192.");
		return new(image.Width,image.Height,image.Data);
	}
	public static PaintImage Artwork(PaintImage source,LiveryProject project,CancellationToken cancellation=default)
	{
		cancellation.ThrowIfCancellationRequested();
		source.Validate();var result=new PaintImage(source.Width,source.Height,(byte[])source.Pixels.Clone());
		if(project.OverrideArtworkFinish){var gloss=(byte)Math.Round(project.ArtworkGloss*255);var metal=(byte)Math.Round(project.ArtworkMetalness*255);for(var i=0;i<result.Pixels.Length;i+=4){if((i&4095)==0)cancellation.ThrowIfCancellationRequested();result.Pixels[i]=gloss;result.Pixels[i+1]=metal;}}
		return result;
	}
	// Match the material compositor before replaying local finish strokes. The
	// mask must be the immutable base image, not albedo promoted by later decals.
	public static void InitializePaintSurface(PaintImage surface,PaintImage originalColor,LiveryProject project,CancellationToken cancellation=default)
	{
		surface.Validate();originalColor.Validate();
		for(var y=0;y<surface.Height;y++)
		{
			cancellation.ThrowIfCancellationRequested();for(var x=0;x<surface.Width;x++)
			{
				var mask=originalColor.Pixels[((y*originalColor.Height/surface.Height)*originalColor.Width+x*originalColor.Width/surface.Width)*4+3]/255f;var k=(y*surface.Width+x)*4;
				surface.Pixels[k]=(byte)MathF.Round(float.Lerp((float)project.InnerGloss*255,surface.Pixels[k],mask));
				surface.Pixels[k+1]=(byte)MathF.Round(float.Lerp((float)project.Metalness*255,surface.Pixels[k+1],mask));
			}
		}
	}
	public static void WriteTga(PaintImage image,string path)
	{
		image.Validate();using var writer=new BinaryWriter(File.Create(path));var header=new byte[18];header[2]=2;header[12]=(byte)image.Width;header[13]=(byte)(image.Width>>8);header[14]=(byte)image.Height;header[15]=(byte)(image.Height>>8);header[16]=32;header[17]=40;writer.Write(header);
		var pixels=(byte[])image.Pixels.Clone();for(var i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);writer.Write(pixels);
	}
}
