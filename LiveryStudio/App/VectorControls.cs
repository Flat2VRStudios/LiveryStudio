using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private VectorArtworkOptions? pendingVector;
    private async Task<int?> ChooseVectorResolution(int current=2048)
    {
        var resolution=new ComboBox{Header="Longest edge",HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=new[]{512,1024,2048,4096},SelectedItem=current};
        var body=new StackPanel{Spacing=12};body.Children.Add(new TextBlock{Text="Keep the original SVG in your project. Choose how much detail to use for the sticker texture.",TextWrapping=TextWrapping.Wrap});body.Children.Add(resolution);
        body.Children.Add(new TextBlock{Text="2,048 px works well for most logos. You can change the resolution later from Layers.",TextWrapping=TextWrapping.Wrap,FontSize=12});
        var dialog=new ContentDialog{Title="SVG artwork",Content=body,XamlRoot=XamlRoot,PrimaryButtonText="Use SVG",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
        return await dialog.ShowAsync()==ContentDialogResult.Primary?(int)resolution.SelectedItem:null;
    }
    private async void ImportArtworkFile(object sender,RoutedEventArgs e)
    {
        long request=0;
        try
        {
            var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));picker.FileTypeFilter.Add(".png");picker.FileTypeFilter.Add(".tga");picker.FileTypeFilter.Add(".svg");
            if(await picker.PickSingleFileAsync() is not {} file)return;
            request=++stickerRequestRevision;stickerLoading=true;string path;
            if(Path.GetExtension(file.Path).Equals(".svg",StringComparison.OrdinalIgnoreCase))
            {
                var size=await ChooseVectorResolution();if(size is null||request!=stickerRequestRevision)return;CanvasStatus.Text="Rendering SVG artwork…";
                path=await Task.Run(()=>VectorArtwork.Import(file.Path,WorkshopService.DefaultWorkspace,size.Value));
            }
            else path=await Task.Run(()=>ArtworkReplacement.Import(file.Path,WorkshopService.DefaultWorkspace));
            if(request!=stickerRequestRevision)return;RefreshArtwork();if(!await UseSticker(path,request:request))return;InspectorSection.SelectedIndex=0;if(floatingInspector)HideInspector();
            CanvasStatus.Text=pendingVector is null?"Sticker ready · hover over the car and click to place it.":"SVG ready · hover and click to place it. The original vector travels with your project.";
        }
        catch(Exception x){if(request==0||request==stickerRequestRevision)Fail(x);}
        finally{if(request==stickerRequestRevision)stickerLoading=false;}
    }
    private async void ChangeVectorResolution(object sender,RoutedEventArgs e)
    {
        if(document is null||SelectedLayers.Count!=1||SelectedLayer is not {Locked:false,VectorArtwork:{} source} layer)return;
        var target=document;var id=layer.Id;
        try
        {
            var size=await ChooseVectorResolution(source.RasterSize);if(size is null||size==source.RasterSize)return;
            await FlushAsync();CanvasStatus.Text="Re-rendering original SVG…";var options=source with {RasterSize=size.Value};
            var path=await Task.Run(()=>VectorArtwork.Rebuild(options,WorkshopService.DefaultWorkspace));
            if(!ReferenceEquals(document,target)||document.Layers.FirstOrDefault(l=>l.Id==id) is not {Locked:false})return;
            document.Commit("SVG resolution · "+size+" px",layers=>{var selected=layers.Single(l=>l.Id==id);selected.Asset=path;selected.VectorArtwork=options;});
            selectedLayer=id;QueueComposite();RefreshArtwork();
        }
        catch(Exception x){Fail(x);}
    }
}
