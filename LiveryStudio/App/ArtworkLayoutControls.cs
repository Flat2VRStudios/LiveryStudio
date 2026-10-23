using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private void SyncArtworkLayoutControls()
	{
		if(LayoutActions is null)return;var count=ArtworkLayout.ObjectCount(SelectedLayers,LayoutKeepGroups.IsChecked==true);var editable=!SelectedLayers.Any(l=>l.Locked);
		LayoutReadout.Text=$"{count} layout object"+(count==1?"":"s")+(count==1&&SelectedLayers.Count>1?" · uncheck the option below to arrange members":" · in the selection plane");
		foreach(var child in LayoutActions.Children.OfType<Button>()){var operation=Enum.Parse<LayoutOperation>((string)child.Tag);var distribution=operation is LayoutOperation.HorizontalCenters or LayoutOperation.VerticalCenters or LayoutOperation.HorizontalGaps or LayoutOperation.VerticalGaps;child.IsEnabled=editable&&count>=(distribution?3:2);}
		LayoutReference.IsEnabled=editable&&count>=2;LayoutGap.IsEnabled=editable&&count>=2;
	}
	private void LayoutOptionsChanged(object sender,RoutedEventArgs e)=>SyncArtworkLayoutControls();
	private void ReusableGroupChosen(object sender,SelectionChangedEventArgs e)=>SyncReusableLibrary();
	private void SyncReusableLibrary(){if(UseGroupButton is null||ExportGroupButton is null)return;UseGroupButton.IsEnabled=ExportGroupButton.IsEnabled=GroupLibrary.SelectedItem is ReusableGroup;}
	private void ArrangeSelected(object sender,RoutedEventArgs e)
	{
		if(sender is not Button {Tag:string tag}||!Enum.TryParse<LayoutOperation>(tag,out var operation)||!double.IsFinite(LayoutGap.Value))return;
		var keep=LayoutKeepGroups.IsChecked==true;var gap=(float)LayoutGap.Value;var anchor=LayoutReference.IsChecked==true?selectedLayer:null;var mirrored=directSelection?.Mirrored==true;
		EditSelection("Arranged selection · "+operation,(layers,ids)=>{var shown=LayerSets.Display(layers.Where(l=>ids.Contains(l.Id)).ToArray(),mirrored);ReplaceLayerSet(layers,LayerSets.Display(ArtworkLayout.Arrange(shown,operation,keep,gap,anchor),mirrored));});Select(6);SelectTool.Focus(FocusState.Programmatic);if(floatingInspector)HideInspector();
	}
	private async void ImportArtworkGroup(object sender,RoutedEventArgs e)
	{
		try{var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".flart");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));if(await picker.PickSingleFileAsync() is {} file){var imported=await Task.Run(()=>ReusableArtwork.Import(file.Path,WorkshopService.DefaultWorkspace));RefreshGroupLibrary(imported);CanvasStatus.Text="Editable artwork imported · choose Place saved artwork to use it.";}}
		catch(Exception x){Fail(x);}
	}
	private async void ExportArtworkGroup(object sender,RoutedEventArgs e)
	{
		if(GroupLibrary.SelectedItem is not ReusableGroup group){CanvasStatus.Text="Choose saved artwork to export.";return;}
		try{var picker=new FileSavePicker{SuggestedFileName=string.Concat(group.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))};picker.FileTypeChoices.Add("Editable Livery Studio artwork",new[]{".flart"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));if(await picker.PickSaveFileAsync() is {} file){await Task.Run(()=>File.Copy(group.Path,file.Path,true));CanvasStatus.Text="Artwork exported with its text, components and finish settings.";}}
		catch(Exception x){Fail(x);}
	}
}
