using LiveryStudio.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class RoutedEventArgs : EventArgs { }
}
namespace Microsoft.UI.Xaml.Controls
{
    public sealed class TextChangedEventArgs : EventArgs { }
    public sealed class SelectionChangedEventArgs : EventArgs { }
    public enum ContentDialogButton { Close, Primary }
    public enum ContentDialogResult { None, Primary }
    public sealed class ContentDialog
    {
        public object? XamlRoot { get; set; }
        public string Title { get; set; }="";
        public string Content { get; set; }="";
        public string PrimaryButtonText { get; set; }="";
        public string CloseButtonText { get; set; }="";
        public ContentDialogButton DefaultButton { get; set; }
        public static int Count;
        public Task<ContentDialogResult> ShowAsync(){Count++;return Task.FromResult(ContentDialogResult.None);}
    }
}
namespace LiveryStudio
{
    public sealed class Widget
    {
        public object? ItemsSource,SelectedItem,XamlRoot;
        public string Text="";
        public bool IsOpen,IsEnabled,IsIndeterminate;
        public bool? IsChecked;
        public double Value;
        public Visibility Visibility;
    }
    public sealed class PainterStub
    {
        public Task Flush=Task.CompletedTask;
        public int Rebuilds,FlushCalls,Loads;
        public Action? BeforeFlush,AfterFlush;
        public Action<LiveryProject>? OnRebuild;
        public async Task FlushAsync(){FlushCalls++;BeforeFlush?.Invoke();await Flush;AfterFlush?.Invoke();}
        public Task RebuildEditableMapsAsync(LiveryProject p){Rebuilds++;OnRebuild?.Invoke(p);return Task.CompletedTask;}
        public Task LoadAsync(LiveryProject p){Loads++;return Task.CompletedTask;}
    }
    public partial class MainWindow
    {
        private bool busy,loading=false;
        private CancellationTokenSource? cancellation;
        private LiveryProject project=new();
        private BuiltLivery? built;
        private GameInstallation? connectedGame;
        private string previousDraft="";
        private int currentWorkspace;
        private readonly List<string> log=[];
        public readonly Widget InstalledList=new(),InstalledStatus=new(),GameFolder=new(),Root=new(),Status=new(),CancelButton=new(),BuildProgress=new(),Notice=new(),ReplaceOnTest=new(),PaintWorkspace=new();
        public readonly Widget InstalledUpdateButton=new(),InstalledRemoveButton=new(),InstalledRefreshButton=new(),InstalledSelectionHint=new();
        public readonly PainterStub Painter=new();
        public LiveryProject FormDraft=new();
        public string Tools="",Workspace="";
        public Exception? Failure;
        public int BusyReleases,SessionSaves,OpenPickerCalls,SavePickerCalls,ProjectApplications;
        public Task<string?> OpenChoice=Task.FromResult<string?>(null),SaveChoice=Task.FromResult<string?>(null);
        public string SuggestedFile="",Information="";
        public TaskCompletionSource Done=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsBusy=>busy;
        public bool HasBuilt=>built is not null;
        public CancellationTokenSource? TokenSource=>cancellation;
        public LiveryProject Project=>project;
        public string PreviousDraft=>previousDraft;
        public int WorkspacePage=>currentWorkspace;
        public MainWindow(string? fileWorkspace=null){if(fileWorkspace is not null)projectFileWorkspace=fileWorkspace;}
        public void Initialize(LiveryProject p,BuiltLivery? prior=null){project=p;FormDraft=p;built=prior;UpdateGameSetup();}
        private void UpdateGameSetup()=>connectedGame=GameInstallationDiscovery.Inspect(GameFolder.Text);
        private WorkshopService Service()=>new(Tools,Workspace);
        private LiveryProject ReadDraft()=>FormDraft with {};
        private void Busy(bool value){busy=value;SyncInstalledActions();if(!value){BusyReleases++;Done.TrySetResult();}}
        private void Info(string title,string message){Information=title+"\n"+message;}
        private void Error(Exception e){Failure=e;}
        private void SaveSession(){SessionSaves++;}
        private void ApplyProject(){ProjectApplications++;FormDraft=project with{};}
        private void EnableOutput(bool value){}
        private void RefreshLibrary(){}
        private void View(int page){currentWorkspace=page;SaveSession();}
        private bool BeginOutputOperation(string status){if(busy)return false;Busy(true);CancelButton.Visibility=Visibility.Collapsed;BuildProgress.IsIndeterminate=true;Notice.IsOpen=false;Status.Text=status;return true;}
        private void EndOutputOperation(){BuildProgress.IsIndeterminate=false;Busy(false);}
        private Task<string?> Pick(string[] types){OpenPickerCalls++;return OpenChoice;}
        private Task<string?> SavePick(string extension,string name){SavePickerCalls++;SuggestedFile=name;return SaveChoice;}
        public void OpenFilePicker()=>OpenProject(this,new());
        public void SaveFilePicker()=>SaveProject(this,new());
        public Task OpenPath(string path)=>OpenProjectPath(path);
        public Task Bake()=>BuildDraft();
        public void Test()=>TestInGame(this,new());
        public void Update()=>UpdateInstalled(this,new());
        public void Remove()=>RemoveInstalled(this,new());
        public bool Begin(bool draft=true)=>BeginBuildWorkflow(draft);
        public void End()=>EndBuildWorkflow();
        public void Cancel()=>cancellation!.Cancel();
        public void MapsSaved(string color,string surface,string editor){project.Albedo=color;project.Structure=surface;project.EditorFile=editor;FormDraft=FormDraft with{Albedo=color,Structure=surface,EditorFile=editor};}
        public Task<LiveryProject> Prepare(LiveryProject p,DesktopGameTarget? target=null,ManagedImport? selected=null)=>PrepareBuildDraft(p,Service(),cancellation!.Token,target,selected);
        public void SelectImport(ManagedImport item){installedTarget=InstalledTargetKey(GameFolder.Text);installedEntries=[item];InstalledList.ItemsSource=installedEntries;InstalledList.SelectedItem=item;InstalledSelectionChanged(this,new());}
        public void SelectListed(ManagedImport? item){InstalledList.SelectedItem=item;InstalledSelectionChanged(this,new());}
        public Task RefreshImports()=>RefreshInstalledAsync();
        public void ChangeGameFolder(string path){GameFolder.Text=path;GameFolderChanged(this,new());}
        public long ImportRevision=>installedRefreshVersion;
        public IReadOnlyList<ManagedImport> Imports=>installedEntries;
        public void ApplyImportScan(long version,string target,IReadOnlyList<ManagedImport> entries,ManagedImport? previous)=>ApplyInstalledResults(version,target,entries,previous);
        public void SyncImports()=>SyncInstalledActions();
    }
}
