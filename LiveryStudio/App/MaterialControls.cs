using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System.Globalization;

namespace LiveryStudio;

public sealed class PercentConverter : IValueConverter
{
	public object Convert(object value,Type targetType,object parameter,string language)=>((double)value).ToString("P0",CultureInfo.CurrentCulture);
	public object ConvertBack(object value,Type targetType,object parameter,string language)=>throw new NotSupportedException();
}

public partial class MainWindow
{
	private LiveryProject ReadFinish(LiveryProject p)=>p with {UseSrgbPaintColors=ColorMode.IsOn,OverrideArtworkFinish=ArtworkOverride.IsOn,ArtworkGloss=ArtworkGloss.Value,ArtworkMetalness=ArtworkMetalness.Value,DamageGloss=DamageGloss.Value,DamageMetalness=DamageMetalness.Value};
	private void ApplyFinishWidgets()
	{
		ColorMode.IsOn=project.UseSrgbPaintColors;ArtworkOverride.IsOn=project.OverrideArtworkFinish;ArtworkGloss.Value=project.ArtworkGloss;ArtworkMetalness.Value=project.ArtworkMetalness;DamageGloss.Value=project.DamageGloss;DamageMetalness.Value=project.DamageMetalness;
		ArtworkGloss.IsEnabled=ArtworkMetalness.IsEnabled=project.OverrideArtworkFinish;
		FinishSelector.SelectedItem=FinishPreset.All.FirstOrDefault(p=>p.OuterGloss==project.OuterGloss&&p.InnerGloss==project.InnerGloss&&p.Metalness==project.Metalness&&p.ClearCoat==project.ClearCoat);
	}
	private void ResetFinishPreset(){var before=loading;loading=true;FinishSelector.SelectedItem=null;loading=before;}
	private void FinishPresetChanged(object sender,SelectionChangedEventArgs e)
	{
		if(loading||FinishSelector.SelectedItem is not FinishPreset preset)return;
		ApplyLiveFinish(preset.Apply(ReadDraft()));
	}
	private void ApplyLiveFinish(LiveryProject changed)
	{
		if(loading||busy)return;project=changed;ApplyProject();Invalidate();Painter.FinishChanged(project);
	}
	private void ApplyPainterFinish(LiveryProject changed)
	{
		// A queued editor change may finish while Save/Close is flushing it.
		// Keep the host fields synchronized even though new input is disabled.
		if(loading)return;project=changed;ApplyProject();Invalidate();Painter.FinishChanged(project);
	}
	private void MaterialToggleChanged(object sender,RoutedEventArgs e)
	{
		if(loading)return;ArtworkGloss.IsEnabled=ArtworkMetalness.IsEnabled=ArtworkOverride.IsOn;UpdateSwatches();Invalidate();Painter.FinishChanged(ReadDraft());
	}
	private void SwapPaintColors(object sender,RoutedEventArgs e)
	{
		project=ReadDraft();(project.OuterColor,project.InnerColor)=(project.InnerColor,project.OuterColor);ApplyProject();Invalidate();Painter.FinishChanged(project);
	}
}
