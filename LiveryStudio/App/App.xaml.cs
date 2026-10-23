using Microsoft.UI.Xaml;

namespace LiveryStudio;
public partial class App : Application
{
	private Window? window;
	public App() { InitializeComponent(); UnhandledException += (_,e)=> { var folder=Core.WorkshopService.DefaultWorkspace; Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder,"crash.log"),e.Exception+Environment.NewLine); }; }
	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		window=new MainWindow();
		if(window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter limits){limits.PreferredMinimumWidth=640;limits.PreferredMinimumHeight=480;}
		window.Activate();
		if(window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Maximize();
	}
}
