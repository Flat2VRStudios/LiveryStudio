using System.Text.Json;

namespace LiveryStudio.Core;
public sealed record ArtworkAsset(string Name,string Path);
public sealed record SavedPalette(string Name,string[] Colors);
public static class ArtworkLibrary
{
	public static readonly SavedPalette[] Palettes=[new("Track day",["#21DCC1","#0D141E","#FFFFFF","#FF4E42"]),new("Copper & carbon",["#F56824","#20242A","#FFFFFF","#FFD166"]),new("Classic racing",["#153E75","#E63946","#FFF3D6","#111111"]),new("Neon night",["#ED39CE","#3F37C9","#4CC9F0","#FAFF00"])];
	public static string Folder(string workspace)=>Path.Combine(workspace,"artwork");
	public static IReadOnlyList<ArtworkAsset> List(string workspace)
	{
		var folder=Folder(workspace);Directory.CreateDirectory(folder);return Directory.EnumerateFiles(folder).Where(p=>new[]{".png",".tga"}.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p=>p).Select(p=>new ArtworkAsset(Path.GetFileNameWithoutExtension(p),p)).ToArray();
	}
	public static string Add(string source,string workspace)
	{
		_ = FinishMaps.Read(source);var folder=Folder(workspace);Directory.CreateDirectory(folder);var destination=Path.Combine(folder,Path.GetFileNameWithoutExtension(source)+"-"+WorkshopService.Hash(source)[..8]+Path.GetExtension(source));if(!File.Exists(destination))File.Copy(source,destination);return destination;
	}
	public static string Generate(string shape,string text,string color,string workspace)
	{
		var image=Shape(shape,text,color);var folder=Folder(workspace);Directory.CreateDirectory(folder);var title=shape=="Number"?"Number "+text:shape;var path=Path.Combine(folder,title+"-"+color.TrimStart('#')+".tga");FinishMaps.WriteTga(image,path);return path;
	}
	public static PaintImage Shape(string shape,string text,string color)
	{
		if(!new[]{"Circle","Ring","Chevron","Racing stripe","Number"}.Contains(shape))throw new ArgumentException("Choose an available shape.");
		if(shape=="Number"&&(text.Length<1||text.Length>3||text.Any(c=>c<'0'||c>'9')))throw new ArgumentException("Racing numbers use one to three digits.");
		var rgb=PaintFinish.Color(color);const int size=512;var pixels=new byte[size*size*4];
		string[] digits=["11111100011000110001100011000111111","00100011000010000100001000010001110","11111000010000111111100001000011111","11111000010000101111000010000111111","10001100011000111111000010000100001","11111100001000011111000010000111111","11111100001000011111100011000111111","11111000010001000100010000100001000","11111100011000111111100011000111111","11111100011000111111000010000111111"];
		bool Covered(float x,float y)
		{
			var radius=MathF.Sqrt((x-.5f)*(x-.5f)+(y-.5f)*(y-.5f));
			return shape switch{"Circle"=>radius<.43f,"Ring"=>radius>.32f&&radius<.43f,"Chevron"=>MathF.Abs(y-(.3f+MathF.Abs(x-.5f)*.7f))<.065f,"Racing stripe"=>MathF.Abs(x-.35f)<.075f||MathF.Abs(x-.65f)<.075f,"Number"=>Number(x,y),_=>false};
		}
		bool Number(float x,float y){if(x<.08f||x>=.92f||y<.13f||y>=.87f)return false;var local=(x-.08f)/.84f*text.Length;var digit=Math.Clamp((int)local,0,text.Length-1);var column=(int)((local-digit)*6);var row=(int)((y-.13f)/.74f*7);return column<5&&row<7&&digits[text[digit]-'0'][row*5+column]=='1';}
		for(var y=0;y<size;y++)for(var x=0;x<size;x++){var k=(y*size+x)*4;var coverage=0;foreach(var offset in new[]{.25f,.75f})foreach(var offsetY in new[]{.25f,.75f})if(Covered((x+offset)/size,(y+offsetY)/size))coverage++;pixels[k]=(byte)MathF.Round(rgb.X*255);pixels[k+1]=(byte)MathF.Round(rgb.Y*255);pixels[k+2]=(byte)MathF.Round(rgb.Z*255);pixels[k+3]=(byte)(coverage*255/4);}
		return new(size,size,pixels);
	}
}
