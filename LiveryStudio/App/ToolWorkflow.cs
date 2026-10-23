using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private void ShowLayers(object sender,RoutedEventArgs e)
    {
        InspectorSection.SelectedIndex=1;
        ShowLayerWorkspace(0);
        ShowInspector();
    }

    private void LayerWorkspaceChanged(object sender,RoutedEventArgs e)=>ShowLayerWorkspace(int.Parse((string)((FrameworkElement)sender).Tag));
    private void ShowLayerBrowser(object sender,RoutedEventArgs e)=>ShowLayerWorkspace(0);
    private void OpenLayerSelection(object sender,RoutedEventArgs e)=>ShowLayerWorkspace(1);
    private void ShowLayerWorkspace(int page)
    {
        if(LayerHistoryPane is null)return;
        LayerBrowseTab.IsChecked=page==0;LayerEditTab.IsChecked=page==1;LayerHistoryTab.IsChecked=page==2;
        LayerBrowserPane.Visibility=page==0?Visibility.Visible:Visibility.Collapsed;
        LayerSelectionPane.Visibility=page==1?Visibility.Visible:Visibility.Collapsed;
        LayerHistoryPane.Visibility=page==2?Visibility.Visible:Visibility.Collapsed;
        if(page==1)LayerSelectionPane.ChangeView(null,0,null,true);
        SyncInspectorCommands();
        SyncBeginnerSelection();
    }
    private void SyncInspectorCommands()
    {
        if(InspectorUndoRow is not null)InspectorUndoRow.Visibility=AdvancedMode&&floatingInspector&&InspectorSection.SelectedIndex==1?Visibility.Collapsed:Visibility.Visible;
        if(CompactHistoryCommands is not null)CompactHistoryCommands.Visibility=AdvancedMode&&floatingInspector?Visibility.Visible:Visibility.Collapsed;
    }
    private void SyncLayerWorkspace()
    {
        if(LayerSelectionActions is null)return;
        var count=SelectedLayers.Count;
        EditSelectionButton.IsEnabled=count>0;
        EditSelectionButton.Content=count==0?"Choose artwork to edit":count==1?"Edit selected artwork":$"Edit {count} selected layers";
        LayerSelectionActions.Visibility=count>0?Visibility.Visible:Visibility.Collapsed;
        LayerSelectionEmpty.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
        LayerTransformSection.Visibility=count==1?Visibility.Visible:Visibility.Collapsed;
        SyncBeginnerSelection();
        ToolTipService.SetToolTip(SelectionSummary,SelectionSummary.Text);
    }

    private void SyncToolWorkflow()
    {
        if(ToolTitle is null)return;
        var brush=mode is 1 or 4;
        var decal=mode is 2 or 3;
        static Visibility Visible(bool value)=>value?Visibility.Visible:Visibility.Collapsed;
        BrushSettings.Visibility=Visible(brush);
        BrushColorSettings.Visibility=Visible(mode==1);
        MaskBrushSettings.Visibility=Visible(mode==8);
        ToolOpacityCaption.Text=mode==8?"Strength":"Opacity";
        DecalArtworkSettings.Visibility=Visible(decal);
        StickerPreview.Visibility=Visible(sticker is not null);
        UpdatePlacementReadout();
        PlacementSettingsPanel.Visibility=Visible(brush||decal||mode==8);
        DecalRotationSettings.Visibility=DecalProjectionSettings.Visibility=Visible(decal);
        BrushEdgeSettings.Visibility=Visible(brush||mode==8);
        MirrorPaint.Visibility=SurfaceSeamSettings.Visibility=Visible(mode!=8);
        ViewSettings.Visibility=Visible(mode==0);
        SurfaceBrushSettings.Visibility=Visible(mode==4);
        ToolTitle.Text=mode switch{1=>"Paint the body",4=>"Paint the finish",2=>"Place a decal",3=>"Move your decal",6=>"Select artwork",5=>"Choose a surface",7=>"Pick a color",8=>"Erase or restore a decal",_=>"Explore your design"};
        ToolDescription.Text=mode switch
        {
            1=>"Choose a color and brush size, then draw on the car. Each stroke stays editable.",
            4=>"Brush gloss or metalness onto a panel. Preview the finish your livery will use.",
            2=>sticker is null?"Choose an image or browse the library. Hover over the car to preview it, then click to place.":"Hover to preview, then click to place. Use Select to adjust it afterwards.",
            3=>"Click a new position on the car for the selected decal.",
            6=>"Click artwork to select it. Shift-click to choose covered decals. Drag to move; use handles to resize and rotate.",
            5=>"Click a panel to keep your painting inside that surface.",
            7=>"Click the car to sample its design color. Escape cancels.",
            8=>"Brush on the selected decal. Its original stays intact. Copies in this layer, including paired sides, share your changes.",
            _=>"Drag to look around. Choose Paint, Decal or Add when you’re ready to create."
        };
        SyncBeginnerTools();
        CanvasHelpText.Text=mode switch
        {
            1 or 4=>"Drag to paint · Right-drag to orbit · Ctrl + wheel to resize · Ctrl + Z to undo",
            2=>"Hover to preview · Click to place · Ctrl + wheel to resize · Shift + wheel to rotate",
            3=>"Click to move the decal · Right-drag to orbit",
            6=>"Click to select · Shift-click chooses covered decals · Drag to move",
            5=>"Click a body panel to isolate it · Right-drag to orbit",
            7=>"Click to sample a color · Escape to cancel",
            8=>"Drag to erase or restore · Right-drag to orbit · Ctrl + wheel resizes · Escape cancels the current stroke",
            _=>"Drag to orbit · Scroll to zoom · Middle-drag to pan"
        };
    }
}
