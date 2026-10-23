using System.Numerics;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
	private PaintLayer PlacementSettings()
	{
		if(mode==3&&SelectedLayer is {} selected)return selected with {OffsetRight=0,OffsetUp=0};
		var layer=new PaintLayer{ExteriorOnly=ProtectSharedParts.IsChecked==true,ProtectSharedUvs=ProtectSharedParts.IsChecked==true};
		if(!AdvancedMode||simplePaintEraser)return SimplePlacement.OnSurface(layer) with {Rotation=mode==2?(float)StickerRotation.Value:0};
		if(mode==2){layer.Rotation=(float)StickerRotation.Value;layer.ScaleX=(float)ToolWidthScale.Value;layer.ScaleY=(float)ToolHeightScale.Value;layer.FlipHorizontal=ToolFlipHorizontal.IsChecked==true;layer.FlipVertical=ToolFlipVertical.IsChecked==true;layer.AlignWithCamera=ToolAlignment.SelectedIndex==1;layer.WrapAngle=double.IsFinite(ToolWrapAngle.Value)?(float)ToolWrapAngle.Value:75.52249f;layer.WrapDepth=double.IsFinite(ToolWrapDepth.Value)?(float)ToolWrapDepth.Value:.35f;}
		return layer;
	}
	private SurfaceProjection MovedProjection(SurfaceHit hit,PaintLayer layer)
	{
		var first=layer.Samples[0];return SurfaceProjection.At(hit,first.Right,first.Width,first.Width/first.Height,0,layer.AlignWithCamera?Vector3.Normalize(Eye-hit.Position):null);
	}
	private void UpdatePlacementReadout()
	{
		if(PlacementReadout is null||ToolHeightScale is null)return;if(!AdvancedMode){PlacementReadout.Text=mode is 1 or 4?"Drag on the car to paint. Right-drag to look around.":"Click to place · Edit to move or resize · Undo to remove";return;}if(mode is 1 or 4 or 8){PlacementReadout.Text=$"{ToolSize.Value:0.00} m brush · Ctrl + wheel resizes";return;}var width=ToolSize.Value*ToolWidthScale.Value;var height=ToolSize.Value*ToolHeightScale.Value/(sticker is null?1:(double)sticker.Width/sticker.Height);
		PlacementReadout.Text=$"{width:0.00} m wide · {height:0.00} m high · {StickerRotation.Value:0}°\nCtrl + wheel resizes · Shift + wheel rotates";
	}
	private void ToolProjectionChanged(object sender,SelectionChangedEventArgs e){if(ToolWrapDepth is not null){UpdatePlacementReadout();Render();}}
	private void ToolWrapValueChanged(NumberBox sender,NumberBoxValueChangedEventArgs e){if(ToolWrapDepth is not null&&double.IsFinite(e.NewValue))Render();}
	private void WrapPresetChanged(object sender,SelectionChangedEventArgs e)
	{
		if(ToolWrapAngle is not null&&ToolWrapDepth is not null)ApplyWrapPreset(ToolWrapPreset.SelectedIndex);
	}
	private void ApplyWrapPreset(int preset)
	{
		ToolWrapAngle.Value=preset switch{0=>55,2 or 3=>85,_=>75.52249};ToolWrapDepth.Value=preset switch{0=>.08,2=>.75,3=>1.25,_=>.35};
		if(preset==3)ToolAlignment.SelectedIndex=1;Render();
	}
	private void ResetPlacement(object sender,RoutedEventArgs e){ToolWidthScale.Value=ToolHeightScale.Value=1;StickerRotation.Value=0;ToolFlipHorizontal.IsChecked=ToolFlipVertical.IsChecked=false;ToolAlignment.SelectedIndex=0;ToolWrapPreset.SelectedIndex=1;ApplyWrapPreset(1);UpdatePlacementReadout();Render();}
	private void ShowArtwork(object sender,RoutedEventArgs e){InspectorSection.SelectedIndex=4;if(floatingInspector)ShowInspector();}
	private void SelectAccelerated(int tool,KeyboardAcceleratorInvokedEventArgs e){if(FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;if(tool==1)simplePaintEraser=false;Select(tool);if(tool==6)SelectTool.Focus(FocusState.Programmatic);e.Handled=true;}
	private void ViewAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e)=>SelectAccelerated(0,e);
	private void BrushAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e)=>SelectAccelerated(1,e);
	private void DecalAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e)=>SelectAccelerated(2,e);
	private void LayerWrapChanged(NumberBox sender,NumberBoxValueChangedEventArgs e){if(!updatingLayers&&double.IsFinite(e.NewValue)&&SelectedLayer is not null){transformTimer?.Stop();transformTimer?.Start();}}
	private void LayerFlipChanged(object sender,RoutedEventArgs e){transformTimer?.Stop();CommitTransform();}
	private void ClearLayerIsolation(object sender,RoutedEventArgs e)=>EditSelected("Allowed layer across panels",layer=>{layer.Triangles=[];layer.ExteriorOnly=false;layer.ProtectSharedUvs=false;});
}
