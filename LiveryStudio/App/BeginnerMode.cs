using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;


namespace LiveryStudio;


public sealed partial class CarPaintView
{
    public bool AdvancedMode { get; private set; }
    private float PlacementOpacity => AdvancedMode&&!simplePaintEraser ? (float)ToolOpacity.Value : 1;
    private float PlacementHardness => simplePaintEraser?1:AdvancedMode ? (float)BrushHardness.Value : .85f;

    public void SetAdvancedMode(bool advanced)
    {
        if(BeginnerWelcome is null)return;
        EndStroke();FinishDirectEdit();transformTimer?.Stop();CommitTransform();
        AdvancedMode=advanced;
        CancelSimplePreview();EndSimpleCompare();if(!advanced&&mode==0)Select(6);
        if(!advanced&&mode is 3 or 4 or 5 or 8)Select(0);
        if(!advanced&&InspectorSection.SelectedIndex>1)InspectorSection.SelectedIndex=0;
        SyncToolWorkflow();SyncBeginnerSelection();if(PaintInspector.IsLoaded)PaintInspector.ChangeView(null,0,null,true);
        if(IsLoaded)UpdatePaintLayout(new(ActualWidth,ActualHeight));UploadSelection();Render();if(!advanced)_=RefreshSimpleAssets();
    }

    private static Visibility ModeVisible(bool value)=>value?Visibility.Visible:Visibility.Collapsed;
    private void SyncBeginnerTools()
    {
        if(BeginnerWelcome is null)return;
        foreach(var control in new FrameworkElement[]{AdvancedAddButton,CameraToolsButton,AdvancedLayersButton,InspectorSection,BrushSettings,AdvancedBrushColor,DesignColorsButton,SampleBrushColor,PrepareStickerButton,ArtworkLibraryButton,DecalProjectionSettings,BrushEdgeSettings,SurfaceSeamSettings,AdvancedOpacity,ToolOpacity,ProtectSharedParts})
            control.Visibility=ModeVisible(AdvancedMode);
        // Context still matters in Advanced mode; the parent panels hide unrelated controls.
        BrushSettings.Visibility=ModeVisible(AdvancedMode&&mode is 1 or 4);
        DecalProjectionSettings.Visibility=ModeVisible(AdvancedMode&&mode is 2 or 3);
        BrushEdgeSettings.Visibility=ModeVisible(AdvancedMode&&mode is 1 or 4 or 8);
        if(simplePaintEraser){BrushSettings.Visibility=AdvancedBrushColor.Visibility=BrushEdgeSettings.Visibility=AdvancedOpacity.Visibility=ToolOpacity.Visibility=Visibility.Collapsed;}
        ProtectSharedParts.IsEnabled=PanelOnly.IsEnabled=!simplePaintEraser;
        SurfaceSeamSettings.Visibility=ModeVisible(AdvancedMode&&mode!=8);
        BeginnerWelcome.Visibility=ModeVisible(!AdvancedMode&&mode==0);
        BeginnerColor.Visibility=ModeVisible(!AdvancedMode);
        PlacementReadout.Visibility=ModeVisible(AdvancedMode);
        BeginnerAngles.Visibility=ModeVisible(!AdvancedMode);
        ViewSettings.Visibility=ModeVisible(AdvancedMode&&mode==0);
        MirrorPaint.Visibility=ModeVisible(mode is 1 or 2 or 3 or 4);
        MirrorPaint.Content=AdvancedMode?"Mirror across the car":"Both sides";
        BeginnerPairHelp.Visibility=ModeVisible(!AdvancedMode&&mode is 1 or 2);
        ChooseStickerButton.Content=AdvancedMode?"Choose PNG, TGA or SVG…":"Choose a PNG sticker…";
        ToolSizeCaption.Text=mode is 1 or 4 or 8?"Brush size":"Sticker size";
        StickerTool.Content=AdvancedMode?"Decal":"Sticker";
        SelectTool.Content=AdvancedMode?"Select":"Edit";
        LayerSelectionActions.Visibility=ModeVisible(AdvancedMode&&SelectedLayers.Count>0);
        BeginnerSelection.Visibility=ModeVisible(!AdvancedMode&&SelectedLayers.Count>0);
        InspectorToggle.Content=AdvancedMode?"Studio tools":"Tools";
        if(!AdvancedMode)
        {
            if(mode==0){ToolTitle.Text="Make this car yours";ToolDescription.Text="Paint with a brush or add a sticker. You can undo every change.";}
            if(mode==2){ToolTitle.Text="Add a sticker";ToolDescription.Text=sticker is null?"Choose a PNG image. Its transparent background stays clear.":"Move over the car to preview your sticker, then click to place it. Choose Edit to move or resize it.";}
            if(mode==6){ToolTitle.Text="Edit your artwork";ToolDescription.Text="Click a sticker on the car. Drag it to move; use its handles to resize or rotate.";}
        }
        SyncSimpleSelection();
        if(!AdvancedMode)CanvasHelp.Visibility=CanvasHeading.Visibility=QuickTools.Visibility=Visibility.Collapsed;
    }

    private void SyncBeginnerSelection()
    {
        if(BeginnerSelection is null)return;
        var layers=SelectedLayers;
        BeginnerSelection.Visibility=ModeVisible(!AdvancedMode&&layers.Count>0);
        LayerSelectionActions.Visibility=ModeVisible(AdvancedMode&&layers.Count>0);
        BeginnerRemove.IsEnabled=layers.Count>0&&!layers.Any(l=>l.Locked);
        BeginnerDuplicate.IsEnabled=layers.Count>0;
        BeginnerOtherSide.IsEnabled=layers.Count>0&&!layers.Any(l=>l.Mirror&&l.Locked);
        BeginnerOtherSide.Content=layers.Any(l=>l.Mirror)?"Edit each side separately…":"Copy to the other side…";
        BeginnerUnlock.Visibility=ModeVisible(layers.Any(l=>l.Locked));
        BeginnerSelectionHelp.Text=layers.Any(l=>l.Locked)?"This artwork is locked. Unlock it to move, resize or remove it.":"Drag the artwork to move it. Drag ⤢ to resize and ↻ to rotate. Undo brings it back.";
        SyncSimpleSelection();
    }

    private void BeginnerPaint(object sender,RoutedEventArgs e){Select(1);ShowInspector();}
    private void BeginnerUnlockArtwork(object sender,RoutedEventArgs e)
        =>EditSelection("Unlocked artwork",(layers,ids)=>{foreach(var layer in layers.Where(l=>ids.Contains(l.Id)))layer.Locked=false;},allowLocked:true);
    private void BeginnerSticker(object sender,RoutedEventArgs e){Select(2);ShowInspector();ImportArtworkFile(sender,e);}
    private void BeginnerSwatch(object sender,RoutedEventArgs e)
    {
        BrushHex.Text=(string)((FrameworkElement)sender).Tag;RememberColors(BrushHex.Text);
    }
    private async void BeginnerColorPicker(object sender,RoutedEventArgs e)
    {
        try{await PickArtworkColor(BrushHex,"Brush color");}catch(Exception x){Fail(x);}
    }
    private async void BeginnerBodyColor(object sender,RoutedEventArgs e)
    {
        if(draft is null)return;
        try
        {
            var current=draft;var version=generation;
            var initial=DesignColor.PaintPickerHex(current,current.InnerColor);
            var choice=await StudioColorDialog.ChooseAsync(XamlRoot,initial,"Car paint color");
            if(version!=generation||draft is null)return;
            async void Apply(string hex){if(version!=generation||draft is null)return;try{if(!AdvancedMode||advancedQuickTools){await ApplySimpleChange((d,p,c)=>SimpleDesigns.Body(d,p,hex));return;}var field=DesignColor.PaintFieldHex(draft,hex);FinishEdited?.Invoke(draft with {InnerColor=field,OuterColor=field});}catch(Exception x){Fail(x);}}
            if(choice is {SampleFromCar:true})BeginColorSampling("car paint",Apply);
            else if(choice?.Hex is {} hex)Apply(hex);
        }
        catch(Exception x){Fail(x);}
    }
}


public partial class MainWindow
{
    private void EditorModeChanged(object sender,RoutedEventArgs e)
    {
        ApplyEditorMode();SaveSession();
    }
    private void ApplyEditorMode()
    {
        var advanced=AdvancedModeToggle.IsChecked==true;
        Painter.SetAdvancedMode(advanced);
        DesignNav.Visibility=CarBuilderNav.Visibility=DesignSettingsButton.Visibility=advanced?Visibility.Visible:Visibility.Collapsed;
        BakeButton.Content=advanced?"Bake livery":"Build for game";
        if(currentWorkspace==3)PageTitle.Text=advanced?"Every angle. Every detail.":"Paint your car";
        AdvancedModeToggle.Content=advanced?"Simple mode":"Advanced mode";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AdvancedModeToggle,advanced?"Switch to Simple mode":"Switch to Advanced mode");
        ToolTipService.SetToolTip(AdvancedModeToggle,advanced?"Switch to Simple mode for painting and stickers":"Switch to Advanced mode for all tools and settings");
        if(!advanced&&currentWorkspace==0&&Root.IsLoaded)ShowPaint(this,new());
        SyncSimpleShell();
    }
}
