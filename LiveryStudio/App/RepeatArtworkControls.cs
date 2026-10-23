using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private CancellationTokenSource? repeatLifetime;
    private async void RepeatArtwork(object sender,RoutedEventArgs e)
    {
        if(repeatLifetime is not null||photoLifetime is not null||maskPreparing||maskCommitting||document is null||mesh is null||draft is null||SelectedLayers.Count==0)return;
        using var lifetime=new CancellationTokenSource();repeatLifetime=lifetime;
        var previews=new List<Task>();var requests=new List<CancellationTokenSource>();CancellationTokenSource? pending=null;
        using var previewGate=new SemaphoreSlim(1,1);
        var previewParent=Path.Combine(WorkshopService.DefaultWorkspace,"repeat-previews");var cache=Path.Combine(previewParent,Guid.NewGuid().ToString("N"));
        Image? image=null;var closed=false;long revision=0;
        try
        {
            var bound=document;var version=generation;await FlushAsync();lifetime.Token.ThrowIfCancellationRequested();
            if(!ReferenceEquals(document,bound)||version!=generation||draft is null||mesh is null)return;
            Select(6);ClearGroupHover();ClearDirectPreview();
            var current=document;var model=mesh;var project=JsonSerializer.Deserialize<LiveryProject>(JsonSerializer.Serialize(draft,Contract.Json),Contract.Json)!;
            var selected=SelectedLayers.ToArray();var ids=selected.Select(l=>l.Id).ToArray();var mirrored=directSelection?.Mirrored==true;
            var before=JsonSerializer.Serialize(current,LayerDocument.Json);var originalCamera=CameraPose;var lighting=CurrentPhotoLighting();
            bool Unchanged()=>ReferenceEquals(document,current)&&generation==version&&JsonSerializer.Serialize(current,LayerDocument.Json)==before;
            var frame=LayerSets.Frame(LayerSets.Display(selected,mirrored));
            var arrangement=new DialogComboBox{Header="Arrange copies",ItemsSource=new[]{"Line","Grid","Circle"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            NumberBox Number(string label,double value,double min,double max,double step=1)=>new(){Header=label,Value=value,Minimum=min,Maximum=max,SmallChange=step,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact};
            var count=Number("Copies · includes the original",3,2,64);var columns=Number("Columns",3,1,32);var rows=Number("Rows",2,1,32);
            var size=Number("Copy size · %",33,5,300,5);
            var gap=Number("Gap between copies · cm",3,0,500);var rowGap=Number("Gap between rows · cm",3,0,500);
            var direction=Number("Line direction · degrees",0,-180,180,15);var radius=Number("Circle radius · cm",Math.Round(Math.Clamp(frame.Width*.35,0.02,3)*100),.5,500);
            var rotate=new CheckBox{Content="Rotate copies around the circle",IsChecked=true};var center=new CheckBox{Content="Center pattern on this selection",IsChecked=true};
            var panels=new CheckBox{Content="Keep panel restrictions",IsChecked=true,Visibility=selected.Any(l=>l.Triangles.Length>0)?Visibility.Visible:Visibility.Collapsed};
            var whole=new CheckBox{Content="Preview the whole car",IsChecked=true};
            image=new Image{Height=XamlRoot.Size.Height<650?110:185,Stretch=Stretch.Uniform};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image,"Repeated artwork preview");
            var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
            var body=new StackPanel{Spacing=10};body.Children.Add(image);body.Children.Add(status);body.Children.Add(arrangement);
            foreach(var control in new FrameworkElement[]{count,columns,rows,size,gap,rowGap,direction,radius,rotate,center,panels,whole})body.Children.Add(control);
            body.Children.Add(new TextBlock{Text="Copies stay editable. One Undo restores your starting design. If artwork falls beyond the body, reduce the count, gap or radius.",FontSize=12,TextWrapping=TextWrapping.Wrap});
            var dialog=new ContentDialog{Title="Repeat artwork",XamlRoot=XamlRoot,Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(XamlRoot.Size.Height-210,140,600),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},PrimaryButtonText="Create pattern",CloseButtonText="Cancel",IsPrimaryButtonEnabled=false,DefaultButton=ContentDialogButton.Primary};
            Directory.CreateDirectory(cache);ArtworkRepeatOptions? ready=null;
            ArtworkRepeatOptions Options()
            {
                var mode=(RepeatArrangement)arrangement.SelectedIndex;
                int Whole(NumberBox field){var value=field.Value;if(!double.IsFinite(value)||value!=Math.Round(value))throw new ArgumentException("Use a whole number for copies, rows and columns.");return (int)value;}
                var options=new ArtworkRepeatOptions{Arrangement=mode,Count=mode==RepeatArrangement.Grid?3:Whole(count),Columns=mode==RepeatArrangement.Grid?Whole(columns):3,Rows=mode==RepeatArrangement.Grid?Whole(rows):2,Scale=(float)(size.Value/100),Gap=mode==RepeatArrangement.Circle?.03f:(float)(gap.Value/100),RowGap=mode==RepeatArrangement.Grid?(float)(rowGap.Value/100):.03f,Direction=mode==RepeatArrangement.Line?(float)direction.Value:0,Radius=mode==RepeatArrangement.Circle?(float)(radius.Value/100):.3f,RotateAroundCircle=rotate.IsChecked==true,CenterPattern=center.IsChecked==true,KeepPanels=panels.IsChecked==true};options.Validate();return options;
            }
            void ShowOptions()
            {
                var mode=(RepeatArrangement)arrangement.SelectedIndex;
                count.Visibility=mode==RepeatArrangement.Grid?Visibility.Collapsed:Visibility.Visible;
                columns.Visibility=rows.Visibility=rowGap.Visibility=mode==RepeatArrangement.Grid?Visibility.Visible:Visibility.Collapsed;
                gap.Visibility=mode==RepeatArrangement.Circle?Visibility.Collapsed:Visibility.Visible;direction.Visibility=mode==RepeatArrangement.Line?Visibility.Visible:Visibility.Collapsed;
                radius.Visibility=rotate.Visibility=mode==RepeatArrangement.Circle?Visibility.Visible:Visibility.Collapsed;
            }
            async Task Refresh(CancellationToken token,long request)
            {
                try
                {
                    ShowOptions();var options=Options();var showWhole=whole.IsChecked==true;await Task.Delay(180,token);
                    await previewGate.WaitAsync(token);
                    try
                    {
                    token.ThrowIfCancellationRequested();if(closed||request!=revision)return;
                    var result=await Task.Run(()=>
                    {
                        var snapshot=JsonSerializer.Deserialize<LayerDocument>(before,LayerDocument.Json)!;var plan=ArtworkRepeats.Create(snapshot,ids,options,mirrored,token,model);plan.Apply(snapshot);
                        var members=snapshot.Layers.Where(l=>plan.PatternIds.Contains(l.Id)).ToArray();var normal=LayerSets.Frame(LayerSets.Display(selected,mirrored)).Normal;
                        var pose=showWhole?StudioCamera.Fit(StudioCamera.Geometry(model),originalCamera.Yaw,originalCamera.Pitch,16f/9):StudioCamera.Fit(StudioCamera.Artwork(LayerSets.Display(members,mirrored)),MathF.Atan2(normal.X,normal.Z),MathF.Asin(Math.Clamp(normal.Y,-1,1)),16f/9);
                        using var photography=new StudioPhotography(AppContext.BaseDirectory,model,snapshot,project,lighting,token);
                        return (Pixels:photography.Capture(pose,640,360,token),Layers:plan.TotalLayers,Copies:plan.CopyIds.Count);
                    },token);
                    token.ThrowIfCancellationRequested();if(closed||request!=revision||!Unchanged())return;
                    var path=Path.Combine(cache,request+".png");await ImageFiles.WriteAsync(result.Pixels,path);token.ThrowIfCancellationRequested();
                    if(closed||request!=revision||!Unchanged())return;
                    image.Source=new BitmapImage{UriSource=new Uri(path)};ready=options;
                    status.Text=$"{options.Total} copies · {result.Copies} new editable layers · {result.Layers} total layers · preview only";dialog.IsPrimaryButtonEnabled=true;
                    }
                    finally{previewGate.Release();}
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(!closed&&request==revision){status.Text=x.Message;dialog.IsPrimaryButtonEnabled=false;}}
            }
            void Schedule()
            {
                if(closed)return;var request=++revision;pending?.Cancel();pending=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);requests.Add(pending);
                ready=null;dialog.IsPrimaryButtonEnabled=false;status.Text="Preparing your pattern…";previews.Add(Refresh(pending.Token,request));
            }
            arrangement.SelectionChanged+=(_,_)=>Schedule();foreach(var number in new[]{count,columns,rows,size,gap,rowGap,direction,radius})number.ValueChanged+=(_,_)=>Schedule();
            foreach(var check in new[]{rotate,center,panels,whole})check.Click+=(_,_)=>Schedule();
            ShowOptions();Schedule();var outcome=await dialog.ShowAsync();closed=true;revision++;lifetime.Cancel();await Task.WhenAll(previews);
            if(outcome!=ContentDialogResult.Primary)return;
            if(ready is null||!Unchanged()||ready!=Options()){CanvasStatus.Text="The design changed during the preview. Select the artwork and try again.";return;}
            var applied=ArtworkRepeats.Create(current,ids,ready,mirrored,mesh:model);applied.Apply(current);ResetCreativeEditing();ClearDirectPreview();SetLayerSelection(applied.PatternIds,ids.Last());QueueComposite();await FlushAsync();HideInspector();
            CanvasStatus.Text=$"Created {ready.Total}-copy pattern · copies stay editable · one Undo restores the original.";SelectTool.Focus(FocusState.Programmatic);
        }
        catch(OperationCanceledException){}
        catch(Exception x){Fail(x);}
        finally
        {
            closed=true;lifetime.Cancel();await Task.WhenAll(previews);foreach(var request in requests)request.Dispose();if(image is not null)image.Source=null;repeatLifetime=null;
            try{if(Directory.Exists(cache)&&string.Equals(Path.GetDirectoryName(Path.GetFullPath(cache)),Path.GetFullPath(previewParent),StringComparison.OrdinalIgnoreCase))Directory.Delete(cache,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
