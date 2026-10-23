using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LiveryStudio;
public sealed partial class CarPaintView
{
    private long designColorsRevision;
    private bool updatingSharedPaint;
    private Guid? sharedBrushColor;
    private RasterArtworkOptions? pendingRaster;
    private void ShowDesignColors(object sender,RoutedEventArgs e){InspectorSection.SelectedIndex=5;ShowInspector();_ = RefreshDesignColors();}
    private void RefreshDesignColorsClicked(object sender,RoutedEventArgs e)=>_ = RefreshDesignColors();
    private async Task RefreshDesignColors()
    {
        if(document is null||draft is null||DesignColorTiles is null||InspectorSection.SelectedIndex!=5)return;
        var revision=++designColorsRevision;var source=document;var current=draft;DesignColorsStatus.Text="Finding colors in your design…";
        try
        {
            var snapshot=JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(source,LayerDocument.Json),LayerDocument.Json)!;
            var uses=await Task.Run(()=>DesignPalette.Uses(snapshot,current));if(revision!=designColorsRevision||!ReferenceEquals(source,document))return;
            SharedColorCards.Children.Clear();DesignColorTiles.Children.Clear();DesignColorTiles.RowDefinitions.Clear();
            var palette=document.History[document.Cursor].Palette;
            foreach(var role in palette)
            {
                var bound=uses.Where(u=>u.Link?.Id==role.Id).ToArray();var locked=bound.Count(u=>u.Locked);var card=new StackPanel{Spacing=6};
                var edit=new Button{HorizontalAlignment=HorizontalAlignment.Stretch,Content=ColorChip(role.Hex,role.Name,bound.Length+" linked fields")};AutomationProperties.SetName(edit,"Edit shared color "+role.Name+" "+role.Hex);edit.Click+=async(_,_)=>await EditDesignColor(role);card.Children.Add(edit);
                var actions=new Grid{ColumnSpacing=6};actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new());
                var use=new Button{Content="Use for brush",HorizontalAlignment=HorizontalAlignment.Stretch};use.Click+=(_,_)=>{sharedBrushColor=role.Id;BrushHex.Text=role.Hex;InspectorSection.SelectedIndex=0;Select(1);if(floatingInspector)HideInspector();CanvasStatus.Text="Brush linked to "+role.Name+" · new strokes follow this shared color.";};actions.Children.Add(use);
                var unlink=new Button{Content="Unlink fields",HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=locked==0};Grid.SetColumn(unlink,1);unlink.Click+=async(_,_)=>{try{await FlushAsync();if(document is null||draft is null)return;ApplyDesignColorEdit(DesignPalette.Unlink(document,draft,role.Id));}catch(Exception x){Fail(x);}};actions.Children.Add(unlink);card.Children.Add(actions);
                if(locked>0)card.Children.Add(new TextBlock{Text=$"Unlock {locked} linked fields before changing this color.",TextWrapping=TextWrapping.Wrap,FontSize=11});
                SharedColorCards.Children.Add(card);
            }
            var colors=uses.GroupBy(u=>u.Hex).OrderByDescending(g=>g.Any(u=>u.LayerId is null)).ThenByDescending(g=>g.Count()).ThenBy(g=>g.Key).Take(64).ToArray();
            for(var i=0;i<colors.Length;i++)
            {
                if(i%2==0)DesignColorTiles.RowDefinitions.Add(new(){Height=GridLength.Auto});var group=colors[i];var color=group.Key;var available=group.Count(u=>!u.Locked);
                var button=new Button{HorizontalAlignment=HorizontalAlignment.Stretch,Content=ColorChip(color,color,available+" matching fields"),IsEnabled=available>0};Grid.SetRow(button,i/2);Grid.SetColumn(button,i%2);AutomationProperties.SetName(button,"Link design color "+color);button.Click+=async(_,_)=>await LinkDesignColor(color);DesignColorTiles.Children.Add(button);
            }
            SharedColorsEmpty.Visibility=palette.Count==0?Visibility.Visible:Visibility.Collapsed;
            DesignColorsStatus.Text=$"{palette.Count} shared colors · {uses.Select(u=>u.Hex).Distinct().Count()} colors found. Click a color below to link its matching fields.";
        }
        catch(Exception x){if(revision==designColorsRevision)DesignColorsStatus.Text=x.Message;}
    }
    private static UIElement ColorChip(string hex,string name,string detail)
    {
        var row=new Grid{ColumnSpacing=8};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
        row.Children.Add(new Border{Width=28,Height=34,CornerRadius=new(4),Background=new SolidColorBrush(StudioColorDialog.Parse(hex)),BorderBrush=(Brush)Application.Current.Resources["StudioBorderBrush"],BorderThickness=new(1)});
        var labels=new StackPanel{Spacing=2};labels.Children.Add(new TextBlock{Text=name,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,FontSize=12});labels.Children.Add(new TextBlock{Text=detail,FontSize=10,TextWrapping=TextWrapping.Wrap});Grid.SetColumn(labels,1);row.Children.Add(labels);return row;
    }
    private async Task LinkDesignColor(string hex)
    {
        if(document is null||draft is null)return;
        try
        {
            await FlushAsync();var target=document;var project=draft;var before=JsonSerializer.Serialize(target,LayerDocument.Json);var uses=await Task.Run(()=>DesignPalette.Uses(target,project));
            var matches=uses.Where(u=>u.Hex==hex).ToArray();var name=new TextBox{Header="Shared color name",Text=matches.Any(u=>u.LayerId is null)?"Body color":"Accent "+(target.History[target.Cursor].Palette.Count+1),MaxLength=48};
            var selection=new CheckBox{Content="Only link selected layers",IsEnabled=SelectedLayers.Count>0};var chosen=SelectedLayers.Select(l=>l.Id).ToHashSet();
            var body=new StackPanel{Spacing=12};body.Children.Add(ColorChip(hex,hex,$"{matches.Count(u=>!u.Locked)} unlocked matching fields"));body.Children.Add(name);body.Children.Add(selection);body.Children.Add(new TextBlock{Text="Matching paint, text, patterns and SVG colors can share one color. Image matches recolor exact RGB pixels and keep transparency. Locked fields are left unchanged.",TextWrapping=TextWrapping.Wrap,FontSize=12});
            var dialog=new ContentDialog{Title="Link a design color",Content=body,XamlRoot=XamlRoot,PrimaryButtonText="Link matching colors",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;var label=name.Text;var only=selection.IsChecked==true;
            var edit=await Task.Run(()=>DesignPalette.Link(target,project,hex,label,only?chosen:null));
            if(!ReferenceEquals(document,target)||JsonSerializer.Serialize(target,LayerDocument.Json)!=before||draft!=project)return;ApplyDesignColorEdit(edit);
        }
        catch(Exception x){Fail(x);}
    }
    private async Task EditDesignColor(SharedDesignColor role)
    {
        if(document is null||draft is null)return;
        try
        {
            await FlushAsync();var target=document;var project=draft;var before=JsonSerializer.Serialize(target,LayerDocument.Json);
            var width=Math.Min(330,Math.Max(240,XamlRoot.Size.Width-110));var picker=new ColorPicker{Width=width,IsAlphaEnabled=false,IsMoreButtonVisible=false,IsColorSpectrumVisible=XamlRoot.Size.Height>=700,Color=StudioColorDialog.Parse(role.Hex)};
            var name=new TextBox{Header="Color name",Text=role.Name,MaxLength=48};var summary=new TextBlock{Text="Apply changes to every field linked to this color. Placement, finish and source artwork stay editable. One undo restores the entire color change.",TextWrapping=TextWrapping.Wrap,FontSize=12};
            var choices=new DialogComboBox{Header="Try a palette",ItemsSource=StudioColorDialog.Palettes(),DisplayMemberPath="Name",HorizontalAlignment=HorizontalAlignment.Stretch};var chips=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};choices.SelectionChanged+=(_,_)=>{chips.Children.Clear();if(choices.SelectedItem is not SavedPalette palette)return;foreach(var color in palette.Colors){var button=new Button{Width=40,Height=30,Padding=new(3),Content=new Border{Background=new SolidColorBrush(StudioColorDialog.Parse(color)),CornerRadius=new(3)}};AutomationProperties.SetName(button,"Choose "+color);button.Click+=(_,_)=>picker.Color=StudioColorDialog.Parse(color);chips.Children.Add(button);}};
            var fields=new StackPanel{Spacing=10};fields.Children.Add(name);fields.Children.Add(picker);fields.Children.Add(choices);fields.Children.Add(chips);var body=new StackPanel{Spacing=10};body.Children.Add(summary);body.Children.Add(new ScrollViewer{Content=fields,MaxHeight=Math.Clamp(XamlRoot.Size.Height-310,100,460),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var dialog=new ContentDialog{Title="Edit shared color · "+role.Name,Content=body,XamlRoot=XamlRoot,PrimaryButtonText="Apply color",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;var hex=StudioColorDialog.Hex(picker.Color);var label=name.Text;CanvasStatus.Text="Updating linked artwork…";
            var edit=await Task.Run(()=>DesignPalette.Change(target,project,role.Id,hex,WorkshopService.DefaultWorkspace,name:label));
            if(!ReferenceEquals(document,target)||JsonSerializer.Serialize(target,LayerDocument.Json)!=before||draft!=project)return;ApplyDesignColorEdit(edit);RememberColors(hex);
        }
        catch(Exception x){Fail(x);}
    }
    private void ApplyDesignColorEdit(DesignColorEdit edit)
    {
        document=edit.Document;draft=edit.Project;selectedLayers.IntersectWith(document.Layers.Select(l=>l.Id));
        updatingSharedPaint=true;try{FinishEdited?.Invoke(draft);}finally{updatingSharedPaint=false;}
        if(sharedBrushColor is {} id&&document.History[document.Cursor].Palette.FirstOrDefault(c=>c.Id==id) is {} role)BrushHex.Text=role.Hex;
        SyncTextHistory();SyncPatternHistory();QueueComposite();_ = RefreshDesignColors();
    }
    private void SyncSharedPaintHistory()
    {
        if(document is null||draft is null)return;var resolved=DesignPalette.ResolvePaint(document,draft);
        if(resolved==draft){_ = RefreshDesignColors();return;}draft=resolved;
        updatingSharedPaint=true;try{FinishEdited?.Invoke(resolved);}finally{updatingSharedPaint=false;}
        _ = RefreshDesignColors();
    }
}
