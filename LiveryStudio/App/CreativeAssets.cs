using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LiveryStudio;
public sealed record ArtworkShelfItem(BrowseArtwork Artwork,BitmapImage Preview,bool Favorite)
{
	public string Name=>Artwork.Name;
	public string Description=>Artwork.Description;
	public string FavoriteIcon=>Favorite?"★":"☆";
	public string FavoriteAction=>(Favorite?"Unpin ":"Pin ")+Name;
	public override string ToString()=>Name;
}
public sealed partial class CarPaintView
{
	private IReadOnlyList<BrowseArtwork> artworkCatalog=[];
	private ArtworkBrowsingState artworkBrowsing=new();
	private readonly Dictionary<string,BitmapImage> artworkThumbnails=new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim artworkThumbnailGate=new(1,1);
	private int artworkPageSize=24;
	private long artworkFilterRevision,artworkPreferencesRevision;
	private string artworkBrowsingNotice="";
	private long artworkRevision,textRevision;
	private TextArtworkOptions? pendingText;
	private Guid? editingTextLayer;
	private Microsoft.UI.Dispatching.DispatcherQueueTimer? textPreviewTimer;
	private bool updatingText;
	private void InitializeCreativeControls()
	{
		updatingText=true;try{var fonts=TextArtwork.Fonts();TextFont.ItemsSource=fonts;TextFont.SelectedItem=fonts.FirstOrDefault(f=>f=="Impact")??fonts.FirstOrDefault(f=>f=="Segoe UI")??fonts.FirstOrDefault();}finally{updatingText=false;}
		textPreviewTimer=DispatcherQueue.CreateTimer();textPreviewTimer.Interval=TimeSpan.FromMilliseconds(250);textPreviewTimer.IsRepeating=false;textPreviewTimer.Tick+=async(_,_)=>await RefreshTextPreview();ScheduleTextPreview();
		InitializePatternControls();
	}
	private async Task RefreshArtworkShelf()
	{
		var revision=++artworkRevision;++artworkFilterRevision;
		try
		{
			var catalog=await Task.Run(()=>ArtworkBrowsing.Catalog(WorkshopService.DefaultWorkspace));
			var preferencesRevision=artworkPreferencesRevision;ArtworkBrowsingState choices;string notice;
			try{choices=await Task.Run(()=>ArtworkBrowsing.Read(WorkshopService.DefaultWorkspace));notice="";}
			catch(Exception x){choices=new();notice="Saved choices unavailable: "+x.Message;}
			if(revision!=artworkRevision)return;artworkCatalog=catalog;if(preferencesRevision==artworkPreferencesRevision){artworkBrowsing=choices;artworkBrowsingNotice=notice;}await ApplyArtworkFilter();
		}
		catch(Exception x){if(revision==artworkRevision)ArtworkShelfStatus.Text="Could not read your artwork: "+x.Message;}
	}
	private static PaintImage Thumbnail(PaintImage source)
	{
		var scale=Math.Min(1,128d/Math.Max(source.Width,source.Height));var w=Math.Max(1,(int)Math.Round(source.Width*scale));var h=Math.Max(1,(int)Math.Round(source.Height*scale));var pixels=new byte[w*h*4];
		for(var y=0;y<h;y++)for(var x=0;x<w;x++){var sy=Math.Clamp((int)((y+.5)*source.Height/h),0,source.Height-1);var sx=Math.Clamp((int)((x+.5)*source.Width/w),0,source.Width-1);Array.Copy(source.Pixels,(sy*source.Width+sx)*4,pixels,(y*w+x)*4,4);}return new(w,h,pixels);
	}
	private async void ArtworkSearchChanged(object sender,TextChangedEventArgs e){if(ArtworkShelf is not null){artworkPageSize=24;await ApplyArtworkFilter();}}
	private async void ArtworkViewChanged(object sender,SelectionChangedEventArgs e){if(ArtworkShelf is not null){artworkPageSize=24;await ApplyArtworkFilter();}}
	private async void MoreArtwork(object sender,RoutedEventArgs e){artworkPageSize+=24;await ApplyArtworkFilter();}
	private async void RefreshArtworkClicked(object sender,RoutedEventArgs e)=>await RefreshArtworkShelf();
	private async Task<BitmapImage?> ArtworkThumbnail(BrowseArtwork asset,Func<bool> current)
	{
		if(!current())return null;
		var file=new FileInfo(asset.Path);var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(asset.Path+file.Length+file.LastWriteTimeUtc.Ticks)))[..24];
		if(artworkThumbnails.TryGetValue(key,out var cached))return cached;
		await artworkThumbnailGate.WaitAsync();
		try
		{
		if(!current())return null;if(artworkThumbnails.TryGetValue(key,out cached))return cached;
		var cache=Path.Combine(WorkshopService.DefaultWorkspace,"artwork-thumbnails");Directory.CreateDirectory(cache);var thumbnail=Path.Combine(cache,key+".png");
		if(!File.Exists(thumbnail)){var image=await Task.Run(()=>asset.IsGroup?ReusableArtwork.PreviewFile(asset.Path):Thumbnail(FinishMaps.Read(asset.Path)));if(!current())return null;await ImageFiles.WriteAsync(image,thumbnail);}
		if(!current())return null;
		var preview=new BitmapImage{UriSource=new Uri(thumbnail),DecodePixelWidth=128};if(artworkThumbnails.Count>=96)artworkThumbnails.Clear();artworkThumbnails[key]=preview;return preview;
		}
		finally{artworkThumbnailGate.Release();}
	}
	private async Task ApplyArtworkFilter()
	{
		if(ArtworkCollectionPicker is null||ArtworkCategoryPicker is null)return;
		var revision=++artworkFilterRevision;var catalogRevision=artworkRevision;
		try
		{
			var collection=(ArtworkCollection)Math.Max(0,ArtworkCollectionPicker.SelectedIndex);
			var visible=ArtworkBrowsing.Query(artworkCatalog,artworkBrowsing,ArtworkSearch.Text,collection,(ArtworkCategory)Math.Max(0,ArtworkCategoryPicker.SelectedIndex));
			var selected=(ArtworkShelf.SelectedItem as ArtworkShelfItem)?.Artwork.Key;var items=new List<ArtworkShelfItem>();var skipped=0;var favorites=artworkBrowsing.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ArtworkShelfStatus.Text="Loading artwork…";
			foreach(var asset in visible.Take(artworkPageSize))
			{
				try
				{
					var preview=await ArtworkThumbnail(asset,()=>revision==artworkFilterRevision&&catalogRevision==artworkRevision);if(preview is null)return;
					items.Add(new(asset,preview,favorites.Contains(asset.Key)));
				}
				catch(Exception x) when(x is IOException or InvalidDataException or ArgumentException or JsonException or InvalidOperationException){skipped++;}
				if(revision!=artworkFilterRevision||catalogRevision!=artworkRevision)return;
			}
			if(revision!=artworkFilterRevision||catalogRevision!=artworkRevision)return;ArtworkShelf.ItemsSource=items;ArtworkShelf.SelectedItem=items.FirstOrDefault(a=>a.Artwork.Key==selected);
			MoreArtworkButton.Visibility=visible.Count>artworkPageSize?Visibility.Visible:Visibility.Collapsed;
			ArtworkShelfStatus.Text=visible.Count==0?(ArtworkSearch.Text.Trim().Length>0?"No artwork matches. Try fewer words or another view.":collection==ArtworkCollection.Favorites?"Pin artwork with ☆ to keep it here.":collection==ArtworkCollection.Recent?"Choose artwork to place and it will appear here.":"Import a decal or save a group to start your collection."):$"{items.Count} of {visible.Count} artwork · "+(collection==ArtworkCollection.Recent?"last used first":"newest first");
			if(skipped>0)ArtworkShelfStatus.Text+=$" · {skipped} previews unavailable";if(artworkBrowsingNotice.Length>0)ArtworkShelfStatus.Text+=" · "+artworkBrowsingNotice;
		}
		catch(Exception x){if(revision==artworkFilterRevision&&catalogRevision==artworkRevision)ArtworkShelfStatus.Text="Could not show artwork: "+x.Message;}
	}

	private async void FavoriteArtwork(object sender,RoutedEventArgs e)
	{
		if(sender is not Button {DataContext:ArtworkShelfItem item} button)return;button.IsEnabled=false;
		try{artworkPreferencesRevision++;await Task.Run(()=>ArtworkBrowsing.Favorite(WorkshopService.DefaultWorkspace,item.Artwork.Path,!item.Favorite));await ReadArtworkChoices();await ApplyArtworkFilter();}
		catch(Exception x){ArtworkShelfStatus.Text=x.Message;}finally{button.IsEnabled=true;}
	}
	private async Task ReadArtworkChoices()
	{
		var revision=++artworkPreferencesRevision;var choices=await Task.Run(()=>ArtworkBrowsing.Read(WorkshopService.DefaultWorkspace));
		if(revision==artworkPreferencesRevision){artworkBrowsing=choices;artworkBrowsingNotice="";}
	}
	private async Task RememberArtwork(string path)
	{
		try{artworkPreferencesRevision++;await Task.Run(()=>ArtworkBrowsing.Use(WorkshopService.DefaultWorkspace,path));await ReadArtworkChoices();}
		catch(Exception x){artworkBrowsingNotice="Could not remember artwork: "+x.Message;}
	}
	private async void ArtworkClicked(object sender,ItemClickEventArgs e)
	{
		if(e.ClickedItem is not ArtworkShelfItem item)return;
		if(item.Artwork.IsGroup)await PlaceSavedArtwork(new(item.Name,item.Artwork.Path));
		else if(await UseSticker(item.Artwork.Path)){if(floatingInspector)HideInspector();}
	}
	private TextArtworkOptions CurrentText()=>new(){Text=TextContent.Text,FontName=TextFont.SelectedItem as string??"Segoe UI",Color=TextFill.Text,OutlineColor=TextOutlineColor.Text,Bold=TextBold.IsChecked==true,Italic=TextItalic.IsChecked==true,OutlineWidth=(float)TextOutlineWidth.Value,Tracking=(float)TextTracking.Value};
	private void ScheduleTextPreview(){if(updatingText||textPreviewTimer is null)return;textRevision++;textPreviewTimer.Stop();textPreviewTimer.Start();}
	private void TextChanged(object sender,TextChangedEventArgs e)=>ScheduleTextPreview();
	private void TextFontChanged(object sender,SelectionChangedEventArgs e)=>ScheduleTextPreview();
	private void TextStyleChanged(object sender,RoutedEventArgs e)=>ScheduleTextPreview();
	private void TextSettingChanged(object sender,Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)=>ScheduleTextPreview();
	private async void PickCreativeColor(object sender,RoutedEventArgs e)
	{
		var field=(((Button)sender).Tag as string) switch{"text"=>TextFill,"outline"=>TextOutlineColor,_=>BrushHex};
		try{await PickArtworkColor(field,field==TextFill?"Text fill":field==TextOutlineColor?"Text outline":"Brush / shape color");}
		catch(Exception x){Fail(x);}
	}
	private async Task RefreshTextPreview()
	{
		var revision=textRevision;
		try{var options=CurrentText();options.Validate();var image=await Task.Run(()=>TextArtwork.Render(options));if(revision!=textRevision)return;var path=Path.Combine(WorkshopService.DefaultWorkspace,"text-preview.png");await ImageFiles.WriteAsync(image,path);if(revision!=textRevision)return;TextPreview.Source=new BitmapImage{CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(path)};TextStatus.Text=$"{image.Width} × {image.Height} · transparent, editable text";}
		catch(Exception x){if(revision==textRevision)TextStatus.Text=x.Message;}
	}
	private void ShowText(object sender,RoutedEventArgs e){ShowArtwork(sender,e);TextExpander.IsExpanded=true;AssetsInspector.ChangeView(null,0,null);}
	private async void UseText(object sender,RoutedEventArgs e)
	{
		UseTextButton.IsEnabled=UpdateTextButton.IsEnabled=false;var request=++stickerRequestRevision;stickerLoading=true;
		try{var options=CurrentText();options.Validate();var path=await Task.Run(()=>TextArtwork.Generate(options,WorkshopService.DefaultWorkspace));if(request!=stickerRequestRevision||!await UseSticker(path,request:request))return;RememberColors(options.OutlineColor,options.Color);ArtworkSearch.Text="";RefreshArtwork();if(floatingInspector)HideInspector();CanvasStatus.Text="Your text is ready · hover over the car and click to add it.";}
		catch(Exception x){TextStatus.Text=x.Message;}
		finally{if(request==stickerRequestRevision)stickerLoading=false;UseTextButton.IsEnabled=true;UpdateTextButton.IsEnabled=editingTextLayer.HasValue;}
	}
	private void EditText(object sender,RoutedEventArgs e)
	{
		if(SelectedLayer is not {TextArtwork:{} options} layer)return;editingTextLayer=layer.Id;LoadTextFields(options);
		UpdateTextButton.Visibility=Visibility.Visible;UpdateTextButton.IsEnabled=true;UpdateTextButton.Content="Update text layer";TextEditStatus.Text="Editing: "+layer.Name;ShowText(sender,e);ScheduleTextPreview();
	}
	private void LoadTextFields(TextArtworkOptions options)
	{
		updatingText=true;try{TextContent.Text=options.Text;TextFont.SelectedItem=options.FontName;if(TextFont.SelectedItem is null)TextFont.SelectedItem="Segoe UI";TextFill.Text=options.Color;TextOutlineColor.Text=options.OutlineColor;TextBold.IsChecked=options.Bold;TextItalic.IsChecked=options.Italic;TextOutlineWidth.Value=options.OutlineWidth;TextTracking.Value=options.Tracking;}finally{updatingText=false;}
	}
	private void SyncTextHistory()
	{
		if(editingTextLayer is not {} id)return;var layer=document?.Layers.FirstOrDefault(l=>l.Id==id);if(layer?.TextArtwork is not {} options){ResetCreativeEditing();return;}LoadTextFields(options);TextEditStatus.Text="Editing: "+layer.Name;ScheduleTextPreview();
	}
	private async void UpdateText(object sender,RoutedEventArgs e)
	{
		if(document is null||editingTextLayer is not {} id||!document.Layers.Any(l=>l.Id==id))return;var targetDocument=document;UseTextButton.IsEnabled=UpdateTextButton.IsEnabled=false;
		try{var options=CurrentText();options.Validate();var path=await Task.Run(()=>TextArtwork.Generate(options,WorkshopService.DefaultWorkspace));if(!ReferenceEquals(document,targetDocument)||!document.Layers.Any(l=>l.Id==id))return;document.Commit("Edited text",layers=>{var layer=layers.Single(l=>l.Id==id);layer.Asset=path;layer.TextArtwork=options with {};layer.Name="Text · "+options.Text.Replace('\n',' ').Trim()[..Math.Min(70,options.Text.Replace('\n',' ').Trim().Length)];});selectedLayer=id;RememberColors(options.OutlineColor,options.Color);QueueComposite();RefreshArtwork();TextEditStatus.Text="Text updated · placement and finish preserved.";}
		catch(Exception x){TextStatus.Text=x.Message;}
		finally{UseTextButton.IsEnabled=UpdateTextButton.IsEnabled=true;}
	}
	private void ResetCreativeEditing(){editingTextLayer=null;ResetPatternEditing();if(UpdateTextButton is not null){UpdateTextButton.Visibility=Visibility.Collapsed;TextEditStatus.Text="";}}
}
