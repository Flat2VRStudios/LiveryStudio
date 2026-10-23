using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private ContentDialog? artworkCollectionDialog;
    private long artworkCollectionEpoch;
    private void CancelArtworkCollection(){artworkCollectionEpoch++;artworkCollectionDialog?.Hide();}

    private async void OpenArtworkCollection(object sender,RoutedEventArgs e)
    {
        if(artworkCollectionDialog is not null||ImageReviewActive||document is null||XamlRoot is not {} root)return;
        var target=document;var version=generation;var checkpoint=target.History[target.Cursor];var epoch=++artworkCollectionEpoch;
        bool SameTarget()=>epoch==artworkCollectionEpoch&&ReferenceEquals(document,target)&&generation==version&&ReferenceEquals(target.History[target.Cursor],checkpoint);
        var active=true;var revision=0;var limit=48;var loading=false;var ready=false;
        IReadOnlyList<BrowseArtwork> catalog=[];var choices=new ArtworkBrowsingState();var notice="";
        var tasks=new List<Task>();
        var search=new TextBox{PlaceholderText="Find a name, lettering, SVG or group",Text=ArtworkSearch.Text};
        AutomationProperties.SetName(search,"Search the artwork collection");
        DialogComboBox Choice(string name,string[] options,int index)
        {
            var box=new DialogComboBox{ItemsSource=options,SelectedIndex=index,HorizontalAlignment=HorizontalAlignment.Stretch};AutomationProperties.SetName(box,name);return box;
        }
        var collection=Choice("Collection view",["All artwork","Pinned","Recent"],Math.Max(0,ArtworkCollectionPicker.SelectedIndex));
        var category=Choice("Collection artwork type",["Everything","Decals","Editable groups"],Math.Max(0,ArtworkCategoryPicker.SelectedIndex));
        var sort=Choice("Collection sort order",["Newest first","Name A–Z"],0);
        collection.Header="Collection";category.Header="Artwork type";sort.Header="Sort order";
        var filterOptions=new StackPanel{Spacing=10,Width=240};filterOptions.Children.Add(collection);filterOptions.Children.Add(category);filterOptions.Children.Add(sort);
        var filterButton=new Button{Content="Filter & sort",Flyout=new Flyout{Content=new ScrollViewer{Content=filterOptions,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}}};
        var filters=new Grid{ColumnSpacing=8};filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        filters.Children.Add(search);filters.Children.Add(filterButton);Grid.SetColumn(filterButton,1);
        var status=new TextBlock{Text="Loading your collection…",FontSize=11,TextWrapping=TextWrapping.Wrap};
        var shelf=new GridView{SelectionMode=ListViewSelectionMode.Single,IsItemClickEnabled=false,HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetName(shelf,"Expanded artwork collection");
        var largeTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border Width="132" Height="154" Padding="8" Margin="2" CornerRadius="8" Background="{ThemeResource StudioBorderBrush}" ToolTipService.ToolTip="{Binding Name}">
                <StackPanel Spacing="5">
                  <Image Source="{Binding Preview}" Height="78" Stretch="Uniform"/>
                  <TextBlock Text="{Binding Name}" FontSize="12" MaxLines="2" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Text="{Binding Description}" FontSize="10" MaxLines="1" TextTrimming="CharacterEllipsis" Foreground="{ThemeResource StudioSecondaryTextBrush}"/>
                </StackPanel>
              </Border>
            </DataTemplate>
            """);
        var smallTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border Width="132" Height="108" Padding="8" Margin="2" CornerRadius="8" Background="{ThemeResource StudioBorderBrush}" ToolTipService.ToolTip="{Binding Name}">
                <StackPanel Spacing="4">
                  <Image Source="{Binding Preview}" Height="52" Stretch="Uniform"/>
                  <TextBlock Text="{Binding Name}" FontSize="12" MaxLines="1" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Text="{Binding Description}" FontSize="10" MaxLines="1" TextTrimming="CharacterEllipsis" Foreground="{ThemeResource StudioSecondaryTextBrush}"/>
                </StackPanel>
              </Border>
            </DataTemplate>
            """);
        var selectedImage=new Image{Width=86,Height=58,Stretch=Stretch.Uniform};
        var selectedName=new TextBlock{Text="Choose artwork to see its details",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
        var selectedDescription=new TextBlock{Text="Browsing leaves your design unchanged.",FontSize=11,TextWrapping=TextWrapping.Wrap};
        var pin=new Button{Content="☆ Pin",IsEnabled=false,MinWidth=72};AutomationProperties.SetName(pin,"Pin selected collection artwork");
        var detailText=new StackPanel{Spacing=4,VerticalAlignment=VerticalAlignment.Center};detailText.Children.Add(selectedName);detailText.Children.Add(selectedDescription);
        var detail=new Grid{ColumnSpacing=10};detail.ColumnDefinitions.Add(new(){Width=GridLength.Auto});detail.ColumnDefinitions.Add(new());detail.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        detail.Children.Add(selectedImage);detail.Children.Add(detailText);Grid.SetColumn(detailText,1);detail.Children.Add(pin);Grid.SetColumn(pin,2);
        var more=new Button{Content="Show 48 more",HorizontalAlignment=HorizontalAlignment.Stretch,Visibility=Visibility.Collapsed};
        var body=new Grid{RowSpacing=10};foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})body.RowDefinitions.Add(new(){Height=height});
        void Row(FrameworkElement child,int row){body.Children.Add(child);Grid.SetRow(child,row);}
        Row(filters,0);Row(status,1);Row(shelf,2);Row(more,3);Row(detail,4);
        var dialog=new ContentDialog{Title="Your artwork collection",Content=body,XamlRoot=root,PrimaryButtonText="Place on car",CloseButtonText="Cancel",IsPrimaryButtonEnabled=false,DefaultButton=ContentDialogButton.None};
        dialog.Resources["ContentDialogMaxWidth"]=1100d;artworkCollectionDialog=dialog;
        void Layout()
        {
            body.Width=Math.Clamp(root.Size.Width-110,240,980);body.Height=Math.Clamp(root.Size.Height-(root.Size.Height<650?185:210),180,560);
            // Keep the grid and the final action visible in a short window.
            var compact=root.Size.Height<650;var template=compact?smallTemplate:largeTemplate;if(!ReferenceEquals(shelf.ItemTemplate,template))shelf.ItemTemplate=template;selectedImage.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            selectedDescription.Visibility=compact?Visibility.Collapsed:Visibility.Visible;body.RowSpacing=compact?6:10;
        }
        void Selected()
        {
            var item=shelf.SelectedItem as ArtworkShelfItem;dialog.IsPrimaryButtonEnabled=item is not null&&!loading;pin.IsEnabled=item is not null&&!loading;
            selectedImage.Source=item?.Preview;selectedName.Text=item?.Name??"Choose artwork to see its details";
            selectedDescription.Text=item is null?"Browsing leaves your design unchanged.":item.Description+(item.Artwork.IsGroup?" · members remain editable":" · hover over the car to position it");
            var favorite=item is not null&&choices.Favorites.Contains(item.Artwork.Key,StringComparer.OrdinalIgnoreCase);pin.Content=favorite?"★ Pinned":"☆ Pin";
            AutomationProperties.SetName(pin,item is null?"Pin selected collection artwork":(favorite?"Unpin ":"Pin ")+item.Name);
        }
        async Task Refresh()
        {
            if(!active||!ready)return;var request=++revision;bool Current()=>active&&request==revision&&SameTarget();
            var selected=(shelf.SelectedItem as ArtworkShelfItem)?.Artwork.Key;loading=true;Selected();status.Text="Loading previews…";
            try
            {
                var recent=collection.SelectedIndex==(int)ArtworkCollection.Recent;
                var matches=ArtworkBrowsing.Query(catalog,choices,search.Text,(ArtworkCollection)collection.SelectedIndex,(ArtworkCategory)category.SelectedIndex);
                var visible=sort.SelectedIndex==1?matches.OrderBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).ThenBy(a=>a.Key,StringComparer.OrdinalIgnoreCase).ToArray():matches;
                var items=new List<ArtworkShelfItem>();var skipped=0;var favorites=choices.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach(var asset in visible.Take(limit))
                {
                    try{var preview=await ArtworkThumbnail(asset,Current);if(preview is null)return;items.Add(new(asset,preview,favorites.Contains(asset.Key)));}
                    catch(Exception x) when(x is IOException or InvalidDataException or ArgumentException or JsonException or InvalidOperationException){skipped++;}
                    if(!Current())return;
                }
                if(!Current())return;shelf.ItemsSource=items;shelf.SelectedItem=items.FirstOrDefault(i=>i.Artwork.Key==selected);
                more.Visibility=visible.Count>limit?Visibility.Visible:Visibility.Collapsed;
                status.Text=visible.Count==0?"No artwork here. Try another view or fewer search words.":$"{items.Count} of {visible.Count} artwork · "+(sort.SelectedIndex==1?"name A–Z":recent?"last used first":"newest first");
                if(skipped>0)status.Text+=$" · {skipped} previews unavailable";if(notice.Length>0)status.Text+=" · "+notice;
            }
            catch(Exception x){if(Current())status.Text="Could not show artwork: "+x.Message;}
            finally{if(request==revision&&active){loading=false;Selected();}}
        }
        void Queue(bool reset=true){if(reset)limit=48;tasks.Add(Refresh());}
        shelf.SelectionChanged+=(_,_)=>Selected();
        search.TextChanged+=(_,_)=>Queue();collection.SelectionChanged+=(_,_)=>Queue();category.SelectionChanged+=(_,_)=>Queue();sort.SelectionChanged+=(_,_)=>Queue();
        more.Click+=(_,_)=>{limit+=48;Queue(false);};
        pin.Click+=async(_,_)=>
        {
            if(shelf.SelectedItem is not ArtworkShelfItem item||loading)return;loading=true;Selected();
            async Task ChangePin()
            {
                try
                {
                    artworkPreferencesRevision++;var favorite=choices.Favorites.Contains(item.Artwork.Key,StringComparer.OrdinalIgnoreCase);
                    await Task.Run(()=>ArtworkBrowsing.Favorite(WorkshopService.DefaultWorkspace,item.Artwork.Path,!favorite));
                    await ReadArtworkChoices();if(!active||!SameTarget())return;choices=artworkBrowsing;notice="";await Refresh();
                }
                catch(Exception x){if(active){status.Text=x.Message;loading=false;Selected();}}
            }
            var task=ChangePin();tasks.Add(task);await task;
        };
        async Task Load()
        {
            try
            {
                var loaded=await Task.Run(()=>ArtworkBrowsing.Catalog(WorkshopService.DefaultWorkspace));var preferences=artworkPreferencesRevision;
                ArtworkBrowsingState saved;try{saved=await Task.Run(()=>ArtworkBrowsing.Read(WorkshopService.DefaultWorkspace));}catch(Exception x){saved=new();notice="Saved choices unavailable: "+x.Message;}
                if(!active||!SameTarget())return;catalog=loaded;choices=preferences==artworkPreferencesRevision?saved:artworkBrowsing;ready=true;await Refresh();
            }
            catch(Exception x){if(active)status.Text="Could not read your artwork: "+x.Message;}
        }
        dialog.Opened+=(_,_)=>{Layout();tasks.Add(Load());search.Focus(FocusState.Programmatic);};
        void Resized(XamlRoot changedRoot,XamlRootChangedEventArgs args){if(active)Layout();}
        root.Changed+=Resized;
        try
        {
            var result=await dialog.ShowAsync();var chosen=shelf.SelectedItem as ArtworkShelfItem;
            active=false;revision++;await Task.WhenAll(tasks);
            if(result!=ContentDialogResult.Primary||chosen is null||!SameTarget())return;
            if(chosen.Artwork.IsGroup)await PlaceSavedArtwork(new(chosen.Name,chosen.Artwork.Path),SameTarget);
            else if(await UseSticker(chosen.Artwork.Path,stillCurrent:SameTarget))HideInspector();
        }
        catch(Exception x){Fail(x);}
        finally{active=false;revision++;root.Changed-=Resized;if(ReferenceEquals(artworkCollectionDialog,dialog))artworkCollectionDialog=null;if(SameTarget())RefreshArtwork();}
    }
}
