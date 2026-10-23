using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private LayerDocument? document;
	private string editorPath="",stickerPath="";
	private DecalEffectsOptions? pendingEffects;
	private PaintLayer? stroke;
	private bool strokeChanged,updatingLayers,recomposing;
	private Guid? selectedLayer;
	private int[] panelMask=[];
    private PaintMesh? pairedPanelMesh;
    private int[]? pairedPanelSource,pairedPanelMask;
	private long compositeRevision;
	private readonly CompositionCache compositionCache=new();
	private CancellationTokenSource? compositeCancellation;
	private Microsoft.UI.Dispatching.DispatcherQueueTimer? transformTimer;
	public event Action<string,string,string>? MapsSaved;
	public event Action<string>? TemplateRequested;
	private PaintLayer? SelectedLayer=>document?.Layers.FirstOrDefault(l=>l.Id==selectedLayer);
	private void UndoAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e){if(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;Undo(this,new());e.Handled=true;}
	private void RedoAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e){if(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;Redo(this,new());e.Handled=true;}
	private void InitializeEditorControls()
	{

		foreach(var field in new[]{ToolWrapAngle,ToolWrapDepth,LayerWrapAngle,LayerWrapDepth,LayerWidthScale,LayerHeightScale,LayerScale,LayerOffsetX,LayerOffsetY})field.NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{IntegerDigits=1,FractionDigits=2,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=.001}};
		foreach(var field in new[]{SetScale,SetRotation,SetOffsetX,SetOffsetY,LayoutGap})field.NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{IntegerDigits=1,FractionDigits=2,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=.001}};RefreshGroupLibrary();
		transformTimer=DispatcherQueue.CreateTimer();transformTimer.Interval=TimeSpan.FromMilliseconds(300);transformTimer.IsRepeating=false;transformTimer.Tick+=(_,_)=>CommitTransform();
		RefreshArtwork();PaletteSelector.ItemsSource=Palettes();PaletteSelector.SelectedIndex=0;InitializePreviewChoices();InitializeCreativeControls();
	}
	private void InspectorSectionChanged(object sender,SelectionChangedEventArgs e)
	{
		if(AssetsInspector is null)return;FrameworkElement[] sections=[PaintInspector,LayersInspector,FinishInspector,LightInspector,AssetsInspector,ColorsInspector];for(var i=0;i<sections.Length;i++)sections[i].Visibility=i==InspectorSection.SelectedIndex?Visibility.Visible:Visibility.Collapsed;SyncInspectorCommands();if(InspectorSection.SelectedIndex==5)_=RefreshDesignColors();if(InspectorSection.SelectedIndex==4)_=RefreshArtworkShelf();
	}
	private void PaintChannelChanged(object sender,SelectionChangedEventArgs e)
	{
		if(SurfaceBrushSettings is null)return;SurfaceBrushSettings.Visibility=PaintChannel.SelectedIndex>0?Visibility.Visible:Visibility.Collapsed;if(mode is 1 or 4)Select(PaintChannel.SelectedIndex>0?4:1);Render();
	}
	private void ProjectionOptionsChanged(object sender,RoutedEventArgs e){try{UploadSelection();UpdatePlacementReadout();Render();}catch(Exception x){Fail(x);}}
	private void SelectPanel(object sender,RoutedEventArgs e){Select(5);CanvasStatus.Text="Click the body surface to isolate. Connected facing triangles will be selected.";HideInspector();}
	private void PickPanel(SurfaceHit hit)
	{
		panelMask=MeshEditing.Panel(mesh!,hit);PanelOnly.IsChecked=true;PanelStatus.Text=$"Selected surface · {panelMask.Length:N0} triangles";UploadSelection();Select(0);
	}
	private int[] ActiveMask()
	{
		if(!AdvancedMode||PanelOnly.IsChecked!=true||mesh is null)return [];
		if(MirrorPaint.IsChecked!=true)return panelMask;
        if(ReferenceEquals(pairedPanelMesh,mesh)&&ReferenceEquals(pairedPanelSource,panelMask)&&pairedPanelMask is not null)return pairedPanelMask;
        var paired=MeshEditing.PairedPanels(mesh,panelMask);
        pairedPanelMesh=mesh;pairedPanelSource=panelMask;pairedPanelMask=paired;return paired;
	}
	private void UploadSelection()
	{
		if(ImageReviewActive){UploadTriangleMask(null);return;}
		if(renderer==0||mesh is null)return;var projected=directLayer??(mode==8&&maskUnderMaps is not null?maskBrushLayer:mode==3?SelectedLayer:null);int[] active;try{active=projected is {} layer?layer.Triangles:ActiveMask();}catch(InvalidOperationException x){UploadTriangleMask(null);Fail(x);return;}UploadTriangleMask(active.Length==0?null:active);
	}
	private object? uploadedTriangleMask;
	private PaintMesh? uploadedTriangleMesh;
	private void UploadTriangleMask(IReadOnlyCollection<int>? active)
	{
		if(renderer==0||mesh is null)return;if(ReferenceEquals(uploadedTriangleMask,active)&&ReferenceEquals(uploadedTriangleMesh,mesh))return;
		if(active is null)Check(ls_selection(renderer,[],0));else{var mask=new uint[mesh.Vertices.Length/3];foreach(var t in active)if(t<mask.Length)mask[t]=1;Check(ls_selection(renderer,mask,(uint)mask.Length));}
		uploadedTriangleMask=active;uploadedTriangleMesh=mesh;
	}
	private PaintLayer NewStroke(){var layer=PlacementSettings();layer.Name=mode==2?pendingText is {} text?"Text · "+text.Text.Replace('\n',' ').Trim()[..Math.Min(70,text.Text.Replace('\n',' ').Trim().Length)]:pendingPattern is {} pattern?PatternArtwork.Name(pattern.Kind):pendingVector is {} vector?"SVG · "+VectorArtwork.Name(vector):Path.GetFileNameWithoutExtension(stickerPath):mode==4?((ComboBoxItem)PaintChannel.SelectedItem).Content+" stroke":"Color stroke";layer.Kind=mode==2?LayerKind.Sticker:mode==4?PaintChannel.SelectedIndex switch{1=>LayerKind.Gloss,2=>LayerKind.Metalness,_=>LayerKind.Surface}:LayerKind.Color;layer.Asset=mode==2?stickerPath:"";layer.TextArtwork=mode==2&&pendingText is {} textSource?textSource with {}:null;layer.PatternArtwork=mode==2&&pendingPattern is {} patternSource?patternSource with {}:null;layer.VectorArtwork=mode==2&&pendingVector is {} vectorSource?vectorSource with {}:null;layer.RasterArtwork=mode==2&&pendingRaster is {} rasterSource?rasterSource with {}:null;layer.Effects=mode==2?pendingEffects:null;layer.Color=BrushHex.Text;layer.Opacity=PlacementOpacity;layer.Mirror=MirrorPaint.IsChecked==true;layer.Gloss=(float)BrushGloss.Value;layer.Metalness=(float)BrushMetal.Value;layer.Hardness=PlacementHardness;layer.Triangles=ActiveMask();if(document is not null&&AdvancedMode){var palette=document.History[document.Cursor].Palette;DesignPalette.AttachUniqueLinks(layer,palette);if(layer.Kind==LayerKind.Color&&sharedBrushColor is {} roleId&&palette.FirstOrDefault(c=>c.Id==roleId&&c.Hex==ColorLibrary.Normalize(layer.Color)) is {} role)layer.ColorLinks["paint"]=role;}return layer;}
	private void MoveSelected(SurfaceHit hit)
	{
		if(document is null||SelectedLayer is not {Kind:LayerKind.Sticker} layer)return;
		if(layer.Triangles.Length>0&&!layer.Triangles.Contains(hit.Triangle)){CanvasStatus.Text="This layer is isolated to its selected surface. Choose a point there, or allow the layer across panels in its wrap settings.";return;}
		document.Commit("Moved "+layer.Name,layers=>{var changed=layers.Single(l=>l.Id==layer.Id);changed.Samples=[MovedProjection(hit,layer)];changed.OffsetRight=changed.OffsetUp=0;});QueueComposite();Select(0);UploadSelection();
	}
	private void QueueComposite(bool history=false)
	{
        if(document is null||mesh is null||draft is null)return;
		if(!history)historyRequestStarted=0;
		historyWarmCancellation?.Cancel();
		if(maskBrush is not null&&!maskCommitting){ClearMaskBrush();if(mode==8)Select(6);}
		ClearLayerInspection();
		SyncSharedPaintHistory();
		compositeCancellation?.Cancel();compositeCancellation?.Dispose();compositeCancellation=new();var cancellation=compositeCancellation.Token;var revision=++compositeRevision;var version=generation;
		var snapshot=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(document,LayerDocument.Json),LayerDocument.Json)!;var project=draft with {};var model=mesh;var path=editorPath;
		RefreshLayers();CanvasStatus.Text="Updating layers and surface maps…";recomposing=true;saving=ComposeAndSave(snapshot,project,model,path,version,revision,cancellation);
	}
	private async Task ComposeAndSave(LayerDocument snapshot,LiveryProject project,PaintMesh model,string path,int version,long revision,CancellationToken cancellation)
	{
		try
		{
			var maps=await Task.Run(()=>snapshot.Compose(model,project,cancellation,compositionCache),cancellation);cancellation.ThrowIfCancellationRequested();
			if(version!=generation||revision!=compositeRevision)return;
			var folder=Path.GetDirectoryName(path)!;var colorPath=Path.Combine(folder,"albedo.png");var surfacePath=Path.Combine(folder,"surface.tga");
			// The car responds before PNG encoding or disk writes. Brushes own a copy
			// of these pixels, so the immutable save snapshot can run concurrently.
			painter=new(maps.Albedo);structure=maps.Structure;source=colorPath;destination=colorPath;ClearDirectPreview();UploadTexture();UploadStructure();UploadPaintFoundation();draft=project with {Albedo=colorPath,Structure=surfacePath,EditorFile=path};
			var historyAction=historyRequestStarted!=0;recomposing=false;CanvasStatus.Text="Saving draft…";Render();RecordHistoryFrame();if(simplePaintEraser)_=PrepareSimpleEraser();if(!historyAction)_ = RefreshSimplePair(snapshot,project,model,maps,revision,version);else{SimplePairImage.Source=null;SimplePairPreview.Visibility=Visibility.Collapsed;}
			await saveGate.WaitAsync(cancellation);
			try
			{
				using var prepared=await StagedCompositeSave.StageAsync(snapshot,maps,colorPath,surfacePath,path,(image,target,_)=>ImageFiles.WriteAsync(image,target),cancellation);
				cancellation.ThrowIfCancellationRequested();if(version!=generation||revision!=compositeRevision)return;
				// No await between the revision check and synchronous publication.
				prepared.Publish();MapsSaved?.Invoke(colorPath,surfacePath,path);CanvasStatus.Text=AdvancedMode?$"Saved · {snapshot.Layers.Count} editable layers":"Saved";
			}
			finally{saveGate.Release();}
			if(!historyAction&&version==generation&&revision==compositeRevision)_=WarmUndoNeighbor();
		}
		catch(OperationCanceledException){}
		catch(Exception x){if(version==generation&&revision==compositeRevision){Fail(x);throw;}}
		finally{if(revision==compositeRevision)recomposing=false;}
	}
	private void RefreshLayers()
	{
		if(document is null)return;updatingLayers=true;
		try{selectedLayers.IntersectWith(document.Layers.Select(l=>l.Id));if(selectedLayers.Count==0&&selectedLayer is {} id&&document.Layers.Any(l=>l.Id==id))selectedLayers.UnionWith(LayerSets.ExpandGroups(document.Layers,[id]));if(selectedLayer is null||!selectedLayers.Contains(selectedLayer.Value))selectedLayer=document.Layers.LastOrDefault(l=>selectedLayers.Contains(l.Id))?.Id;var ordered=LayerBrowser.Filter(document.Layers,LayerSearch.Text,(LayerFilter)Math.Max(0,LayerTypeFilter.SelectedIndex));if(LayerList.ItemsSource is not PaintLayer[] bound||bound.Length!=ordered.Length||bound.Where((l,i)=>!ReferenceEquals(l,ordered[i])).Any())LayerList.ItemsSource=ordered;LayerList.SelectedItems.Clear();foreach(var layer in ordered.Where(l=>selectedLayers.Contains(l.Id)))LayerList.SelectedItems.Add(layer);HistoryList.ItemsSource=document.History.ToArray();HistoryList.SelectedIndex=document.Cursor;UndoButton.IsEnabled=document.CanUndo;RedoButton.IsEnabled=document.CanRedo;SeamPadding.Value=document.SeamPadding;SyncLayerFields();SyncGroupControls();UpdateLayerBrowserSelection();}
		finally{updatingLayers=false;}
	}
	private void LayerSelected(object sender,SelectionChangedEventArgs e)
	{
		if(updatingLayers)return;var ids=LayerList.SelectedItems.Cast<PaintLayer>().Select(l=>l.Id).ToArray();var primary=(e.AddedItems.LastOrDefault() as PaintLayer)?.Id??ids.LastOrDefault();FinishDirectEdit(cancel:true);transformTimer?.Stop();CommitTransform();directSelection=null;SetLayerSelection(ids,primary,expand:ids.Length==1&&!IsDown(Windows.System.VirtualKey.Menu));Render();
	}
	private void SyncLayerFields()
	{
		var layer=SelectedLayer;var single=SelectedLayers.Count==1&&layer?.Locked!=true;foreach(var field in new Control[]{LayerName,LayerVisible,LayerMirror,LayerOpacity,LayerScale,LayerRotation,LayerOffsetX,LayerOffsetY,LayerWidthScale,LayerHeightScale,LayerFlipHorizontal,LayerFlipVertical,LayerWrapAngle,LayerWrapDepth})field.IsEnabled=layer is not null&&single;MoveLayerButton.IsEnabled=single&&layer?.Kind==LayerKind.Sticker;EditTextButton.Visibility=single&&layer?.TextArtwork is not null?Visibility.Visible:Visibility.Collapsed;EditPatternButton.Visibility=single&&layer?.PatternArtwork is not null?Visibility.Visible:Visibility.Collapsed;VectorResolutionButton.Visibility=single&&layer?.VectorArtwork is not null?Visibility.Visible:Visibility.Collapsed;DecalEffectsButton.Visibility=single&&layer?.Kind==LayerKind.Sticker?Visibility.Visible:Visibility.Collapsed;EraseDecalButton.Visibility=single&&layer is {Kind:LayerKind.Sticker,Visible:true}?Visibility.Visible:Visibility.Collapsed;
		ReplaceArtworkButton.Visibility=single&&layer?.Kind==LayerKind.Sticker?Visibility.Visible:Visibility.Collapsed;
        EditImageButton.Visibility=single&&layer is not null&&ImagePreparation.CanEdit(layer)?Visibility.Visible:Visibility.Collapsed;
		if(layer is null){LayerName.Text="";return;}LayerName.Text=layer.Name;LayerVisible.IsChecked=layer.Visible;LayerMirror.IsChecked=layer.Mirror;LayerOpacity.Value=layer.Opacity;LayerScale.Value=layer.Scale;LayerRotation.Value=layer.Rotation;LayerOffsetX.Value=layer.OffsetRight;LayerOffsetY.Value=layer.OffsetUp;LayerWidthScale.Value=layer.ScaleX;LayerHeightScale.Value=layer.ScaleY;LayerFlipHorizontal.IsChecked=layer.FlipHorizontal;LayerFlipVertical.IsChecked=layer.FlipVertical;LayerWrapAngle.Value=layer.WrapAngle;LayerWrapDepth.Value=layer.WrapDepth;
		if(SelectedLayers.Count>1){LayerVisible.IsEnabled=LayerMirror.IsEnabled=!SelectedLayers.Any(l=>l.Locked);LayerVisible.IsChecked=SelectedLayers.All(l=>l.Visible)?true:SelectedLayers.All(l=>!l.Visible)?false:null;LayerMirror.IsChecked=SelectedLayers.All(l=>l.Mirror)?true:SelectedLayers.All(l=>!l.Mirror)?false:null;}
	}
	private void EditSelected(string label,Action<PaintLayer> edit)
	{
		if(updatingLayers||document is null||SelectedLayer is not {} layer||SelectedLayers.Count!=1||layer.Locked)return;document.Commit(label,layers=>edit(layers.Single(l=>l.Id==layer.Id)));QueueComposite();
	}
	private void RenameLayer(object sender,RoutedEventArgs e){if(SelectedLayer is {} layer&&layer.Name!=LayerName.Text&&LayerName.Text.Length>0)EditSelected("Renamed layer",l=>l.Name=LayerName.Text);}
	private void LayerVisibilityChanged(object sender,RoutedEventArgs e){if(updatingLayers)return;var value=LayerVisible.IsChecked==true;EditSelection("Selection visibility",(layers,ids)=>{foreach(var l in layers.Where(l=>ids.Contains(l.Id)))l.Visible=value;});}
	private void LayerMirrorChanged(object sender,RoutedEventArgs e){if(updatingLayers)return;var value=LayerMirror.IsChecked==true;EditSelection("Selection symmetry",(layers,ids)=>{foreach(var l in layers.Where(l=>ids.Contains(l.Id)))l.Mirror=value;});}
	private void LayerTransformChanged(object sender,RangeBaseValueChangedEventArgs e){if(updatingLayers||SelectedLayer is null)return;transformTimer?.Stop();transformTimer?.Start();}
	private void CommitTransform()
	{
		if(updatingLayers||SelectedLayers.Count!=1||SelectedLayer is not {} l||l.Locked)return;var opacity=(float)LayerOpacity.Value;var scale=double.IsFinite(LayerScale.Value)?(float)LayerScale.Value:l.Scale;var rotation=(float)LayerRotation.Value;var x=double.IsFinite(LayerOffsetX.Value)?(float)LayerOffsetX.Value:l.OffsetRight;var y=double.IsFinite(LayerOffsetY.Value)?(float)LayerOffsetY.Value:l.OffsetUp;
		var width=double.IsFinite(LayerWidthScale.Value)?(float)LayerWidthScale.Value:l.ScaleX;var height=double.IsFinite(LayerHeightScale.Value)?(float)LayerHeightScale.Value:l.ScaleY;var flipX=LayerFlipHorizontal.IsChecked==true;var flipY=LayerFlipVertical.IsChecked==true;var angle=double.IsFinite(LayerWrapAngle.Value)?(float)LayerWrapAngle.Value:l.WrapAngle;var depth=double.IsFinite(LayerWrapDepth.Value)?(float)LayerWrapDepth.Value:l.WrapDepth;
		if(l.Opacity==opacity&&l.Scale==scale&&l.Rotation==rotation&&l.OffsetRight==x&&l.OffsetUp==y&&l.ScaleX==width&&l.ScaleY==height&&l.FlipHorizontal==flipX&&l.FlipVertical==flipY&&l.WrapAngle==angle&&l.WrapDepth==depth)return;
		EditSelected("Transformed "+l.Name,layer=>{layer.Opacity=opacity;layer.Scale=scale;layer.Rotation=rotation;layer.OffsetRight=x;layer.OffsetUp=y;layer.ScaleX=width;layer.ScaleY=height;layer.FlipHorizontal=flipX;layer.FlipVertical=flipY;layer.WrapAngle=angle;layer.WrapDepth=depth;});
	}
	private async void MoveLayer(object sender,RoutedEventArgs e)
	{
		if(document is not {} target||SelectedLayer is not {Kind:LayerKind.Sticker} layer)return;var cursor=target.Cursor;var checkpoint=target.History[cursor];
		bool SameTarget()=>ReferenceEquals(document,target)&&target.Cursor==cursor&&ReferenceEquals(target.History[cursor],checkpoint)&&SelectedLayer?.Id==layer.Id;
		if(!await UseSticker(layer.Asset,select:false,effects:layer.Effects,vectorOverride:layer.VectorArtwork,stillCurrent:SameTarget)||!SameTarget())return;
		Select(3);UploadSelection();HideInspector();CanvasStatus.Text="Click a new position on the car to move the selected sticker.";
	}
	private void Reorder(int direction)
	{
		EditSelection("Reordered selection",(layers,ids)=>LayerSets.Reorder(layers,ids,direction));
	}
	private void LayerForward(object sender,RoutedEventArgs e)=>Reorder(1);
	private void LayerBackward(object sender,RoutedEventArgs e)=>Reorder(-1);
	private void DuplicateLayer(object sender,RoutedEventArgs e){if(document is null||SelectedLayers.Count==0)return;var copies=LayerSets.Duplicate(SelectedLayers);document.Commit("Duplicated selection",layers=>layers.AddRange(copies));SetLayerSelection(copies.Select(l=>l.Id),copies[^1].Id);QueueComposite();Select(6);HideInspector();CanvasStatus.Text="Copy selected · drag it to a new position, or use the arrow keys.";SelectTool.Focus(FocusState.Programmatic);}
	private void RemoveLayer(object sender,RoutedEventArgs e)=>EditSelection("Removed selection",(layers,ids)=>{layers.RemoveAll(l=>ids.Contains(l.Id));selectedLayers.Clear();selectedLayer=null;});
	private void RestoreHistory(object sender,RoutedEventArgs e){if(document is null||HistoryList.SelectedIndex<0)return;transformTimer?.Stop();document.Restore(HistoryList.SelectedIndex);SyncSharedPaintHistory();SyncTextHistory();QueueComposite();}
	private void SaveCheckpoint(object sender,RoutedEventArgs e){if(document is null)return;document.Commit("Checkpoint "+DateTime.Now.ToString("HH:mm:ss"),_=>{});QueueComposite();}
	private void SeamPaddingChanged(NumberBox sender,NumberBoxValueChangedEventArgs e){if(updatingLayers||document is null||!double.IsFinite(e.NewValue))return;document.SeamPadding=(int)Math.Clamp(e.NewValue,0,8);QueueComposite();}
	private void RefreshArtwork(){_ = RefreshArtworkShelf();var templates=Path.Combine(WorkshopService.DefaultWorkspace,"templates");Directory.CreateDirectory(templates);TemplateList.ItemsSource=Directory.EnumerateFiles(templates,"*.flproject").Select(p=>new ArtworkAsset(Path.GetFileNameWithoutExtension(p),p)).ToArray();}
	private long stickerRequestRevision;
	private bool stickerLoading;
	private async Task<bool> UseSticker(string path,bool select=true,DecalEffectsOptions? effects=null,List<PaintLayer>? preparedGroup=null,string? groupName=null,long? request=null,VectorArtworkOptions? vectorOverride=null,Func<bool>? stillCurrent=null)
	{
		var revision=request??++stickerRequestRevision;stickerLoading=true;CanvasStatus.Text="Preparing artwork…";
		try
		{
			var text=TextArtwork.ReadMetadata(path);var pattern=PatternArtwork.ReadMetadata(path);var vector=vectorOverride??VectorArtwork.ReadMetadata(path);var raster=ArtworkColors.ReadMetadata(path);
			var raw=await ImageFiles.ReadAsync(path);var image=await Task.Run(()=>DecalEffects.Apply(raw,effects));
			if(revision!=stickerRequestRevision||stillCurrent?.Invoke()==false)return false;
			var key=image.Width+"x"+image.Height+"-"+await Task.Run(()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Pixels)));
			var preview=Path.Combine(WorkshopService.DefaultWorkspace,"artwork-thumbnails","placement-"+key+".png");
			Directory.CreateDirectory(Path.GetDirectoryName(preview)!);if(!File.Exists(preview))await ImageFiles.WriteAsync(image,preview,overwrite:false);
			if(revision!=stickerRequestRevision||stillCurrent?.Invoke()==false)return false;
			if(renderer!=0)Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,1));
            pendingGroup=preparedGroup;pendingText=text;pendingPattern=pattern;pendingVector=vector;pendingRaster=raster;pendingEffects=effects;sticker=image;stickerPath=path;
            PrepareStickerButton.IsEnabled=preparedGroup is null&&text is null&&pattern is null&&vector is null;
			StickerPreview.Source=new BitmapImage{UriSource=new Uri(preview)};
			StickerName.Text=preparedGroup is not null?groupName+" · "+preparedGroup.Count+" editable layer"+(preparedGroup.Count==1?"":"s"):vector is not null?VectorArtwork.Name(vector)+" · SVG · "+vector.RasterSize+" px":Path.GetFileNameWithoutExtension(path);
			UpdatePlacementReadout();if(select){Select(2);if(Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(Path.GetFullPath(ArtworkLibrary.Folder(WorkshopService.DefaultWorkspace)),StringComparison.OrdinalIgnoreCase))await RememberArtwork(path);}return revision==stickerRequestRevision&&stillCurrent?.Invoke()!=false;
		}
		catch(Exception x){if(revision==stickerRequestRevision)Fail(x);return false;}
		finally{if(revision==stickerRequestRevision)stickerLoading=false;}
	}

	private async void CreateShape(object sender,RoutedEventArgs e){try{var path=ArtworkLibrary.Generate(((ComboBoxItem)ShapeSelector.SelectedItem).Content.ToString()!,RacingNumber.Text,BrushHex.Text,WorkshopService.DefaultWorkspace);RefreshArtwork();if(!await UseSticker(path))return;RememberColors(BrushHex.Text);InspectorSection.SelectedIndex=0;}catch(Exception x){Fail(x);}}
	private IReadOnlyList<SavedPalette> Palettes(){var path=Path.Combine(WorkshopService.DefaultWorkspace,"palettes.json");try{return ArtworkLibrary.Palettes.Concat(File.Exists(path)?JsonSerializer.Deserialize<List<SavedPalette>>(File.ReadAllText(path),Contract.Json)??[]:[]).ToArray();}catch(JsonException){return ArtworkLibrary.Palettes;}}
	private void PaletteSelected(object sender,SelectionChangedEventArgs e){if(PaletteColor is not null&&PaletteSelector.SelectedItem is SavedPalette p){PaletteColor.ItemsSource=p.Colors;PaletteColor.SelectedIndex=0;}}
	private void PaletteColorSelected(object sender,SelectionChangedEventArgs e){if(PaletteColor.SelectedItem is string hex)BrushHex.Text=hex;}
	private void SavePalette(object sender,RoutedEventArgs e){if(draft is null)return;try{var path=Path.Combine(WorkshopService.DefaultWorkspace,"palettes.json");var custom=Palettes().Skip(ArtworkLibrary.Palettes.Length).ToList();custom.Add(new(draft.Name,[DesignColor.PaintPickerHex(draft,draft.OuterColor),DesignColor.PaintPickerHex(draft,draft.InnerColor),BrushHex.Text,"#FFFFFF"]));AtomicFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(custom,Contract.Json));PaletteSelector.ItemsSource=Palettes();PaletteSelector.SelectedIndex=Palettes().Count-1;}catch(Exception x){Fail(x);}}
	private async void SaveDesignTemplate(object sender,RoutedEventArgs e){if(draft is null)return;try{await FlushAsync();var folder=Path.Combine(WorkshopService.DefaultWorkspace,"templates");Directory.CreateDirectory(folder);var name=string.Concat(draft.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));ProjectBundle.Save(draft,Path.Combine(folder,name+".flproject"));RefreshArtwork();CanvasStatus.Text="Design template saved with its editable layers and artwork.";}catch(Exception x){Fail(x);}}
	private void OpenDesignTemplate(object sender,RoutedEventArgs e){if(TemplateList.SelectedItem is ArtworkAsset asset)TemplateRequested?.Invoke(asset.Path);}
}
