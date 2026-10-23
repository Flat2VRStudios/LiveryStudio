using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private async void ReplaceArtwork(object sender,RoutedEventArgs e)
    {
        if(maskPreparing||maskCommitting||document is null||SelectedLayers.Count!=1||SelectedLayer is not {Kind:LayerKind.Sticker,Locked:false} layer)return;
        var targetDocument=document;var version=generation;var id=layer.Id;var targetMesh=mesh;var project=draft;
        try
        {
            await FlushAsync();
            // A mask brush may keep an under-layer atlas and draw the former
            // decal separately. End that transient renderer before previewing.
            Select(6);ClearGroupHover();ClearDirectPreview();
            var cursor=targetDocument.Cursor;var checkpoint=targetDocument.History[cursor];var padding=targetDocument.SeamPadding;
            var before=JsonSerializer.Serialize(targetDocument,LayerDocument.Json);
            var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));
            foreach(var extension in new[]{".png",".tga",".svg"})picker.FileTypeFilter.Add(extension);
            if(await picker.PickSingleFileAsync() is not {} file)return;
            string asset;
            if(Path.GetExtension(file.Path).Equals(".svg",StringComparison.OrdinalIgnoreCase))
            {
                var size=await ChooseVectorResolution(layer.VectorArtwork?.RasterSize??2048);if(size is null)return;
                asset=await Task.Run(()=>VectorArtwork.Import(file.Path,WorkshopService.DefaultWorkspace,size.Value));
            }
            else asset=await Task.Run(()=>ArtworkReplacement.Import(file.Path,WorkshopService.DefaultWorkspace));
            bool Unchanged()=>ReferenceEquals(document,targetDocument)&&version==generation&&targetDocument.Cursor==cursor&&targetDocument.SeamPadding==padding&&ReferenceEquals(targetDocument.History[cursor],checkpoint);
            if(!Unchanged()){CanvasStatus.Text="The design changed while choosing artwork. Select the decal and try again.";return;}
            var image=await Task.Run(()=>FinishMaps.Read(asset));var initial=ArtworkReplacement.Create(layer,asset);
            var fit=new DialogComboBox{Header="Size of the new artwork",ItemsSource=new[]{"Keep the exact decal box","Fit inside the box · keep image proportions"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            var preview=new Image{Height=XamlRoot.Size.Height<700?88:150,Stretch=Stretch.Uniform};
            var label=new TextBlock{Text=Path.GetFileName(file.Path),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
            var status=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap};
            var body=new StackPanel{Spacing=12,Width=Math.Min(370,Math.Max(230,XamlRoot.Size.Width-110))};
            body.Children.Add(label);body.Children.Add(new Border{Child=preview,Padding=new(8),CornerRadius=new(6),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,55,60,67))});
            body.Children.Add(fit);body.Children.Add(new TextBlock{Text="Position, rotation, paired sides, opacity, finish and masks stay with this decal. The name and group stay too. The exact box can stretch an image with different proportions.",TextWrapping=TextWrapping.Wrap,FontSize=12});
            body.Children.Add(status);body.Children.Add(new TextBlock{Text="Preview the replacement on the car. Cancel keeps your design; one Undo restores the original artwork.",TextWrapping=TextWrapping.Wrap,FontSize=11});
            var dialog=new ContentDialog{Title="Replace artwork",Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(XamlRoot.Size.Height-190,140,650)},XamlRoot=XamlRoot,PrimaryButtonText="Replace artwork",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
            var active=true;long revision=0;CancellationTokenSource? cancellation=null;var tasks=new List<Task>();
            var previewPath=Path.Combine(WorkshopService.DefaultWorkspace,"replacement-previews",Guid.NewGuid().ToString("N")+".png");Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
            ArtworkReplacementFit Fit()=>fit.SelectedIndex==1?ArtworkReplacementFit.PreserveProportions:ArtworkReplacementFit.ExistingBox;
            async Task Refresh()
            {
                var current=++revision;cancellation?.Cancel();cancellation?.Dispose();cancellation=new();var token=cancellation.Token;
                dialog.IsPrimaryButtonEnabled=false;status.Text="Preparing replacement preview…";
                try
                {
                    var sizing=Fit();var effectPreview=await Task.Run(()=>DecalEffects.Apply(image,initial.Effects,token),token);
                    if(!active||current!=revision)return;
                    // A unique image per refresh prevents a delayed image decoder
                    // from showing another sizing request's in-flight file.
                    var path=Path.ChangeExtension(previewPath,current+".png");await ImageFiles.WriteAsync(effectPreview,path);
                    if(!active||current!=revision)return;preview.Source=new BitmapImage{CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(path)};
                    if(targetMesh is not null&&project is not null)
                    {
                        var snapshot=JsonSerializer.Deserialize<LayerDocument>(before,LayerDocument.Json)!;ArtworkReplacement.Apply(snapshot,id,asset,sizing);
                        var maps=await Task.Run(()=>snapshot.Compose(targetMesh,project,token),token);
                        if(!active||current!=revision||!Unchanged())return;
                        Check(ls_texture(renderer,maps.Albedo.Pixels,(uint)maps.Albedo.Width,(uint)maps.Albedo.Height,0));
                        Check(ls_texture(renderer,maps.Structure.Pixels,(uint)maps.Structure.Width,(uint)maps.Structure.Height,2));Render();
                    }
                    status.Text=$"{image.Width:N0} × {image.Height:N0} · replacement preview · design not changed";dialog.IsPrimaryButtonEnabled=true;
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(active&&current==revision){status.Text=x.Message;dialog.IsPrimaryButtonEnabled=false;}}
            }
            void Schedule(){if(active)tasks.Add(Refresh());}
            fit.SelectionChanged+=(_,_)=>Schedule();dialog.Opened+=(_,_)=>Schedule();
            ContentDialogResult result;
            try{result=await dialog.ShowAsync();}
            finally{active=false;cancellation?.Cancel();await Task.WhenAll(tasks);cancellation?.Dispose();UploadTexture();UploadStructure();Render();}
            if(result!=ContentDialogResult.Primary)return;
            if(!Unchanged()){CanvasStatus.Text="The design changed during the preview. Select the decal and try again.";return;}
            ArtworkReplacement.Apply(targetDocument,id,asset,Fit());selectedLayer=id;ResetCreativeEditing();ClearDirectPreview();QueueComposite();RefreshArtwork();
            CanvasStatus.Text="Artwork replaced · placement and effects preserved · Ctrl+Z restores the original.";
        }
        catch(Exception x){Fail(x);}
    }
}
