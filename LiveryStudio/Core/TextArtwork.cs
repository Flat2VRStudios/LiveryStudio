using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace LiveryStudio.Core;

public sealed record TextArtworkOptions
{
	public string Text {get;set;}="TEAM FURY";
	public string FontName {get;set;}="Segoe UI";
	public string Color {get;set;}="#FFFFFF";
	public string OutlineColor {get;set;}="#101A27";
	public bool Bold {get;set;}=true;
	public bool Italic {get;set;}
	public float OutlineWidth {get;set;}
	public float Tracking {get;set;}
	public void Validate()
	{
		if(string.IsNullOrWhiteSpace(Text)||Text.Length>160||Text.Count(c=>c=='\n')>3||Text.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n')))throw new ArgumentException("Type 1–160 visible characters, on up to four lines.");
		if(string.IsNullOrWhiteSpace(FontName)||FontName.Length>100)throw new ArgumentException("Choose an installed font.");
		_ = PaintFinish.Color(Color);_ = PaintFinish.Color(OutlineColor);
		if(!float.IsFinite(OutlineWidth)||OutlineWidth<0||OutlineWidth>.15f||!float.IsFinite(Tracking)||Tracking<-.1f||Tracking>.5f)throw new ArgumentException("Text outline and spacing are outside their supported range.");
	}
}

public static class TextArtwork
{
	[SupportedOSPlatform("windows")]
	public static IReadOnlyList<string> Fonts()
	{
		using var collection=new InstalledFontCollection();var families=collection.Families;
		try{return families.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();}
		finally{foreach(var family in families)family.Dispose();}
	}
	[SupportedOSPlatform("windows")]
	public static string Generate(TextArtworkOptions options,string workspace)
	{
		var image=Render(options);var folder=ArtworkLibrary.Folder(workspace);Directory.CreateDirectory(folder);
		var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json)))[..16];
		var title=string.Concat(options.Text.Take(30).Select(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)?'_':c));
		var path=Path.Combine(folder,"Text "+title+"-"+hash+".tga");if(!File.Exists(path))FinishMaps.WriteTga(image,path);AtomicFiles.Write(path+".text.json",JsonSerializer.SerializeToUtf8Bytes(options,Contract.Json));return path;
	}
	public static TextArtworkOptions? ReadMetadata(string image)
	{
		var path=image+".text.json";if(!File.Exists(path))return null;if(new FileInfo(path).Length>65536)throw new InvalidDataException("Text artwork metadata is too large.");
		var options=JsonSerializer.Deserialize<TextArtworkOptions>(File.ReadAllText(path),Contract.Json)??throw new InvalidDataException("Empty text artwork metadata.");options.Validate();return options;
	}
	[SupportedOSPlatform("windows")]
	public static PaintImage Render(TextArtworkOptions options)
	{
		options.Validate();const float em=384;
		using var family=new FontFamily(options.FontName);var style=(options.Bold?FontStyle.Bold:FontStyle.Regular)|(options.Italic?FontStyle.Italic:FontStyle.Regular);
		if(!family.IsStyleAvailable(style))style=family.IsStyleAvailable(FontStyle.Regular)?FontStyle.Regular:family.IsStyleAvailable(FontStyle.Bold)?FontStyle.Bold:FontStyle.Italic;
		using var font=new Font(family,em,style,GraphicsUnit.Pixel);using var format=(StringFormat)StringFormat.GenericTypographic.Clone();format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
		using var measureBitmap=new Bitmap(1,1);using var measure=Graphics.FromImage(measureBitmap);using var path=new GraphicsPath();
		var y=0f;
		foreach(var line in options.Text.Replace("\r","").Split('\n'))
		{
			if(options.Tracking==0)path.AddString(line,family,(int)style,em,new PointF(0,y),format);
			else{var x=0f;var elements=StringInfo.GetTextElementEnumerator(line);while(elements.MoveNext()){var element=elements.GetTextElement();path.AddString(element,family,(int)style,em,new PointF(x,y),format);x+=measure.MeasureString(element,font,new PointF(0,0),format).Width+options.Tracking*em;}}
			y+=font.GetHeight(measure)*1.1f;
		}
		var bounds=path.GetBounds();if(bounds.Width<=0||bounds.Height<=0)throw new ArgumentException("This text has no visible glyphs in the selected font.");
		var outline=options.OutlineWidth*em;var padding=outline*.5f+5;var scale=Math.Min(1,2046/Math.Max(bounds.Width+padding*2,bounds.Height+padding*2));
		var width=Math.Max(1,(int)Math.Ceiling((bounds.Width+padding*2)*scale)+2);var height=Math.Max(1,(int)Math.Ceiling((bounds.Height+padding*2)*scale)+2);
		using(var transform=new Matrix()){transform.Translate(-bounds.Left+padding,-bounds.Top+padding,MatrixOrder.Append);transform.Scale(scale,scale,MatrixOrder.Append);path.Transform(transform);}
		using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
		using(var graphics=Graphics.FromImage(bitmap))
		{
			graphics.Clear(System.Drawing.Color.Transparent);graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.CompositingQuality=CompositingQuality.HighQuality;
			if(outline>0){using var pen=new Pen(Ink(options.OutlineColor),outline*scale){LineJoin=LineJoin.Round};graphics.DrawPath(pen,path);}
			using var brush=new SolidBrush(Ink(options.Color));graphics.FillPath(brush,path);
		}
		var pixels=new byte[width*height*4];var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
		try{var row=new byte[width*4];for(var line=0;line<height;line++){Marshal.Copy(IntPtr.Add(data.Scan0,line*data.Stride),row,0,row.Length);for(var x=0;x<width;x++){var input=x*4;var output=(line*width+x)*4;pixels[output]=row[input+2];pixels[output+1]=row[input+1];pixels[output+2]=row[input];pixels[output+3]=row[input+3];}}}
		finally{bitmap.UnlockBits(data);}return new(width,height,pixels);
	}
	private static System.Drawing.Color Ink(string hex){var rgb=PaintFinish.Color(hex);return System.Drawing.Color.FromArgb(255,(int)MathF.Round(rgb.X*255),(int)MathF.Round(rgb.Y*255),(int)MathF.Round(rgb.Z*255));}
}
