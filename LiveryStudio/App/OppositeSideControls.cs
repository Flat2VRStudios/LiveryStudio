using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private void FrameSideArtwork(IReadOnlyList<PaintLayer> layers)
    {
        var normal=LayerSets.Frame(layers).Normal;
        yaw=MathF.Atan2(normal.X,normal.Z);pitch=MathF.Asin(Math.Clamp(normal.Y,-1,1));
        cameraFitPoints=StudioCamera.Artwork(layers);RefitCamera();
    }

    private static bool OtherSideSharesArtwork(PaintMesh model,IReadOnlyList<PaintLayer> layers,CancellationToken cancellation)
    {
        foreach(var layer in layers)foreach(var sample in layer.Samples)
        {
            cancellation.ThrowIfCancellationRequested();
            var p=layer.Transform(sample);var hit=model.Raycast(p.Center+p.Normal*(p.DepthRange+.02f),-p.Normal);
            if(hit is {} at&&p.Map(at.Position,at.Normal,out _)&&MeshEditing.SharedUvCount(model,at)>0)return true;
        }
        return false;
    }

    private async void OtherSideArtwork(object sender,RoutedEventArgs e)
    {
        if(maskPreparing||maskCommitting||document is null||mesh is null||draft is null||SelectedLayers.Count==0)return;
        try
        {
            await FlushAsync();Select(6);ClearGroupHover();ClearDirectPreview();
            var current=document;var model=mesh;var project=draft;var version=generation;
            var ids=SelectedLayers.Select(l=>l.Id).ToArray();var selected=SelectedLayers.ToArray();
            var split=selected.Any(l=>l.Mirror);var cursor=current.Cursor;var checkpoint=current.History[cursor];var padding=current.SeamPadding;
            var before=JsonSerializer.Serialize(current,LayerDocument.Json);var originalCamera=CameraPose;var originalFit=cameraFitPoints;
            bool Unchanged()=>ReferenceEquals(document,current)&&version==generation&&current.Cursor==cursor&&current.SeamPadding==padding&&ReferenceEquals(current.History[cursor],checkpoint);
            var orientation=new DialogComboBox{Header="Artwork on the other side",ItemsSource=new[]{"Readable lettering and layout","Mirror design · reverse lettering"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch,Visibility=split?Visibility.Collapsed:Visibility.Visible};
            var panels=new CheckBox{Content="Keep panel restrictions",IsChecked=true,Visibility=!split&&selected.Any(l=>l.Triangles.Length>0)?Visibility.Visible:Visibility.Collapsed};
            var view=new DialogComboBox{Header="Preview angle",ItemsSource=new[]{"Other side","Original side"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            var image=new Image{Height=XamlRoot.Size.Height<650?105:185,Stretch=Stretch.Uniform};
            var status=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap};
            var body=new StackPanel{Spacing=12,Width=Math.Min(370,Math.Max(230,XamlRoot.Size.Width-110))};
            body.Children.Add(new Border{Child=image,Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,12,20,30)),CornerRadius=new(6)});
            body.Children.Add(new TextBlock{Text=split?"Give each side its own editable layers. Your design keeps its exact appearance.":"Make editable copies on the other side. Readable mode keeps a whole badge's lettering and layout together; mirror mode reflects every part of the design.",TextWrapping=TextWrapping.Wrap,FontSize=12});
            body.Children.Add(orientation);body.Children.Add(panels);
            if(panels.Visibility==Visibility.Visible)body.Children.Add(new TextBlock{Text="If the opposite body panels differ, turn this off to allow nearby panels. Projection angle and depth still apply.",FontSize=11,TextWrapping=TextWrapping.Wrap});
            body.Children.Add(view);body.Children.Add(status);
            body.Children.Add(new TextBlock{Text="One Undo restores the original. Named design colors still affect every linked layer. Original artwork files stay intact.",TextWrapping=TextWrapping.Wrap,FontSize=11});
            var dialog=new ContentDialog{Title=split?"Separate paired sides":"Copy to the other side",Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(XamlRoot.Size.Height-190,140,650)},XamlRoot=XamlRoot,PrimaryButtonText=split?"Separate sides":"Create copy",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
            var active=true;var sharedArtwork=false;long revision=0;CancellationTokenSource? cancellation=null;var tasks=new List<Task>();
            var previewRoot=Path.Combine(WorkshopService.DefaultWorkspace,"other-side-previews",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(previewRoot);
            OppositeSideMode Mode()=>split?OppositeSideMode.SeparatePaired:orientation.SelectedIndex==1?OppositeSideMode.Mirrored:OppositeSideMode.Readable;
            async Task Refresh()
            {
                var request=++revision;cancellation?.Cancel();cancellation?.Dispose();cancellation=new();var token=cancellation.Token;
                dialog.IsPrimaryButtonEnabled=false;status.Text="Preparing the other side…";
                var choice=Mode();var keep=panels.IsChecked==true;var originalView=view.SelectedIndex==1;
                try
                {
                    var prepared=await Task.Run(()=>
                    {
                        var snapshot=JsonSerializer.Deserialize<LayerDocument>(before,LayerDocument.Json)!;
                        var source=snapshot.Layers.Where(l=>ids.Contains(l.Id)).ToArray();
                        var plan=OppositeSideArtwork.Create(snapshot,model,ids,choice,keep,token);plan.Apply(snapshot);
                        var copies=snapshot.Layers.Where(l=>plan.CopyIds.Contains(l.Id)).ToArray();
                        return (Maps:snapshot.Compose(model,project,token),Source:source,Copies:copies,Total:snapshot.Layers.Count,Shared:OtherSideSharesArtwork(model,copies,token));
                    },token);
                    if(!active||request!=revision||!Unchanged())return;
                    Check(ls_texture(renderer,prepared.Maps.Albedo.Pixels,(uint)prepared.Maps.Albedo.Width,(uint)prepared.Maps.Albedo.Height,0));
                    Check(ls_texture(renderer,prepared.Maps.Structure.Pixels,(uint)prepared.Maps.Structure.Width,(uint)prepared.Maps.Structure.Height,2));
                    FrameSideArtwork(originalView?prepared.Source:prepared.Copies);Render();
                    var renderSize=PreviewPixelSize;var width=(int)renderSize.Width;var height=(int)renderSize.Height;var pixels=new byte[checked(width*height*4)];
                    Check(ls_snapshot(renderer,pixels,(uint)pixels.Length));var path=Path.Combine(previewRoot,request+".png");
                    await ImageFiles.WriteAsync(new(width,height,pixels),path);
                    if(!active||request!=revision||!Unchanged())return;
                    image.Source=new BitmapImage{CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(path)};
                    status.Text=$"{prepared.Copies.Length} editable layer{(prepared.Copies.Length==1?"":"s")} on the other side · {prepared.Total} total · preview only";
                    if(split&&prepared.Copies.Length>selected.Count(l=>l.Mirror))status.Text+=". Stroke samples are grouped by side to keep their exact blending order.";
                    sharedArtwork=prepared.Shared;
                    if(sharedArtwork)status.Text+=". This body surface shares its artwork area with other panels; edits can appear on those panels too.";
                    dialog.IsPrimaryButtonEnabled=true;
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(active&&request==revision){status.Text=x.Message;dialog.IsPrimaryButtonEnabled=false;}}
            }
            void Schedule(){if(active)tasks.Add(Refresh());}
            orientation.SelectionChanged+=(_,_)=>Schedule();view.SelectionChanged+=(_,_)=>Schedule();panels.Click+=(_,_)=>Schedule();dialog.Opened+=(_,_)=>Schedule();
            ContentDialogResult result;
            try{result=await dialog.ShowAsync();}
            finally
            {
                active=false;cancellation?.Cancel();await Task.WhenAll(tasks);cancellation?.Dispose();
                UploadTexture();UploadStructure();cameraFitPoints=originalFit;ApplyCamera(originalCamera,render:false);RefitCamera();Render();
            }
            if(result!=ContentDialogResult.Primary)return;
            if(!Unchanged()){CanvasStatus.Text="The design changed during the preview. Select the artwork and try again.";return;}
            var applied=OppositeSideArtwork.Create(current,model,ids,Mode(),panels.IsChecked==true);applied.Apply(current);
            ResetCreativeEditing();ClearDirectPreview();SetLayerSelection(applied.CopyIds,applied.CopyIds.Last());QueueComposite();
            RememberCamera();FrameSideArtwork(SelectedLayers);HideInspector();Render();
            CanvasStatus.Text=split?"Paired sides separated · the other side is selected · one Undo restores the pair":"Other-side copy selected · edit it independently · one Undo removes the copy";
            if(sharedArtwork)CanvasStatus.Text+=" · Shared artwork area: edits may also show on other panels";
            SelectTool.Focus(FocusState.Programmatic);
        }
        catch(Exception x){Fail(x);}
    }
}
