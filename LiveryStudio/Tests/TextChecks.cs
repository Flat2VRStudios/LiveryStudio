using System.Runtime.Versioning;
using LiveryStudio.Core;

internal static class TextChecks
{
	[SupportedOSPlatform("windows")]
	public static void Run(string temp,Action<bool,string> check,Action<Action,string> reject)
	{
		var options=new TextArtworkOptions{Text="TEAM FURY 27",FontName="Segoe UI",Color="#F56824",Bold=true};
		var image=TextArtwork.Render(options);image.Validate();
		check(image.Width<=2048&&image.Height<=2048&&image.Width>image.Height,"Typed sponsor text is fitted into a bounded transparent decal");
		check(image.Pixels.Where((_,i)=>i%4==3).Any(a=>a==0)&&image.Pixels.Where((_,i)=>i%4==3).Any(a=>a==255),"Text decals preserve transparent backgrounds and solid glyph interiors");
		check(image.Pixels.Where((v,i)=>i%4==0&&image.Pixels[i+3]==255).Any(v=>v==245),"Font rasterization preserves the chosen sticker RGB color");
		var outlined=TextArtwork.Render(options with {OutlineWidth=.04f,OutlineColor="#FFFFFF"});
		check(!outlined.Pixels.SequenceEqual(image.Pixels)&&outlined.Pixels.Where((v,i)=>i%4==0&&outlined.Pixels[i+3]==255).Any(v=>v==255),"Text outlines are actually drawn into the reusable decal pixels");
		var spaced=TextArtwork.Render(options with {Tracking=.18f});check((double)spaced.Width/spaced.Height>(double)image.Width/image.Height,"Letter spacing changes the layout's aspect ratio");
		var accented=TextArtwork.Render(options with {Text="ÉQUIPE 27\nRACING"});check(accented.Height>image.Height,"Multiline and accented text render through the installed font");
		var first=TextArtwork.Generate(options,temp);var second=TextArtwork.Generate(options with {Text="TEAM FURY 28"},temp);
		check(first!=second&&File.Exists(first)&&File.Exists(second)&&FinishMaps.Read(first).Pixels.SequenceEqual(image.Pixels),"Editing text creates retained source assets without overwriting earlier history");
		check(TextArtwork.ReadMetadata(first) is {Text:"TEAM FURY 27",FontName:"Segoe UI"},"Reusable text artwork retains its editable source definition in the library");
		reject(()=>TextArtwork.Render(options with {Text="  "}),"Reject text decals with no visible content");
		reject(()=>TextArtwork.Render(options with {Tracking=float.NaN}),"Reject nonfinite text spacing");
		reject(()=>TextArtwork.Render(options with {OutlineWidth=1}),"Reject unbounded text outline widths");
	}
}
