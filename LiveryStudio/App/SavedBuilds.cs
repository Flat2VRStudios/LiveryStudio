using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;

internal sealed record SavedBuildItem(BuiltLivery Build,DateTime Saved)
{
    public string Name=>Build.Project.Name;
    public string Car=>Build.Project.SelectedCar.Display;
    public string CarImage=>Path.Combine(AppContext.BaseDirectory,"Assets","Cars",Build.Project.CarIndex.ToString("00")+".png");
    public string Source=>Path.Combine(Build.BuildRoot,"Source.flproject");
    public bool HasSource=>File.Exists(Source);
    public bool CanOpen=>HasSource||File.Exists(Build.Project.Albedo)&&File.Exists(Build.Project.Structure);
    public string Detail=>Saved.ToString("g")+" · "+(HasSource?"Editable source included":"Texture build");
    public override string ToString()=>Name+" · "+Car+" · "+Detail;
}

public partial class MainWindow
{
    private SavedBuildItem[] savedBuilds=[];
    private bool loadingBuildLibrary;
    private int libraryLoadRevision,unreadableBuilds;
    private BuiltLivery? OutputBuild=>currentWorkspace==1?(BuildList.SelectedItem as SavedBuildItem)?.Build:built;

    private async void RefreshLibrary()
    {
        if(BuildList is null)return;
        var revision=++libraryLoadRevision;var selected=(BuildList.SelectedItem as SavedBuildItem)?.Build.Id;
        loadingBuildLibrary=true;LibraryStatus.Text="Loading saved builds…";SyncLibraryActions();
        try
        {
            var result=await Task.Run(()=>
            {
                var items=new List<SavedBuildItem>();var skipped=0;var root=Path.Combine(WorkshopService.DefaultWorkspace,"builds");
                if(Directory.Exists(root))foreach(var folder in Directory.EnumerateDirectories(root))
                {
                    var file=Path.Combine(folder,"livery.json");if(!File.Exists(file))continue;
                    try
                    {
                        if(new FileInfo(file).Length>2*1024*1024)throw new InvalidDataException("Build record is too large.");
                        var item=JsonSerializer.Deserialize<BuiltLivery>(File.ReadAllText(file),Contract.Json)??throw new InvalidDataException("Empty build record.");
                        if(item.Project is null||string.IsNullOrWhiteSpace(item.BuildRoot))throw new InvalidDataException("Incomplete build record.");
                        item.Project.Validate(textureSources:false);items.Add(new(item,File.GetLastWriteTime(file)));
                    }
                    catch(Exception e) when(e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException){skipped++;}
                }
                return (Items:items.OrderByDescending(i=>i.Saved).ToArray(),Skipped:skipped);
            });
            if(revision!=libraryLoadRevision)return;savedBuilds=result.Items;unreadableBuilds=result.Skipped;FilterBuilds(selected);
        }
        catch(Exception e){if(revision==libraryLoadRevision){savedBuilds=[];BuildList.ItemsSource=savedBuilds;LibraryStatus.Text="Could not read saved builds: "+e.Message;}}
        finally{if(revision==libraryLoadRevision){loadingBuildLibrary=false;SyncLibraryActions();}}
    }
    private void RefreshBuildsClick(object sender,RoutedEventArgs e)=>RefreshLibrary();
    private void BuildSearchChanged(object sender,TextChangedEventArgs e){if(BuildList is not null)FilterBuilds((BuildList.SelectedItem as SavedBuildItem)?.Build.Id);}
    private void FilterBuilds(string? selected)
    {
        var query=BuildSearch.Text.Trim();var items=savedBuilds.Where(i=>query.Length==0||i.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||i.Car.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        BuildList.ItemsSource=items;BuildList.SelectedItem=items.FirstOrDefault(i=>i.Build.Id==selected);
        LibraryStatus.Text=savedBuilds.Length==0?"Your baked liveries will appear here. Start in Create livery or the 3D paint studio.":items.Length==0?"No builds match. Try another name or car.":$"{items.Length} saved {(items.Length==1?"build":"builds")} · newest first";
        if(unreadableBuilds>0)LibraryStatus.Text+=$" · {unreadableBuilds} unreadable {(unreadableBuilds==1?"record":"records")} skipped";
        SyncLibraryActions();
    }
    private void BuildSelected(object sender,SelectionChangedEventArgs e)=>SyncLibraryActions();
    private void SyncLibraryActions()
    {
        if(OpenSavedBuildButton is null)return;
        var selected=BuildList.SelectedItem as SavedBuildItem;var ready=!busy&&!loadingBuildLibrary;
        BuildList.IsEnabled=BuildSearch.IsEnabled=ready;RefreshBuildsButton.IsEnabled=ready;
        LibraryExportButton.IsEnabled=LibraryImportButton.IsEnabled=LibraryPublishButton.IsEnabled=ready&&selected is not null;
        OpenSavedBuildButton.IsEnabled=ready&&selected?.CanOpen==true;
        OpenSavedBuildButton.Content=selected is {HasSource:false}?"Open texture copy":"Open editable copy";
        LibrarySelectionTitle.Text=selected?.Name??"Choose a saved build";
        LibrarySelectionHint.Text=selected is null?"Browse freely. Your current design stays open until you choose an editable copy.":selected.HasSource?"Open a copy to keep editing. Your current design will be preserved in Previous draft.":selected.CanOpen?"This older build has texture maps only. Opening a copy preserves your current design.":"Editable sources are unavailable. You can still import or export the compiled build.";
    }
    private bool BeginOutputOperation(string status)
    {
        if(busy)return false;Busy(true);CancelButton.Visibility=Visibility.Collapsed;BuildProgress.IsIndeterminate=true;Notice.IsOpen=false;Status.Text=status;return true;
    }
    private void EndOutputOperation(){BuildProgress.IsIndeterminate=false;Busy(false);}
    private async void OpenSelectedBuild(object sender,RoutedEventArgs e)
    {
        if(BuildList.SelectedItem is not SavedBuildItem item||!BeginOutputOperation("Opening an editable copy…"))return;
        try
        {
            built=null;EnableOutput(false);await Painter.FlushAsync();var current=ReadDraft();
            var opened=await Task.Run(()=>item.HasSource?ProjectBundle.Open(item.Source,WorkshopService.DefaultWorkspace):item.Build.Project with {EditorFile=""});opened.Validate();
            previousDraft=await Task.Run(()=>DraftRecovery.Save(current,WorkshopService.DefaultWorkspace));
            project=opened;built=null;ApplyProject();View(string.IsNullOrWhiteSpace(opened.EditorFile)?0:3);
            if(currentWorkspace==3)await Painter.LoadAsync(project);SaveSession();Status.Text="Opened "+project.Name+" · your previous draft is preserved.";
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
    private async void ImportToGame(object sender,RoutedEventArgs e)
    {
        if(OutputBuild is not {} ready||!BeginOutputOperation("Importing "+ready.Project.Name+"…"))return;
        try
        {
            var service=Service();var data=GameFolder.Text;var receipt=await Task.Run(()=>service.Import(ready,data));
            Status.Text="Imported "+receipt.Name+" · restart the game to see it.";RefreshInstalled();Info("Livery imported",$"Select {ready.Project.SelectedCar.Name}, then Garage → Customize → Liveries. Choose {receipt.Key}. Game settings were backed up.");SaveSession();
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
    private async void ExportPackage(object sender,RoutedEventArgs e)
    {
        if(OutputBuild is not {} ready||!BeginOutputOperation("Choose where to export "+ready.Project.Name+"…"))return;
        try
        {
            if(await SavePick(".zip",ready.Project.Name+".workshop") is not {} file){Status.Text="Export canceled · your build is unchanged.";return;}
            Status.Text="Exporting "+ready.Project.Name+"…";await Task.Run(()=>WorkshopService.Export(ready,file));Status.Text="Package exported · "+Path.GetFileName(file);Info("Package exported",file);
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
    private async void Publish(object sender,RoutedEventArgs e)
    {
        if(OutputBuild is not {} ready||!BeginOutputOperation("Review Steam Workshop publication…"))return;
        try
        {
            ready.Project.Validate(publishing:true,textureSources:false);
            var target=new ComboBox{Header="Game Workshop"};
            target.Items.Add(SteamWorkshop.GameName(SteamWorkshop.FlatOutVR));target.Items.Add(SteamWorkshop.GameName(SteamWorkshop.FlatOut4));target.SelectedIndex=0;
            var content=new StackPanel{Spacing=12};content.Children.Add(new TextBlock{Text=$"Publish '{ready.Project.Name}' publicly using your signed-in Steam account? Publishing this saved build again updates the same item. Check the title, description and banner before continuing.",TextWrapping=TextWrapping.Wrap});content.Children.Add(target);
            var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Publish to Steam Workshop?",Content=content,PrimaryButtonText="Publish",CloseButtonText="Keep local",DefaultButton=ContentDialogButton.Close};
            if(await dialog.ShowAsync()!=ContentDialogResult.Primary){Status.Text="Publication canceled · your build remains local.";return;}
            Status.Text="Uploading to Steam Workshop…";var publication=await Service().PublishAsync(ready,appId:target.SelectedIndex==0?SteamWorkshop.FlatOutVR:SteamWorkshop.FlatOut4);
            Info(publication.Published?"Published to Steam Workshop":"Workshop agreement required",(publication.Published?"Steam confirmed the upload. ":"Open the item and accept Steam's Workshop agreement to make it visible. ")+publication.Url);
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
}
