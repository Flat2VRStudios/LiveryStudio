using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private CancellationTokenSource? imagePreparationCancellation;
    private ContentDialog? imagePreparationDialog;
    private long imagePreparationEpoch;
    private readonly SemaphoreSlim imagePreparationGate=new(1,1);
    private void CancelImagePreparation(){imagePreparationEpoch++;FinishImageReview(ImageReviewResult.Cancel);imagePreparationCancellation?.Cancel();imagePreparationDialog?.Hide();}
    private async void EditImageArtwork(object sender,RoutedEventArgs e)=>await PrepareImageArtwork(editing:true);
    private async void PreparePendingImage(object sender,RoutedEventArgs e)=>await PrepareImageArtwork(editing:false);

    private static PaintImage ImagePreparationPreview(PaintImage source)
    {
        var scale=Math.Min(1,640d/Math.Max(source.Width,source.Height));var width=Math.Max(1,(int)Math.Round(source.Width*scale));var height=Math.Max(1,(int)Math.Round(source.Height*scale));var bytes=new byte[width*height*4];
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {
            var sx=Math.Min(source.Width-1,(int)((x+.5)*source.Width/width));var sy=Math.Min(source.Height-1,(int)((y+.5)*source.Height/height));var i=(sy*source.Width+sx)*4;var o=(y*width+x)*4;
            var alpha=source.Pixels[i+3]/255f;var checker=((x/12+y/12)&1)==0?45:72;
            for(var c=0;c<3;c++)bytes[o+c]=(byte)Math.Round(source.Pixels[i+c]*alpha+checker*(1-alpha));bytes[o+3]=255;
        }
        return new(width,height,bytes);
    }

    private async Task PrepareImageArtwork(bool editing)
    {
        if(maskPreparing||maskCommitting||imagePreparationCancellation is not null)return;
        if(editing&&(document is null||SelectedLayers.Count!=1||SelectedLayer is not {Locked:false} selected||!ImagePreparation.CanEdit(selected)))return;
        if(!editing&&(sticker is null||stickerPath.Length==0||pendingGroup is not null||pendingText is not null||pendingPattern is not null||pendingVector is not null))return;
        var openingDocument=document;var openingGeneration=generation;var openingLayer=SelectedLayer?.Id;var openingRequest=stickerRequestRevision;var openingPath=stickerPath;
        using var lifetime=new CancellationTokenSource();var lifetimeToken=lifetime.Token;var operation=++imagePreparationEpoch;imagePreparationCancellation=lifetime;
        string? ownedPreviewFolder=null;Image? previewImage=null;
        try
        {
            await FlushAsync();lifetimeToken.ThrowIfCancellationRequested();
            if(!ReferenceEquals(document,openingDocument)||generation!=openingGeneration||(editing?SelectedLayers.Count!=1||SelectedLayer?.Id!=openingLayer:stickerRequestRevision!=openingRequest||stickerPath!=openingPath))return;
            ClearGroupHover();ClearDirectPreview();if(editing)Select(6);
            var target=document;var version=generation;var layer=editing?SelectedLayer:null;var id=layer?.Id;
            var before=target is null?"":JsonSerializer.Serialize(target,LayerDocument.Json);var currentPath=editing?layer!.Asset:stickerPath;var placementRequest=stickerRequestRevision;
            var initialRecipe=editing?layer!.RasterArtwork:pendingRaster;
            var recipe=JsonSerializer.Deserialize<RasterArtworkOptions>(JsonSerializer.Serialize(initialRecipe??new(currentPath),LayerDocument.Json),LayerDocument.Json)!;
            var original=await Task.Run(()=>FinishMaps.Read(recipe.SourceFile,lifetimeToken),lifetimeToken);
            bool TargetStillCurrent()=>!lifetimeToken.IsCancellationRequested&&operation==imagePreparationEpoch&&ReferenceEquals(document,target)&&generation==version&&(editing?mode==6&&SelectedLayers.Count==1&&SelectedLayer?.Id==id&&target is not null&&JsonSerializer.Serialize(target,LayerDocument.Json)==before:mode==2&&stickerRequestRevision==placementRequest&&stickerPath==currentPath);
            bool Unchanged()=>!lifetime.IsCancellationRequested&&TargetStillCurrent();
            if(!Unchanged())return;
            using var previewLifetime=CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);var previewToken=previewLifetime.Token;
            var initial=recipe.Preparation??new ImagePreparationOptions{BackgroundColor=ImagePreparation.GuessBackground(original)};
            var remove=new CheckBox{Content="Remove a flat background",IsChecked=initial.RemoveBackground};
            var key=new TextBox{Header="Background color",Text=initial.BackgroundColor,MaxLength=7};
            var pick=new Button{Content="Pick from image",HorizontalAlignment=HorizontalAlignment.Stretch};
            var scope=new DialogComboBox{Header="Where to remove it",ItemsSource=new[]{"Connected to image edges","Every matching color · includes enclosed holes"},SelectedIndex=(int)initial.Scope,HorizontalAlignment=HorizontalAlignment.Stretch};
            NumberBox Percent(string name,double value)=>new(){Header=name,Value=Math.Round(value*100,2),Minimum=0,Maximum=100,SmallChange=1,LargeChange=10,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Compact,HorizontalAlignment=HorizontalAlignment.Stretch};
            var tolerance=Percent("Color tolerance · %",initial.Tolerance);var softness=Percent("Soft edge · %",initial.Softness);
            var left=Percent("Left · %",initial.CropLeft);var right=Percent("Right · %",initial.CropRight);var top=Percent("Top · %",initial.CropTop);var bottom=Percent("Bottom · %",initial.CropBottom);
            var originalView=new CheckBox{Content="Show the original image",IsChecked=false};
            var preview=new Image{Height=Math.Clamp(400d*original.Height/original.Width+8,80,XamlRoot.Size.Height<700?100:170),Stretch=Stretch.Uniform};
            previewImage=preview;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(preview,"Image preparation preview · click to pick when armed");
            var previewBox=new Border{Child=preview,Padding=new(4),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(255,38,40,43)),CornerRadius=new(6)};
            var status=new TextBlock{Text="Preparing image preview…",FontSize=12,TextWrapping=TextWrapping.Wrap};
            var pickStatus=new TextBlock{Text="Checkerboard areas are transparent. The original stays in your project.",FontSize=11,TextWrapping=TextWrapping.Wrap};
            var fit=new DialogComboBox{Header="Crop sizing on the car",ItemsSource=new[]{"Keep the existing decal box","Fit inside the box · keep image proportions"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            var reset=new Button{Content="Reset background and crop",HorizontalAlignment=HorizontalAlignment.Stretch};
            Grid Pair(FrameworkElement a,FrameworkElement b){var grid=new Grid{ColumnSpacing=8};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());grid.Children.Add(a);Grid.SetColumn(b,1);grid.Children.Add(b);return grid;}
            var body=new StackPanel{Spacing=10,Width=Math.Clamp(XamlRoot.Size.Width-140,220,400)};
            body.Children.Add(previewBox);body.Children.Add(originalView);body.Children.Add(status);body.Children.Add(remove);body.Children.Add(Pair(key,pick));body.Children.Add(scope);body.Children.Add(Pair(tolerance,softness));body.Children.Add(pickStatus);
            var crop=new StackPanel{Spacing=8};crop.Children.Add(new TextBlock{Text="Remove a percentage from each edge of the original image.",FontSize=11,TextWrapping=TextWrapping.Wrap});crop.Children.Add(Pair(left,right));crop.Children.Add(Pair(top,bottom));
            if(editing){crop.Children.Add(fit);crop.Children.Add(new TextBlock{Text="Existing masks stay normalized to the resulting decal box. Cropping changes the artwork inside that box; it does not recut an existing mask.",FontSize=11,TextWrapping=TextWrapping.Wrap});}
            body.Children.Add(new Expander{Header="Crop image",Content=crop,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch});body.Children.Add(reset);
            var settingsScroll=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(XamlRoot.Size.Height-190,130,650)};
            var dialog=new ContentDialog{Title=editing?"Prepare this image":"Prepare image for stamping",Content=settingsScroll,XamlRoot=XamlRoot,PrimaryButtonText=editing?"Apply to this decal":"Use prepared image",CloseButtonText="Cancel",SecondaryButtonText=editing?"Inspect on car":"",DefaultButton=ContentDialogButton.Primary,IsPrimaryButtonEnabled=false};
            imagePreparationDialog=dialog;
            var active=true;var updating=false;var pickArmed=false;var displayedOriginal=false;var ready=false;var hasPixels=false;long revision=0;
            CancellationTokenSource? refreshCancellation=null;var tasks=new List<Task>();
            var previewRoot=Path.Combine(WorkshopService.DefaultWorkspace,"image-previews",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(previewRoot);
            ownedPreviewFolder=previewRoot;
            var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(180);timer.IsRepeating=false;
            ImagePreparationOptions Read()=>new(){RemoveBackground=remove.IsChecked==true,BackgroundColor=key.Text,Scope=(BackgroundRemovalScope)scope.SelectedIndex,Tolerance=(float)(tolerance.Value/100),Softness=(float)(softness.Value/100),CropLeft=(float)(left.Value/100),CropTop=(float)(top.Value/100),CropRight=(float)(right.Value/100),CropBottom=(float)(bottom.Value/100)};
            bool Changed()=>JsonSerializer.Serialize(Read(),LayerDocument.Json)!=JsonSerializer.Serialize(initial,LayerDocument.Json)||editing&&fit.SelectedIndex!=0;
            ArtworkReplacementFit Fit()=>fit.SelectedIndex==1?ArtworkReplacementFit.PreserveProportions:ArtworkReplacementFit.ExistingBox;
            async Task Refresh()
            {
                var request=revision;refreshCancellation?.Cancel();refreshCancellation=new CancellationTokenSource();using var cancellation=refreshCancellation;using var linked=CancellationTokenSource.CreateLinkedTokenSource(previewToken,cancellation.Token);var token=linked.Token;
                try
                {
                    var options=Read();options.Validate();var showOriginal=originalView.IsChecked==true;var currentRecipe=recipe with {Preparation=options};
                    await imagePreparationGate.WaitAsync(token);
                    PaintImage image;bool visible;
                    try
                    {
                        var prepared=await Task.Run(()=>ArtworkColors.Render(original,currentRecipe,token),token);visible=false;
                        for(var i=3;i<prepared.Pixels.Length;i+=4){if((i&65535)==3)token.ThrowIfCancellationRequested();if(prepared.Pixels[i]>0){visible=true;break;}}
                        image=showOriginal?original:prepared;var display=await Task.Run(()=>ImagePreparationPreview(image),token);await ImageFiles.WriteAsync(display,Path.Combine(previewRoot,request+".png"));
                    }
                    finally{imagePreparationGate.Release();}
                    token.ThrowIfCancellationRequested();if(!active||request!=revision||!Unchanged())return;
                    preview.Source=new BitmapImage{UriSource=new Uri(Path.Combine(previewRoot,request+".png")),CreateOptions=BitmapCreateOptions.IgnoreImageCache};displayedOriginal=showOriginal;
                    ready=true;hasPixels=visible;dialog.IsPrimaryButtonEnabled=Changed()&&visible;dialog.IsSecondaryButtonEnabled=editing&&visible;
                    status.Text=visible?$"{image.Width:N0} × {image.Height:N0} · "+(showOriginal?"original image":"prepared preview · checkerboard is transparent"):"No visible pixels remain. Reduce tolerance, change the color, or reset the image.";
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(active&&request==revision){status.Text=x.Message;dialog.IsPrimaryButtonEnabled=dialog.IsSecondaryButtonEnabled=false;}}
                finally{if(ReferenceEquals(refreshCancellation,cancellation))refreshCancellation=null;}
            }
            void Schedule()
            {
                if(!active||updating)return;revision++;ready=false;refreshCancellation?.Cancel();dialog.IsPrimaryButtonEnabled=dialog.IsSecondaryButtonEnabled=false;timer.Stop();timer.Start();
                if(editing){UploadTexture();UploadStructure();Render();}
            }
            timer.Tick+=(_,_)=>tasks.Add(Refresh());
            remove.Click+=(_,_)=>Schedule();scope.SelectionChanged+=(_,_)=>Schedule();key.TextChanged+=(_,_)=>Schedule();originalView.Click+=(_,_)=>Schedule();fit.SelectionChanged+=(_,_)=>Schedule();
            foreach(var number in new[]{tolerance,softness,left,right,top,bottom})number.ValueChanged+=(_,_)=>Schedule();
            pick.Click+=(_,_)=>{pickArmed=true;originalView.IsChecked=true;pickStatus.Text="Click a background pixel in the original image above. Transparent padding is left intact.";Schedule();};
            preview.PointerPressed+=(_,args)=>
            {
                if(!pickArmed||!ready||!displayedOriginal)return;
                var point=args.GetCurrentPoint(preview).Position;var scale=Math.Min(preview.ActualWidth/original.Width,preview.ActualHeight/original.Height);if(scale<=0)return;
                var x=(point.X-(preview.ActualWidth-original.Width*scale)/2)/scale;var y=(point.Y-(preview.ActualHeight-original.Height*scale)/2)/scale;
                if(x<0||y<0||x>=original.Width||y>=original.Height)return;var i=((int)y*original.Width+(int)x)*4;
                if(original.Pixels[i+3]==0){pickStatus.Text="That pixel is already transparent. Choose a visible background color.";return;}
                updating=true;try{key.Text=$"#{original.Pixels[i]:X2}{original.Pixels[i+1]:X2}{original.Pixels[i+2]:X2}";remove.IsChecked=true;originalView.IsChecked=false;}finally{updating=false;}
                pickArmed=false;pickStatus.Text="Background picked. Edge mode keeps enclosed details; Everywhere also removes matching holes.";Schedule();args.Handled=true;
            };
            reset.Click+=(_,_)=>
            {
                updating=true;try{remove.IsChecked=false;key.Text=ImagePreparation.GuessBackground(original);scope.SelectedIndex=0;tolerance.Value=4;softness.Value=8;left.Value=right.Value=top.Value=bottom.Value=0;originalView.IsChecked=false;}finally{updating=false;}pickArmed=false;Schedule();
            };
            CompositeMaps? reviewCandidate=null;var requestReview=false;
            async Task PreviewCar()
            {
                reviewCandidate=null;var request=revision;dialog.IsSecondaryButtonEnabled=dialog.IsPrimaryButtonEnabled=false;
                try
                {
                    if(!ready||!hasPixels||!editing||layer is null||mesh is null||draft is null||!Unchanged())return;var options=Read();options.Validate();var sizing=Fit();var model=mesh;var project=draft;
                    status.Text="Preparing the image on your car…";await imagePreparationGate.WaitAsync(previewToken);
                    CompositeMaps maps;
                    try
                    {
                        maps=await Task.Run(()=>{var asset=ArtworkColors.Generate(recipe with {Preparation=options},previewRoot,previewToken);var snapshot=JsonSerializer.Deserialize<LayerDocument>(before,LayerDocument.Json)!;ImagePreparation.Apply(snapshot,layer.Id,asset,sizing);return snapshot.Compose(model,project,previewToken);},previewToken);
                    }
                    finally{imagePreparationGate.Release();}
                    if(!active||request!=revision||!Unchanged())return;
                    reviewCandidate=maps;
                }
                catch(OperationCanceledException){}
                catch(Exception x){if(active&&request==revision)status.Text=x.Message;}
                finally{if(active&&request==revision&&ready){dialog.IsPrimaryButtonEnabled=Changed()&&hasPixels;dialog.IsSecondaryButtonEnabled=editing&&hasPixels;}}
            }
            dialog.SecondaryButtonClick+=async(_,args)=>{args.Cancel=true;var deferral=args.GetDeferral();var task=PreviewCar();tasks.Add(task);try{await task;if(reviewCandidate is not null&&Unchanged()){requestReview=true;dialog.Hide();}}finally{deferral.Complete();}};
            dialog.Opened+=(_,_)=>{revision++;tasks.Add(Refresh());};
            var result=ContentDialogResult.None;
            try
            {
                while(Unchanged())
                {
                    requestReview=false;reviewCandidate=null;imagePreparationDialog=dialog;
                    body.Width=Math.Clamp(XamlRoot.Size.Width-140,220,400);settingsScroll.MaxHeight=Math.Clamp(XamlRoot.Size.Height-190,130,650);
                    preview.Height=Math.Clamp(400d*original.Height/original.Width+8,80,XamlRoot.Size.Height<700?100:170);
                    result=await dialog.ShowAsync();
                    if(!requestReview||reviewCandidate is not {} maps||!Unchanged())break;
                    imagePreparationDialog=null;timer.Stop();refreshCancellation?.Cancel();
                    var review=await InspectPreparedImage(maps,Changed()&&hasPixels,previewToken);
                    if(review==ImageReviewResult.Settings)continue;
                    result=review==ImageReviewResult.Apply?ContentDialogResult.Primary:ContentDialogResult.None;break;
                }
            }
            finally
            {
                FinishImageReview(ImageReviewResult.Cancel);
                active=false;timer.Stop();refreshCancellation?.Cancel();previewLifetime.Cancel();await Task.WhenAll(tasks);if(editing){UploadTexture();UploadStructure();Render();}
                if(ReferenceEquals(imagePreparationDialog,dialog))imagePreparationDialog=null;
            }
            // Preview ends at close; the operation token still protects final generation.
            if(result!=ContentDialogResult.Primary)return;
            if(!TargetStillCurrent()){CanvasStatus.Text="The design changed. Open image preparation again.";return;}
            var finalRecipe=recipe with {Preparation=Read()};var finalFit=Fit();finalRecipe.Validate();
            CanvasStatus.Text="Saving the prepared image…";var finalPath=await Task.Run(()=>ArtworkColors.Generate(finalRecipe,WorkshopService.DefaultWorkspace,lifetimeToken),lifetimeToken);
            if(!TargetStillCurrent()){CanvasStatus.Text="The design changed while preparing the image. Your artwork was not replaced.";return;}
            if(editing)
            {
                ImagePreparation.Apply(target!,id!.Value,finalPath,finalFit);selectedLayer=id;ClearDirectPreview();QueueComposite();RefreshArtwork();CanvasStatus.Text="Image prepared · original retained · Ctrl+Z restores the previous image.";
            }
            else if(await UseSticker(finalPath,request:placementRequest,stillCurrent:TargetStillCurrent)){RefreshArtwork();HideInspector();CanvasStatus.Text="Prepared image ready · hover and stamp · original and settings stay editable.";}
        }
        catch(OperationCanceledException){}
        catch(Exception x){Fail(x);}
        finally
        {
            if(ReferenceEquals(imagePreparationCancellation,lifetime))imagePreparationCancellation=null;
            if(previewImage is not null)previewImage.Source=null;
            if(ownedPreviewFolder is {} folder)
            {
                var parent=Path.GetFullPath(Path.Combine(WorkshopService.DefaultWorkspace,"image-previews"));var full=Path.GetFullPath(folder);
                if(full.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileName(full),"N",out _))
                    for(var attempt=0;attempt<2;attempt++)
                    {
                        try{await Task.Run(()=>{if(Directory.Exists(full))Directory.Delete(full,true);});break;}
                        catch(Exception x) when(x is IOException or UnauthorizedAccessException){if(attempt==0)await Task.Delay(75);}
                    }
            }
        }
    }
}
