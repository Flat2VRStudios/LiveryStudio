using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;
public partial class MainWindow
{
    private long installedRefreshVersion;
    private bool installedRefreshing;
    private string installedTarget="";
    private IReadOnlyList<ManagedImport> installedEntries=[];

    private static string InstalledTargetKey(string path)
    {
        if(string.IsNullOrWhiteSpace(path))return "";
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }
    private bool InstalledTargetIsCurrent()
    {
        try{return installedTarget.Length>0&&installedTarget.Equals(InstalledTargetKey(GameFolder.Text),StringComparison.OrdinalIgnoreCase);}
        catch(ArgumentException){return false;}catch(NotSupportedException){return false;}catch(IOException){return false;}
    }
    private ManagedImport? SelectedInstalledImport()
    {
        if(installedRefreshing||!InstalledTargetIsCurrent()||InstalledList.SelectedItem is not ManagedImport selected)return null;
        return selected.Status=="Installed"&&installedEntries.Contains(selected)?selected:null;
    }
    private void ClearInstalledList()
    {
        installedEntries=[];installedTarget="";InstalledList.SelectedItem=null;InstalledList.ItemsSource=installedEntries;ReplaceOnTest.IsChecked=false;
    }
    private void GameFolderChanged(object sender,TextChangedEventArgs e)
    {
        if(loading||InstalledList is null)return;
        // Do this synchronously: an old hash scan must never restore a different game's selection.
        installedRefreshVersion++;installedRefreshing=false;ClearInstalledList();
        UpdateGameSetup();InstalledStatus.Text="Refresh to check imports for the connected game.";
        SyncInstalledActions();SaveSession();
    }
    private void InstalledSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!installedRefreshing&&SelectedInstalledImport() is null)ReplaceOnTest.IsChecked=false;
        SyncInstalledActions();
    }
    private void SyncInstalledActions()
    {
        if(InstalledSelectionHint is null)return;
        var selected=SelectedInstalledImport();var enabled=!busy&&!installedRefreshing&&connectedGame is {CanImport:true};
        InstalledUpdateButton.IsEnabled=InstalledRemoveButton.IsEnabled=enabled&&selected is not null;
        InstalledRefreshButton.IsEnabled=enabled;InstalledList.IsEnabled=enabled;
        ReplaceOnTest.IsEnabled=enabled&&selected is not null;
        InstalledSelectionHint.Text=installedRefreshing?"Checking files and import records…":busy?"Wait for the current operation to finish.":selected is not null?"Update bakes your current draft. Remove archives the selected import's files.":InstalledList.SelectedItem is ManagedImport unavailable?unavailable.Status switch
        {
            "Removed"=>"This is an archived import. Choose an installed livery to update or remove.",
            "Files changed"=>"These files changed outside the app. Review them, then refresh; update and removal are unavailable.",
            "Files missing"=>"Some installed files are missing. Restore them, then refresh to update or remove this import.",
            "Registration missing"=>"The game's livery registration is missing. Review the game settings, then refresh.",
            "Needs saved build"=>"This older import needs its saved build to verify ownership before update or removal.",
            _=>"This import could not be verified. Check the connected game folder, then refresh."
        }:installedEntries.Count>0?installedEntries.Any(x=>x.Status=="Installed")?"Choose an installed livery to update or remove. Other entries are shown for reference.":"No entries are ready to update or remove. Select one to see its status.":InstalledTargetIsCurrent()?"Bake a draft, then import it to create your first entry for this game.":"Refresh the connected game's list before selecting an import.";
    }
    private async void RefreshInstalled()=>await RefreshInstalledAsync();
    private void RefreshInstalledClick(object sender,RoutedEventArgs e)
    {
        if(!busy&&!installedRefreshing)RefreshInstalled();
    }
    private async Task RefreshInstalledAsync()
    {
        var version=++installedRefreshVersion;
        try
        {
            UpdateGameSetup();
            var target=InstalledTargetKey(GameFolder.Text);
            var previous=InstalledTargetIsCurrent()?InstalledList.SelectedItem as ManagedImport:null;
            if(target.Length==0){ClearInstalledList();InstalledStatus.Text="Connect a game to see this app's import history.";return;}
            if(!Directory.Exists(target))throw new DirectoryNotFoundException("The selected game Data folder does not exist. Choose an existing folder, then refresh.");
            if(connectedGame is null){ClearInstalledList();InstalledStatus.Text="FlatOut 4 wasn't found in that folder. Find or choose its installation in Setup.";return;}
            if(!connectedGame.CanImport){ClearInstalledList();InstalledStatus.Text="Direct imports are unavailable for this installation. Use Steam Workshop for your liveries.";return;}
            if(!InstalledTargetIsCurrent())ClearInstalledList();
            var service=Service();installedRefreshing=true;InstalledStatus.Text="Checking this app's import history and installed files…";SyncInstalledActions();
            var entries=await Task.Run(()=>new ImportManager(service).List(target));
            ApplyInstalledResults(version,target,entries,previous);
        }
        catch(Exception x)
        {
            if(version!=installedRefreshVersion)return;
            ClearInstalledList();InstalledStatus.Text="Could not refresh imports: "+x.Message;
        }
        finally
        {
            if(version==installedRefreshVersion){installedRefreshing=false;if(SelectedInstalledImport() is null)ReplaceOnTest.IsChecked=false;SyncInstalledActions();}
        }
    }
    private void ApplyInstalledResults(long version,string target,IReadOnlyList<ManagedImport> entries,ManagedImport? previous)
    {
        if(version!=installedRefreshVersion)return;
        // TextChanged normally invalidates the scan; the extra check also covers programmatic changes.
        if(!target.Equals(InstalledTargetKey(GameFolder.Text),StringComparison.OrdinalIgnoreCase)){ClearInstalledList();InstalledStatus.Text="Game folder changed. Refresh to check its imports.";return;}
        installedEntries=entries;installedTarget=target;InstalledList.ItemsSource=entries;
        InstalledList.SelectedItem=previous is null?null:entries.FirstOrDefault(x=>x.Id==previous.Id&&x.ReceiptPath.Equals(previous.ReceiptPath,StringComparison.OrdinalIgnoreCase));
        var ready=entries.Count(x=>x.Status=="Installed");var removed=entries.Count(x=>x.Status=="Removed");var attention=entries.Count-ready-removed;
        InstalledStatus.Text=entries.Count==0?"No imports recorded for this game yet. Only imports made by this app appear here.":$"{ready} installed · {attention} need attention · {removed} removed. This app's import history includes archived and changed entries.";
    }
}
