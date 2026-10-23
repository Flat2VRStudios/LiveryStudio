using System.Runtime.InteropServices;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private PreviewQualitySettings previewQuality=ReadPreviewQuality();
    private bool qualityEventsAttached;
    [DllImport("LiveryPreview",CallingConvention=CallingConvention.Cdecl)]private static extern int ls_quality(nint renderer,float scaleX,float scaleY,uint anisotropy);
    private static PreviewQualitySettings ReadPreviewQuality()
    {
        try{return PreviewQualitySettings.Read(WorkshopService.DefaultWorkspace);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException){return new();}
    }
    public PreviewQualitySettings PreviewQuality=>previewQuality;
    public void SetPreviewQuality(PreviewQualitySettings settings)
    {
        settings.Validate();previewQuality=settings;Render();
    }
    private PreviewRenderSize GetRenderSize()
    {
        if(!qualityEventsAttached)
        {
            qualityEventsAttached=true;
            Viewport.CompositionScaleChanged+=(_,_)=>Render();
        }
        var size=previewQuality.RenderSize(Viewport.ActualWidth,Viewport.ActualHeight,Viewport.CompositionScaleX,Viewport.CompositionScaleY);
        // A physical-pixel buffer needs the inverse scale so its image and the
        // logical XAML hit targets occupy exactly the same panel rectangle.
        if(renderer!=0)Check(ls_quality(renderer,(float)(Viewport.ActualWidth/size.Width),(float)(Viewport.ActualHeight/size.Height),(uint)previewQuality.Filtering));
        return size;
    }
    public PreviewRenderSize PreviewPixelSize=>previewQuality.RenderSize(Math.Max(1,Viewport.ActualWidth),Math.Max(1,Viewport.ActualHeight),Viewport.CompositionScaleX,Viewport.CompositionScaleY);
}

public partial class MainWindow
{
    private async void ShowQualitySettings(object sender,RoutedEventArgs e)
    {
        var before=Painter.PreviewQuality;
        var quality=new ComboBox{Header="Preview quality",HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=new[]{"Low · faster · 75% resolution","Balanced · sharp · 100% resolution","High · finer edges · 125% resolution"},SelectedIndex=(int)before.Quality};
        var filtering=new ComboBox{Header="Texture detail",HorizontalAlignment=HorizontalAlignment.Stretch,ItemsSource=new[]{"Standard","Sharp · 4× filtering","Sharper · 8× filtering","Maximum · 16× filtering"}};
        PreviewFiltering[] filters=[PreviewFiltering.Standard,PreviewFiltering.Sharp,PreviewFiltering.Sharper,PreviewFiltering.Maximum];filtering.SelectedIndex=Array.IndexOf(filters,before.Filtering);
        AutomationProperties.SetName(quality,"Preview quality");AutomationProperties.SetName(filtering,"Preview texture detail");
        var size=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        PreviewQualitySettings Read()=>new(){Quality=(PreviewQuality)Math.Max(0,quality.SelectedIndex),Filtering=filters[Math.Max(0,filtering.SelectedIndex)]};
        void Update()
        {
            try
            {
                var selected=Read();Painter.SetPreviewQuality(selected);var pixels=Painter.PreviewPixelSize;
                size.Text=$"Current preview: {pixels.Width:N0} × {pixels.Height:N0} pixels. Resolution follows your display scale, up to 4K.";status.Text="";
            }
            catch(Exception x){status.Text=x.Message;}
        }
        quality.SelectionChanged+=(_,_)=>{filtering.SelectedIndex=Array.IndexOf(filters,PreviewQualitySettings.Preset((PreviewQuality)Math.Max(0,quality.SelectedIndex)).Filtering);Update();};
        filtering.SelectionChanged+=(_,_)=>Update();
        var body=new StackPanel{Spacing=16,Width=Math.Clamp(Root.ActualWidth-100,220,430)};
        body.Children.Add(new TextBlock{Text="Sharper car details while you paint. Choose Low if turning the car feels slow.",TextWrapping=TextWrapping.Wrap});body.Children.Add(quality);body.Children.Add(filtering);body.Children.Add(size);
        body.Children.Add(new TextBlock{Text="These settings apply to the studio preview. Your saved artwork, game textures and photo exports keep their full resolution.",TextWrapping=TextWrapping.Wrap,FontSize=12});body.Children.Add(status);
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Display & quality",Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(Root.ActualHeight-230,120,520)},PrimaryButtonText="Save settings",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
        dialog.PrimaryButtonClick+=(_,args)=>{try{Read().Save(WorkshopService.DefaultWorkspace);}catch(Exception x){status.Text=x.Message;args.Cancel=true;}};
        try
        {
            Update();if(await dialog.ShowAsync()!=ContentDialogResult.Primary)Painter.SetPreviewQuality(before);
            else Status.Text="Display settings saved.";
        }
        catch(Exception x){Painter.SetPreviewQuality(before);Error(x);}
    }
}
