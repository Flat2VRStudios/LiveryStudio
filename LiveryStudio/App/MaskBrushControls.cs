using System.Numerics;
using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private DecalMaskPainting? maskBrush;
    private PaintImage? maskArtworkOriginal,maskStrokeBefore;
    private PaintLayer? maskBrushLayer;
    private LayerDocument? maskBrushDocument;
    private CompositeMaps? maskUnderMaps,maskLiveMaps;
    private DecalMaskPoint? maskLastPoint;
    private CancellationTokenSource? maskCancellation;
    private long maskEpoch;
    private long maskPreviewRevision;
    private bool maskPreparing,maskPainting,maskChanged,maskCommitting,maskPreviewBusy,maskPreviewRequested,maskUploadDirty;
    private bool maskRestore;
    private float maskStrength,maskHardness,maskWorldRadius;
    private Dictionary<Control,bool>? maskDisabledControls;
    private string maskDocumentState="";
    private PaintImage? MaskDisplayImage=>mode==8?maskLiveMaps?.Albedo??maskUnderMaps?.Albedo:null;
    private async void SelectMaskBrush(object sender,RoutedEventArgs e)=>await ActivateMaskBrush();
    private async void MaskBrushAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e)
    {
        if(!AdvancedMode||FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;e.Handled=true;await ActivateMaskBrush();
    }
    private async Task ActivateMaskBrush()
    {
        if(maskPreparing||maskCommitting)return;
        var activationEpoch=maskEpoch;
        try
        {
            await FlushAsync();
            if(document is null||mesh is null||draft is null||SelectedLayers.Count!=1||SelectedLayer is not {Kind:LayerKind.Sticker,Locked:false,Visible:true} layer){CanvasStatus.Text="Select one visible, unlocked decal to erase or restore.";return;}
            ClearMaskBrush();maskPreparing=true;var epoch=maskEpoch;activationEpoch=epoch;var sourceDocument=document;var project=draft with {};var model=mesh;var before=JsonSerializer.Serialize(document,LayerDocument.Json);
            CanvasStatus.Text="Preparing editable stencil…";maskCancellation=new();var token=maskCancellation.Token;
            var sourceImage=await Task.Run(()=>FinishMaps.Read(layer.Asset,token),token);
            var brush=await Task.Run(()=>{var value=new DecalMaskPainting(sourceImage.Width,sourceImage.Height,layer.Effects,token);value.AttachArtwork(sourceImage);return value;},token);
            var width=painter!.Image.Width;var height=painter.Image.Height;
            var fast=document.Layers.LastOrDefault(l=>l.Visible)?.Id==layer.Id&&await Task.Run(()=>DecalMaskPainting.CanPreviewSingleProjection(model,layer,width,height,token),token);
            CompositeMaps? under=null;
            if(fast){var snapshot=document.Snapshot("Under selected artwork",document.Layers.Where(l=>l.Id!=layer.Id).ToArray());under=await Task.Run(()=>snapshot.Compose(model,project,token),token);}
            if(epoch!=maskEpoch)return;
            if(!ReferenceEquals(document,sourceDocument)||before!=JsonSerializer.Serialize(document,LayerDocument.Json)){ClearMaskBrush();CanvasStatus.Text="The design changed. Select the decal and choose Erase / restore again.";return;}
            maskBrush=brush;maskArtworkOriginal=sourceImage;maskBrushLayer=layer;maskBrushDocument=sourceDocument;maskDocumentState=before;maskUnderMaps=under;maskPreparing=false;
            UploadMaskArtwork();Select(8);ShowInspector();UploadTexture();UploadSelection();Render();CanvasStatus.Text="Drag to erase this decal · Restore reveals its original artwork · Each stroke has one Undo";
        }
        catch(OperationCanceledException){}
        catch(Exception x){ClearMaskBrush();Fail(x);}
        finally{if(activationEpoch==maskEpoch)maskPreparing=false;}
    }
    private void ClearMaskBrush()
    {
        maskEpoch++;maskCancellation?.Cancel();maskCancellation?.Dispose();maskCancellation=null;
        maskBrush=null;maskArtworkOriginal=null;maskBrushLayer=null;maskBrushDocument=null;maskStrokeBefore=null;maskLastPoint=null;maskUnderMaps=maskLiveMaps=null;
        maskPainting=maskChanged=maskPreparing=maskPreviewRequested=maskPreviewBusy=maskUploadDirty=false;if(MaskBrushOverlay is not null)MaskBrushOverlay.Visibility=Visibility.Collapsed;
    }
    private bool BeginMaskStroke()
    {
        if(maskPreparing||maskCommitting||maskPreviewBusy||maskBrush is null||maskBrushLayer is null||!ReferenceEquals(document,maskBrushDocument))return false;
        if(SelectedLayer?.Id!=maskBrushLayer.Id||SelectedLayers.Count!=1||maskDocumentState!=JsonSerializer.Serialize(document,LayerDocument.Json))
        {ClearMaskBrush();Select(6);CanvasStatus.Text="The design changed. Select a decal and choose Erase / restore again.";return false;}
        maskStrokeBefore=new(maskBrush.Stencil.Width,maskBrush.Stencil.Height,(byte[])maskBrush.Stencil.Pixels.Clone());maskPainting=true;maskChanged=false;maskLastPoint=null;
        maskRestore=MaskBrushAction.SelectedIndex==1;maskStrength=(float)ToolOpacity.Value;maskHardness=(float)BrushHardness.Value;maskWorldRadius=(float)ToolSize.Value*.5f;PaintMaskAtHover();return true;
    }
    private void PaintMaskAtHover(bool preview=true)
    {
        if(!maskPainting||maskBrush is null||maskBrushLayer is null||mesh is null||painter is null)return;
        var point=hover is {} hit?DecalMaskPainting.SourcePoint(mesh,maskBrushLayer,hit,painter.Image.Width,painter.Image.Height,maskRestore?maskArtworkOriginal:maskBrush.Artwork):null;
        if(point is not {} at){maskLastPoint=null;return;}
        var radius=new Vector2(maskWorldRadius/at.Projection.Width,maskWorldRadius/at.Projection.Height);
        var count=maskLastPoint is {} previous&&previous.Sample==at.Sample&&previous.Mirrored==at.Mirrored?maskBrush.Line(previous.UV,at.UV,radius,maskHardness,maskStrength,maskRestore):maskBrush.Dab(at.UV,radius,maskHardness,maskStrength,maskRestore);
        maskLastPoint=at;if(count==0)return;maskChanged=maskUploadDirty=true;if(preview)UpdateMaskPreview();
    }
    private void UpdateMaskPreview(){if(!maskUploadDirty)return;maskUploadDirty=false;if(maskUnderMaps is not null)UploadMaskArtwork();else RequestMaskPreview();}
    private void UploadMaskArtwork()
    {
        if(renderer!=0&&maskBrush?.Artwork is {} artwork&&maskUnderMaps is not null)Check(ls_texture(renderer,artwork.Pixels,(uint)artwork.Width,(uint)artwork.Height,1));
    }
    private void RequestMaskPreview()
    {
        // One worker displays completed frames in order and coalesces newer
        // samples. Cancellation advances the revision so a discarded stroke
        // can never reappear; continuous drawing does not starve the preview.
        maskPreviewRequested=true;if(!maskPreviewBusy)_=ComposeMaskPreview();
    }
    private async Task ComposeMaskPreview()
    {
        if(maskBrush is null||maskBrushLayer is null||document is null||mesh is null||draft is null||maskCancellation is null)return;
        maskPreviewBusy=true;var epoch=maskEpoch;var token=maskCancellation.Token;var previewFile=Path.Combine(WorkshopService.DefaultWorkspace,"mask-previews",Guid.NewGuid().ToString("N")+".tga");
        try
        {
            while(maskPreviewRequested&&!token.IsCancellationRequested&&epoch==maskEpoch&&maskBrush is not null&&maskBrushLayer is not null)
            {
                maskPreviewRequested=false;var revision=maskPreviewRevision;var stencil=new PaintImage(maskBrush.Stencil.Width,maskBrush.Stencil.Height,(byte[])maskBrush.Stencil.Pixels.Clone());var model=mesh;var project=draft with {};var id=maskBrushLayer.Id;var blend=maskBrushLayer.Effects?.Blend??DecalBlend.Normal;var snapshot=document.Snapshot("Freehand preview",document.Layers);
                var maps=await Task.Run(()=>{Directory.CreateDirectory(Path.GetDirectoryName(previewFile)!);FinishMaps.WriteTga(stencil,previewFile);snapshot.Commit("Temporary stencil",layers=>layers.Single(l=>l.Id==id).Effects=DecalMaskPainting.Recipe(previewFile,blend));return snapshot.Compose(model,project,token);},token);
                if(epoch!=maskEpoch||token.IsCancellationRequested)return;if(revision!=maskPreviewRevision)continue;maskLiveMaps=maps;UploadTexture();UploadStructure();Render();
            }
        }
        catch(OperationCanceledException){}
        catch(Exception x){if(epoch==maskEpoch)Fail(x);}
        finally{if(epoch==maskEpoch)maskPreviewBusy=false;}
    }
    private void EndMaskStroke(bool cancel=false)
    {
        if(!maskPainting)return;maskPainting=false;maskLastPoint=null;
        if(cancel)
        {
            maskPreviewRevision++;maskLiveMaps=null;
            if(maskStrokeBefore is {} before&&maskArtworkOriginal is {} original){maskBrush=new(before);maskBrush.AttachArtwork(original);if(maskUnderMaps is not null)UploadMaskArtwork();else RequestMaskPreview();}
            maskStrokeBefore=null;maskChanged=false;UploadTexture();UploadStructure();CanvasStatus.Text="Stroke canceled · original artwork and history are unchanged.";Render();return;
        }
        maskStrokeBefore=null;if(!maskChanged)return;maskChanged=false;maskCommitting=true;SetMaskControlsBusy(true);CanvasStatus.Text="Saving stroke…";saving=SaveMaskStroke();
    }
    private async Task SaveMaskStroke()
    {
        var epoch=maskEpoch;
        try
        {
            if(maskBrush is null||maskBrushLayer is null||document is null||!ReferenceEquals(document,maskBrushDocument)||maskDocumentState!=JsonSerializer.Serialize(document,LayerDocument.Json))throw new InvalidOperationException("The design changed before the stroke could be saved.");
            var brush=maskBrush;var sourceDocument=document;var before=maskDocumentState;var id=maskBrushLayer.Id;var blend=maskBrushLayer.Effects?.Blend??DecalBlend.Normal;var label=(maskRestore?"Restored ":"Erased ")+maskBrushLayer.Name;var file=await Task.Run(()=>brush.Save(WorkshopService.DefaultWorkspace));
            if(!ReferenceEquals(document,sourceDocument)||before!=JsonSerializer.Serialize(document,LayerDocument.Json))throw new InvalidOperationException("The design changed before the stroke could be saved.");
            document.Commit(label,layers=>layers.Single(l=>l.Id==id).Effects=DecalMaskPainting.Recipe(file,blend));
            if(epoch==maskEpoch){maskBrushLayer=document.Layers.Single(l=>l.Id==id);maskDocumentState=JsonSerializer.Serialize(document,LayerDocument.Json);maskLiveMaps=null;maskCancellation?.Cancel();maskCancellation?.Dispose();maskCancellation=new();}
            QueueComposite();await saving;
            if(epoch==maskEpoch){UploadMaskArtwork();Render();CanvasStatus.Text="Saved stroke · Ctrl + Z restores it · Original artwork stays intact";}
        }
        catch(Exception x){ClearMaskBrush();UploadTexture();UploadStructure();Fail(x);throw;}
        finally{maskCommitting=false;SetMaskControlsBusy(false);}
    }
    private void SetMaskControlsBusy(bool busy)
    {
        if(busy)
        {
            var controls=new List<Control>();
            void Collect(DependencyObject node){if(node is Control control){controls.Add(control);return;}for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Collect(VisualTreeHelper.GetChild(node,i));}
            Collect(InspectorTabs);maskDisabledControls=controls.ToDictionary(control=>control,control=>control.IsEnabled);foreach(var control in controls)control.IsEnabled=false;
        }
        else if(maskDisabledControls is {} previous){foreach(var (control,enabled) in previous)control.IsEnabled=enabled;maskDisabledControls=null;RefreshLayers();}
    }
    private void DrawMaskCursor()
    {
        MaskBrushOverlay.Visibility=Visibility.Collapsed;
        if(mode!=8||hover is not {} hit||maskBrushLayer is null||mesh is null||painter is null||DecalMaskPainting.SourcePoint(mesh,maskBrushLayer,hit,painter.Image.Width,painter.Image.Height,MaskBrushAction.SelectedIndex==1?maskArtworkOriginal:maskBrush?.Artwork) is not {} sourcePoint)return;
        var radius=maskPainting?maskWorldRadius:(float)ToolSize.Value*.5f;var points=new PointCollection();var matrix=Matrix;
        for(var i=0;i<40;i++){var angle=i*MathF.PI/20;var position=hit.Position+(sourcePoint.Projection.Right*MathF.Cos(angle)+sourcePoint.Projection.Up*MathF.Sin(angle))*radius;if(!DecalEditing.Project(position,matrix,(float)Viewport.ActualWidth,(float)Viewport.ActualHeight,out var at))return;points.Add(new(at.X,at.Y));}
        MaskBrushRing.Points=points;MaskBrushOverlay.Clip=new RectangleGeometry{Rect=new(0,0,Viewport.ActualWidth,Viewport.ActualHeight)};MaskBrushOverlay.Visibility=Visibility.Visible;
    }
    private bool RenderMaskArtwork(float[] values)
    {
        if(mode!=8||maskUnderMaps is null||maskBrushLayer is not {} layer)return false;
        var p=layer.Transform(layer.Samples[0]);
        void V(int offset,Vector3 v,float w){values[offset]=v.X;values[offset+1]=v.Y;values[offset+2]=v.Z;values[offset+3]=w;}
        V(28,p.Center,p.Width);V(32,p.Right,p.Height);V(36,p.Up,layer.Opacity);V(40,p.Normal,2);V(44,Vector3.One,(float)(layer.Effects?.Blend??DecalBlend.Normal));V(68,Vector3.Zero,layer.Mirror?1:0);V(72,new(p.MinimumFacing,p.DepthRange,p.FlipHorizontal?1:0),p.FlipVertical?1:0);return true;
    }
}
