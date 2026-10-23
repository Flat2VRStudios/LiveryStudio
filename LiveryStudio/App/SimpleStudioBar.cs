using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;

public sealed class SimpleStudioBar:UserControl
{
    public event Action<string,object?>? Requested;
    private readonly StackPanel tabs=new(){Orientation=Orientation.Horizontal,Spacing=5};
    private readonly Grid body=new();
    private readonly ScrollViewer trayScroller=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=180};
    private bool initialSizeApplied;
    private double previousAvailableHeight=double.PositiveInfinity;
    private readonly Dictionary<string,ToggleButton> choices=[];
    private readonly Dictionary<StarterKind,Image> lookImages=[];
    private readonly Dictionary<string,Image> stickerImages=[];
    private readonly StackPanel lookShelf=new(){Orientation=Orientation.Horizontal,Spacing=8};
    private readonly StackPanel stickerShelf=new(){Orientation=Orientation.Horizontal,Spacing=8};
    private readonly ToggleButton collapse=new(){Content="Hide",Padding=new(9,5,9,5)};
    private readonly TextBlock hint=new(){Text="Pick a look. Make it yours.",FontSize=12,VerticalAlignment=VerticalAlignment.Center};
    private readonly CheckBox repeat=new(){Content="Keep stamping",IsChecked=true,FontSize=12};
    private readonly CheckBox paired=new(){Content="Both doors",FontSize=12};
    private readonly Slider size=new(){Minimum=.04,Maximum=2.5,Value=.45,Width=160,StepFrequency=.02};
    private readonly Slider rotation=new(){Minimum=-180,Maximum=180,Value=0,Width=130,StepFrequency=5};
    private readonly StackPanel sectionHost=new(){Spacing=8};
    private bool synchronizing;
    public string Section {get;private set;}="Looks";
    public bool KeepStamping=>repeat.IsChecked==true;
    public bool BothDoors=>paired.IsChecked==true;
    public double BrushSize=>size.Value;
    public bool Expanded=>body.Visibility==Visibility.Visible;
    public SimpleStudioBar()
    {
        var root=new Grid{RowSpacing=9};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{ColumnSpacing=10};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        foreach(var name in new[]{"Looks","Colors","Stickers","Paint"})
        {
            var button=new ToggleButton{Content=name,Padding=new(15,7,15,7),FontSize=14,IsChecked=name==Section};AutomationProperties.SetName(button,name+" tray");button.Click+=(_,_)=>ShowSection(name);choices.Add(name,button);tabs.Children.Add(button);
        }
        header.Children.Add(tabs);var right=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        right.Children.Add(Action("Arrange","arrange"));right.Children.Add(Action("Photo / Spin","photo"));collapse.Click+=(_,_)=>SetExpanded(!Expanded);right.Children.Add(collapse);Grid.SetColumn(right,1);header.Children.Add(right);root.Children.Add(header);
        trayScroller.Content=sectionHost;body.Children.Add(trayScroller);Grid.SetRow(body,1);root.Children.Add(body);
        var border=new Border{Background=(Brush)Application.Current.Resources["StudioCardBrush"],BorderBrush=(Brush)Application.Current.Resources["StudioBorderBrush"],BorderThickness=new(1),CornerRadius=new(12),Padding=new(12,10,12,9),Child=root};Content=border;
        BuildLooks();BuildStickers();ShowSection(Section,false);SizeChanged+=(_,e)=>{var compact=e.NewSize.Width<650;foreach(var b in choices.Values){b.Padding=new(compact?9:15,7,compact?9:15,7);b.FontSize=compact?12:14;}right.Children[0].Visibility=compact?Visibility.Collapsed:Visibility.Visible;((Button)right.Children[1]).Content=compact?"Photo":"Photo / Spin";};
        repeat.Click+=(_,_)=>Requested?.Invoke("repeat",KeepStamping);paired.Click+=(_,_)=>Requested?.Invoke("paired",BothDoors);
        AutomationProperties.SetName(size,"Simple brush or sticker size");AutomationProperties.SetName(rotation,"Simple sticker rotation");
        size.ValueChanged+=(_,e)=>{if(!synchronizing)Requested?.Invoke("size",e.NewValue);};rotation.ValueChanged+=(_,e)=>{if(!synchronizing)Requested?.Invoke("rotation",e.NewValue);};
    }
    private Button Action(string text,string command,object? payload=null){var button=new Button{Content=text,Padding=new(10,7,10,7)};AutomationProperties.SetName(button,text);button.Click+=(_,_)=>Requested?.Invoke(command,payload);return button;}
    private static StackPanel Row()=>new(){Orientation=Orientation.Horizontal,Spacing=7};
    private static Brush Swatch(string hex)=>new SolidColorBrush(StudioColorDialog.Parse(hex));
    private static ScrollViewer Scroll(UIElement content)=>new(){Content=content,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
    private void BuildLooks()
    {
        foreach(var style in DesignStarters.Styles)
        {
            var image=new Image{Width=156,Height=72,Stretch=Stretch.Uniform};lookImages[style.Kind]=image;
            var fallback=new Grid{Width=156,Height=72,Background=Swatch(style.Base)};fallback.Children.Add(new Border{Width=15,Background=Swatch(style.Accent),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(-28,0,0,0)});fallback.Children.Add(new Border{Width=10,Background=Swatch(style.Ink),HorizontalAlignment=HorizontalAlignment.Center,Margin=new(20,0,0,0)});fallback.Children.Add(image);
            var content=new StackPanel{Spacing=4};content.Children.Add(fallback);content.Children.Add(new TextBlock{Text=style.Name,FontSize=12});var button=Action(style.Name,"look",style.Kind);button.Content=content;button.Padding=new(6);ToolTipService.SetToolTip(button,style.Description);lookShelf.Children.Add(button);
        }
    }
    private void BuildStickers()
    {
        stickerShelf.Children.Add(Action("Your image…","import"));stickerShelf.Children.Add(Action("Number / name","number"));
        foreach(var name in FunArtwork.Shapes.Keys)
        {
            var image=new Image{Width=44,Height=44,Stretch=Stretch.Uniform};stickerImages[name]=image;var content=new StackPanel{Spacing=4};content.Children.Add(image);content.Children.Add(new TextBlock{Text=name,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center});var button=Action(name,"sticker",name);button.Content=content;button.Width=78;stickerShelf.Children.Add(button);
        }
        stickerShelf.Children.Add(Action("Recent artwork","recent"));
    }
    public void ShowSection(string section,bool notify=true)
    {
        Section=section;paired.Content=section=="Paint"?"Both sides":"Both doors";foreach(var element in new FrameworkElement[]{lookShelf,stickerShelf,hint,repeat,paired,size,rotation})if(element.Parent is Panel parent)parent.Children.Remove(element);foreach(var (name,button) in choices)button.IsChecked=name==section;sectionHost.Children.Clear();SetExpanded(true);
        if(section=="Looks")
        {
            sectionHost.Children.Add(lookShelf);
            var row=Row();row.Children.Add(Action("Shuffle colors","shuffle"));row.Children.Add(Action("Surprise me","surprise"));row.Children.Add(Action("Clean paint","clean"));row.Children.Add(hint);sectionHost.Children.Add(row);
        }
        else if(section=="Colors")
        {
            var palettes=Row();foreach(var palette in StudioColorDialog.Palettes().Take(10))
            {
                var colors=Row();colors.Spacing=0;foreach(var hex in palette.Colors.Take(3))colors.Children.Add(new Border{Width=22,Height=25,Background=Swatch(hex)});var b=Action(palette.Name,"palette",palette.Colors);b.Content=colors;b.Padding=new(4);ToolTipService.SetToolTip(b,palette.Name);AutomationProperties.SetName(b,palette.Name+" palette");palettes.Children.Add(b);
            }
            palettes.Children.Add(Action("Shuffle","shuffle"));sectionHost.Children.Add(palettes);
            var row=ColorChoices("body");row.Children.Add(Action("More colors…","body-picker"));row.Children.Add(Action("Stock paint","stock"));sectionHost.Children.Add(row);
            var finish=Row();foreach(var name in new[]{"Matte","Gloss","Metallic","Chrome","Candy","Pearl / two-tone"})finish.Children.Add(Action(name=="Pearl / two-tone"?"Pearl":name,"finish",name));sectionHost.Children.Add(finish);
        }
        else if(section=="Stickers")
        {
            sectionHost.Children.Add(stickerShelf);var row=Row();row.Children.Add(repeat);row.Children.Add(paired);row.Children.Add(Action("Edit last sticker","edit"));row.Children.Add(new TextBlock{Text="Tint",VerticalAlignment=VerticalAlignment.Center,FontSize=12});var tint=ColorChoices("tint");row.Children.Add(tint);sectionHost.Children.Add(row);
            var controls=Row();controls.Children.Add(new TextBlock{Text="Size",VerticalAlignment=VerticalAlignment.Center});controls.Children.Add(size);controls.Children.Add(new TextBlock{Text="Rotate",VerticalAlignment=VerticalAlignment.Center});controls.Children.Add(rotation);sectionHost.Children.Add(controls);
        }
        else
        {
            sectionHost.Children.Add(ColorChoices("brush"));var row=Row();row.Children.Add(Action("Brush","brush"));row.Children.Add(Action("Erase paint","eraser"));row.Children.Add(new TextBlock{Text="Size",VerticalAlignment=VerticalAlignment.Center});row.Children.Add(size);row.Children.Add(paired);row.Children.Add(Action("Independent sides","independent"));row.Children.Add(Action("More colors…","brush-picker"));sectionHost.Children.Add(row);
            sectionHost.Children.Add(new TextBlock{Text="Draw on the body. Right-drag to turn the car. Each stroke is one Undo.",FontSize=12,Foreground=(Brush)Application.Current.Resources["StudioSecondaryTextBrush"]});
        }
        if(notify)Requested?.Invoke("section",section);
    }
    private StackPanel ColorChoices(string command)
    {
        var row=Row();foreach(var hex in new[]{"#FFF3ED","#18202B","#EF3946","#FFB511","#228CFF","#21DCC1","#E943C4","#F77329"})
        {
            var b=Action(hex,command,hex);b.Content=new Border{Width=23,Height=23,CornerRadius=new(12),Background=Swatch(hex)};b.Padding=new(4);AutomationProperties.SetName(b,command+" color "+hex);row.Children.Add(b);
        }return row;
    }
    public void SetExpanded(bool expanded){body.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;collapse.Content=expanded?"Hide":"Show";collapse.IsChecked=!expanded;}
    public void SetAvailableHeight(double height)
    {
        if(height<100)return;trayScroller.MaxHeight=Math.Clamp(height-230,40,180);
        if((!initialSizeApplied||previousAvailableHeight>=320)&&height<320)SetExpanded(false);
        initialSizeApplied=true;previousAvailableHeight=height;
    }
    public void SetLookImage(StarterKind kind,string path){if(lookImages.TryGetValue(kind,out var image))image.Source=new BitmapImage(new Uri(path));}
    public void SetStickerImage(string name,string path){if(stickerImages.TryGetValue(name,out var image))image.Source=new BitmapImage(new Uri(path));}
    public void SetPlacement(double width,double angle,bool mirror){synchronizing=true;try{size.Value=width;rotation.Value=angle;paired.IsChecked=mirror;}finally{synchronizing=false;}}
}
