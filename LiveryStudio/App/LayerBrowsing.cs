using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private readonly SemaphoreSlim layerThumbnailGate=new(2,2);
    private bool inspectingLayers,updatingInspection;
    private CompositeMaps? inspectionMaps;
    private bool inspectionHasMaterial;
    private long inspectionRevision;
    private CancellationTokenSource? inspectionCancellation;
    private string inspectionKey="";
    private void LayerSearchChanged(object sender,TextChangedEventArgs e){if(LayerList is not null&&document is not null)RefreshLayers();}
    private void LayerFilterChanged(object sender,SelectionChangedEventArgs e){if(LayerList is not null&&document is not null)RefreshLayers();}
    private void ClearLayerSearch(object sender,RoutedEventArgs e){LayerSearch.Text="";LayerTypeFilter.SelectedIndex=0;}
    private void UpdateLayerBrowserSelection()
    {
        var shown=(LayerList.ItemsSource as PaintLayer[])??[];var missing=SelectedLayers.Count(l=>shown.All(s=>s.Id!=l.Id));
        LayerBrowserStatus.Text=shown.Length==0?"No layers match · clear the search or filter.":$"{shown.Length} of {document?.Layers.Count??0} layers · topmost first";
        if(missing>0)LayerBrowserStatus.Text+=$" · {missing} selected outside this filter";
        ToolTipService.SetToolTip(LayerBrowserStatus,LayerBrowserStatus.Text);
        LayerFilterButton.Content=LayerTypeFilter.SelectedIndex>0?"Filtered":"Filter";
        InspectSelectionButton.IsEnabled=SelectedLayers.Count>0;
        if(inspectingLayers){if(SelectedLayers.Count==0)ClearLayerInspection();else _=RefreshLayerInspection();}
    }
    private async void LayerPreviewLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is not Image image||image.DataContext is not PaintLayer layer)return;
        var key="";
        try
        {
            var file=layer.Kind==LayerKind.Sticker?new FileInfo(layer.Asset):null;
            var stencil=layer.Effects is {StencilFile.Length:>0} effect?new FileInfo(effect.StencilFile):null;
            var value=layer.Kind+"|"+layer.Color+"|"+layer.Asset+"|"+(file is null?"":file.Length+"|"+file.LastWriteTimeUtc.Ticks)+"|"+JsonSerializer.Serialize(layer.Effects)+"|"+(stencil is null?"":stencil.Length+"|"+stencil.LastWriteTimeUtc.Ticks);
            key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];image.Tag=key;image.Source=null;
            var folder=Path.Combine(WorkshopService.DefaultWorkspace,"layer-thumbnails");Directory.CreateDirectory(folder);var path=Path.Combine(folder,key+".png");
            await layerThumbnailGate.WaitAsync();try{if(image.Tag as string!=key||!ReferenceEquals(image.DataContext,layer))return;if(!File.Exists(path)){var thumbnail=await Task.Run(()=>LayerBrowser.Thumbnail(layer));await ImageFiles.WriteAsync(thumbnail,path,overwrite:false);}}finally{layerThumbnailGate.Release();}
            if(image.Tag as string==key&&ReferenceEquals(image.DataContext,layer))image.Source=new BitmapImage{UriSource=new Uri(path),DecodePixelWidth=64};
        }
        catch(Exception x) when(x is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){if(image.Tag as string==key)ToolTipService.SetToolTip(image,"Artwork preview unavailable · "+x.Message);}
    }
    private void LayerPreviewUnloaded(object sender,RoutedEventArgs e){if(sender is Image image){image.Tag=null;image.Source=null;}}
    private void LayerPreviewContextChanged(FrameworkElement sender,DataContextChangedEventArgs args)=>LayerPreviewLoaded(sender,new());
    private async void InspectSelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updatingInspection)return;
        try
        {
            if(InspectSelectionButton.IsChecked!=true){ClearLayerInspection();return;}
            InspectSelectionButton.IsEnabled=false;await FlushAsync();if(SelectedLayers.Count==0)return;Select(0);hover=null;inspectingLayers=true;LayerInspectionNotice.Visibility=Visibility.Visible;CanvasHelp.Visibility=Visibility.Collapsed;updatingInspection=true;InspectSelectionButton.IsChecked=true;InspectSelectionButton.Content="Show full design";updatingInspection=false;await RefreshLayerInspection();
        }
        catch(Exception x){ClearLayerInspection();Fail(x);}
        finally{InspectSelectionButton.IsEnabled=SelectedLayers.Count>0;}
    }
    private async Task RefreshLayerInspection()
    {
        if(!inspectingLayers||document is null||mesh is null||draft is null)return;
        var ids=SelectedLayers.Select(l=>l.Id).ToArray();var key=generation+"|"+document.Cursor+"|"+string.Join(',',ids.Order());if(key==inspectionKey)return;inspectionKey=key;
        inspectionCancellation?.Cancel();inspectionCancellation?.Dispose();inspectionCancellation=new();var token=inspectionCancellation.Token;var revision=++inspectionRevision;var current=document;var version=generation;
        var snapshot=LayerBrowser.Inspection(current,ids);var model=mesh;var project=draft with {};CanvasStatus.Text="Preparing a temporary selection view…";
        try
        {
            var maps=await Task.Run(()=>snapshot.Compose(model,project,token),token);if(!inspectingLayers||revision!=inspectionRevision||version!=generation||!ReferenceEquals(current,document))return;
            inspectionMaps=maps;inspectionHasMaterial=snapshot.HasMaterial;UploadTexture();UploadStructure();CanvasStatus.Text=$"Inspecting {ids.Length} selected layers · hidden selections shown temporarily · design unchanged";LayerInspectionNotice.UpdateLayout();RefitCamera();if(floatingInspector){HideInspector();InspectorToggle.Focus(FocusState.Programmatic);}Render();
        }
        catch(OperationCanceledException){}
        catch(Exception x){if(revision==inspectionRevision){ClearLayerInspection();Fail(x);}}
    }
    private void ClearLayerInspection()
    {
        var active=inspectingLayers;inspectingLayers=false;inspectionKey="";inspectionMaps=null;inspectionRevision++;inspectionCancellation?.Cancel();inspectionCancellation?.Dispose();inspectionCancellation=null;
        if(LayerInspectionNotice is not null)LayerInspectionNotice.Visibility=Visibility.Collapsed;if(CanvasHelp is not null)CanvasHelp.Visibility=AdvancedMode&&Viewport.ActualHeight>=360?Visibility.Visible:Visibility.Collapsed;
        if(InspectSelectionButton is not null){updatingInspection=true;InspectSelectionButton.IsChecked=false;InspectSelectionButton.Content="Inspect selection on car";updatingInspection=false;}
        if(active){UploadTexture();UploadStructure();CanvasStatus.Text="Full design restored · inspection changed no artwork";CanvasHelp?.UpdateLayout();RefitCamera();Render();}
    }
}
