using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private enum ImageReviewResult { Settings, Apply, Cancel }
    private TaskCompletionSource<ImageReviewResult>? imageReviewCompletion;
    private CompositeMaps? imageReviewMaps;
    private (KeyboardAccelerator Accelerator,bool Enabled)[]? imageReviewAccelerators;
    private bool ImageReviewActive=>imageReviewCompletion is not null;
    public event Action<bool>? ImagePreparationReviewChanged;

    private async Task<ImageReviewResult> InspectPreparedImage(CompositeMaps maps,bool canApply,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion=new TaskCompletionSource<ImageReviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        imageReviewCompletion=completion;imageReviewMaps=maps;
        try
        {
            imageReviewAccelerators=KeyboardAccelerators.Select(a=>(a,a.IsEnabled)).ToArray();
            foreach(var (accelerator,_) in imageReviewAccelerators)accelerator.IsEnabled=false;
            hover=null;dragging=panning=painting=false;Viewport.ReleasePointerCaptures();
            ImageReviewSaved.IsChecked=false;ImageReviewApply.IsEnabled=canApply;ImageReviewBar.Visibility=Visibility.Visible;
            ImagePreparationReviewChanged?.Invoke(true);
            UpdatePaintLayout(new(PaintLayout.ActualWidth,PaintLayout.ActualHeight));UploadSelection();
            FocusPreparedImage(this,new());ShowImageReviewTextures();ImageReviewBack.Focus(FocusState.Programmatic);
            using var registration=token.Register(()=>DispatcherQueue.TryEnqueue(()=>{if(ReferenceEquals(imageReviewCompletion,completion))FinishImageReview(ImageReviewResult.Cancel);}));
            return await completion.Task;
        }
        finally{if(ReferenceEquals(imageReviewCompletion,completion))FinishImageReview(ImageReviewResult.Cancel);}
    }

    private void ShowImageReviewTextures()
    {
        if(!ImageReviewActive||imageReviewMaps is not {} maps||renderer==0)return;
        var saved=ImageReviewSaved.IsChecked==true;
        if(saved){UploadTexture();UploadStructure();}
        else
        {
            Check(ls_texture(renderer,maps.Albedo.Pixels,(uint)maps.Albedo.Width,(uint)maps.Albedo.Height,0));
            Check(ls_texture(renderer,maps.Structure.Pixels,(uint)maps.Structure.Width,(uint)maps.Structure.Height,2));
        }
        ImageReviewCaption.Text=saved?"Saved design · unchanged":"Prepared preview · not saved";
        CanvasStatus.Text="Drag to orbit · scroll to zoom · Escape returns to settings";Render();
    }

    private void FinishImageReview(ImageReviewResult result)
    {
        if(imageReviewCompletion is not {} completion)return;
        imageReviewCompletion=null;imageReviewMaps=null;
        try
        {
            dragging=panning=painting=false;hover=null;Viewport.ReleasePointerCaptures();ImageReviewBar.Visibility=Visibility.Collapsed;
            if(imageReviewAccelerators is {} accelerators)foreach(var (accelerator,enabled) in accelerators)accelerator.IsEnabled=enabled;
            imageReviewAccelerators=null;ImagePreparationReviewChanged?.Invoke(false);
            UpdatePaintLayout(new(PaintLayout.ActualWidth,PaintLayout.ActualHeight));UploadSelection();UploadTexture();UploadStructure();
            CanvasStatus.Text="Image preview closed · saved design unchanged.";Render();
        }
        finally{completion.TrySetResult(result);}
    }

    private void ImageReviewComparisonChanged(object sender,RoutedEventArgs e)=>ShowImageReviewTextures();
    private void FocusPreparedImage(object sender,RoutedEventArgs e)
    {
        FocusArtwork(sender,e);
        if(SelectedLayers.Count==0)return;
        var frame=LayerSets.Frame(LayerSets.Display(SelectedLayers,directSelection?.Mirrored==true));
        // A tiny slope on a hood/roof should not choose a 180-degree yaw and
        // turn the logo upside down. Keep its own top edge upright instead.
        if(frame.Normal.Y>.85f){yaw=MathF.Atan2(frame.Up.X,-frame.Up.Z);RefitCamera();Render();}
    }
    private void ImageReviewBackClicked(object sender,RoutedEventArgs e)=>FinishImageReview(ImageReviewResult.Settings);
    private void ImageReviewApplyClicked(object sender,RoutedEventArgs e)=>FinishImageReview(ImageReviewResult.Apply);
    private void ImageReviewCancelClicked(object sender,RoutedEventArgs e)=>CancelImagePreparation();
}

public partial class MainWindow
{
    private (Control Control,bool Enabled)[]? imageReviewHostControls;
    private void ImageReviewHostChanged(bool active)
    {
        // Leave window sizing and closing available, and keep document-changing
        // commands out of the temporary inspection view.
        var previous=loading;loading=true;
        try
        {
            if(active)
            {
                imageReviewHostControls=new Control[]{DesignNav,PaintNav,LibraryNav,SetupNav,NewButton,OpenButton,SaveButton,PaintCarSelector,DesignSettingsButton,BakeButton,OutputCommands,LibraryOutputCommands}.Select(c=>(c,c.IsEnabled)).ToArray();
                foreach(var (control,_) in imageReviewHostControls)control.IsEnabled=false;
            }
            else if(imageReviewHostControls is {} controls)
            {
                imageReviewHostControls=null;
                if(!busy&&!shutdown.IsClosing)foreach(var (control,enabled) in controls)control.IsEnabled=enabled;
            }
        }
        finally{loading=previous;}
    }
}
