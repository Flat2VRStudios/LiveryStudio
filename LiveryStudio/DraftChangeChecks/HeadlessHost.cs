using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class RoutedEventArgs : EventArgs { }
    public class FrameworkElement { public object? Tag { get; set; } }
    public sealed class DragEventArgs : EventArgs
    {
        public bool Handled;
        public DataPackageOperation AcceptedOperation;
        public DataPackageView DataView = new();
    }
}
namespace Microsoft.UI.Xaml.Controls { public sealed class SelectionChangedEventArgs : EventArgs { } }
namespace Windows.ApplicationModel.DataTransfer
{
    public enum DataPackageOperation { None, Copy }
    public static class StandardDataFormats { public const string StorageItems="StorageItems"; }
    public sealed record StorageItem(string Path);
    public sealed class DataPackageView
    {
        public bool HasStorage=true;
        public int Reads;
        public Task<IReadOnlyList<StorageItem>> Items=Task.FromResult<IReadOnlyList<StorageItem>>([]);
        public bool Contains(string format)=>HasStorage&&format==StandardDataFormats.StorageItems;
        public Task<IReadOnlyList<StorageItem>> GetStorageItemsAsync(){Reads++;return Items;}
    }
}
namespace LiveryStudio
{
    // Only the live-app workspace location is substituted. Sample lookup and all decoding,
    // bundling, recovery, and handler implementations are the real production code.
    internal sealed class WorkshopService(string tools,string workspace)
    {
        public static string DefaultWorkspace="";
        public (string Albedo,string Structure) Sample(Car car)=>new Core.WorkshopService(tools,workspace).Sample(car);
    }
    public sealed class Widget
    {
        public object? SelectedItem;
        public string Text="";
        public bool IsIndeterminate;
        public Visibility Visibility;
    }
    public sealed class PainterStub
    {
        public Task Flush=Task.CompletedTask;
        public Action? AfterFlush;
        public int FlushCalls,Loads;
        public TaskCompletionSource FlushEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task FlushAsync(){FlushCalls++;FlushEntered.TrySetResult();await Flush;AfterFlush?.Invoke();}
        public Task LoadAsync(LiveryProject project){Loads++;return Task.CompletedTask;}
    }
    public partial class MainWindow
    {
        private bool busy,loading;
        private LiveryProject project=new();
        private object? built=new();
        private string previousDraft="";
        public readonly Widget CarSelector=new(),PaintCarSelector=new(),Status=new(),BuildProgress=new();
        public readonly PainterStub Painter=new();
        public LiveryProject FormDraft=new();
        public string Tools="",Workspace="";
        public Task<string?> Choice=Task.FromResult<string?>(null);
        public Exception? Failure;
        public int BusyReleases,PickerCalls,SessionSaves,ProjectApplications,Page=-1;
        public bool OutputEnabled=true;
        public string Information="";
        public TaskCompletionSource Done=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsBusy=>busy;
        public bool HasBuilt=>built is not null;
        public LiveryProject Project=>project;
        public string PreviousDraft=>previousDraft;
        public void Initialize(LiveryProject value){project=value;FormDraft=value with{};CarSelector.SelectedItem=PaintCarSelector.SelectedItem=value.SelectedCar;}
        public void Previous(string path)=>previousDraft=path;
        public void MapsSaved(string albedo,string structure){project.Albedo=albedo;project.Structure=structure;}
        private WorkshopService Service()=>new(Tools,Workspace);
        private LiveryProject ReadDraft()=>project with{Name=FormDraft.Name,CarIndex=(CarSelector.SelectedItem as Car)?.Index??0,OuterColor=FormDraft.OuterColor,InnerColor=FormDraft.InnerColor,OuterGloss=FormDraft.OuterGloss,InnerGloss=FormDraft.InnerGloss,Metalness=FormDraft.Metalness,ClearCoat=FormDraft.ClearCoat,Description=FormDraft.Description};
        private bool BeginOutputOperation(string status){if(busy)return false;busy=true;BuildProgress.IsIndeterminate=true;Status.Text=status;return true;}
        private void EndOutputOperation(){BuildProgress.IsIndeterminate=false;busy=false;BusyReleases++;Done.TrySetResult();}
        private void EnableOutput(bool value)=>OutputEnabled=value;
        private Task<string?> Pick(string[] types){PickerCalls++;return Choice;}
        private void Error(Exception error)=>Failure=error;
        private void Info(string title,string message)=>Information=title+"\n"+message;
        private void SaveSession()=>SessionSaves++;
        private void UpdateCar(){}
        private void View(int page)=>Page=page;
        private void ApplyProject(){ProjectApplications++;FormDraft=project with{};RestoreCarSelection();}
        public void ChooseColor()=>PickAlbedo(this,new());
        public void ChooseSurface()=>PickStructure(this,new());
        public void Templates()=>UseTemplates(this,new());
        public void SelectCar(Car car,bool studio=false){if(studio){PaintCarSelector.SelectedItem=car;PaintCarChanged(this,new());}else{CarSelector.SelectedItem=car;CarChanged(this,new());}}
        public DragEventArgs Drop(Task<IReadOnlyList<StorageItem>> items,string? target=null,bool storage=true){var args=new DragEventArgs{DataView=new(){Items=items,HasStorage=storage}};TextureDrop(new FrameworkElement{Tag=target},args);return args;}
        public DragEventArgs Drag(bool storage=true){var args=new DragEventArgs{DataView=new(){HasStorage=storage}};TextureDragOver(this,args);return args;}
    }
}
