using LiveryStudio.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Color = Windows.UI.Color;

namespace LiveryStudio;
public partial class MainWindow : Window
{
	private LiveryProject project=new(){UseSrgbPaintColors=true};
	private BuiltLivery? built;
    private CarBuilderWindow? carBuilder;
    private void ShowCarBuilder(object sender,RoutedEventArgs e){try{if(carBuilder is null){carBuilder=new CarBuilderWindow(GameFolder.Text,WorkshopService.DefaultTools,advanced:AdvancedModeToggle.IsChecked==true);carBuilder.Closed+=(_,_)=>carBuilder=null;}carBuilder.Activate();}catch(Exception x){Error(x);}}
	private bool loading=true, busy;
	private bool restoringSession=true, restoredSession;
	private int currentWorkspace, resumeWorkspace;
	private CancellationTokenSource? cancellation;
	private readonly List<string> log=[];
	private readonly long hwnd;
	public MainWindow()
	{
		InitializeComponent(); Title="Livery Studio · FlatOut 4";
		FinishSelector.ItemsSource=FinishPreset.All;Painter.FinishEdited+=ApplyPainterFinish;
		AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","LiveryStudio.ico"));
		hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
		var workArea=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
		var windowSize=new Windows.Graphics.SizeInt32(Math.Min(1420,workArea.Width-32),Math.Min(1040,workArea.Height-32));AppWindow.Resize(windowSize);
		AppWindow.Move(new Windows.Graphics.PointInt32(workArea.X+(workArea.Width-windowSize.Width)/2,workArea.Y+(workArea.Height-windowSize.Height)/2));ExtendsContentIntoTitleBar=true;SetTitleBar(TitleBar);
		AppWindow.TitleBar.ButtonBackgroundColor=Color.FromArgb(255,24,26,28); AppWindow.TitleBar.ButtonForegroundColor=Colors.White;
		CarSelector.ItemsSource=Contract.Cars;
		PaintCarSelector.ItemsSource=Contract.Cars;Painter.HostWindow=this;Painter.ImagePreparationReviewChanged+=ImageReviewHostChanged;
		Painter.CameraBookmarksChanged+=views=>{project.CameraBookmarks=views;SaveSession();};
		Root.AddHandler(UIElement.PointerPressedEvent,new Microsoft.UI.Xaml.Input.PointerEventHandler((_,e)=>Painter.DismissInspectorOutside(e.GetCurrentPoint(Root).Position)),true);
		Painter.MapsSaved+=(albedo,surface,editor)=>{project.Albedo=albedo;project.Structure=surface;project.EditorFile=editor;texturePreviewsDirty=true;if(currentWorkspace==0)UpdateImages();Invalidate();Status.Text=AdvancedModeToggle.IsChecked==true?"Editable layers saved · bake to build the latest design.":"Draft saved · Save project keeps a portable copy.";};
		Painter.TemplateRequested+=async path=>{try{await OpenProjectPath(path);}catch(Exception x){Error(x);}};
		Painter.Failure+=message=>log.Add(message);Painter.IndependentSidesRequested+=MakeIndependentSides;
		GameFolder.Text="";
		try { if(sessions.Read() is {} state) { AdvancedModeToggle.IsChecked=state.AdvancedMode;project=state.Project;GameFolder.Text=state.Game??"";previousDraft=state.PreviousDraft??"";resumeWorkspace=state.Workspace is >=0 and <=3?state.Workspace.Value:string.IsNullOrWhiteSpace(project.EditorFile)?0:3;restoredSession=true; } } catch(Exception e){log.Add("Could not restore session: "+e.Message);}
		ApplyProject(); loading=false; UpdateGameSetup();ApplyEditorMode();if(AdvancedModeToggle.IsChecked!=true&&resumeWorkspace==0)resumeWorkspace=3; RefreshLibrary();PreviousDraftButton.IsEnabled=File.Exists(previousDraft);
		Root.Loaded+=RestoreWorkspace;
		AppWindow.Closing+=ClosingWindow;Closed+=(_,_)=>Painter.Dispose();
	}
	private async void RestoreWorkspace(object sender,RoutedEventArgs e)
	{
		Root.Loaded-=RestoreWorkspace;ApplyWindowLayout("comfort");var restored=false;
		try
		{
			Busy(true);BuildProgress.Visibility=CancelButton.Visibility=Visibility.Collapsed;
			await IsolateResumedDraft();
			if(resumeWorkspace==3)await OpenPaintStudio();
			else
			{
				View(resumeWorkspace);if(resumeWorkspace==2)RefreshInstalled();
				Status.Text=restoredSession?$"Restored {project.Name} · your draft is ready.":"Choose a car or start with a design.";
			}
			restored=true;
		}
		catch(Exception x){View(0);Error(x);}
		finally{Busy(false);restoringSession=false;if(restored)SaveSession();}
        await DetectGameAsync();
	}
	private WorkshopService Service()
	{
		var s=new WorkshopService(WorkshopService.DefaultTools,WorkshopService.DefaultWorkspace);
		s.Log+=line=>DispatcherQueue.TryEnqueue(()=>{ log.Add(line); if(log.Count>10000)log.RemoveAt(0); });
		s.Progress+=(value,text)=>DispatcherQueue.TryEnqueue(()=>{BuildProgress.Value=value;Status.Text=text;}); return s;
	}
	private LiveryProject ReadDraft() => ReadFinish(project with { Name=LiveryName.Text,CarIndex=(CarSelector.SelectedItem as Car)?.Index ?? 0,OuterColor=OuterHex.Text,InnerColor=InnerHex.Text,OuterGloss=OuterGloss.Value,InnerGloss=InnerGloss.Value,Metalness=Metalness.Value,ClearCoat=ClearCoat.Value,Description=Description.Text });
	private void ApplyProject()
	{
		var before=loading;loading=true;
		LiveryName.Text=project.Name; CarSelector.SelectedItem=Contract.Cars.FirstOrDefault(c=>c.Index==project.CarIndex) ?? Contract.Cars[0];PaintCarSelector.SelectedItem=CarSelector.SelectedItem;
		OuterHex.Text=project.OuterColor;InnerHex.Text=project.InnerColor;OuterGloss.Value=project.OuterGloss;InnerGloss.Value=project.InnerGloss;Metalness.Value=project.Metalness;ClearCoat.Value=project.ClearCoat;Description.Text=project.Description;
		ApplyFinishWidgets();
		loading=before; UpdateCar();UpdateImages();UpdateSwatches();
	}
	private void Invalidate()
	{
		if(loading || busy)return; built=null;EnableOutput(false);Status.Text=AdvancedModeToggle.IsChecked==true?"Draft changed · bake to apply your latest edits.":"Saving your changes…";SaveSession();
	}
	private void DraftChanged(object sender,TextChangedEventArgs e){ if(loading)return;UpdateSwatches();Invalidate();Painter.FinishChanged(ReadDraft()); }
	private void FinishChanged(object sender,RangeBaseValueChangedEventArgs e){if(!loading){ResetFinishPreset();Invalidate();Painter.FinishChanged(ReadDraft());}}
	private void UpdateCar(){ if(CarSelector.SelectedItem is not Car car)return;CarTitle.Text=car.Name;CarSubtitle.Text=car.Category+" collection";CarImage.Source=new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","Cars",car.Index.ToString("00")+".png"))); }
	private void UpdateSwatches()
	{
		if(OuterHex is null || InnerHex is null || OuterSwatch is null)return;
        try { var c=ParseColor(DesignColor.PaintPickerHex(ReadDraft(),OuterHex.Text));OuterSwatch.Background=new SolidColorBrush(c);OuterSwatchLabel.Foreground=SwatchText(c); } catch { }
        try { var c=ParseColor(DesignColor.PaintPickerHex(ReadDraft(),InnerHex.Text));InnerSwatch.Background=new SolidColorBrush(c);InnerSwatchLabel.Foreground=SwatchText(c); } catch { }
	}
    private static Brush SwatchText(Color color){var rgb=PaintFinish.ToLinear(new System.Numerics.Vector3(color.R/255f,color.G/255f,color.B/255f));return new SolidColorBrush(System.Numerics.Vector3.Dot(rgb,new(.2126f,.7152f,.0722f))>.179f?Colors.Black:Colors.White);}
	private static Color ParseColor(string text){_ = LiveryProject.ColorVector(text); return Color.FromArgb(255,Convert.ToByte(text.Substring(1,2),16),Convert.ToByte(text.Substring(3,2),16),Convert.ToByte(text.Substring(5,2),16));}
	private bool texturePreviewsDirty;
	private void UpdateImages(){texturePreviewsDirty=false;Preview(AlbedoPreview,project.Albedo);Preview(StructurePreview,project.Structure);Preview(BannerPreview,project.Banner);AlbedoName.Text=File.Exists(project.Albedo)?Path.GetFileName(project.Albedo):"Choose texture";StructureName.Text=File.Exists(project.Structure)?Path.GetFileName(project.Structure):"Choose texture";BannerName.Text=File.Exists(project.Banner)?Path.GetFileName(project.Banner):"256 × 256 recommended for Steam Workshop";}
	private async void Preview(Microsoft.UI.Xaml.Controls.Image target,string path)
	{
		try
		{
			if(!File.Exists(path)){target.Source=null;return;}
			if(target==BannerPreview){target.Source=new BitmapImage(new Uri(path)){DecodePixelWidth=600};return;}
			TgaFrame frame;
			if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase))frame=TgaDecoder.Decode(File.ReadAllBytes(path));
			else
			{
				var source=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
				using var read=await source.OpenReadAsync();
				var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(read);
				var pixels=await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,Windows.Graphics.Imaging.BitmapAlphaMode.Straight,new Windows.Graphics.Imaging.BitmapTransform(),Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
				frame=new TgaFrame((int)decoder.PixelWidth,(int)decoder.PixelHeight,pixels.DetachPixelData());
			}
			// Texture alpha is a shader mask, so show RGB even where alpha is zero.
			for(var i=3;i<frame.Pixels.Length;i+=4)frame.Pixels[i]=255;
			var dir=Path.Combine(WorkshopService.DefaultWorkspace,"previews");Directory.CreateDirectory(dir);
			var previewPath=Path.Combine(dir,Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(previewPath,[]);
			var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(previewPath);
			using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
			var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
			encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Rgba8,Windows.Graphics.Imaging.BitmapAlphaMode.Straight,(uint)frame.Width,(uint)frame.Height,96,96,frame.Pixels);
			await encoder.FlushAsync();target.Source=new BitmapImage(new Uri(previewPath)){DecodePixelWidth=600};
		}
		catch(Exception e){target.Source=null;log.Add("Preview: "+e.Message);}
	}
	private async Task<string?> Pick(string[] types)
	{
		var picker=new FileOpenPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,new IntPtr(hwnd));foreach(var type in types)picker.FileTypeFilter.Add(type);return (await picker.PickSingleFileAsync())?.Path;
	}
	private async Task<string?> SavePick(string extension,string name)
	{
		var picker=new FileSavePicker { SuggestedFileName=name };WinRT.Interop.InitializeWithWindow.Initialize(picker,new IntPtr(hwnd));picker.FileTypeChoices.Add(extension==".flproject"?"Editable Livery Studio project":"Livery Studio file",[extension]);return (await picker.PickSaveFileAsync())?.Path;
	}
	private async Task<string?> FolderPick(){var picker=new FolderPicker();WinRT.Interop.InitializeWithWindow.Initialize(picker,new IntPtr(hwnd));picker.FileTypeFilter.Add("*");return (await picker.PickSingleFolderAsync())?.Path;}
	private async void PickBanner(object sender,RoutedEventArgs e){try {if(await Pick([".png",".jpg",".jpeg"]) is { } p){project.Banner=p;UpdateImages();Invalidate();}}catch(Exception x){Error(x);}}
	private void EnableOutput(bool enabled){ImportButton.IsEnabled=ExportButton.IsEnabled=PublishButton.IsEnabled=enabled && !busy;SyncLibraryActions();}
	private static void SetControls(DependencyObject parent,bool enabled){if(parent is Control control)control.IsEnabled=enabled;for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)SetControls(VisualTreeHelper.GetChild(parent,i),enabled);}
	private void Busy(bool value)
	{
		if(!value)project=ReadDraft();
		busy=value;var before=loading;loading=true;
		try
		{
			// Enabling WinUI controls can raise selection/value events. They must not
			// invalidate a completed operation or overwrite its result message.
			SetControls(LibraryView,!value);SetControls(PaintWorkspace,!value);PaintWorkspace.IsHitTestVisible=!value;PaintNav.IsEnabled=!value;
			SetControls(DesignView,!value);SetControls(SetupView,!value);SetupView.IsHitTestVisible=!value;
			AdvancedModeToggle.IsEnabled=NewButton.IsEnabled=OpenButton.IsEnabled=SaveButton.IsEnabled=!value;
			PreviousDraftButton.IsEnabled=!value&&File.Exists(previousDraft);Form.IsHitTestVisible=!value;
			foreach(var control in new Control[]{LiveryName,CarSelector,OuterHex,InnerHex,OuterGloss,InnerGloss,Metalness,ClearCoat,Description})control.IsEnabled=!value;
			BakeButton.IsEnabled=TestGameButton.IsEnabled=!value;DesignNav.IsEnabled=LibraryNav.IsEnabled=SetupNav.IsEnabled=!value;
			BuildProgress.Visibility=CancelButton.Visibility=value?Visibility.Visible:Visibility.Collapsed;
			if(!value)ApplyFinishWidgets();
		}
		finally{loading=before;}
		if(!value){Painter.FinishChanged(ReadDraft());Painter.RefreshControlState();}
		EnableOutput(built is not null);UpdateGameSetup();SyncInstalledActions();SyncLibraryActions();
	}
	private async void Bake(object sender,RoutedEventArgs e){if(busy)return;if(AdvancedModeToggle.IsChecked!=true){ShowSimpleGameMenu();return;}try{await BuildDraft();Info("Ready for the track","Your editable design was compiled and packaged with the bundled tools.");}catch(OperationCanceledException){Status.Text="Build canceled · your draft is safe.";}catch(Exception x){Error(x);}}
	private void CancelBuild(object sender,RoutedEventArgs e)=>cancellation?.Cancel();
	private void View(int page)
	{
		if(page is <0 or >3)throw new ArgumentOutOfRangeException(nameof(page));currentWorkspace=page;
		if(page==0&&texturePreviewsDirty)UpdateImages();SyncSimpleShell();
		DesignView.Visibility=page==0?Visibility.Visible:Visibility.Collapsed;LibraryView.Visibility=page==1?Visibility.Visible:Visibility.Collapsed;SetupView.Visibility=page==2?Visibility.Visible:Visibility.Collapsed;PaintWorkspace.Visibility=page==3?Visibility.Visible:Visibility.Collapsed;
		var nav=new[]{DesignNav,LibraryNav,SetupNav,PaintNav};for(var i=0;i<nav.Length;i++)nav[i].Background=i==page?(Brush)Application.Current.Resources["StudioNavSelectedBrush"]:new SolidColorBrush(Colors.Transparent);
		PageTitle.Text=page==3&&AdvancedModeToggle.IsChecked!=true?"Paint your car":new[]{"Make it yours.","Your collection.","Ready to drive.","Every angle. Every detail."}[page];
		PageSubtitle.Text=new[]{"Create a signature look. Take it straight to the track.","Your finished liveries, always close at hand.","Find your game and take your design to the track.","Paint on the car. Place your artwork. The texture takes care of itself."}[page];
		OutputCommands.Visibility=BakeButton.Visibility=page==1?Visibility.Collapsed:Visibility.Visible;LibraryOutputCommands.Visibility=OpenSavedBuildButton.Visibility=page==1?Visibility.Visible:Visibility.Collapsed;SyncLibraryActions();
		SyncSimpleShell();SaveSession();
	}

	private void ShowDesign(object sender,RoutedEventArgs e)=>View(0);private void ShowLibrary(object sender,RoutedEventArgs e){RefreshLibrary();View(1);}private void ShowSetup(object sender,RoutedEventArgs e){UpdateGameSetup();RefreshInstalled();View(2);}
	private async void ShowPaint(object sender,RoutedEventArgs e)
	{
        if(!BeginOutputOperation("Opening the 3D paint studio…"))return;
        built=null;EnableOutput(false);
        try {await OpenPaintStudio();}catch(Exception x){Error(x);}finally{EndOutputOperation();}
	}
    private async Task OpenPaintStudio()
    {
        await Painter.FlushAsync();project=ReadDraft();var missingMaps=!File.Exists(project.Albedo)||!File.Exists(project.Structure);var hasEditor=!string.IsNullOrWhiteSpace(project.EditorFile);
        if(!hasEditor&&missingMaps){var maps=Service().Sample(project.SelectedCar);project.Albedo=maps.Albedo;project.Structure=maps.Structure;UpdateImages();Invalidate();}
        var before=loading;loading=true;try{PaintCarSelector.SelectedItem=project.SelectedCar;}finally{loading=before;}
        View(3);Status.Text=$"Opening {project.Name}…";await Painter.LoadAsync(project);
        if(hasEditor&&missingMaps)await Painter.RebuildEditableMapsAsync(project);
        Status.Text=$"Editing {project.Name} · changes save automatically.";
    }
	private void OpenManual(object sender,RoutedEventArgs e){try {Process.Start(new ProcessStartInfo(Path.Combine(WorkshopService.DefaultTools,"Doc","FLATOUT_4_Tool.PDF")){UseShellExecute=true});}catch(Exception x){Error(x);}}
	private async void ShowLog(object sender,RoutedEventArgs e){var text=string.Join(Environment.NewLine,log);if(OutputBuild is {} selected && File.Exists(Path.Combine(selected.BuildRoot,"build.log")))text=File.ReadAllText(Path.Combine(selected.BuildRoot,"build.log"));var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Build log",Content=new TextBox{Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=Math.Min(420,Math.Max(120,Root.ActualHeight-220)),MaxWidth=650,HorizontalAlignment=HorizontalAlignment.Stretch},PrimaryButtonText="Copy log",CloseButtonText="Close"};if(await dialog.ShowAsync()==ContentDialogResult.Primary){var data=new DataPackage();data.SetText(text);Clipboard.SetContent(data);}}
	private void Info(string title,string message){Notice.Title=title;Notice.Message=message;Notice.Severity=InfoBarSeverity.Success;Notice.IsOpen=true;}
	private void Error(Exception e){log.Add(e.ToString());Notice.Title="Let's fix this";Notice.Message=e.Message;Notice.Severity=InfoBarSeverity.Error;Notice.IsOpen=true;Status.Text="Action needs attention · your draft is safe.";}
}
