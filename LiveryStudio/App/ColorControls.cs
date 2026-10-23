using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private Action<string>? colorPickApply;
    private string colorPickLabel="";
    private int colorPickReturnMode;
    public void BeginColorSampling(string label,Action<string> apply)
    {
        if(mesh is null||painter is null||draft is null)throw new InvalidOperationException("Open a car in the 3D studio before sampling a color.");
        EndStroke();colorPickReturnMode=mode==7?colorPickReturnMode:mode;colorPickLabel=label;colorPickApply=apply;Select(7);HideInspector();Focus(FocusState.Programmatic);CanvasStatus.Text="Pick "+label+" · click a body panel · Escape cancels.";
    }
    private void SampleDesignColor(SurfaceHit hit)
    {
        if(painter is null||draft is null||colorPickApply is null)return;
        try{var hex=DesignColor.Sample(draft,painter.Image,hit,Eye);var apply=colorPickApply;var label=colorPickLabel;colorPickApply=null;Select(colorPickReturnMode);apply(hex);RememberColors(hex);CanvasStatus.Text="Sampled "+hex+" for "+label+" · design color, without lighting or weather.";}
        catch(Exception x){Fail(x);}
    }
    private string ColorSampleReadout()=>hover is {} hit&&painter is not null&&draft is not null?"Pick "+DesignColor.Sample(draft,painter.Image,hit,Eye)+" for "+colorPickLabel+" · click to use · Escape cancels":"Pick "+colorPickLabel+" · hover over a body panel · Escape cancels";
    private void PickBrushFromCar(object sender,RoutedEventArgs e){try{BeginColorSampling("brush / shape",hex=>BrushHex.Text=hex);}catch(Exception x){Fail(x);}}
    private void ColorSampleAccelerator(KeyboardAccelerator sender,KeyboardAcceleratorInvokedEventArgs e){if(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is TextBox)return;try{BeginColorSampling("brush / shape",hex=>BrushHex.Text=hex);}catch(Exception x){Fail(x);}e.Handled=true;}
    private static void RememberColors(params string[] colors){_ = Task.Run(()=>{try{ColorLibrary.Use(WorkshopService.DefaultWorkspace,colors);}catch{/* Optional color history does not block artwork edits. */}});}
    private async Task PickArtworkColor(TextBox field,string title)
    {
        var choice=await StudioColorDialog.ChooseAsync(XamlRoot,field.Text,title);if(choice is {SampleFromCar:true})BeginColorSampling(title.ToLowerInvariant(),hex=>field.Text=hex);else if(choice?.Hex is {} hex)field.Text=hex;
    }
}

public partial class MainWindow
{
    private async void PickPaintColor(object sender,RoutedEventArgs e)
    {
        var edge=(sender as FrameworkElement)?.Tag as string=="outer";var field=edge?OuterHex:InnerHex;var title=edge?"Edge paint color":"Face paint color";
        try{var initial=DesignColor.PaintPickerHex(ReadDraft(),field.Text);var choice=await StudioColorDialog.ChooseAsync(Root.XamlRoot,initial,title);
            if(choice is {SampleFromCar:true}){await OpenPaintStudio();Painter.BeginColorSampling(title.ToLowerInvariant(),hex=>field.Text=DesignColor.PaintFieldHex(ReadDraft(),hex));}
            else if(choice?.Hex is {} hex&&hex!=initial)field.Text=DesignColor.PaintFieldHex(ReadDraft(),hex);}
        catch(Exception x){Error(x);}
    }
}
