using System.Numerics;
using System.Runtime.InteropServices;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LiveryStudio;
public sealed partial class CarPaintView : UserControl,IDisposable
{
    private nint renderer;
    private PaintMesh? mesh;
    private int meshCarIndex=-1;
    private TexturePainter? painter;
    private PaintImage? sticker,structure,originalPaintMask,strokeSurfaceBefore;
    private LiveryProject? draft;
    private SurfaceHit? hover;
    private float yaw=.65f,pitch=.22f,distance=7;
    private Vector3 target;
    private Vector2 last;
    private bool dragging,painting,panning;
    private int mode,generation;
    private string source="",destination="";
    private Task saving=Task.CompletedTask;
    private readonly SemaphoreSlim saveGate=new(1,1);
    public event Action<string>? Failure;
    public event Action<LiveryProject>? FinishEdited;
    private bool updatingFinish;
    public CarPaintView(){InitializeComponent();FinishPicker.ItemsSource=FinishPreset.All;InitializeEditorControls();InitializeSimpleStudio();SyncToolWorkflow();}
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern nint ls_create(nint panel);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern void ls_destroy(nint renderer);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern nint ls_error();
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_mesh(nint r,float[] p,uint count);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_texture(nint r,byte[] p,uint w,uint h,int sticker);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_render_ex(nint r,float[] p,uint floats,uint w,uint h);
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_snapshot(nint r,[Out]byte[] pixels,uint capacity);
    private static void Check(int result){if(result!=0)throw new InvalidOperationException(Marshal.PtrToStringAnsi(ls_error())??"3D preview failed.");}
    private void ViewportLoaded(object sender,RoutedEventArgs e)
    {
        try {if(renderer!=0)return;var abi=WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(Viewport);try{renderer=ls_create(abi);}finally{Marshal.Release(abi);}if(renderer==0)Check(-1);if(mesh is not null)UploadMesh();Render();}catch(Exception x){Fail(x);}
    }
    public async Task LoadAsync(LiveryProject project,string? modelOverride=null)
    {
        if(project.IndependentPaintSides&&modelOverride is null)throw new InvalidOperationException("Open the saved car project in Car Builder to load its matching independent paint model.");
        historyWarmCancellation?.Cancel();CancelSimplePreview();EndSimpleCompare();simpleAssetsCancellation?.Cancel();CancelArtworkCollection();CancelImagePreparation();CancelArtworkPointPick();await FlushAsync();stickerRequestRevision++;stickerLoading=false;ClearMaskBrush();if(mode==8)Select(0);colorPickApply=null;if(mode==7)Select(0);ClearGroupHover();ClearDirectPreview();directSelection=null;selectedLayers.Clear();pendingGroup=null;pickImages.Clear();pickImageBytes=0;var version=++generation;ResetCreativeEditing();draft=project;SyncFinish();var car=project.SelectedCar;VehicleTitle.Text=car.Name+" · "+car.Category;
        CanvasStatus.Text="Loading model, layers and original game materials…";
        try {
            var loadedMesh=await Task.Run(()=>PaintMesh.Read(modelOverride??Path.Combine(AppContext.BaseDirectory,"Models",car.Index.ToString("00")+".flsm")));if(version!=generation)return;var cameraPoints=await Task.Run(()=>StudioCamera.Geometry(loadedMesh));if(version!=generation)return;mesh=loadedMesh;meshCarIndex=car.Index;carCameraPoints=cameraPoints;
            if(project.EditorFile.Length>0){document=LayerDocument.Read(project.EditorFile);editorPath=project.EditorFile;}
            else{var folder=Path.Combine(WorkshopService.DefaultWorkspace,"drafts",Guid.NewGuid().ToString("N"));document=LayerDocument.Create(project,folder);editorPath=Path.Combine(folder,"editor.json");document.Save(editorPath);project.EditorFile=editorPath;MapsSaved?.Invoke(project.Albedo,project.Structure,editorPath);}
            compositionCache.Clear();DesignPalette.EnsureProjectHistory(document,project);DesignPalette.SyncManualPaint(document,project);draft=DesignPalette.ResolvePaint(document,project);SyncSharedPaintHistory();project=draft!;var sourceDocument=document;var sourceMesh=mesh;var loaded=await Task.Run(()=>{SimplePlacement.UnsharedExterior(sourceMesh);return (Maps:sourceDocument.Compose(sourceMesh,project,cache:compositionCache),Mask:FinishMaps.Read(sourceDocument.BaseAlbedo));});if(version!=generation)return;var maps=loaded.Maps;originalPaintMask=loaded.Mask;
            painter=new(maps.Albedo);structure=maps.Structure;source=project.Albedo;destination="";Tag=car.Index.ToString();yaw=.65f;pitch=.22f;orthographic=false;previousCamera=null;previousFitPoints=null;FrameCar(remember:false,render:false);hover=null;panelMask=[];selectedLayer=document.Layers.LastOrDefault()?.Id;
            uploadedCleanPaint=null;if(!AdvancedMode&&!previewOnly)Select(6);if(renderer!=0){UploadMesh();UploadSelection();}RefreshLayers();await LoadPreviewMaps(car.Index,version);NormalEnabled.IsOn=!project.FlatExteriorNormals;NormalEnabled.IsEnabled=!project.FlatExteriorNormals;CanvasStatus.Text=$"Ready · {document.Layers.Count} editable layers";Render();_ = WarmUndoNeighbor();_ = RefreshSimpleAssets();
        }catch(Exception x){Fail(x);throw;}
    }
    private void UploadMesh(){Check(ls_mesh(renderer,mesh!.NativeVertices,(uint)mesh.Vertices.Length));UploadTexture();UploadStructure();uploadedCleanPaint=null;UploadPaintFoundation();}
    private bool PreviewHasMaterial=>simpleCompareMaterial??simplePreview?.Document.HasMaterial??(inspectionMaps is not null?inspectionHasMaterial:document?.HasMaterial==true||stroke?.IsMaterial==true||(groupHoverActive&&pendingGroup?.Any(l=>l.Visible&&l.IsMaterial)==true));
    private void UploadStructure(){var image=SimpleDisplayMaps?.Structure??(mode==8?maskLiveMaps?.Structure??maskUnderMaps?.Structure??structure:inspectionMaps?.Structure??structure);if(renderer!=0&&image is not null)Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,2));}
    private void UploadTexture(){var image=SimpleDisplayMaps?.Albedo??MaskDisplayImage??inspectionMaps?.Albedo??painter?.Image;if(renderer!=0&&image is not null)Check(ls_texture(renderer,image.Pixels,(uint)image.Width,(uint)image.Height,0));}
    public void FinishChanged(LiveryProject project)
    {
        // A restored draft can belong to another car while the studio is hidden.
        // Its maps must wait for that car's mesh instead of using the old geometry.
        if(meshCarIndex!=project.CarIndex){mesh=null;painter=null;structure=null;}
        try{_=PaintFinish.Color(project.OuterColor);_=PaintFinish.Color(project.InnerColor);}catch(ArgumentException){CanvasStatus.Text="Enter six hex digits for each paint color.";return;}
        if(document is not null&&(document.CarIndex!=project.CarIndex||project.EditorFile!=editorPath))document=null;
        if(document is null&&project.EditorFile.Length>0&&File.Exists(project.EditorFile))
        {
            try{var cached=LayerDocument.Read(project.EditorFile);if(cached.CarIndex==project.CarIndex){document=cached;editorPath=project.EditorFile;}}catch(Exception x){Failure?.Invoke(x.Message);}
        }
        var changed=draft is {} previous&&(previous.InnerColor!=project.InnerColor||previous.OuterColor!=project.OuterColor||previous.InnerGloss!=project.InnerGloss||previous.Metalness!=project.Metalness||previous.UseSrgbPaintColors!=project.UseSrgbPaintColors||previous.OverrideArtworkFinish!=project.OverrideArtworkFinish||previous.ArtworkGloss!=project.ArtworkGloss||previous.ArtworkMetalness!=project.ArtworkMetalness);
        var historyChanged=false;if(!updatingSharedPaint&&document is not null){var cursor=document.Cursor;DesignPalette.SyncManualPaint(document,project);historyChanged=cursor!=document.Cursor;if(historyChanged&&mesh is null)document.Save(editorPath);}
        draft=project;SyncFinish();if((changed||historyChanged)&&!updatingSharedPaint&&document is not null)QueueComposite();else Render();
    }
    public async Task RebuildEditableMapsAsync(LiveryProject project)
    {
        if(project.EditorFile.Length==0)return;
        if(mesh is null||document is null||document.CarIndex!=project.CarIndex||editorPath!=project.EditorFile)await LoadAsync(project);else FinishChanged(project);
        QueueComposite();await FlushAsync();
    }

    private Vector3 Eye => CameraPose.Eye;
    private Matrix4x4 Matrix => CameraPose.Matrix(CameraAspect);
    private Vector3 CameraRight=>CameraPose.Right;
    private SurfaceProjection? BaseStampProjection=>hover is {} hit?mode==3&&SelectedLayer is {} layer?MovedProjection(hit,layer):SurfaceProjection.At(hit,CameraRight,(float)ToolSize.Value,mode==2&&sticker is not null?(float)sticker.Width/sticker.Height:1,0,mode==2&&AdvancedMode&&ToolAlignment.SelectedIndex==1?Vector3.Normalize(Eye-hit.Position):null):null;
    private SurfaceProjection? StampProjection=>BaseStampProjection is {} p?PlacementSettings().Transform(p):null;
    private static Vector3 Color(string hex){_ = LiveryProject.ColorVector(hex);return new(Convert.ToByte(hex.Substring(1,2),16)/255f,Convert.ToByte(hex.Substring(3,2),16)/255f,Convert.ToByte(hex.Substring(5,2),16)/255f);}
    private void Render()
    {
        if(renderer==0 || mesh is null || Viewport.ActualWidth<1 || Viewport.ActualHeight<1 || draft is null)return;
        var shown=SimpleDisplayProject??draft;try{
            UpdateGroupHover();var m=Matrix;var values=new float[76];float[] matrix=[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];matrix.CopyTo(values,0);
            void V(int offset,Vector3 v,float w){values[offset]=v.X;values[offset+1]=v.Y;values[offset+2]=v.Z;values[offset+3]=w;}
            V(16,Eye,0);V(20,PaintFinish.PaintColor(shown,shown.OuterColor),(float)shown.OuterGloss);V(24,PaintFinish.PaintColor(shown,shown.InnerColor),PreviewHasMaterial?1:0);
            V(48,new((float)shown.Metalness,(float)shown.ClearCoat,(float)shown.InnerGloss),shown.OverrideArtworkFinish&&!PreviewHasMaterial?1:0);
            V(52,new((float)shown.ArtworkGloss,(float)shown.ArtworkMetalness,(float)PreviewExposure.Value),(PreviewEnvironment.SelectedItem as PreviewChoice)?.Mode??0);
            V(56,new((float)PreviewLight.Value*MathF.PI/180,(float)NormalStrength.Value,(float)DirtAmount.Value),(float)DamageAmount.Value);
            V(60,new((float)shown.DamageGloss,(float)shown.DamageMetalness,NormalEnabled.IsOn?1:0),actualProbe?1:0);
            var live=directPreviewReady?directLayer:null;var projectedLayer=live??(mode==8&&maskUnderMaps is not null?maskBrushLayer:mode==3?SelectedLayer:null);
            var previewLayer=projectedLayer??(mode is 1 or 2 or 4?PlacementSettings() with {Mirror=MirrorPaint.IsChecked==true}:null);
            var previewProjection=live is not null?live.Transform(live.Samples[directSelection!.Sample]):StampProjection;
            var isolated=previewLayer is not null&&(previewLayer.ExteriorOnly||previewLayer.ProtectSharedUvs)&&previewProjection is not null;
            if(isolated)UploadTriangleMask(SimplePlacement.Mask(mesh,previewLayer!,previewProjection!.Value.Normal,includeMirror:true));else UploadSelection();
            V(64,new(actualProbe?.6f:1,sunStrength,isolated?1:projectedLayer is {} selected?selected.Triangles.Length>0?1:0:AdvancedMode&&PanelOnly.IsChecked==true&&panelMask.Length>0?1:0),PlacementHardness);
            V(68,new(PaintChannel.SelectedIndex==1?0:PaintChannel.SelectedIndex==2?1:2,(float)BrushGloss.Value,(float)BrushMetal.Value),(projectedLayer?.Mirror??(MirrorPaint.IsChecked==true))?1:0);
            if(RenderMaskArtwork(values)){}
            else if(live is not null){var p=live.Transform(live.Samples[directSelection!.Sample]);V(28,p.Center,p.Width);V(32,p.Right,p.Height);V(36,p.Up,live.Opacity);V(40,p.Normal,2);V(72,new(p.MinimumFacing,p.DepthRange,p.FlipHorizontal?1:0),p.FlipVertical?1:0);}
            else if(!(simplePaintEraser)&&!painting && StampProjection is {} p && mode is 1 or 2 or 3 or 4 && (mode is not (2 or 3)||sticker is not null) && !(mode==2&&pendingGroup is not null)){V(28,p.Center,p.Width);V(32,p.Right,p.Height);V(36,p.Up,mode==3?SelectedLayer!.Opacity:PlacementOpacity);V(40,p.Normal,mode);V(44,Color(BrushHex.Text),(float)((mode==3?SelectedLayer?.Effects:mode==2?pendingEffects:null)?.Blend??DecalBlend.Normal));V(72,new(p.MinimumFacing,p.DepthRange,p.FlipHorizontal?1:0),p.FlipVertical?1:0);}
            var pixels=GetRenderSize();Check(ls_render_ex(renderer,values,(uint)values.Length,pixels.Width,pixels.Height));DrawDirectSelection();DrawMaskCursor();DrawSimpleEraser();
        }catch(Exception x){Fail(x);}
    }
    private SurfaceHit? Hit(Vector2 point)
    {
        if(mesh is null || !Matrix4x4.Invert(Matrix,out var inverse))return null;
        var x=point.X/(float)Viewport.ActualWidth*2-1;var y=1-point.Y/(float)Viewport.ActualHeight*2;
        var near=Vector4.Transform(new Vector4(x,y,0,1),inverse);var far=Vector4.Transform(new Vector4(x,y,1,1),inverse);
        var a=new Vector3(near.X,near.Y,near.Z)/near.W;var b=new Vector3(far.X,far.Y,far.Z)/far.W;return mesh.Raycast(a,b-a);
    }
    private void PointerDown(object sender,PointerRoutedEventArgs e)
    {
        if(mesh is null || painter is null)return;Focus(FocusState.Pointer);var p=e.GetCurrentPoint(Viewport);last=new((float)p.Position.X,(float)p.Position.Y);hover=Hit(last);panning=p.Properties.IsMiddleButtonPressed;
        if(panning||p.Properties.IsRightButtonPressed||mode==0||ImageReviewActive){CancelArtworkPointPick(keepArmed:true);dragging=true;Viewport.CapturePointer(e.Pointer);e.Handled=true;return;}
        if(SimplePreviewActive||simpleCompareHeld){dragging=true;Viewport.CapturePointer(e.Pointer);e.Handled=true;return;}
        if(recomposing||maskCommitting||maskPreparing||stickerLoading)return;
        if(simplePaintEraser&&mode==1&&(simpleEraserUnder is null||document is null||!ReferenceEquals(simpleEraserState,document.History[document.Cursor]))){_ = PrepareSimpleEraser();return;}
        if(mode==8&&p.Properties.IsLeftButtonPressed){if(BeginMaskStroke())Viewport.CapturePointer(e.Pointer);e.Handled=true;Render();return;}
        if(mode==7&&p.Properties.IsLeftButtonPressed){if(hover is {} colorHit)SampleDesignColor(colorHit);e.Handled=true;return;}
        if(mode==2&&pendingGroup is not null&&p.Properties.IsLeftButtonPressed){if(hover is {} groupHit)StampArtworkGroup(groupHit);e.Handled=true;return;}
        if(mode==6&&p.Properties.IsLeftButtonPressed){if(chooseArtworkAtPoint||IsDown(Windows.System.VirtualKey.Shift))ChooseArtworkAtPoint(hover,last);else{CancelArtworkPointPick();if(!TryDirectHandle(last,e))PickArtwork(hover,last,e);if(!AdvancedMode&&directSelection is null){dragging=true;Viewport.CapturePointer(e.Pointer);}}e.Handled=true;return;}
        if(mode==5&&p.Properties.IsLeftButtonPressed){if(hover is {} hit)PickPanel(hit);e.Handled=true;return;}
        if(mode==3&&p.Properties.IsLeftButtonPressed){if(hover is {} hit)MoveSelected(hit);e.Handled=true;return;}
        dragging=panning||p.Properties.IsRightButtonPressed||mode==0;painting=!dragging&&p.Properties.IsLeftButtonPressed;
        if(painting){if(AdvancedMode&&PanelOnly.IsChecked==true&&panelMask.Length==0){CanvasStatus.Text="Pick a surface before using panel-only painting.";painting=false;return;}try{_ = Color(BrushHex.Text);}catch(Exception x){Fail(x);painting=false;return;}try{stroke=NewStroke();}catch(Exception x){Fail(x);painting=false;return;}strokeChanged=false;strokeSurfaceBefore=null;if(stroke.IsMaterial&&document?.HasMaterial!=true&&structure is not null&&originalPaintMask is not null&&draft is not null){strokeSurfaceBefore=structure;structure=new(structure.Width,structure.Height,(byte[])structure.Pixels.Clone());FinishMaps.InitializePaintSurface(structure,originalPaintMask,draft);}painter.Begin();Stamp();if(mode==2)hover=null;}
        Viewport.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void PointerMove(object sender,PointerRoutedEventArgs e)
    {
        if(mesh is null)return;var p=e.GetCurrentPoint(Viewport);var at=new Vector2((float)p.Position.X,(float)p.Position.Y);var delta=at-last;
        if(directDragging){UpdateDirectEdit(at,e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift));last=at;e.Handled=true;return;}
        if(dragging){if(panning){cameraFitPoints=null;var units=2*distance*MathF.Tan(StudioCamera.FieldOfView*.5f)/(float)Math.Max(1,Viewport.ActualHeight);target-=CameraRight*delta.X*units;target+=CameraPose.Up*delta.Y*units;}else{yaw=MathF.IEEERemainder(yaw-delta.X*.009f,2*MathF.PI);pitch=Math.Clamp(pitch+delta.Y*.009f,-MathF.PI/2,MathF.PI/2);RefitCamera();}hover=null;}
        else{
            // Sample the screen path so quick brush strokes have no gaps.
            if(maskPainting){var steps=Math.Clamp((int)(delta.Length()/4)+1,1,64);for(var i=1;i<=steps;i++){hover=Hit(Vector2.Lerp(last,at,(float)i/steps));PaintMaskAtHover(preview:false);}UpdateMaskPreview();}
            else if(painting&&mode is 1 or 4){var steps=Math.Clamp((int)(delta.Length()/4)+1,1,64);for(var i=1;i<=steps;i++){hover=Hit(Vector2.Lerp(last,at,(float)i/steps));Stamp(false);}UploadTexture();if(mode==4)UploadStructure();}
            else hover=Hit(at);
            if(!painting&&hover is {} sharedHit){var shared=MeshEditing.SharedUvCount(mesh,sharedHit);UvWarning.Text=shared>0?$"Shared UV: this position also affects {shared} other surface(s). Texture sharing cannot be isolated by a panel mask.":"";}
            if(!painting&&!maskPainting)CanvasStatus.Text=ImageReviewActive?"Drag to orbit · scroll to zoom · Escape returns to settings":mode==8?"Drag on the selected decal to erase or restore · Right-drag to orbit":mode==7?ColorSampleReadout():mode==6?"Click artwork to select · drag its handles · right-drag to orbit":hover is null?"Move over a body panel to paint or place a sticker.":mode==2?"Sticker preview · click to stamp":mode==1?"Brush ready · drag to paint":"Drag to orbit · scroll to zoom";
            if((!AdvancedMode||ProtectSharedParts.IsChecked==true)&&!painting&&mode is 1 or 2 or 4&&hover is {} protectedHit&&!SimplePlacement.UnsharedExterior(mesh).Contains(protectedHit.Triangle))CanvasStatus.Text=AdvancedMode?"Linked parts protected · turn off Protect shared parts to paint them together":"Linked parts protected · Advanced mode can paint these parts together";
        }
        last=at;Render();e.Handled=true;
    }
    private void Stamp(bool upload=true)
    {
        if(painter is null||mesh is null||draft is null||stroke is null||BaseStampProjection is not {} placement||mode==0||(mode==2&&sticker is null))return;
        var p=stroke.Transform(placement);stroke.Samples.Add(placement);var mask=stroke.Triangles.Length>0?stroke.Triangles.ToHashSet():null;var paintColor=PaintFinish.ToSrgb(PaintFinish.PaintColor(draft,draft.InnerColor));
        void Apply(SurfaceProjection projection){var targetMask=SimplePlacement.Mask(mesh,stroke,projection.Normal);var count=painter.Stamp(mesh,projection,mode==2?sticker:null,Color(BrushHex.Text),stroke.Opacity,paintColor,targetMask,simplePaintEraser?1:stroke.Hardness,markSurface:stroke.IsMaterial,blend:stroke.Effects?.Blend??DecalBlend.Normal,restore:simplePaintEraser?simpleEraserUnder?.Albedo:null);if(stroke.IsMaterial&&structure is not null){var surface=new TexturePainter(structure);count+=surface.Stamp(mesh,projection,null,new(stroke.Gloss,stroke.Metalness,0),stroke.Opacity,paintColor,targetMask,stroke.Hardness,material:stroke.Kind);structure=surface.Image;}strokeChanged|=count>0;}
        Apply(p);if(stroke.Mirror)Apply(PaintLayer.Reflected(p));if(upload){UploadTexture();UploadStructure();}Render();
    }
    private void PointerUp(object sender,PointerRoutedEventArgs e){if(directDragging)FinishDirectEdit();else EndStroke();Viewport.ReleasePointerCapture(e.Pointer);e.Handled=true;}
    private void PointerLost(object sender,PointerRoutedEventArgs e){if(directDragging)FinishDirectEdit();else EndStroke();}
    private void EndStroke()
    {
        dragging=panning=painting=false;if(maskPainting){EndMaskStroke();Render();return;}painter?.Commit();
        if(stroke is {} finished&&strokeChanged&&document is not null)
        {
            if(simplePaintEraser&&finished.Kind==LayerKind.Color)
            {
                var dabs=finished.Samples.Select(finished.Transform).SelectMany(p=>finished.Mirror?new[]{p,PaintLayer.Reflected(p)}:new[]{p}).ToArray();
                document.Commit("Erased paint",layers=>{foreach(var layer in layers.Where(l=>l.Kind==LayerKind.Color&&!l.Locked))layer.Erasures.AddRange(dabs);});
            }
            else{document.Commit("Added "+finished.Name,layers=>layers.Add(finished));selectedLayer=finished.Id;selectedLayers.Clear();selectedLayers.Add(finished.Id);if(finished.Kind==LayerKind.Color)RememberColors(finished.Color);}
            stroke=null;QueueComposite();if((!AdvancedMode||advancedQuickTools)&&finished.Kind==LayerKind.Sticker){if(!SimpleTray.KeepStamping){Select(6);SimpleTray.SetExpanded(false);}else CanvasStatus.Text="Click to stamp again · Edit last sticker to move it";}
        }
        else{stroke=null;if(strokeSurfaceBefore is not null){structure=strokeSurfaceBefore;UploadStructure();}}
        strokeSurfaceBefore=null;Render();
    }
    private void PointerExit(object sender,PointerRoutedEventArgs e){if(!dragging&&!painting&&!directDragging){hover=null;maskLastPoint=null;Render();}}
    private void PointerWheel(object sender,PointerRoutedEventArgs e){CancelArtworkPointPick(keepArmed:true);var point=e.GetCurrentPoint(Viewport);var delta=point.Properties.MouseWheelDelta;if((mode is 1 or 2 or 4 or 8)&&e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)){ToolSize.Value=Math.Clamp(ToolSize.Value*Math.Exp(delta*.001),ToolSize.Minimum,ToolSize.Maximum);}else if(mode==2&&e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift)){StickerRotation.Value=Math.Clamp(StickerRotation.Value+delta/120d*5,-180,180);}else{var anchor=Hit(new((float)point.Position.X,(float)point.Position.Y))?.Position;cameraFitPoints=null;ApplyCamera(StudioCamera.Zoom(CameraPose,MathF.Exp(-delta*.001f),anchor));}UpdatePlacementReadout();Render();e.Handled=true;}
    private void ViewportSizeChanged(object sender,SizeChangedEventArgs e){RefitCamera();Render();}
    private void Select(int next){if(previewOnly)next=0;if(ImageReviewActive)return;CancelArtworkPointPick();if(stickerLoading&&next!=2){stickerRequestRevision++;stickerLoading=false;}if(next!=8&&(maskBrush is not null||maskPreparing)){EndMaskStroke(cancel:true);ClearMaskBrush();UploadTexture();UploadStructure();}if(next!=0)ClearLayerInspection();FinishDirectEdit();if(next!=7)colorPickApply=null;if(next==1&&AdvancedMode&&PaintChannel.SelectedIndex>0)next=4;if(next!=1)simplePaintEraser=false;mode=next;OrbitTool.IsChecked=next==0;BrushTool.IsChecked=next is 1 or 4 or 8;BrushTool.Content=next==8||next==1&&simplePaintEraser?"Erase":"Paint";StickerTool.IsChecked=next is 2 or 3;if(SelectTool is not null)SelectTool.IsChecked=next==6;hover=null;if(next is 0 or 1 or 2 or 4 or 8)InspectorSection.SelectedIndex=0;else if(next==6)InspectorSection.SelectedIndex=1;SyncToolWorkflow();if(PaintInspector.IsLoaded)PaintInspector.ChangeView(null,0,null,true);CanvasStatus.Text=next==6?"Click artwork on the car · drag to move · use its resize and rotate handles":next==2?(sticker is null?"Choose artwork, then hover over a body panel.":"Hover to preview. Click to add an editable sticker layer."):next is 1 or 4?"Drag on the car to paint. Right-drag to orbit.":next==3?"Click a new location for the selected sticker.":"Drag to orbit. Scroll to zoom.";UploadSelection();Render();}
    private void SelectOrbit(object s,RoutedEventArgs e){Select(0);HideInspector();}private void SelectBrush(object s,RoutedEventArgs e){simplePaintEraser=false;Select(1);ShowInspector();}private void SelectSticker(object s,RoutedEventArgs e){Select(2);ShowInspector();if(!AdvancedMode&&sticker is null)ImportArtworkFile(s,e);}
    private void BrushChanged(object s,TextChangedEventArgs e)
    {
        if(Viewport is null)return;
        if(BeginnerBrushSwatch is not null){try{BeginnerBrushSwatch.Fill=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,Convert.ToByte(BrushHex.Text.Substring(1,2),16),Convert.ToByte(BrushHex.Text.Substring(3,2),16),Convert.ToByte(BrushHex.Text.Substring(5,2),16)));}catch{}}
        Render();
    }
    private void SettingsChanged(object s,RangeBaseValueChangedEventArgs e){if(Viewport is not null){UpdatePlacementReadout();Render();}}
    private void SyncFinish()
    {
        if(draft is null)return;updatingFinish=true;
        try{FaceGloss.Value=draft.InnerGloss;QuickMetalness.Value=draft.Metalness;QuickClearCoat.Value=draft.ClearCoat;QuickArtwork.IsOn=draft.OverrideArtworkFinish;QuickArtworkGloss.Value=draft.ArtworkGloss;QuickArtworkMetal.Value=draft.ArtworkMetalness;QuickArtworkGloss.IsEnabled=QuickArtworkMetal.IsEnabled=draft.OverrideArtworkFinish;FinishPicker.SelectedItem=FinishPreset.All.FirstOrDefault(p=>p.OuterGloss==draft.OuterGloss&&p.InnerGloss==draft.InnerGloss&&p.Metalness==draft.Metalness&&p.ClearCoat==draft.ClearCoat);}finally{updatingFinish=false;}
    }
    private void PickFinish(object sender,SelectionChangedEventArgs e){if(!updatingFinish&&draft is not null&&FinishPicker.SelectedItem is FinishPreset p)FinishEdited?.Invoke(p.Apply(draft));}
    private void LiveFinishChanged(object sender,RangeBaseValueChangedEventArgs e)=>PublishFinish();
    private void LiveArtworkChanged(object sender,RoutedEventArgs e)=>PublishFinish();
    private void PublishFinish(){if(updatingFinish||draft is null)return;FinishEdited?.Invoke(draft with {InnerGloss=FaceGloss.Value,Metalness=QuickMetalness.Value,ClearCoat=QuickClearCoat.Value,OverrideArtworkFinish=QuickArtwork.IsOn,ArtworkGloss=QuickArtworkGloss.Value,ArtworkMetalness=QuickArtworkMetal.Value});}
    private void EnvironmentChanged(object sender,SelectionChangedEventArgs e){if(Viewport is not null)ApplyEnvironment();}
    public Window? HostWindow {get;set;}
    private void Undo(object s,RoutedEventArgs e)=>StepHistory(false);
    private void Redo(object s,RoutedEventArgs e)=>StepHistory(true);
    private void StepHistory(bool redo)
    {
        var started=System.Diagnostics.Stopwatch.GetTimestamp();historyRequestStarted=0;
        if(previewOnly||ImageReviewActive||maskCommitting||CancelSimplePreview())return;EndSimpleCompare();
        if(maskPainting){EndMaskStroke(cancel:true);return;}if(painting)EndStroke();if(mode==8)Select(6);FinishDirectEdit(cancel:true);ClearDirectPreview();transformTimer?.Stop();CommitTransform();
        if((redo?document?.Redo():document?.Undo())!=true)return;
        historyRequestStarted=started;historyRequestName=redo?"Redo":"Undo";hover=null;SyncSharedPaintHistory();SyncTextHistory();SyncPatternHistory();QueueComposite(history:true);
    }
    private void Preset(object s,RoutedEventArgs e)
    {
        if(mesh is null||s is not FrameworkElement element)return;RememberCamera();var view=element.Tag as string;pitch=view=="top"?MathF.PI/2:view=="reset"?.22f:0;yaw=view switch{"front" or "top"=>0,"rear"=>MathF.PI,"left"=>-MathF.PI/2,"right"=>MathF.PI/2,_=>.65f};FrameCar(false);HideInspector();hover=null;CanvasStatus.Text=(view=="reset"||view is null?"Three-quarter":char.ToUpperInvariant(view[0])+view[1..])+" view · "+(orthographic?"flat working projection":"perspective projection");Render();
    }
    public async Task FlushAsync(){if(SimplePreviewActive)CancelSimplePreview();EndSimpleCompare();CancelArtworkCollection();if(ImageReviewActive)CancelImagePreparation();await simplePending;await FlushAsync(false);}
    private async Task FlushAsync(bool preserveInspection){if(!preserveInspection)ClearLayerInspection();FinishDirectEdit();transformTimer?.Stop();CommitTransform();EndStroke();await saving;await saveGate.WaitAsync();saveGate.Release();}
    public void RefreshControlState(){RefreshLayers();SyncFinish();}
    private void Fail(Exception x){CanvasStatus.Text=x.Message;Failure?.Invoke(x.ToString());}
    public void Dispose(){historyWarmCancellation?.Cancel();simpleAssetsCancellation?.Cancel();simpleCancellation?.Cancel();compareRevision++;compositionCache.Clear();CancelArtworkCollection();CancelImagePreparation();CancelArtworkPointPick();repeatLifetime?.Cancel();photoLifetime?.Cancel();ClearMaskBrush();ClearLayerInspection();DisposePatternPreview();ClearGroupHover();directPreparation++;directPreviewTimer?.Stop();directCancellation?.Cancel();directCancellation?.Dispose();designColorsRevision++;stickerRequestRevision++;artworkRevision++;textRevision++;textPreviewTimer?.Stop();compositeCancellation?.Cancel();compositeCancellation?.Dispose();transformTimer?.Stop();if(renderer!=0){ls_destroy(renderer);renderer=0;}}
}
