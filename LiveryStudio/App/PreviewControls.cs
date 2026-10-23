using System.Runtime.InteropServices;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private sealed record PreviewChoice(string Name,string Specular="",string Irradiance="",float LightAngle=120,float SunStrength=1,int Mode=0);
	private bool actualProbe;
	private float sunStrength=1;
	[DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]private static extern int ls_environment(nint renderer,string specular,string irradiance);
	[DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_selection(nint renderer,uint[] mask,uint triangles);
	private void InitializePreviewChoices()
	{
		var choices=new List<PreviewChoice>{new("Studio lighting"),new("Outdoor sky",Mode:1),new("Color check · unlit",Mode:2)};
		var path=Path.Combine(AppContext.BaseDirectory,"Preview","environments.json");if(File.Exists(path))choices.AddRange(JsonSerializer.Deserialize<List<PreviewChoice>>(File.ReadAllText(path),Contract.Json)??[]);
		PreviewEnvironment.ItemsSource=choices;PreviewEnvironment.SelectedIndex=0;
	}
	private void ApplyEnvironment()
	{
		if(renderer==0||PreviewEnvironment.SelectedItem is not PreviewChoice choice)return;
		try{var root=Path.Combine(AppContext.BaseDirectory,"Preview");Check(ls_environment(renderer,choice.Specular.Length>0?Path.Combine(root,choice.Specular):"",choice.Irradiance.Length>0?Path.Combine(root,choice.Irradiance):""));actualProbe=choice.Specular.Length>0;sunStrength=choice.SunStrength;if(actualProbe)PreviewLight.Value=choice.LightAngle;Render();}catch(Exception x){Fail(x);}
	}
	private PaintImage ReadPreviewMap(string file){var image=FinishMaps.Read(file);return draft?.IndependentPaintSides==true?IndependentPaintSides.Atlas(image):image;}
	private async Task LoadPreviewMaps(int car,int version)
	{
		var root=Path.Combine(AppContext.BaseDirectory,"Preview",car.ToString("00"));if(!Directory.Exists(root))throw new DirectoryNotFoundException("The app's car preview assets are missing. Extract the complete bundle.");
		foreach(var (name,slot) in new[]{("normal",3),("dirt-surface",5),("damage",6),("damage-normal",7)})
		{
			var image=await Task.Run(()=>ReadPreviewMap(Path.Combine(root,name+".png")));if(version!=generation)return;if(renderer!=0)Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,slot));
		}
		await LoadDirt(car,version);ApplyEnvironment();
	}
	private async Task LoadDirt(int car,int version)
	{
		var type=new[]{"dust","mud","sand","snow"}[Math.Clamp(DirtType.SelectedIndex,0,3)];var image=await Task.Run(()=>ReadPreviewMap(Path.Combine(AppContext.BaseDirectory,"Preview",car.ToString("00"),"dirt-"+type+".png")));
		if(version==generation&&renderer!=0)Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,4));Render();
	}
	private async void DirtTypeChanged(object sender,SelectionChangedEventArgs e){if(mesh is null||draft is null)return;try{await LoadDirt(draft.CarIndex,generation);}catch(Exception x){Fail(x);}}
	private void PreviewToggled(object sender,RoutedEventArgs e){if(Viewport is not null)Render();}
}
