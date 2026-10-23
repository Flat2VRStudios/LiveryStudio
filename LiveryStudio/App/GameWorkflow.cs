using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;
public partial class MainWindow
{
	private bool BeginBuildWorkflow(bool preparesDraft=true)
	{
		if(busy)return false;
		// Flush can finish an edit while Invalidate is suppressed by the operation guard.
		if(preparesDraft)built=null;
		cancellation=new();Busy(true);Notice.IsOpen=false;BuildProgress.Value=0;
		return true;
	}
	private void EndBuildWorkflow()
	{
		cancellation?.Dispose();cancellation=null;Busy(false);
	}
	private async Task<bool> CloseConnectedGame(DesktopGameTarget target,CancellationToken token)
	{
		token.ThrowIfCancellationRequested();var running=DesktopGame.Running(target);
		try
		{
			if(running.Count==0)return true;var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Restart the desktop game?",Content="The connected game is running. Close it normally before changing installed liveries and launch it again in desktop mode. Finish your current race first.",PrimaryButtonText="Close game & continue",CloseButtonText="Keep playing",DefaultButton=ContentDialogButton.Close};
			if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return false;token.ThrowIfCancellationRequested();await DesktopGame.CloseAsync(running,token);return true;
		}
		finally{foreach(var process in running)process.Dispose();}
	}
	private async void RemoveInstalled(object sender,RoutedEventArgs e)
	{
		if(busy||SelectedInstalledImport() is not {} selected||!BeginBuildWorkflow(preparesDraft:false))return;
		try
		{
			var service=Service();var target=DesktopGame.Resolve(GameFolder.Text);var token=cancellation!.Token;
			Status.Text="Checking installed livery…";
			await Task.Run(()=>new ImportManager(service).ValidateRemoval(selected,target.Data),token);token.ThrowIfCancellationRequested();
			CancelButton.Visibility=Visibility.Collapsed;
			var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Remove "+selected.Receipt.Name+"?",Content="The app will unregister this livery and archive its four verified files. Other liveries and later game-setting edits will be preserved.",PrimaryButtonText="Remove import",CloseButtonText="Keep installed",DefaultButton=ContentDialogButton.Close};
			if(await dialog.ShowAsync()!=ContentDialogResult.Primary||!await CloseConnectedGame(target,token)){Status.Text="Removal canceled · your livery is still installed.";return;}
			Status.Text="Removing installed livery…";
			await Task.Run(()=>new ImportManager(service).Remove(selected,target.Data));Info("Import removed","Its files and current settings were archived beside the import receipt.");RefreshInstalled();Status.Text="Import removed · its files were archived.";
		}
		catch(OperationCanceledException){Status.Text="Removal canceled · your livery is still installed.";}
		catch(Exception x){Error(x);}finally{EndBuildWorkflow();}
	}
	private async void UpdateInstalled(object sender,RoutedEventArgs e)
	{
		if(busy||SelectedInstalledImport() is not {} selected||!BeginBuildWorkflow())return;
		try
		{
			var draft=ReadDraft();var service=Service();var target=DesktopGame.Resolve(GameFolder.Text);var token=cancellation!.Token;
			draft=await PrepareBuildDraft(draft,service,token,target,selected);
			CancelButton.Visibility=Visibility.Collapsed;
			var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Update this installed livery?",Content=$"Bake your current draft '{draft.Name}' and replace '{selected.Receipt.Name}'? The previous files will be archived. The current game must close first.",PrimaryButtonText="Bake & update",CloseButtonText="Keep installed",DefaultButton=ContentDialogButton.Close};
			if(await dialog.ShowAsync()!=ContentDialogResult.Primary||!await CloseConnectedGame(target,token)){Status.Text="Update canceled · your draft and installed livery are safe.";return;}
			var replacement=await CompileBuildDraft(draft,service,token);token.ThrowIfCancellationRequested();CancelButton.Visibility=Visibility.Collapsed;Status.Text="Updating installed livery…";
			await Task.Run(()=>new ImportManager(service).Replace(selected,replacement,target.Data));Info("Installed livery updated","Restart the game and select the new livery entry. The prior files were archived.");RefreshInstalled();Status.Text="Installed livery updated · restart the game to see it.";
		}
		catch(OperationCanceledException){Status.Text="Update canceled · your draft and installed livery are safe.";}
		catch(Exception x){Error(x);}finally{EndBuildWorkflow();}
	}
	private async Task BuildDraft()
	{
		if(!BeginBuildWorkflow())throw new InvalidOperationException("Wait for the current operation to finish.");
		try
		{
			var draft=ReadDraft();var service=Service();var token=cancellation!.Token;
			draft=await PrepareBuildDraft(draft,service,token);await CompileBuildDraft(draft,service,token);
		}
		finally{EndBuildWorkflow();}
	}
	private async Task<LiveryProject> PrepareBuildDraft(LiveryProject draft,WorkshopService service,CancellationToken token,DesktopGameTarget? target=null,ManagedImport? selected=null)
	{
		Status.Text="Checking your draft…";
		await Painter.FlushAsync();token.ThrowIfCancellationRequested();
		// Finishing a pending stroke can save new map paths. Keep the captured authoring fields.
		draft=draft with {Albedo=project.Albedo,Structure=project.Structure,EditorFile=project.EditorFile};
		await Task.Run(()=>
		{
			draft.Validate(textureSources:string.IsNullOrWhiteSpace(draft.EditorFile));service.ValidateTools();
			if(!string.IsNullOrWhiteSpace(draft.EditorFile))
			{
				var document=LayerDocument.Read(draft.EditorFile);
				if(document.CarIndex!=draft.CarIndex)throw new InvalidDataException("The editable design belongs to a different car. Open its original project before building.");
			}
			if(target is not null)
			{
				ValidateImportTarget(draft,target.Data);
				if(selected is not null)new ImportManager(service).ValidateRemoval(selected,target.Data);
			}
		},token);
		token.ThrowIfCancellationRequested();project=draft;
		if(!string.IsNullOrWhiteSpace(draft.EditorFile))
		{
			Status.Text="Preparing editable layers…";await Painter.RebuildEditableMapsAsync(draft);token.ThrowIfCancellationRequested();
			draft=draft with {Albedo=project.Albedo,Structure=project.Structure,EditorFile=project.EditorFile};
		}
		draft.Validate();project=draft;SaveSession();return draft;
	}
	private static void ValidateImportTarget(LiveryProject draft,string data)
	{
		// Advisory preflight only. Import/Replace still revalidate under their game lock before writing.
		foreach(var name in new[]{"CarsSkins.cfg","Rewards.cfg"})
		{
			var file=Path.Combine(data,"Common","Tuning","Factories",name);
			if(!File.Exists(file))throw new InvalidOperationException($"The selected game is missing editable {name} settings. Use Steam Workshop, or choose an installation that supports direct imports in Setup.");
			if((File.GetAttributes(file)&FileAttributes.ReadOnly)!=0)throw new InvalidOperationException($"The game's {name} is read-only. Make it writable before importing a livery.");
			_ = File.ReadAllText(file);
		}
		var car=draft.SelectedCar;
		if(!File.Exists(Path.Combine(data,"WIN32","GRAPH","CARS",car.Class,car.CarId,"GLASS_01.PMA"))&&!File.Exists(Path.Combine(data,"WIN32","GRAPH","CARS","COMMON","GLASS.PMA")))throw new InvalidOperationException("The selected game is missing its car glass material.");
	}
	private async Task<BuiltLivery> CompileBuildDraft(LiveryProject draft,WorkshopService service,CancellationToken token)
	{
		token.ThrowIfCancellationRequested();built=null;log.Clear();BuildProgress.Value=0;CancelButton.Visibility=Visibility.Visible;
		var result=await service.BuildAsync(draft,token);built=result;Status.Text="Livery baked · ready to drive.";RefreshLibrary();return result;
	}
	private async void TestInGame(object sender,RoutedEventArgs e)
	{
		if(!BeginBuildWorkflow())return;
		try
		{
			var draft=ReadDraft();var service=Service();var target=DesktopGame.Resolve(GameFolder.Text);var token=cancellation!.Token;var selected=ReplaceOnTest.IsChecked==true?SelectedInstalledImport():null;
			if(ReplaceOnTest.IsChecked==true&&selected is null)throw new InvalidOperationException("Choose an installed livery in Setup before using update-on-test.");
			draft=await PrepareBuildDraft(draft,service,token,target,selected);CancelButton.Visibility=Visibility.Collapsed;
			if(!await CloseConnectedGame(target,token)){Status.Text="Game test canceled · your draft is safe.";return;}
			var ready=await CompileBuildDraft(draft,service,token);token.ThrowIfCancellationRequested();CancelButton.Visibility=Visibility.Collapsed;Status.Text="Importing livery…";
			var receipt=await Task.Run(()=>selected is null?service.Import(ready,target.Data):new ImportManager(service).Replace(selected,ready,target.Data));
			using var process=DesktopGame.Launch(target);Status.Text="Desktop game launched · "+receipt.Key;Info("Livery imported · desktop game launched",$"Choose {ready.Project.SelectedCar.Name}, then Garage → Customize → Liveries. Select {receipt.Key}.");RefreshInstalled();SaveSession();
		}
		catch(OperationCanceledException){Status.Text="Game test canceled · your draft is safe.";}
		catch(Exception x){Error(x);}finally{EndBuildWorkflow();}
	}
}
