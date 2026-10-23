using LiveryStudio.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LiveryStudio;

internal static class StarterDialog
{
    internal static async Task<DesignStarterOptions?> ChooseAsync(XamlRoot root,Car car)
    {
        var width=Math.Min(460,Math.Max(260,root.Size.Width-110));var styles=new DialogComboBox{Header="Choose a starting look",ItemsSource=DesignStarters.Styles,DisplayMemberPath="Name",HorizontalAlignment=HorizontalAlignment.Stretch};
        var description=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        var baseHex=new TextBox{Text="#153E75",MaxLength=7};var accentHex=new TextBox{Text="#E63946",MaxLength=7};var inkHex=new TextBox{Text="#FFF3D6",MaxLength=7};
        var number=new TextBox{Header="Racing number",Text="27",MaxLength=3};var team=new TextBox{Header="Team / sponsor",Text="TEAM FURY",MaxLength=24};var name=new TextBox{Header="Design name",PlaceholderText="Automatic from your look and number",MaxLength=100};var badge=new CheckBox{Content="Include paired number badges",IsChecked=true};
        var palette=new DialogComboBox{Header="Try a palette",ItemsSource=StudioColorDialog.Palettes(),DisplayMemberPath="Name",PlaceholderText="Colors from the selected look",HorizontalAlignment=HorizontalAlignment.Stretch};
        var sketch=new Grid{Height=110,CornerRadius=new(8)};var stripes=new Grid{HorizontalAlignment=HorizontalAlignment.Center,Width=90};stripes.ColumnDefinitions.Add(new());stripes.ColumnDefinitions.Add(new(){Width=new(18)});stripes.ColumnDefinitions.Add(new());var left=new Border{Width=12,HorizontalAlignment=HorizontalAlignment.Center};var right=new Border{Width=12,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(right,2);stripes.Children.Add(left);stripes.Children.Add(right);sketch.Children.Add(stripes);
        var panel=new Border{Width=60,Height=55,CornerRadius=new(5),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,24,0)};var numberLabel=new TextBlock{Text="27",FontFamily=new("Bahnschrift"),FontWeight=Microsoft.UI.Text.FontWeights.Bold,FontSize=28,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};panel.Child=numberLabel;sketch.Children.Add(panel);
        void Refresh()
        {
            try{sketch.Background=new SolidColorBrush(StudioColorDialog.Parse(baseHex.Text));left.Background=right.Background=new SolidColorBrush(StudioColorDialog.Parse(accentHex.Text));panel.Background=new SolidColorBrush(StudioColorDialog.Parse(inkHex.Text));var c=PaintFinish.ToLinear(PaintFinish.Color(inkHex.Text));numberLabel.Foreground=new SolidColorBrush(System.Numerics.Vector3.Dot(c,new(.2126f,.7152f,.0722f))>.179f?Colors.Black:Colors.White);numberLabel.Text=number.Text;panel.Visibility=badge.IsChecked==true?Visibility.Visible:Visibility.Collapsed;status.Text="";}catch{status.Text="Colors use six-digit hex values, such as #FFB511.";}
        }
        FrameworkElement ColorRow(string title,TextBox field)
        {
            AutomationProperties.SetName(field,"Starter "+title.ToLowerInvariant()+" color");var grid=new Grid{ColumnSpacing=6};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});field.Header=title;grid.Children.Add(field);
            var picker=new ColorPicker{IsAlphaEnabled=false,IsMoreButtonVisible=false,IsColorSpectrumVisible=root.Size.Height>=700,Width=270};
            var saved=new DialogComboBox{Header="Recent & favorite colors",PlaceholderText="Choose a saved color",HorizontalAlignment=HorizontalAlignment.Stretch,MaxDropDownHeight=240};AutomationProperties.SetName(saved,"Saved starter "+title.ToLowerInvariant()+" colors");
            saved.SelectionChanged+=(_,_)=>{if(saved.SelectedItem is ComboBoxItem {Tag:string hex}){picker.Color=StudioColorDialog.Parse(hex);field.Text=hex;}};
            var contents=new StackPanel{Spacing=12,Width=270};contents.Children.Add(saved);contents.Children.Add(picker);
            var pick=new Button{Content="Pick",VerticalAlignment=VerticalAlignment.Bottom,Flyout=new Flyout{Content=new ScrollViewer{Content=contents,MaxHeight=Math.Clamp(root.Size.Height-240,120,440),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}}};AutomationProperties.SetName(pick,"Choose starter "+title.ToLowerInvariant()+" color");
            pick.Flyout.Opening+=(_,_)=>
            {
                try{picker.Color=StudioColorDialog.Parse(field.Text);}catch{picker.Color=Colors.White;}saved.Items.Clear();
                try{var library=ColorLibrary.Read(WorkshopService.DefaultWorkspace);var colors=library.Favorites.Concat(library.Recent.Select(c=>new SavedColor(c,"Recent "+c))).DistinctBy(c=>c.Hex);foreach(var color in colors){var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};row.Children.Add(new Border{Width=22,Height=18,CornerRadius=new(3),Background=new SolidColorBrush(StudioColorDialog.Parse(color.Hex))});row.Children.Add(new TextBlock{Text=color.Name,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=185});var item=new ComboBoxItem{Content=row,Tag=color.Hex};AutomationProperties.SetName(item,color.Name+" "+color.Hex);saved.Items.Add(item);}}catch{/* The numeric picker remains usable without optional saved colors. */}
            };
            picker.ColorChanged+=(_,e)=>field.Text=StudioColorDialog.Hex(e.NewColor);Grid.SetColumn(pick,1);grid.Children.Add(pick);return grid;
        }
        styles.SelectionChanged+=(_,_)=>{if(styles.SelectedItem is StarterStyle style){baseHex.Text=style.Base;accentHex.Text=style.Accent;inkHex.Text=style.Ink;description.Text=style.Description;palette.SelectedItem=null;Refresh();}};
        palette.SelectionChanged+=(_,_)=>{if(palette.SelectedItem is SavedPalette p&&p.Colors.Length>0){baseHex.Text=p.Colors[0];accentHex.Text=p.Colors[Math.Min(1,p.Colors.Length-1)];inkHex.Text=p.Colors[Math.Min(2,p.Colors.Length-1)];Refresh();}};
        foreach(var field in new[]{baseHex,accentHex,inkHex,number})field.TextChanged+=(_,_)=>Refresh();badge.Click+=(_,_)=>{number.IsEnabled=team.IsEnabled=badge.IsChecked==true;Refresh();};
        var body=new StackPanel{Spacing=12,Width=width};
        body.Children.Add(new TextBlock{Text=car.Display+" · a new editable design",TextWrapping=TextWrapping.Wrap});body.Children.Add(styles);body.Children.Add(description);body.Children.Add(sketch);
        body.Children.Add(new TextBlock{Text="Palette sketch · placement adjusts to the actual car",FontSize=11,TextWrapping=TextWrapping.Wrap});body.Children.Add(palette);body.Children.Add(badge);
        var identity=new Grid{ColumnSpacing=10};identity.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});identity.ColumnDefinitions.Add(new(){Width=new(2,GridUnitType.Star)});identity.Children.Add(number);Grid.SetColumn(team,1);identity.Children.Add(team);body.Children.Add(identity);body.Children.Add(name);
        var custom=new StackPanel{Spacing=10};custom.Children.Add(ColorRow("Base paint",baseHex));custom.Children.Add(ColorRow("Accent",accentHex));custom.Children.Add(ColorRow("Number panel",inkHex));body.Children.Add(new Expander{Header="Customize palette colors",Content=custom,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch});
        body.Children.Add(new TextBlock{Text="Stripes, numbers and text remain separate editable layers. Your current design is preserved under More options → Previous draft.",TextWrapping=TextWrapping.Wrap,FontSize=11});body.Children.Add(status);styles.SelectedIndex=0;
        DesignStarterOptions Read()=>new(){Kind=((StarterStyle)styles.SelectedItem).Kind,BaseColor=baseHex.Text,AccentColor=accentHex.Text,InkColor=inkHex.Text,Number=number.Text,Team=team.Text,Name=name.Text,IncludeNumber=badge.IsChecked==true};
        var dialog=new ContentDialog{XamlRoot=root,Title="Start a design",Content=new ScrollViewer{Content=body,MaxHeight=Math.Clamp(root.Size.Height-220,100,680),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},PrimaryButtonText="Create design",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary};
        dialog.PrimaryButtonClick+=(_,e)=>{try{Read().Validate();}catch(Exception x){status.Text=x.Message;status.StartBringIntoView();e.Cancel=true;}};
        return await dialog.ShowAsync()==ContentDialogResult.Primary?Read():null;
    }
}

public partial class MainWindow
{
    private string previousDraft="";
    private async void StartDesign(object sender,RoutedEventArgs e)
    {
        if(AdvancedModeToggle.IsChecked!=true){Painter.ShowSimpleLooks();return;}
        if(!BeginOutputOperation("Choose a starting look…"))return;
        CancellationTokenSource? starterCancellation=null;
        try
        {
            BuildProgress.Visibility=Visibility.Collapsed;
            var car=ReadDraft().SelectedCar;var choice=await StarterDialog.ChooseAsync(Root.XamlRoot,car);
            if(choice is null){Status.Text="New design canceled · your current draft is unchanged.";return;}
            if(currentWorkspace==3){await Painter.ApplyStarterAsync(choice);await Painter.FlushAsync();SaveSession();Status.Text="Look applied · Undo restores your previous design.";return;}

            built=null;EnableOutput(false);Status.Text="Preserving your current draft…";
            await Painter.FlushAsync();var current=ReadDraft();
            previousDraft=await Task.Run(()=>DraftRecovery.Save(current,WorkshopService.DefaultWorkspace));SaveSession();

            starterCancellation=new();cancellation=starterCancellation;var token=starterCancellation.Token;
            BuildProgress.IsIndeterminate=true;BuildProgress.Visibility=CancelButton.Visibility=Visibility.Visible;Status.Text="Placing your design on "+car.Name+"…";
            var service=Service();var maps=await Task.Run(()=>service.Sample(car),token);token.ThrowIfCancellationRequested();
            var seed=current with {Albedo=maps.Albedo,Structure=maps.Structure,CarIndex=car.Index};
            var mesh=await Task.Run(()=>PaintMesh.Read(Path.Combine(AppContext.BaseDirectory,"Models",car.Index.ToString("00")+".flsm")),token);token.ThrowIfCancellationRequested();
            var created=await Task.Run(()=>DesignStarters.Create(seed,mesh,choice,WorkshopService.DefaultWorkspace,token),token);token.ThrowIfCancellationRequested();
            // Cancellation ends before the new draft becomes the active project.
            BuildProgress.Visibility=CancelButton.Visibility=Visibility.Collapsed;
            project=created.Project;ApplyProject();SaveSession();await OpenPaintStudio();
            try{await Task.Run(()=>ColorLibrary.Use(WorkshopService.DefaultWorkspace,choice.InkColor,choice.AccentColor,choice.BaseColor));}catch{/* Optional color history does not block the design. */}
            Status.Text="Created "+project.Name+" · every accent is editable.";Info("Your design is ready",created.Layout.Layers.Count+" editable layers. "+string.Join(" ",created.Layout.Notes));
        }
        catch(OperationCanceledException){Status.Text="Starter canceled · your previous design is preserved.";}
        catch(Exception x){Error(x);}
        finally
        {
            if(starterCancellation is not null)
            {
                if(ReferenceEquals(cancellation,starterCancellation))cancellation=null;
                starterCancellation.Dispose();
            }
            EndOutputOperation();
        }
    }
    private async void OpenPreviousDraft(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        if(!File.Exists(previousDraft)){Info("No previous draft","Start a new design or open a saved build to preserve your current draft here.");return;}
        if(!BeginOutputOperation("Restoring your previous draft…"))return;
        try
        {
            built=null;EnableOutput(false);await Painter.FlushAsync();
            var incomplete=DraftRecovery.IsIncomplete(previousDraft);
            var opened=await Task.Run(()=>DraftRecovery.Open(previousDraft,WorkshopService.DefaultWorkspace));
            if(!incomplete)opened.Validate();project=opened;ApplyProject();View(incomplete||string.IsNullOrWhiteSpace(opened.EditorFile)?0:3);
            if(currentWorkspace==3)await Painter.LoadAsync(project);SaveSession();Status.Text=incomplete?"Previous draft restored · finish its details before baking.":"Previous draft restored.";
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
}
