using System.Numerics;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private bool orthographic;
    private Vector3[]? cameraFitPoints,previousFitPoints,carCameraPoints;
    private StudioCameraPose? previousCamera;
    public event Action<List<CameraBookmark>>? CameraBookmarksChanged;
    private float CameraAspect=>(float)Math.Max(1,Viewport.ActualWidth)/(float)Math.Max(1,Viewport.ActualHeight);
    private StudioCameraPose CameraPose=>new(yaw,pitch,distance,target.X,target.Y,target.Z,orthographic);
    private void ApplyCamera(StudioCameraPose pose,bool render=true){yaw=pose.Yaw;pitch=pose.Pitch;distance=pose.Distance;target=pose.Target;orthographic=pose.Orthographic;FlatViewToggle.IsChecked=orthographic;hover=null;if(render)Render();}
    private void RememberCamera(){previousCamera=CameraPose;previousFitPoints=cameraFitPoints;PreviousCameraItem.IsEnabled=true;}
    private Vector4 CameraFramingArea
    {
        get{if(ImageReviewActive)return new(-.94f,-.94f,.94f,.94f);var w=(float)Math.Max(1,Viewport.ActualWidth);var h=(float)Math.Max(1,Viewport.ActualHeight);if(!AdvancedMode&&!previewOnly)return new(-.94f,-.88f,.94f,1-2*Math.Min(60,h*.2f)/h);var top=Math.Min(h*.38f,(float)Math.Max(CanvasHeading.Margin.Top+CanvasHeading.ActualHeight+12,QuickTools.Visibility==Visibility.Visible?QuickTools.Margin.Top+QuickTools.ActualHeight+12:0));var inspection=LayerInspectionNotice.Visibility==Visibility.Visible;var overlay=inspection?LayerInspectionNotice:CanvasHelp;var bottom=Math.Min(h*(inspection?.45f:.22f),overlay.Visibility==Visibility.Visible?(float)(overlay.ActualHeight+overlay.Margin.Bottom+10):16);return new(-1+Math.Min(.08f,28/w),-1+2*bottom/h,1-Math.Min(.08f,28/w),1-2*top/h);}
    }
    private void RefitCamera(){if(cameraFitPoints is not null&&Viewport.ActualWidth>0&&Viewport.ActualHeight>0)ApplyCamera(StudioCamera.Fit(cameraFitPoints,yaw,pitch,CameraAspect,orthographic,CameraFramingArea),render:false);}
    private void FrameCar(bool remember=true,bool render=true){if(mesh is null)return;if(remember)RememberCamera();cameraFitPoints=carCameraPoints??StudioCamera.Geometry(mesh);RefitCamera();if(render)Render();}
    private void FitCar(object sender,RoutedEventArgs e){FrameCar();HideInspector();CanvasStatus.Text="Whole car framed · wheel to zoom towards the pointer.";}
    private void FocusArtwork(object sender,RoutedEventArgs e)
    {
        if(SelectedLayers.Count==0)return;FinishDirectEdit();RememberCamera();var layers=LayerSets.Display(SelectedLayers,directSelection?.Mirrored==true);var frame=LayerSets.Frame(layers);var normal=frame.Normal;yaw=MathF.Atan2(normal.X,normal.Z);pitch=MathF.Asin(Math.Clamp(normal.Y,-1,1));cameraFitPoints=StudioCamera.Artwork(layers);RefitCamera();HideInspector();CanvasStatus.Text="Selected artwork framed · Previous view returns to the car.";Render();
    }
    private void PreviousView(object sender,RoutedEventArgs e){if(previousCamera is not {} pose)return;var fit=previousFitPoints;RememberCamera();cameraFitPoints=fit;ApplyCamera(pose,render:false);RefitCamera();HideInspector();CanvasStatus.Text="Previous camera view restored.";Render();}
    private void ToggleFlatView(object sender,RoutedEventArgs e){RememberCamera();orthographic=FlatViewToggle.IsChecked;RefitCamera();Render();CanvasStatus.Text=orthographic?"Flat working view · parallel lines stay parallel.":"Perspective view · natural depth restored.";}
    private void CameraMenuOpened(object sender,object e){FocusArtworkCameraItem.IsEnabled=SelectedLayers.Count>0;PreviousCameraItem.IsEnabled=previousCamera is not null;FlatViewToggle.IsChecked=orthographic;SyncCameraBookmarks();}
    private void ShowCompactCamera(object sender,RoutedEventArgs e)=>CameraToolsButton.Flyout.ShowAt(CompactCameraButton);
    private void SyncCameraBookmarks(){if(draft is null)return;foreach(var item in new[]{RestoreCamera1,RestoreCamera2,RestoreCamera3})item.IsEnabled=draft.CameraBookmarks.Any(v=>v.CarIndex==draft.CarIndex&&v.Slot==int.Parse((string)item.Tag));}
    private void SaveCameraView(object sender,RoutedEventArgs e)
    {
        if(draft is null||mesh is null||sender is not FrameworkElement {Tag:string slotText})return;try{var slot=int.Parse(slotText);var view=new CameraBookmark(draft.CarIndex,slot,CameraPose,CameraAspect);view.Validate();draft.CameraBookmarks=draft.CameraBookmarks.Where(v=>v.CarIndex!=draft.CarIndex||v.Slot!=slot).Append(view).ToList();CameraBookmarksChanged?.Invoke(draft.CameraBookmarks);SyncCameraBookmarks();CanvasStatus.Text=$"Camera view {slot+1} saved with this car in the project.";}catch(Exception x){Fail(x);}
    }
    private void RestoreCameraView(object sender,RoutedEventArgs e)
    {
        if(draft is null||sender is not FrameworkElement {Tag:string slotText})return;var slot=int.Parse(slotText);if(draft.CameraBookmarks.FirstOrDefault(v=>v.CarIndex==draft.CarIndex&&v.Slot==slot) is not {} view)return;RememberCamera();cameraFitPoints=null;ApplyCamera(view.Restore(CameraAspect));HideInspector();CanvasStatus.Text=$"Camera view {slot+1} restored.";
    }
    private void FitCameraShortcut(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e){if(FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;if(sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift))FocusArtwork(this,new());else FitCar(this,new());e.Handled=true;}
}
