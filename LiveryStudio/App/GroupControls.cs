using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using System.Text.Json;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private readonly HashSet<Guid> selectedLayers=[];
	private List<PaintLayer>? pendingGroup;
	private SurfaceProjection? groupHoverProjection;
	private bool groupHoverMirror,groupHoverActive,groupHoverBusy;
	private float groupHoverOpacity;
	private int[] groupHoverMask=[];
	private long groupHoverRevision;
	private CancellationTokenSource? groupHoverCancellation;
	private Microsoft.UI.Dispatching.DispatcherQueueTimer? groupHoverTimer;
	private IReadOnlyList<PaintLayer> SelectedLayers=>document?.Layers.Where(l=>selectedLayers.Contains(l.Id)||(selectedLayers.Count==0&&l.Id==selectedLayer)).ToArray()??[];
	private void SetLayerSelection(IEnumerable<Guid> ids,Guid? primary=null,bool expand=false)
	{
		var values=ids.ToArray();if(expand&&document is not null)values=LayerSets.ExpandGroups(document.Layers,values);selectedLayers.Clear();selectedLayers.UnionWith(values);selectedLayer=primary is {} id&&selectedLayers.Contains(id)?id:values.Length>0?values[^1]:null;RefreshLayers();
	}
	private void SyncGroupControls()
	{
		if(SelectionSummary is null)return;var layers=SelectedLayers;var locked=layers.Any(l=>l.Locked);SelectionSummary.Text=layers.Count==0?"Select layers · Ctrl adds, Shift selects a range":$"{layers.Count} selected"+(locked?" · locked":"")+(layers.Select(l=>l.GroupId).Distinct().Count()==1&&layers[0].GroupId.HasValue?" · "+layers[0].GroupName:"");
		GroupButton.IsEnabled=layers.Count>=2&&!locked;UngroupButton.IsEnabled=layers.Any(l=>l.GroupId.HasValue)&&!locked;LockSelectionButton.IsEnabled=layers.Count>0;LockSelectionButton.Content=layers.Count>0&&layers.All(l=>l.Locked)?"Unlock selection":"Lock selection";ApplySetTransformButton.IsEnabled=layers.Count>0&&!locked;SaveGroupButton.IsEnabled=layers.Count>0;SelectionGroupName.IsEnabled=layers.Count>0;
		ForwardSelectionButton.IsEnabled=BackwardSelectionButton.IsEnabled=RemoveSelectionButton.IsEnabled=layers.Count>0&&!locked;DuplicateSelectionButton.IsEnabled=layers.Count>0;
		RepeatArtworkButton.IsEnabled=layers.Count>0&&!locked&&layers.All(l=>l.Visible);
		OtherSideButton.Content=layers.Any(l=>l.Mirror)?"Separate paired sides…":"Copy to other side…";
		OtherSideButton.IsEnabled=layers.Count>0&&!layers.Any(l=>l.Mirror&&l.Locked);
		if(layers.Count==1)SelectionSummary.Text=layers[0].Name+(locked?" · locked":"");
		SyncLayerWorkspace();SyncArtworkLayoutControls();
		if(layers.Count>0&&layers[0].GroupId.HasValue)SelectionGroupName.Text=layers[0].GroupName;
	}
	private void EditSelection(string label,Action<List<PaintLayer>,HashSet<Guid>> edit,bool allowLocked=false)
	{
		if(document is null||SelectedLayers.Count==0)return;try{FinishDirectEdit(cancel:true);transformTimer?.Stop();CommitTransform();var ids=SelectedLayers.Select(l=>l.Id).ToHashSet();if(!allowLocked)LayerSets.RequireEditable(SelectedLayers);document.Commit(label,layers=>edit(layers,ids));QueueComposite();Render();}catch(Exception x){Fail(x);}
	}
	private void GroupSelection(object sender,RoutedEventArgs e)=>EditSelection("Grouped artwork",(layers,ids)=>LayerSets.Group(layers,ids,SelectionGroupName.Text));
	private void UngroupSelection(object sender,RoutedEventArgs e)=>EditSelection("Ungrouped artwork",(layers,ids)=>LayerSets.Ungroup(layers,ids));
	private void LockSelection(object sender,RoutedEventArgs e){var unlock=SelectedLayers.All(l=>l.Locked);EditSelection(unlock?"Unlocked selection":"Locked selection",(layers,ids)=>{foreach(var layer in layers.Where(l=>ids.Contains(l.Id)))layer.Locked=!unlock;},allowLocked:true);}
	private void ApplySetTransform(object sender,RoutedEventArgs e)
	{
		if(!double.IsFinite(SetScale.Value)||!double.IsFinite(SetRotation.Value)||!double.IsFinite(SetOffsetX.Value)||!double.IsFinite(SetOffsetY.Value))return;
		EditSelection("Transformed selection",(layers,ids)=>ReplaceLayerSet(layers,LayerSets.Edit(layers.Where(l=>ids.Contains(l.Id)).ToArray(),(float)SetOffsetX.Value,(float)SetOffsetY.Value,(float)SetScale.Value,(float)SetRotation.Value)));
		SetScale.Value=1;SetRotation.Value=SetOffsetX.Value=SetOffsetY.Value=0;
	}
	private static void ReplaceLayerSet(List<PaintLayer> layers,IEnumerable<PaintLayer> changed){foreach(var layer in changed){var i=layers.FindIndex(l=>l.Id==layer.Id);if(i>=0)layers[i]=layer;}}
	private async void SaveArtworkGroup(object sender,RoutedEventArgs e)
	{
		if(SelectedLayers.Count==0)return;SaveGroupButton.IsEnabled=false;
		try{await FlushAsync();var selection=SelectedLayers.ToArray();var name=SelectionGroupName.Text;var file=await Task.Run(()=>ReusableArtwork.Save(name,selection,WorkshopService.DefaultWorkspace));RefreshGroupLibrary(file);CanvasStatus.Text="Saved reusable artwork · "+name;}
		catch(Exception x){Fail(x);}finally{SaveGroupButton.IsEnabled=true;}
	}
	private void RefreshGroupLibrary(string? selected=null){if(GroupLibrary is null)return;selected??=(GroupLibrary.SelectedItem as ReusableGroup)?.Path;var groups=ReusableArtwork.List(WorkshopService.DefaultWorkspace);GroupLibrary.ItemsSource=groups;GroupLibrary.SelectedItem=groups.FirstOrDefault(g=>g.Path==selected)??groups.FirstOrDefault();SyncReusableLibrary();_ = RefreshArtworkShelf();}
	private async void UseArtworkGroup(object sender,RoutedEventArgs e)
	{
		if(GroupLibrary.SelectedItem is ReusableGroup group)await PlaceSavedArtwork(group);
	}
	private async Task PlaceSavedArtwork(ReusableGroup group,Func<bool>? stillCurrent=null)
	{
		var revision=++stickerRequestRevision;stickerLoading=true;CanvasStatus.Text="Preparing editable artwork…";
		try
		{
			var layers=await Task.Run(()=>ReusableArtwork.Open(group.Path,WorkshopService.DefaultWorkspace));
			if(revision!=stickerRequestRevision||stillCurrent?.Invoke()==false)return;var image=await Task.Run(()=>ReusableArtwork.Preview(layers));if(revision!=stickerRequestRevision||stillCurrent?.Invoke()==false)return;
			var path=Path.Combine(WorkshopService.DefaultWorkspace,"artwork-thumbnails","group-placement-"+WorkshopService.Hash(group.Path)+".tga");Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(!File.Exists(path))FinishMaps.WriteTga(image,path);
			if(!await UseSticker(path,preparedGroup:layers,groupName:group.Name,request:revision,stillCurrent:stillCurrent))return;
			if(stillCurrent?.Invoke()==false)return;await RememberArtwork(group.Path);if(revision!=stickerRequestRevision||stillCurrent?.Invoke()==false)return;CanvasStatus.Text="Hover to position your reusable group · click to place its editable layers.";HideInspector();
		}
		catch(Exception x){if(revision==stickerRequestRevision)Fail(x);}
		finally{if(revision==stickerRequestRevision)stickerLoading=false;}
	}

	private bool StampArtworkGroup(SurfaceHit hit)
	{
		if(pendingGroup is null||document is null||BaseStampProjection is not {} p)return false;
		if(AdvancedMode&&PanelOnly.IsChecked==true&&panelMask.Length==0){CanvasStatus.Text="Pick a surface before placing panel-only artwork.";return true;}
		try{var frame=PlacementSettings().Transform(p);var layers=PlacePendingGroup(frame,MirrorPaint.IsChecked==true,PlacementOpacity,ActiveMask());ClearGroupHover();hover=null;document.Commit("Placed reusable artwork",all=>all.AddRange(layers));SetLayerSelection(layers.Select(l=>l.Id),layers[^1].Id);QueueComposite();Render();return true;}catch(Exception x){Fail(x);return true;}
	}
	private List<PaintLayer> PlacePendingGroup(SurfaceProjection frame,bool mirror,float opacity,int[] mask)
	{
		var layers=ReusableArtwork.Place(pendingGroup!,frame,mirror,opacity,frame.FlipHorizontal,frame.FlipVertical);foreach(var l in layers){l.Triangles=mask;l.WrapAngle=AdvancedMode?(float)ToolWrapAngle.Value:75.52249f;l.WrapDepth=AdvancedMode?(float)ToolWrapDepth.Value:.35f;l.AlignWithCamera=AdvancedMode&&ToolAlignment.SelectedIndex==1;}return layers;
	}
	private void ClearGroupHover()
	{
		groupHoverRevision++;groupHoverTimer?.Stop();groupHoverCancellation?.Cancel();groupHoverCancellation?.Dispose();groupHoverCancellation=null;groupHoverProjection=null;if(groupHoverActive){groupHoverActive=false;UploadTexture();UploadStructure();}
	}
	private void UpdateGroupHover()
	{
		if(mode!=2||pendingGroup is null||document is null||mesh is null||draft is null||hover is null||recomposing||StampProjection is not {} p||AdvancedMode&&PanelOnly.IsChecked==true&&panelMask.Length==0){if(groupHoverProjection is not null||groupHoverActive)ClearGroupHover();return;}
		var mirror=MirrorPaint.IsChecked==true;var opacity=PlacementOpacity;var mask=ActiveMask();if(groupHoverProjection==p&&groupHoverMirror==mirror&&groupHoverOpacity==opacity&&groupHoverMask.SequenceEqual(mask))return;
		groupHoverProjection=p;groupHoverMirror=mirror;groupHoverOpacity=opacity;groupHoverMask=mask;groupHoverRevision++;groupHoverCancellation?.Cancel();groupHoverCancellation?.Dispose();groupHoverCancellation=new();
		groupHoverTimer??=CreateGroupHoverTimer();groupHoverTimer.Start();
	}
	private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateGroupHoverTimer()
	{
		var timer=DispatcherQueue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(80);timer.IsRepeating=true;timer.Tick+=async(_,_)=>await ComposeGroupHover();return timer;
	}
	private async Task ComposeGroupHover()
	{
		if(groupHoverBusy||groupHoverProjection is not {} p||document is null||mesh is null||draft is null||groupHoverCancellation is null)return;groupHoverBusy=true;var revision=groupHoverRevision;var version=generation;var current=document;var model=mesh;var project=draft with {};var cancellation=groupHoverCancellation.Token;
		try{var snapshot=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(document,LayerDocument.Json),LayerDocument.Json)!;var placed=PlacePendingGroup(p,groupHoverMirror,groupHoverOpacity,groupHoverMask);snapshot.Commit("Group hover",layers=>layers.AddRange(placed));var maps=await Task.Run(()=>snapshot.Compose(model,project,cancellation),cancellation);if(revision!=groupHoverRevision||version!=generation||!ReferenceEquals(current,document))return;Check(ls_texture(renderer,maps.Albedo.Pixels,(uint)maps.Albedo.Width,(uint)maps.Albedo.Height,0));Check(ls_texture(renderer,maps.Structure.Pixels,(uint)maps.Structure.Width, (uint)maps.Structure.Height,2));groupHoverActive=true;groupHoverTimer?.Stop();Render();}
		catch(OperationCanceledException){}catch(Exception x){if(revision==groupHoverRevision){ClearGroupHover();Fail(x);}}finally{groupHoverBusy=false;}
	}
	private static bool IsDown(VirtualKey key)=>Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
