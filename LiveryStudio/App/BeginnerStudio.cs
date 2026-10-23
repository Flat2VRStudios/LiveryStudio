using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private DesignColorEdit? simplePreview;
    private CompositeMaps? simplePreviewMaps,simpleCompareMaps;
    private LiveryProject? simpleCompareProject;
    private CancellationTokenSource? simpleCancellation,simpleAssetsCancellation;
    private long simpleRevision,compareRevision;
    private string? simpleStickerName;
    private int simplePaletteIndex;
    private bool simpleCompareHeld;
    private bool? simpleCompareClean;
    private bool? simpleCompareMaterial;
    private CancellationTokenSource? historyWarmCancellation;
    private Task simplePending=Task.CompletedTask;
    private bool simplePaintEraser;
    private bool advancedQuickTools;
    private CompositeMaps? simpleEraserUnder;
    private EditCheckpoint? simpleEraserState;
    private bool? uploadedCleanPaint;
    private long historyRequestStarted;
    private string historyRequestName="";
    private bool SimplePreviewActive=>simplePreview is not null||SimplePreviewNotice.Visibility==Visibility.Visible;
    private CompositeMaps? SimpleDisplayMaps=>simpleCompareMaps??simplePreviewMaps;
    private LiveryProject? SimpleDisplayProject=>simpleCompareProject??simplePreview?.Project??draft;
    private void InitializeSimpleStudio(){SimpleTray.Requested+=SimpleRequested;}
    private void ToggleQuickCreate(object sender,RoutedEventArgs e){advancedQuickTools=QuickCreateMenu.IsChecked;UpdatePaintLayout(new(ActualWidth,ActualHeight));if(advancedQuickTools){CancelSimplePreview();Select(6);SimpleTray.ShowSection("Looks");}}
    public Task ApplyStarterAsync(DesignStarterOptions options){var model=mesh??throw new InvalidOperationException("Open a car before applying a look.");return ApplySimpleChange((d,p,c)=>SimpleDesigns.Look(d,p,model,options,WorkshopService.DefaultWorkspace,c));}
    public void ShowSimpleLooks(){if(AdvancedMode)return;CancelSimplePreview();Select(6);SimpleTray.ShowSection("Looks");}
    private static string FriendlyArtwork(string name)=>Regex.Replace(name,@"-[A-Fa-f0-9]{8,16}(?:-\d+)?$","");
    private void SyncSimpleSelection()
    {
        if(SimpleSelectionActions is null)return;var selected=SelectedLayers;SimpleSelectionActions.Visibility=!AdvancedMode&&!previewOnly&&!SimplePreviewActive&&selected.Count>0&&mode==6?Visibility.Visible:Visibility.Collapsed;
        SimpleSelectionName.Text=selected.Count==1?FriendlyArtwork(selected[0].Name):selected.Count+" selected";
        SimpleTrimPaint.Visibility=selected.Any(l=>l.Kind==LayerKind.Color&&(!l.ProtectSharedUvs||l.WrapDepth>SimplePlacement.Depth))?Visibility.Visible:Visibility.Collapsed;
        SimpleSelectionUnlock.Visibility=selected.Any(l=>l.Locked)?Visibility.Visible:Visibility.Collapsed;SimpleSelectionRemove.IsEnabled=!selected.Any(l=>l.Locked);SimpleSelectionOtherSide.IsEnabled=!selected.Any(l=>l.Locked);SimpleSelectionOtherSide.Content=selected.Any(l=>l.Mirror)?"Separate sides":"Both doors";
        SimpleTray.SetPlacement(ToolSize.Value,StickerRotation.Value,MirrorPaint.IsChecked==true);
        SimplePairPreview.Visibility=!AdvancedMode&&!SimplePreviewActive&&selected.Any(l=>l.Mirror)&&ActualWidth>=750&&SimplePairImage.Source is not null?Visibility.Visible:Visibility.Collapsed;
    }
    private async void SimpleRequested(string command,object? value)
    {
        if((AdvancedMode&&!advancedQuickTools)||previewOnly||document is null||mesh is null||draft is null)return;
        try
        {
            if(command=="section")
            {
                CancelSimplePreview();simplePaintEraser=false;if((string)value! =="Paint")PaintChannel.SelectedIndex=0;Select((string)value! =="Paint"?1:(string)value! =="Stickers"&&sticker is not null?2:6);return;
            }
            if(command=="look"||command=="surprise")
            {
                var kind=command=="look"?(StarterKind)value!:(StarterKind)Random.Shared.Next(DesignStarters.Styles.Length);var options=DesignStarters.Defaults(kind);
                if(command=="surprise"){var palette=StudioColorDialog.Palettes()[Random.Shared.Next(StudioColorDialog.Palettes().Count)];options=options with {BaseColor=palette.Colors[0],AccentColor=palette.Colors[1],InkColor=palette.Colors[2]};}
                if(document.Layers.LastOrDefault(l=>l.GroupName=="Number badge"&&l.Name.StartsWith("Race number ")) is {} identity)options=options with {Number=identity.TextArtwork?.Text??options.Number,Team=document.Layers.FirstOrDefault(l=>l.GroupId==identity.GroupId&&l.TextArtwork is not null&&l.Id!=identity.Id)?.TextArtwork?.Text??""};
                await PreviewSimpleLook(options);return;
            }
            CancelSimplePreview();EndStroke();FinishDirectEdit();
            switch(command)
            {
                case "palette":await ApplySimpleChange((d,p,c)=>SimpleDesigns.Palette(d,p,(string[])value!,WorkshopService.DefaultWorkspace,c));break;
                case "shuffle":var palettes=StudioColorDialog.Palettes();simplePaletteIndex=(simplePaletteIndex+1)%palettes.Count;await ApplySimpleChange((d,p,c)=>SimpleDesigns.Palette(d,p,palettes[simplePaletteIndex].Colors,WorkshopService.DefaultWorkspace,c));break;
                case "body":await ApplySimpleChange((d,p,c)=>SimpleDesigns.Body(d,p,(string)value!));break;
                case "clean":await ApplySimpleChange((d,p,c)=>SimpleDesigns.Body(d,p,DesignColor.PaintPickerHex(p,p.InnerColor)));break;
                case "stock":await ApplySimpleChange((d,p,c)=>SimpleDesigns.Body(d,p,DesignColor.PaintPickerHex(p,p.InnerColor),false));break;
                case "body-picker":BeginnerBodyColor(this,new());break;
                case "brush-picker":BeginnerColorPicker(this,new());break;
                case "finish":FinishEdited?.Invoke(FinishPreset.All.Single(f=>f.Name==(string)value!).Apply(draft));break;
                case "sticker":simpleStickerName=(string)value!;await UseSimpleSticker();break;
                case "tint":BrushHex.Text=(string)value!;if(simpleStickerName is not null)await UseSimpleSticker();break;
                case "brush":if(value is string color)BrushHex.Text=color;simplePaintEraser=false;Select(1);break;
                case "eraser":PaintChannel.SelectedIndex=0;PanelOnly.IsChecked=false;ProtectSharedParts.IsChecked=true;simplePaintEraser=true;Select(1);await PrepareSimpleEraser();CanvasStatus.Text="Erase paint · stickers and the original body stay intact";break;
                case "import":simpleStickerName=null;await ImportSimpleImage();break;
                case "number":ShowSimpleNumber();break;
                case "recent":await ShowSimpleRecent();break;
                case "arrange":ShowSimpleArrange();break;
                case "independent":RequestIndependentSides(this,new());break;
                case "edit":Select(6);SimpleTray.SetExpanded(false);break;
                case "paired":MirrorPaint.IsChecked=(bool)value!;Render();break;
                case "size":ToolSize.Value=(double)value!;break;
                case "rotation":StickerRotation.Value=(double)value!;break;
                case "photo":ShowSimplePhotos();break;
            }
            if(command is "brush" or "sticker" or "import")SimpleTray.SetExpanded(false);
        }
        catch(OperationCanceledException){}
        catch(Exception x){Fail(x);}
    }
    private Task ApplySimpleChange(Func<LayerDocument,LiveryProject,CancellationToken,DesignColorEdit> prepare)
    {
        var pending=ApplySimpleChangeCore(prepare);simplePending=Observe(pending);return pending;
        static async Task Observe(Task task){try{await task;}catch{/* The initiating command reports its preparation error. */}}
    }
    private async Task ApplySimpleChangeCore(Func<LayerDocument,LiveryProject,CancellationToken,DesignColorEdit> prepare)
    {
        if(document is null||draft is null)return;simpleCancellation?.Cancel();simpleCancellation?.Dispose();simpleCancellation=new();var token=simpleCancellation.Token;var revision=++simpleRevision;var current=document;var cursor=current.Cursor;var state=current.History[cursor];var project=draft with {};
        CanvasStatus.Text="Trying your change…";var edit=await Task.Run(()=>prepare(current,project,token),token);
        if(revision!=simpleRevision||!ReferenceEquals(document,current)||document.Cursor!=cursor||!ReferenceEquals(document.History[cursor],state))return;
        CommitSimpleChange(edit);
    }
    private void CommitSimpleChange(DesignColorEdit edit)
    {
        document=edit.Document;draft=edit.Project;selectedLayers.Clear();selectedLayer=document.Layers.LastOrDefault()?.Id;
        updatingSharedPaint=true;try{FinishEdited?.Invoke(draft);}finally{updatingSharedPaint=false;}
        SyncFinish();SyncTextHistory();SyncPatternHistory();QueueComposite();SyncSimpleSelection();
    }
    private async Task PreviewSimpleLook(DesignStarterOptions options)
    {
        CancelSimplePreview();await FlushAsync();if(document is null||draft is null||mesh is null)return;
        Select(6);SimpleTray.SetExpanded(false);simpleCancellation=new();var token=simpleCancellation.Token;var revision=++simpleRevision;var current=document;var project=draft with {};var model=mesh;
        SimplePreviewCaption.Text="Trying "+DesignStarters.Styles.Single(s=>s.Kind==options.Kind).Name+"…";SimplePreviewKeep.IsEnabled=false;SimplePreviewNotice.Visibility=Visibility.Visible;SyncSimpleSelection();
        try
        {
            var edit=await Task.Run(()=>SimpleDesigns.Look(current,project,model,options,WorkshopService.DefaultWorkspace,token),token);var maps=await Task.Run(()=>edit.Document.Compose(model,edit.Project,token,compositionCache),token);
            if(revision!=simpleRevision||!ReferenceEquals(document,current)||token.IsCancellationRequested)return;
            simplePreview=edit;simplePreviewMaps=maps;SimplePreviewCaption.Text="Try this look · your design is kept until you choose Keep";SimplePreviewKeep.IsEnabled=true;hover=null;UploadTexture();UploadStructure();UploadPaintFoundation();Render();
        }
        catch(OperationCanceledException){}
        catch{CancelSimplePreview();throw;}
    }
    private bool CancelSimplePreview()
    {
        var active=SimplePreviewActive;simpleRevision++;simpleCancellation?.Cancel();simpleCancellation?.Dispose();simpleCancellation=null;simplePreview=null;simplePreviewMaps=null;SimplePreviewNotice.Visibility=Visibility.Collapsed;
        if(active){UploadTexture();UploadStructure();UploadPaintFoundation();SyncSimpleSelection();Render();}return active;
    }
    private void KeepSimpleLook(object sender,RoutedEventArgs e){if(simplePreview is not {} edit)return;simplePreview=null;CancelSimplePreview();CommitSimpleChange(edit);SimpleTray.SetExpanded(false);CanvasStatus.Text="Look kept · Undo restores your previous design";}
    private void CancelSimpleLook(object sender,RoutedEventArgs e)=>CancelSimplePreview();
    private void UploadPaintFoundation()
    {
        if(renderer==0||originalPaintMask is null)return;var clean=simpleCompareClean??((simplePreview?.Document??document)?.History[(simplePreview?.Document??document)!.Cursor].CleanPaint==true);
        if(uploadedCleanPaint==clean)return;var pixels=clean?new byte[originalPaintMask.Pixels.Length]:originalPaintMask.Pixels;Check(ls_texture(renderer,pixels,(uint)originalPaintMask.Width,(uint)originalPaintMask.Height,8));uploadedCleanPaint=clean;
    }
    private async Task UseSimpleSticker()
    {
        if(simpleStickerName is null)return;var name=simpleStickerName;var color=BrushHex.Text;var epoch=++simpleRevision;
        var path=await Task.Run(()=>FunArtwork.Generate(name,color,WorkshopService.DefaultWorkspace));if(epoch!=simpleRevision)return;
        if(await UseSticker(path)){ToolSize.Value=mesh is null?.45:Math.Clamp((mesh.Maximum.Z-mesh.Minimum.Z)*.12,.18,.8);SimpleTray.SetPlacement(ToolSize.Value,StickerRotation.Value,MirrorPaint.IsChecked==true);SimpleTray.SetExpanded(false);CanvasStatus.Text=name+" ready · click to stamp · Edit last sticker to move it";}
    }
    private async Task ImportSimpleImage(string? file=null)
    {
        if(file is null){var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));foreach(var extension in new[]{".png",".tga",".svg"})picker.FileTypeFilter.Add(extension);file=(await picker.PickSingleFileAsync())?.Path;}
        if(file is null)return;var path=await Task.Run(()=>Path.GetExtension(file).Equals(".svg",StringComparison.OrdinalIgnoreCase)?VectorArtwork.Import(file,WorkshopService.DefaultWorkspace,1024):ArtworkReplacement.Import(file,WorkshopService.DefaultWorkspace));
        if(await UseSticker(path)){simpleStickerName=null;ToolSize.Value=mesh is null?.45:Math.Clamp((mesh.Maximum.Z-mesh.Minimum.Z)*.16,.25,1);SimpleTray.ShowSection("Stickers",false);SimpleTray.SetExpanded(false);CanvasStatus.Text="Your image is ready · move onto the car to preview, then click";}
    }
    private void SimpleImageDragOver(object sender,DragEventArgs e){e.AcceptedOperation=e.DataView.Contains(StandardDataFormats.StorageItems)?DataPackageOperation.Copy:DataPackageOperation.None;e.Handled=true;}
    private async void SimpleImageDrop(object sender,DragEventArgs e)
    {
        if(!e.DataView.Contains(StandardDataFormats.StorageItems))return;e.Handled=true;try{var files=await e.DataView.GetStorageItemsAsync();var file=files.SingleOrDefault();if(file is null||!new[]{".png",".tga",".svg"}.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))throw new InvalidOperationException("Drop one PNG, TGA or SVG image onto the car.");CancelSimplePreview();await ImportSimpleImage(file.Path);}catch(Exception x){Fail(x);}
    }
    private void ShowSimpleNumber()
    {
        var existing=document?.Layers.LastOrDefault(l=>l.GroupName=="Number badge"&&l.Name.StartsWith("Race number "));var number=new TextBox{Header="Race number",Text=existing?.TextArtwork?.Text??"37",MaxLength=3,Width=110};var name=new TextBox{Header="Name or team (optional)",Text=document?.Layers.FirstOrDefault(l=>existing is not null&&l.GroupId==existing.GroupId&&l.TextArtwork is not null&&l.Id!=existing.Id)?.TextArtwork?.Text??"",MaxLength=24,Width=230};var status=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxWidth=250};var add=new Button{Content=existing is null?"Add to both doors":"Update both doors",Style=(Style)Application.Current.Resources["AccentButtonStyle"]};var content=new StackPanel{Spacing=10};content.Children.Add(number);content.Children.Add(name);content.Children.Add(add);content.Children.Add(status);var flyout=new Flyout{Content=content};
        add.Click+=async(_,_)=>{try{add.IsEnabled=false;var n=number.Text;var team=name.Text;await ApplySimpleChange((d,p,c)=>SimpleDesigns.Number(d,p,mesh!,n,team,WorkshopService.DefaultWorkspace,c));flyout.Hide();Select(6);if(document is not null){var group=document.Layers.LastOrDefault()?.GroupId;SetLayerSelection(document.Layers.Where(l=>l.GroupId==group).Select(l=>l.Id));}SimpleTray.SetExpanded(false);}catch(Exception x){status.Text=x.Message;}finally{add.IsEnabled=true;}};flyout.ShowAt(SimpleTray);
    }
    private void ShowSimpleArrange()
    {
        if(document is null)return;var list=new ListView{MaxHeight=300,Width=280,SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=true};var seen=new HashSet<Guid>();
        foreach(var layer in document.Layers.AsEnumerable().Reverse())
        {
            if(layer.GroupId is {} group&&!seen.Add(group))continue;var label=layer.GroupId.HasValue?layer.GroupName:FriendlyArtwork(layer.Name);list.Items.Add(new ListViewItem{Content=label,Tag=layer.Id});
        }
        var flyout=new Flyout{Content=list};list.ItemClick+=(_,e)=>{if(e.ClickedItem is ListViewItem {Tag:Guid id}){Select(6);SetLayerSelection([id],id,true);flyout.Hide();SimpleTray.SetExpanded(false);}};flyout.ShowAt(SimpleTray);
    }
    private async Task ShowSimpleRecent()
    {
        var items=await Task.Run(()=>ArtworkBrowsing.Query(ArtworkBrowsing.Catalog(WorkshopService.DefaultWorkspace),ArtworkBrowsing.Read(WorkshopService.DefaultWorkspace),"",ArtworkCollection.Recent,ArtworkCategory.Decals).Take(12).ToArray());
        var list=new ListView{MaxHeight=300,Width=280,IsItemClickEnabled=true};foreach(var item in items)list.Items.Add(new ListViewItem{Content=FriendlyArtwork(item.Name),Tag=item.Path});
        if(items.Length==0)list.Items.Add(new ListViewItem{Content="Your used stickers will appear here",IsEnabled=false});var flyout=new Flyout{Content=list};list.ItemClick+=async(_,e)=>{if(e.ClickedItem is ListViewItem {Tag:string path}){flyout.Hide();simpleStickerName=null;if(await UseSticker(path))SimpleTray.SetExpanded(false);}};flyout.ShowAt(SimpleTray);
    }
    private void TrimSimplePaint(object sender,RoutedEventArgs e)=>EditSelection("Kept paint on the outer surface",(layers,ids)=>{for(var i=0;i<layers.Count;i++)if(ids.Contains(layers[i].Id)&&layers[i].Kind==LayerKind.Color)layers[i]=SimplePlacement.OnSurface(layers[i]);});
    private int? simplePhotoKind;
    private void ShowSimplePhotos()
    {
        var flyout=new MenuFlyout();foreach(var (label,kind) in new[]{("Take a photo…",0),("Make a spinning GIF…",2)}){var item=new MenuFlyoutItem{Text=label};item.Click+=(_,_)=>{simplePhotoKind=kind;ExportPresentation(this,new());};flyout.Items.Add(item);}flyout.ShowAt(SimpleTray);
    }
    private async void SimpleComparePressed(object sender,PointerRoutedEventArgs e){if(sender is UIElement button)button.CapturePointer(e.Pointer);e.Handled=true;await BeginSimpleCompare();}
    private void SimpleCompareReleased(object sender,PointerRoutedEventArgs e){EndSimpleCompare();if(sender is UIElement button)button.ReleasePointerCaptures();e.Handled=true;}
    private async void SimpleCompareKeyDown(object sender,KeyRoutedEventArgs e){if(e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter){e.Handled=true;await BeginSimpleCompare();}}
    private void SimpleCompareKeyUp(object sender,KeyRoutedEventArgs e){if(e.Key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter){EndSimpleCompare();e.Handled=true;}}
    private async Task BeginSimpleCompare()
    {
        if(simpleCompareHeld||document is null||mesh is null||draft is null)return;simpleCompareHeld=true;var revision=++compareRevision;var snapshot=SimpleDesigns.Copy(document);snapshot.Restore(0);var project=DesignPalette.ResolvePaint(snapshot,draft);var model=mesh;
        try{var maps=await Task.Run(()=>snapshot.Compose(model,project,cache:compositionCache));if(!simpleCompareHeld||revision!=compareRevision)return;simpleCompareMaps=maps;simpleCompareProject=project;simpleCompareClean=snapshot.History[snapshot.Cursor].CleanPaint;simpleCompareMaterial=snapshot.HasMaterial;UploadTexture();UploadStructure();UploadPaintFoundation();Render();}catch(Exception x){if(revision==compareRevision)Fail(x);}
    }
    private void EndSimpleCompare(){if(!simpleCompareHeld&&simpleCompareMaps is null)return;simpleCompareHeld=false;compareRevision++;simpleCompareMaps=null;simpleCompareProject=null;simpleCompareClean=null;simpleCompareMaterial=null;UploadTexture();UploadStructure();UploadPaintFoundation();Render();}
    private void RecordHistoryFrame()
    {
        if(historyRequestStarted==0)return;var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(historyRequestStarted).TotalMilliseconds;historyRequestStarted=0;
        if(Environment.GetEnvironmentVariable("LIVERY_STUDIO_PERF_LOG") is not string path)return;
        var line=JsonSerializer.Serialize(new{Action=historyRequestName,VisibleMs=elapsed,Cursor=document?.Cursor,compositionCache.Hits,compositionCache.Misses});_ = Task.Run(()=>{try{File.AppendAllText(path,line+Environment.NewLine);}catch(IOException){}});
    }
    private async Task WarmUndoNeighbor()
    {
        historyWarmCancellation?.Cancel();historyWarmCancellation?.Dispose();historyWarmCancellation=new();var token=historyWarmCancellation.Token;
        if(document?.CanUndo!=true||mesh is null||draft is null)return;var current=SimpleDesigns.Copy(document);var snapshot=SimpleDesigns.Copy(current);snapshot.Undo();var model=mesh;var project=draft;
        try{await Task.Run(()=>{snapshot.Compose(model,project,token,compositionCache);token.ThrowIfCancellationRequested();compositionCache.Touch(current,model,project);},token);}catch(OperationCanceledException){}catch(Exception x){System.Diagnostics.Debug.WriteLine("Undo preload: "+x.Message);}
    }
    private async Task RefreshSimplePair(LayerDocument snapshot,LiveryProject project,PaintMesh model,CompositeMaps maps,long revision,int version)
    {
        if(AdvancedMode||previewOnly||!snapshot.Layers.Any(l=>l.Mirror)||originalPaintMask is null)return;
        try
        {
            var primary=snapshot.Layers.LastOrDefault(l=>l.Mirror)!;var sign=primary.Samples[0].Center.X>0?-1:1;var mask=originalPaintMask;var file=Path.Combine(WorkshopService.DefaultWorkspace,"simple-shelf",$"other-door-{version}-{revision}.png");
            var frame=await Task.Run(()=>{var small=new CompositeMaps(SmallImage(maps.Albedo),SmallImage(maps.Structure));using var photo=new StudioPhotography(AppContext.BaseDirectory,model,snapshot,project,new(),preparedMaps:small,originalMask:SmallImage(mask));var pose=StudioCamera.Fit(StudioCamera.Geometry(model),sign*MathF.PI/2,0,170f/88);return photo.Capture(pose,340,176);});
            if(revision!=compositeRevision||version!=generation||historyRequestStarted!=0)return;await ImageFiles.WriteAsync(frame,file);if(revision!=compositeRevision||version!=generation)return;SimplePairImage.Source=new BitmapImage{UriSource=new Uri(file),CreateOptions=BitmapCreateOptions.IgnoreImageCache};SimplePairView.Tag=sign<0?"left":"right";SyncSimpleSelection();
        }
        catch(Exception x){System.Diagnostics.Debug.WriteLine("Other door preview: "+x.Message);}
    }
    private async Task PrepareSimpleEraser()
    {
        if(document is null||draft is null||mesh is null)return;var current=document;var state=current.History[current.Cursor];if(ReferenceEquals(state,simpleEraserState)&&simpleEraserUnder is not null)return;
        simpleEraserUnder=null;CanvasStatus.Text="Preparing eraser…";var snapshot=current.Snapshot("Under the paint",current.Layers.Where(l=>l.Kind!=LayerKind.Color||l.Locked).ToArray());var project=draft;var model=mesh;
        var maps=await Task.Run(()=>snapshot.Compose(model,project,cache:compositionCache));if(!ReferenceEquals(document,current)||!ReferenceEquals(document.History[document.Cursor],state))return;simpleEraserUnder=maps;simpleEraserState=state;
    }
    private void DrawSimpleEraser()
    {
        MaskBrushRing.Visibility=mode==8?Visibility.Visible:Visibility.Collapsed;SimpleEraserRing.Visibility=Visibility.Collapsed;
        if(!simplePaintEraser||mode!=1||hover is null||dragging||StampProjection is not {} p)return;
        bool Screen(Vector3 point,out Vector2 at)=>DecalEditing.Project(point,Matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,out at);
        if(!Screen(p.Center,out var center)||!Screen(p.Center+p.Right*p.Width*.5f,out var right)||!Screen(p.Center+p.Up*p.Height*.5f,out var up))return;
        var width=Math.Max(4,Vector2.Distance(center,right)*2);var height=Math.Max(4,Vector2.Distance(center,up)*2);SimpleEraserRing.Width=width;SimpleEraserRing.Height=height;Canvas.SetLeft(SimpleEraserRing,center.X-width/2);Canvas.SetTop(SimpleEraserRing,center.Y-height/2);SimpleEraserRing.RenderTransformOrigin=new(.5,.5);SimpleEraserRing.RenderTransform=new RotateTransform{Angle=MathF.Atan2(right.Y-center.Y,right.X-center.X)*180/MathF.PI};SimpleEraserRing.Visibility=MaskBrushOverlay.Visibility=Visibility.Visible;
    }
    private static PaintImage SmallImage(PaintImage image,int edge=512)
    {
        var scale=Math.Min(1,(double)edge/Math.Max(image.Width,image.Height));var w=Math.Max(1,(int)(image.Width*scale));var h=Math.Max(1,(int)(image.Height*scale));var pixels=new byte[w*h*4];for(var y=0;y<h;y++)for(var x=0;x<w;x++)image.Pixels.AsSpan(((y*image.Height/h)*image.Width+x*image.Width/w)*4,4).CopyTo(pixels.AsSpan((y*w+x)*4,4));return new(w,h,pixels);
    }
    private async Task RefreshSimpleAssets()
    {
        simpleAssetsCancellation?.Cancel();simpleAssetsCancellation?.Dispose();simpleAssetsCancellation=new();var token=simpleAssetsCancellation.Token;var version=generation;var model=mesh;var current=document;var project=draft;
        if(model is null||current is null||project is null||AdvancedMode||previewOnly)return;
        try
        {
            var folder=Path.Combine(WorkshopService.DefaultWorkspace,"simple-shelf","v2",project.CarIndex.ToString("00"));Directory.CreateDirectory(folder);
            foreach(var name in FunArtwork.Shapes.Keys)
            {
                var file=Path.Combine(folder,name+".png");if(!File.Exists(file)){var image=await Task.Run(()=>SmallImage(VectorArtwork.Render(VectorArtwork.Validate(Encoding.UTF8.GetBytes(FunArtwork.Svg(name,"#FFF3ED"))),512),128),token);await ImageFiles.WriteAsync(image,file);}if(version!=generation||token.IsCancellationRequested)return;SimpleTray.SetStickerImage(name,file);
            }
            var colorFile=Path.Combine(folder,"foundation.tga");var surfaceFile=Path.Combine(folder,"surface.tga");
            await Task.Run(()=>{if(!File.Exists(colorFile))FinishMaps.WriteTga(new(512,512,new byte[512*512*4]),colorFile);if(!File.Exists(surfaceFile))FinishMaps.WriteTga(SmallImage(FinishMaps.Read(current.BaseStructure)),surfaceFile);},token);
            var seed=project with {Albedo=colorFile,Structure=surfaceFile,EditorFile=""};var baseDocument=new LayerDocument{CarIndex=project.CarIndex,BaseAlbedo=colorFile,BaseStructure=surfaceFile};var pose=StudioCamera.Fit(carCameraPoints??StudioCamera.Geometry(model),1.05f,.18f,156f/72);
            foreach(var style in DesignStarters.Styles)
            {
                token.ThrowIfCancellationRequested();var file=Path.Combine(folder,style.Kind+".png");
                if(!File.Exists(file))
                {
                    var frame=await Task.Run(()=>{var look=SimpleDesigns.Look(baseDocument,seed,model,DesignStarters.Defaults(style.Kind),WorkshopService.DefaultWorkspace,token);using var photo=new StudioPhotography(AppContext.BaseDirectory,model,look.Document,look.Project,new(),token);return photo.Capture(pose,312,144,token);},token);await ImageFiles.WriteAsync(frame,file);
                }
                if(version!=generation||token.IsCancellationRequested)return;SimpleTray.SetLookImage(style.Kind,file);
            }
        }
        catch(OperationCanceledException){}catch(Exception x){System.Diagnostics.Debug.WriteLine("Simple shelf: "+x);}
    }
}

public partial class MainWindow
{
    private void SyncSimpleShell()
    {
        if(SimpleCarButton is null)return;var simple=AdvancedModeToggle.IsChecked!=true;SimpleCarButton.Visibility=simple?Visibility.Visible:Visibility.Collapsed;PaintCarSelector.Visibility=simple?Visibility.Collapsed:Visibility.Visible;NewButton.Visibility=simple?Visibility.Collapsed:Visibility.Visible;
        var car=project.SelectedCar;SimpleCarName.Text=car.Display;SimpleCarImage.Source=new BitmapImage(new Uri(car.ThumbnailPath));
        if(simple){NavigationColumn.Width=new(68);NavigationHeading.Visibility=NavigationPromo.Visibility=Visibility.Collapsed;foreach(var label in new[]{DesignNavLabel,PaintNavLabel,CarBuilderLabel,LibraryNavLabel,SetupNavLabel})label.Visibility=Visibility.Collapsed;PageSubtitle.Visibility=Visibility.Collapsed;PageTitle.FontSize=24;OutputCommands.Visibility=Visibility.Collapsed;BakeButton.Content="Use in game";}
        else if(Root.ActualWidth>=1120){NavigationColumn.Width=new(212);NavigationHeading.Visibility=NavigationPromo.Visibility=Visibility.Visible;foreach(var label in new[]{DesignNavLabel,PaintNavLabel,CarBuilderLabel,LibraryNavLabel,SetupNavLabel})label.Visibility=Visibility.Visible;PageSubtitle.Visibility=Visibility.Visible;}
    }
    private void OpenSimpleCarPicker(object sender,RoutedEventArgs e)
    {
        var grid=new GridView{ItemsSource=Contract.Cars,IsItemClickEnabled=true,SelectionMode=ListViewSelectionMode.Single,MaxHeight=Math.Clamp(Root.ActualHeight-180,180,460),MaxWidth=Math.Clamp(Root.ActualWidth-130,250,860),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        grid.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='145' Spacing='4' Padding='5'><Image Source='{Binding ThumbnailPath}' Height='64' Stretch='Uniform'/><TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis' FontSize='12'/><TextBlock Text='{Binding Category}' FontSize='11' Opacity='.7'/></StackPanel></DataTemplate>");
        var content=new StackPanel{Spacing=9};content.Children.Add(grid);var previous=new Button{Content="Restore previous design",IsEnabled=File.Exists(previousDraft)};content.Children.Add(previous);var flyout=new Flyout{Content=content,Placement=FlyoutPlacementMode.Bottom};previous.Click+=(_,args)=>{flyout.Hide();OpenPreviousDraft(previous,args);};grid.ItemClick+=async(_,args)=>{if(args.ClickedItem is Car car){flyout.Hide();await ChangeCarAsync(car,true);SyncSimpleShell();}};flyout.ShowAt(SimpleCarButton);
    }
    private void ShowSimpleGameMenu()
    {
        UpdateGameSetup();var flyout=new MenuFlyout();
        if(connectedGame is {CanImport:true}){var drive=new MenuFlyoutItem{Text="Drive it · build, import and launch"};drive.Click+=TestInGame;flyout.Items.Add(drive);}
        else if(connectedGame is not null){var launch=new MenuFlyoutItem{Text="Launch FlatOut 4"};launch.Click+=LaunchGame;flyout.Items.Add(launch);}
        var build=new MenuFlyoutItem{Text="Build a livery package"};build.Click+=async(_,_)=>{try{await BuildDraft();Info("Your livery is ready","The editable design was compiled and packaged with the bundled tools.");}catch(OperationCanceledException){Status.Text="Build canceled · your draft is safe.";}catch(Exception x){Error(x);}};flyout.Items.Add(build);
        var export=new MenuFlyoutItem{Text="Export livery package…",IsEnabled=OutputBuild is not null};export.Click+=ExportPackage;flyout.Items.Add(export);
        if(connectedGame is {CanImport:false}){var workshop=new MenuFlyoutItem{Text="Open Steam Workshop"};workshop.Click+=OpenSteamWorkshop;flyout.Items.Add(workshop);}
        var setup=new MenuFlyoutItem{Text="Game setup…"};setup.Click+=ShowSetup;flyout.Items.Add(setup);flyout.ShowAt(BakeButton);
    }
}
