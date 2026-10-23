using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LiveryStudio;

internal sealed record StudioColorChoice(string? Hex,bool SampleFromCar);
internal static class StudioColorDialog
{
    internal static Color Parse(string hex){hex=ColorLibrary.Normalize(hex);return Color.FromArgb(255,Convert.ToByte(hex[1..3],16),Convert.ToByte(hex[3..5],16),Convert.ToByte(hex[5..7],16));}
    internal static string Hex(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
    internal static async Task<StudioColorChoice?> ChooseAsync(XamlRoot root,string initial,string title,bool sampleFromCar=true)
    {
        try{initial=ColorLibrary.Normalize(initial);}catch{initial="#FFFFFF";}
        var width=Math.Min(330,Math.Max(260,root.Size.Width-110));var picker=new ColorPicker{IsAlphaEnabled=false,IsMoreButtonVisible=false,IsColorSpectrumVisible=root.Size.Height>=700,Width=width,Color=Parse(initial)};
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11};var current=new TextBlock{Text=initial};var chip=new Border{Width=28,Height=24,CornerRadius=new(4),Background=new SolidColorBrush(picker.Color)};
        var heading=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};heading.Children.Add(chip);heading.Children.Add(current);picker.ColorChanged+=(_,e)=>{chip.Background=new SolidColorBrush(e.NewColor);current.Text=Hex(e.NewColor);};
        var recent=new Grid{ColumnSpacing=4,RowSpacing=4};var favorites=new StackPanel{Spacing=6};var paletteColors=new Grid{ColumnSpacing=4,RowSpacing=4};var name=new TextBox{Header="Favorite name",MaxLength=48,PlaceholderText="Optional name"};var save=new Button{Content="Save as favorite",HorizontalAlignment=HorizontalAlignment.Stretch};
        var palette=new DialogComboBox{Header="Color palette",ItemsSource=Palettes(),DisplayMemberPath="Name",HorizontalAlignment=HorizontalAlignment.Stretch};
        Button Swatch(string hex,string label,bool named=false)
        {
            var button=new Button{Padding=new(5),HorizontalAlignment=HorizontalAlignment.Stretch};var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};row.Children.Add(new Border{Width=24,Height=20,CornerRadius=new(3),Background=new SolidColorBrush(Parse(hex)),BorderBrush=(Brush)Application.Current.Resources["StudioBorderBrush"],BorderThickness=new(1)});if(named)row.Children.Add(new TextBlock{Text=label,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=200,VerticalAlignment=VerticalAlignment.Center});button.Content=row;AutomationProperties.SetName(button,"Use "+label+" "+hex);ToolTipService.SetToolTip(button,label+" · "+hex);button.Click+=(_,_)=>{picker.Color=Parse(hex);name.Text=label==hex?"":label;};return button;
        }
        void FillGrid(Grid grid,IEnumerable<string> colors)
        {
            grid.Children.Clear();grid.RowDefinitions.Clear();grid.ColumnDefinitions.Clear();for(var c=0;c<6;c++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});var values=colors.ToArray();for(var r=0;r<(values.Length+5)/6;r++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});for(var i=0;i<values.Length;i++){var button=Swatch(values[i],values[i]);Grid.SetRow(button,i/6);Grid.SetColumn(button,i%6);grid.Children.Add(button);}
        }
        void RefreshSaved()
        {
            try
            {
                var library=ColorLibrary.Read(WorkshopService.DefaultWorkspace);FillGrid(recent,library.Recent);favorites.Children.Clear();
                if(library.Favorites.Count==0)favorites.Children.Add(new TextBlock{Text="Save colors you want to keep.",FontSize=11});
                foreach(var color in library.Favorites)
                {
                    var row=new Grid{ColumnSpacing=6};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.Children.Add(Swatch(color.Hex,color.Name,true));var remove=new Button{Content="×",Padding=new(8,4,8,4)};AutomationProperties.SetName(remove,"Remove favorite "+color.Name);ToolTipService.SetToolTip(remove,"Remove this favorite");Grid.SetColumn(remove,1);row.Children.Add(remove);remove.Click+=async(_,_)=>{try{await Task.Run(()=>ColorLibrary.RemoveFavorite(WorkshopService.DefaultWorkspace,color.Hex));RefreshSaved();status.Text="Favorite removed.";}catch(Exception e){status.Text=e.Message;}};favorites.Children.Add(row);
                }
            }
            catch(Exception e){status.Text="Saved colors unavailable: "+e.Message;}
        }
        save.Click+=async(_,_)=>{save.IsEnabled=false;var hex=Hex(picker.Color);var label=name.Text;try{await Task.Run(()=>ColorLibrary.Favorite(WorkshopService.DefaultWorkspace,hex,label));RefreshSaved();status.Text="Favorite saved · available in every color picker.";}catch(Exception e){status.Text=e.Message;}finally{save.IsEnabled=true;}};
        palette.SelectionChanged+=(_,_)=>{if(palette.SelectedItem is SavedPalette p)FillGrid(paletteColors,p.Colors);};palette.SelectedIndex=0;RefreshSaved();
        var saved=new StackPanel{Spacing=10};saved.Children.Add(new TextBlock{Text="RECENT COLORS",FontSize=10});saved.Children.Add(recent);saved.Children.Add(name);saved.Children.Add(save);saved.Children.Add(new TextBlock{Text="FAVORITES",FontSize=10});saved.Children.Add(favorites);saved.Children.Add(palette);saved.Children.Add(paletteColors);
        var height=Math.Clamp(root.Size.Height-320,100,440);ScrollViewer Scroll(UIElement content)=>new(){Content=content,MaxHeight=height,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var tabs=new Pivot{Width=width};tabs.Items.Add(new PivotItem{Header="Color",Content=Scroll(picker)});tabs.Items.Add(new PivotItem{Header="Saved colors",Content=Scroll(saved)});
        var body=new StackPanel{Spacing=8};body.Children.Add(heading);body.Children.Add(tabs);body.Children.Add(status);
        var dialog=new ContentDialog{XamlRoot=root,Title=title,Content=body,PrimaryButtonText="Use color",SecondaryButtonText=sampleFromCar?"Pick from car":"",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
        var result=await dialog.ShowAsync();if(result==ContentDialogResult.Secondary)return new(null,true);if(result!=ContentDialogResult.Primary)return null;var selected=Hex(picker.Color);try{await Task.Run(()=>ColorLibrary.Use(WorkshopService.DefaultWorkspace,selected));}catch{/* Color selection remains usable when optional history cannot be saved. */}return new(selected,false);
    }
    internal static IReadOnlyList<SavedPalette> Palettes()
    {
        var result=ArtworkLibrary.Palettes.ToList();var path=Path.Combine(WorkshopService.DefaultWorkspace,"palettes.json");
        try{if(File.Exists(path)&&new FileInfo(path).Length<64*1024)foreach(var p in JsonSerializer.Deserialize<List<SavedPalette>>(File.ReadAllText(path),Contract.Json)??[]){if(result.Count>=64)break;if(p.Colors.Length>0&&p.Colors.Length<=24){foreach(var c in p.Colors)_=ColorLibrary.Normalize(c);result.Add(p);}}}catch{/* Built-in palettes remain available. */}return result;
    }
}
