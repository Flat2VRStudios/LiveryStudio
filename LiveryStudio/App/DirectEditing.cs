using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private DecalSelection? directSelection;
	private PaintLayer? directOriginal,directLayer;
	private IReadOnlyList<PaintLayer>? directOriginalSet;
	private List<PaintLayer>? directWorkingSet;
	private bool directGroupMirrored;
	private Vector2 directStart,directCenter;
	private SurfaceProjection directPlane;
	private string directKind="";
	private bool directDragging,directPreviewReady;
	private bool directExact,directExactBusy;
	private Microsoft.UI.Dispatching.DispatcherQueueTimer? directPreviewTimer;
	private long directPreparation;
	private CancellationTokenSource? directCancellation;
	private long pickImageBytes;
	private readonly Dictionary<string,(long Length,long Modified,PaintImage Image)> pickImages=[];
	// Win32 VK_OEM_PLUS / VK_OEM_MINUS are delivered by WinUI although its
	// VirtualKey enum does not give these two keys symbolic names.
	private const VirtualKey PlusKey=(VirtualKey)187,MinusKey=(VirtualKey)189;
	private PaintImage PickImage(string path)
	{
		var file=new FileInfo(path);if(pickImages.TryGetValue(path,out var cached)&&cached.Length==file.Length&&cached.Modified==file.LastWriteTimeUtc.Ticks)return cached.Image;
		var image=FinishMaps.Read(path);if(pickImages.Count>=32||pickImageBytes+image.Pixels.Length>64*1024*1024){pickImages.Clear();pickImageBytes=0;}
		if(image.Pixels.Length<=64*1024*1024){if(pickImages.Remove(path,out var previous))pickImageBytes-=previous.Image.Pixels.Length;pickImages[path]=(file.Length,file.LastWriteTimeUtc.Ticks,image);pickImageBytes+=image.Pixels.Length;}return image;
	}
	private DecalSelection SelectionFor(PaintLayer layer)=>directSelection is {} s&&s.LayerId==layer.Id&&s.Sample<layer.Samples.Count?s:new(layer.Id,0,false,false);
	private void SelectArtwork(object sender,RoutedEventArgs e){Select(6);SelectTool.Focus(FocusState.Programmatic);}
	private void SelectAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e)=>SelectAccelerated(6,e);
	private void PickArtwork(SurfaceHit? hit,Vector2 at,PointerRoutedEventArgs e)
	{
		if(document is null||mesh is null||painter is null)return;
		try
		{
			transformTimer?.Stop();CommitTransform();
			directSelection=hit is {} point?DecalEditing.Pick(mesh,document.Layers,point,painter.Image.Width,painter.Image.Height,PickImage):null;
			var picked=directSelection?.LayerId;
			if(IsDown(VirtualKey.Control)&&picked is {} extra){var ids=selectedLayers.ToHashSet();var members=IsDown(VirtualKey.Menu)?new[]{extra}:LayerSets.ExpandGroups(document.Layers,[extra]);foreach(var id in members){if(!ids.Add(id))ids.Remove(id);}SetLayerSelection(ids,picked);}
			else SetLayerSelection(picked is {} id?[id]:[],picked,expand:!IsDown(VirtualKey.Menu));SelectTool.Focus(FocusState.Programmatic);
			if(directSelection is null){CanvasStatus.Text="Click a visible decal to select it. Right-drag or middle-drag to navigate.";Render();return;}
			InspectorSection.SelectedIndex=1;
			ShowLayerWorkspace(1);
			CanvasStatus.Text=directSelection.SharedUv?"Selected shared artwork · edits affect every panel using these UVs.":"Selected · drag the artwork or its handles. Arrow keys nudge; Q / E rotate.";
			if(!directSelection.SharedUv&&!IsDown(VirtualKey.Control))BeginDirectEdit("move",at,e);else Render();
		}
		catch(Exception x){Fail(x);}
	}
	private bool TryDirectHandle(Vector2 at,PointerRoutedEventArgs e)
	{
		if(SelectionOverlay.Visibility!=Visibility.Visible)return false;
		// Keep all pointer capture on the viewport. A separate overlay panel or
		// a Button's built-in capture must not swallow car and gizmo gestures.
		foreach(var handle in new[]{RotateHandle,ResizeHandle,MoveHandle})
		{
			var x=Canvas.GetLeft(handle);var y=Canvas.GetTop(handle);if(at.X<x||at.Y<y||at.X>x+32||at.Y>y+32)continue;BeginDirectEdit((string)handle.Tag,at,e);return true;
		}
		return false;
	}
	private void BeginDirectEdit(string kind,Vector2 at,PointerRoutedEventArgs e)
	{
		transformTimer?.Stop();CommitTransform();
		if(SelectedLayer is not {} primary||SelectedLayers.Any(l=>l.Locked)||document is null||mesh is null||renderer==0||recomposing)return;
		if(SelectedLayers.Count==1&&primary is not {Kind:LayerKind.Sticker,Visible:true})return;
		directSelection=SelectionFor(primary);directGroupMirrored=SelectedLayers.Count>1&&directSelection.Mirrored;directOriginalSet=SelectedLayers.Count>1?SelectedLayers.ToArray():null;directWorkingSet=directOriginalSet?.ToList();
		var layer=directOriginalSet is null?primary:GroupHandleLayer(LayerSets.Display(directOriginalSet,directGroupMirrored));
		directOriginal=layer;directLayer=layer;directPlane=directOriginalSet is null?DecalEditing.DisplayProjection(layer,directSelection):layer.Samples[0];directKind=kind;directStart=at;directDragging=true;directPreviewReady=false;
		DecalEditing.Project(directPlane.Center,Matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,out directCenter);
		Viewport.CapturePointer(e.Pointer);SelectTool.Focus(FocusState.Programmatic);hover=null;UploadSelection();e.Handled=true;
		var revision=++directPreparation;var version=generation;var sourceDocument=document;var model=mesh;var project=draft! with {};
		directCancellation?.Cancel();directCancellation?.Dispose();directCancellation=new();var cancellation=directCancellation.Token;
		directExact=directOriginalSet is not null||layer.Effects is not null||layer.Samples.Count!=1||document.Layers.LastOrDefault(l=>l.Visible&&l.Opacity>.001f&&!l.IsMaterial)?.Id!=layer.Id;
		var snapshot=DirectSnapshot();
		// Remove only the selected artwork from an ephemeral preview. The source
		// history and disk stay untouched until the completed gesture is committed.
		if(!directExact){snapshot.History[snapshot.Cursor].Layers.RemoveAll(l=>l.Id==layer.Id);_ = PrepareDirectPreview(snapshot,project,model,sourceDocument,revision,version,layer.Asset,cancellation);}
		else{directPreviewTimer??=CreateExactPreviewTimer();directPreviewTimer.Start();_ = PrepareExactDirectPreview();}
		DrawDirectSelection();
	}
	private LayerDocument DirectSnapshot()
	{
		var sourceDocument=document!;var layers=JsonSerializer.Deserialize<List<PaintLayer>>(JsonSerializer.Serialize(sourceDocument.Layers,LayerDocument.Json),LayerDocument.Json)!;
		if(directWorkingSet is not null)ReplaceLayerSet(layers,directWorkingSet);else if(directLayer is {} layer){var index=layers.FindIndex(l=>l.Id==layer.Id);if(index>=0)layers[index]=layer;}
		return sourceDocument.Snapshot("Drag preview",layers);
	}
	private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateExactPreviewTimer()
	{
		var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(80);timer.IsRepeating=true;timer.Tick+=async(_,_)=>{if(!directDragging||!directExact){timer.Stop();return;}await PrepareExactDirectPreview();};return timer;
	}
	private async Task PrepareExactDirectPreview()
	{
		if(directExactBusy||directLayer is null||document is null||mesh is null||draft is null||directCancellation is null)return;directExactBusy=true;
		var revision=directPreparation;var version=generation;var model=mesh;var project=draft with {};var snapshot=DirectSnapshot();var sourceDocument=document;var cancellation=directCancellation.Token;
		try
		{
			// Lower layers and multi-stamp layers need their actual composition order.
			// Keep one worker in flight so dragging never floods the thread pool.
			var maps=await Task.Run(()=>snapshot.Compose(model,project,cancellation),cancellation);
			if(revision!=directPreparation||version!=generation||!ReferenceEquals(sourceDocument,document)||directLayer is null)return;
			Check(ls_texture(renderer,maps.Albedo.Pixels,(uint)maps.Albedo.Width,(uint)maps.Albedo.Height,0));Check(ls_texture(renderer,maps.Structure.Pixels,(uint)maps.Structure.Width,(uint)maps.Structure.Height,2));Render();
		}
		catch(OperationCanceledException){}
		catch(Exception x){if(revision==directPreparation){FinishDirectEdit(cancel:true);Fail(x);}}
		finally{directExactBusy=false;}
	}
	private async Task PrepareDirectPreview(LayerDocument snapshot,LiveryProject project,PaintMesh model,LayerDocument sourceDocument,long revision,int version,string asset,CancellationToken cancellation)
	{
		try
		{
			var maps=await Task.Run(()=>snapshot.Compose(model,project,cancellation),cancellation);
			if(revision!=directPreparation||version!=generation||!ReferenceEquals(document,sourceDocument)||directLayer is null)return;
			var image=PickImage(asset);Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,1));Check(ls_texture(renderer,maps.Albedo.Pixels,(uint)maps.Albedo.Width,(uint)maps.Albedo.Height,0));Check(ls_texture(renderer,maps.Structure.Pixels,(uint)maps.Structure.Width,(uint)maps.Structure.Height,2));directPreviewReady=true;Render();
		}
		catch(OperationCanceledException){}
		catch(Exception x){if(revision==directPreparation){FinishDirectEdit(cancel:true);Fail(x);}}
	}
	private void UpdateDirectEdit(Vector2 at,bool snap)
	{
		if(!directDragging||directOriginal is null||directSelection is null)return;
		if(Vector2.DistanceSquared(at,directStart)<4){directLayer=directOriginal;directWorkingSet=directOriginalSet?.ToList();Render();return;}
		var selection=directOriginalSet is null?directSelection:new DecalSelection(directOriginal.Id,0,false,false);
		if(directKind=="move")
		{
			var delta=at-directStart;if(snap){if(Math.Abs(delta.X)>=Math.Abs(delta.Y))delta.Y=0;else delta.X=0;}
			if(Hit(directCenter+delta) is not {} hit)return;
			if((directOriginalSet??[directOriginal]).Any(l=>l.Triangles.Length>0&&!l.Triangles.Contains(hit.Triangle))){CanvasStatus.Text="This selection is restricted to its selected panels.";return;}
			directLayer=DecalEditing.Move(directOriginal,selection,hit,Eye);
		}
		else if(DecalEditing.PlanePoint(directStart,Matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,directPlane,out var start)&&DecalEditing.PlanePoint(at,Matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,directPlane,out var end))
		{
			directLayer=directKind=="resize"?directOriginal with {Scale=DecalEditing.Resize(directOriginal.Scale,Vector3.Distance(start,directPlane.Center),Vector3.Distance(end,directPlane.Center),snap)}:
				directOriginal with {Rotation=MathF.IEEERemainder(directOriginal.Rotation+DecalEditing.RotationDelta(start,end,directPlane,selection.Mirrored,snap),360)};
		}
		if(directOriginalSet is not null&&directLayer is {} next){var displayed=LayerSets.Display(directOriginalSet,directGroupMirrored);var to=next.Transform(next.Samples[0]);directWorkingSet=LayerSets.Display(LayerSets.Transform(displayed,directPlane,to,next.Scale/directOriginal.Scale),directGroupMirrored);}
		Render();
	}
	private void FinishDirectEdit(bool cancel=false)
	{
		if(!directDragging)return;directDragging=false;directPreviewTimer?.Stop();
		if(!cancel&&document is not null&&directLayer is {} changed&&directOriginal is {} original&&(changed.Scale!=original.Scale||changed.Rotation!=original.Rotation||!changed.Samples.SequenceEqual(original.Samples)||changed.OffsetRight!=original.OffsetRight||changed.OffsetUp!=original.OffsetUp))
		{
			document.Commit((directKind=="move"?"Moved ":directKind=="resize"?"Resized ":"Rotated ")+changed.Name,layers=>{if(directWorkingSet is not null)ReplaceLayerSet(layers,directWorkingSet);else layers[layers.FindIndex(l=>l.Id==changed.Id)]=changed;});QueueComposite();
		}
		else {ClearDirectPreview();UploadTexture();UploadStructure();Render();}
	}
	private void ClearDirectPreview()
	{
		directPreparation++;directPreviewTimer?.Stop();directCancellation?.Cancel();directCancellation?.Dispose();directCancellation=null;directDragging=directPreviewReady=false;directOriginal=directLayer=null;directOriginalSet=directWorkingSet=null;UploadSelection();
		if(renderer!=0&&sticker is not null)Check(ls_texture(renderer,sticker.Pixels,(uint)sticker.Width,(uint)sticker.Height,1));
	}
	private void DrawDirectSelection()
	{
		if(SelectionOverlay is null)return;SelectionOverlay.Visibility=SelectionReadout.Visibility=Visibility.Collapsed;
		if(ImageReviewActive)return;
		var grouped=SelectedLayers.Count>1;var layer=directLayer??(grouped?GroupHandleLayer(LayerSets.Display(SelectedLayers,directSelection?.Mirrored==true)):SelectedLayer);if(mode!=6||layer is not {Visible:true}||(!grouped&&layer.Kind!=LayerKind.Sticker)||SelectedLayers.Any(l=>l.Locked))return;
		var selection=grouped?new DecalSelection(layer.Id,0,false,false):SelectionFor(layer);var p=DecalEditing.DisplayProjection(layer,selection);if(Vector3.Dot(p.Normal,Eye-p.Center)<=0)return;
		bool Screen(Vector3 point,out Vector2 screen)=>DecalEditing.Project(point,Matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,out screen);
		if(!Screen(p.Center,out var center)||center.X<0||center.Y<0||center.X>Viewport.ActualWidth||center.Y>Viewport.ActualHeight)return;
		var points=new List<Vector2>();foreach(var corner in new[]{new Vector2(-.5f,.5f),new Vector2(.5f,.5f),new Vector2(.5f,-.5f),new Vector2(-.5f,-.5f)}){if(!Screen(LayerSets.Corner(p,corner),out var point))return;points.Add(point);}
		SelectionOutline.Points=new PointCollection();foreach(var point in points)SelectionOutline.Points.Add(new(point.X,point.Y));
		var top=(points[0]+points[1])*.5f;var outward=top-center;outward=outward.LengthSquared()<1?new(0,-1):Vector2.Normalize(outward);var rotate=top+outward*MathF.Max(36,44-Vector2.Distance(top,center));
		var resizeCorner=points[2];var cornerDirection=resizeCorner-center;var resize=cornerDirection.LengthSquared()>=44*44?resizeCorner:center+(cornerDirection.LengthSquared()<1?new Vector2(1,1)/MathF.Sqrt(2):Vector2.Normalize(cornerDirection))*44;
		Vector2 Fit(Vector2 point,params Vector2[] avoid)
		{
			point.X=Math.Clamp(point.X,16,(float)Math.Max(16,Viewport.ActualWidth-16));point.Y=Math.Clamp(point.Y,16,(float)Math.Max(16,Viewport.ActualHeight-16));
			void Below(double x,double y,double w,double h){if(point.X+16>=x&&point.X-16<=x+w&&point.Y+16>=y&&point.Y-16<=y+h)point.Y=(float)Math.Min(Viewport.ActualHeight-16,y+h+20);}
			if(QuickTools.Visibility==Visibility.Visible)Below(QuickTools.Margin.Left,QuickTools.Margin.Top,QuickTools.ActualWidth,QuickTools.ActualHeight);
			if(InspectorToggle.Visibility==Visibility.Visible)Below(Viewport.ActualWidth-InspectorToggle.Margin.Right-InspectorToggle.ActualWidth,InspectorToggle.Margin.Top,InspectorToggle.ActualWidth,InspectorToggle.ActualHeight);
			if(avoid.Any(other=>Vector2.Distance(point,other)<40)){var origin=point;var candidates=new List<Vector2>();for(var x=-3;x<=3;x++)for(var y=-3;y<=3;y++){var next=origin+new Vector2(x*44,y*44);if(next.X<16||next.Y<16||next.X>Viewport.ActualWidth-16||next.Y>Viewport.ActualHeight-16||avoid.Any(other=>Vector2.Distance(next,other)<40))continue;if(QuickTools.Visibility==Visibility.Visible&&next.X<QuickTools.Margin.Left+QuickTools.ActualWidth+16&&next.Y<QuickTools.Margin.Top+QuickTools.ActualHeight+16)continue;if(InspectorToggle.Visibility==Visibility.Visible&&next.X>Viewport.ActualWidth-InspectorToggle.Margin.Right-InspectorToggle.ActualWidth-16&&next.Y<InspectorToggle.Margin.Top+InspectorToggle.ActualHeight+16)continue;candidates.Add(next);}if(candidates.Count>0)point=candidates.MinBy(next=>Vector2.DistanceSquared(next,origin));}return point;
		}
		var move=Fit(center);resize=Fit(resize,move);rotate=Fit(rotate,move,resize);
		void Place(FrameworkElement control,Vector2 point){Canvas.SetLeft(control,point.X-16);Canvas.SetTop(control,point.Y-16);}
		Place(MoveHandle,move);Place(ResizeHandle,resize);Place(RotateHandle,rotate);RotationStem.X1=top.X;RotationStem.Y1=top.Y;RotationStem.X2=rotate.X;RotationStem.Y2=rotate.Y;ResizeStem.X1=resizeCorner.X;ResizeStem.Y1=resizeCorner.Y;ResizeStem.X2=resize.X;ResizeStem.Y2=resize.Y;
		SelectionOverlay.Clip=new RectangleGeometry{Rect=new(0,0,Viewport.ActualWidth,Viewport.ActualHeight)};SelectionOverlay.Visibility=SelectionReadout.Visibility=Visibility.Visible;
		SelectionReadout.Text=AdvancedMode?$"{layer.Name} · {p.Width:0.00} × {p.Height:0.00} m · "+(grouped?$"{SelectedLayers.Count} layers":$"{layer.Rotation:0}°")+"\nDrag ✥ to move · corner to resize · ↻ to rotate · Shift constrains / snaps":"Drag to move · Corner resizes · Round handle turns";
		SelectionReadout.Margin=new(14,0,14,Viewport.ActualHeight>=360?90:10);
	}
	private static PaintLayer GroupHandleLayer(IReadOnlyList<PaintLayer> layers)=>new(){Id=layers[0].Id,Name=layers.Select(l=>l.GroupId).Distinct().Count()==1&&layers[0].GroupId.HasValue?layers[0].GroupName:$"{layers.Count} layers",Kind=LayerKind.Sticker,AlignWithCamera=layers.All(l=>l.AlignWithCamera),Samples=[LayerSets.Frame(layers)]};
	private void EditDirectSelection(string label,float right=0,float up=0,float scale=1,float rotation=0)
	{
		if(SelectedLayers.Count>1){var mirrored=directSelection?.Mirrored==true;EditSelection(label,(layers,ids)=>{var shown=LayerSets.Display(layers.Where(l=>ids.Contains(l.Id)).ToArray(),mirrored);ReplaceLayerSet(layers,LayerSets.Display(LayerSets.Edit(shown,right,up,scale,rotation),mirrored));});}
		else EditSelected(label,l=>{var nudged=DecalEditing.Nudge(l,SelectionFor(l),right,up);l.OffsetRight=nudged.OffsetRight;l.OffsetUp=nudged.OffsetUp;l.Rotation=DecalEditing.Turn(l,SelectionFor(l),rotation).Rotation;l.Scale=DecalEditing.Resize(l.Scale,1,scale,false);});
	}
	private void DirectHandleClick(object sender,RoutedEventArgs e)
	{
		if(recomposing||maskCommitting||directDragging||SelectedLayer is not {Kind:LayerKind.Sticker} layer)return;
		var kind=(string)((Button)sender).Tag;if(kind=="move"){SelectTool.Focus(FocusState.Programmatic);CanvasStatus.Text="Arrow keys nudge · Shift moves farther · Q / E rotate · + / − resize";return;}
		EditDirectSelection(kind=="resize"?"Resized selection":"Rotated selection",scale:kind=="resize"?1.05f:1,rotation:kind=="resize"?0:5);
	}
	private void DirectKeyDown(object sender,KeyRoutedEventArgs e)
	{
        if(ImageReviewActive){if(e.Key==VirtualKey.Escape){FinishImageReview(ImageReviewResult.Settings);e.Handled=true;}return;}
        if(e.Key==VirtualKey.Escape&&(chooseArtworkAtPoint||artworkPointCancellation is not null||artworkPointFlyout is not null)){CancelArtworkPointPick();CanvasStatus.Text="Decal choice canceled · your selection is unchanged.";e.Handled=true;return;}
        if(maskCommitting){e.Handled=true;return;}
        if(e.Key==VirtualKey.Escape&&mode==8){if(maskPainting)EndMaskStroke(cancel:true);else Select(6);e.Handled=true;return;}
        if(e.Key==VirtualKey.Escape&&inspectingLayers){ClearLayerInspection();e.Handled=true;return;}
        if(e.Key==VirtualKey.Escape&&mode==7){Select(colorPickReturnMode);CanvasStatus.Text="Color pick canceled · your design is unchanged.";e.Handled=true;return;}
		if(e.Key==VirtualKey.Escape&&floatingInspector&&compactInspectorOpen){HideInspector();InspectorToggle.Focus(FocusState.Programmatic);e.Handled=true;return;}
		if(mode!=6)return;
		var focus=FocusManager.GetFocusedElement(XamlRoot);if(!ReferenceEquals(focus,SelectTool)&&!ReferenceEquals(focus,MoveHandle)&&!ReferenceEquals(focus,ResizeHandle)&&!ReferenceEquals(focus,RotateHandle))return;
		if(e.Key==VirtualKey.Escape){if(directDragging)FinishDirectEdit(cancel:true);else{selectedLayer=null;selectedLayers.Clear();directSelection=null;RefreshLayers();Render();}e.Handled=true;return;}
		if(directDragging||SelectedLayers.Count==0||SelectedLayers.Any(l=>l.Locked))return;
		var shift=Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);var step=shift?.05f:.01f;
		if(e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
		{
			var right=e.Key==VirtualKey.Left?-step:e.Key==VirtualKey.Right?step:0;var up=e.Key==VirtualKey.Up?step:e.Key==VirtualKey.Down?-step:0;
			EditDirectSelection("Nudged selection",right,up);e.Handled=true;
		}
		else if(e.Key is VirtualKey.Q or VirtualKey.E){EditDirectSelection("Rotated selection",rotation:(e.Key==VirtualKey.Q?-1:1)*(shift?15:1));e.Handled=true;}
		else if(e.Key is VirtualKey.Add or VirtualKey.Subtract or PlusKey or MinusKey){EditDirectSelection("Resized selection",scale:e.Key is VirtualKey.Add or PlusKey?1.05f:1/1.05f);e.Handled=true;}
	}
}
