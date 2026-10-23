using System.Text.Json;
using LiveryStudio;
using LiveryStudio.Core;
using Windows.ApplicationModel.DataTransfer;
using CoreWorkshop=LiveryStudio.Core.WorkshopService;
using HostWorkshop=LiveryStudio.WorkshopService;

var scratch=Path.Combine(AppContext.BaseDirectory,"scratch",Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
Fixture Create(string name)=>new(Path.Combine(scratch,name));
Task Wait(MainWindow window)=>window.Done.Task.WaitAsync(TimeSpan.FromSeconds(15));
Task<IReadOnlyList<StorageItem>> Files(params string[] paths)=>Task.FromResult<IReadOnlyList<StorageItem>>(paths.Select(p=>new StorageItem(p)).ToArray());
string State(LiveryProject project)=>JsonSerializer.Serialize(project,Contract.Json);

var f=Create("picker-cancel");var w=f.Window();var original=State(w.Project);var choice=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);w.Choice=choice.Task;
w.ChooseColor();
Check(w.IsBusy&&w.PickerCalls==1&&w.Painter.FlushCalls==0,"Picker owns guard before its first await without flushing");
w.ChooseSurface();w.Templates();var denied=w.Drop(Files(f.Color));w.SelectCar(Contract.Cars[1]);
Check(w.BusyReleases==0&&w.PickerCalls==1&&denied.DataView.Reads==0&&w.IsBusy,"Reentry through picker, templates, drop and car cannot release pending picker guard");
Check((w.CarSelector.SelectedItem as Car)?.Index==0&&(w.PaintCarSelector.SelectedItem as Car)?.Index==0,"Rejected car selection restores both selectors");
choice.SetResult(null);await Wait(w);
Check(State(w.Project)==original&&w.HasBuilt&&w.OutputEnabled&&w.PreviousDraft==""&&w.Painter.FlushCalls==0,"Cancel preserves project, compiled output and recovery state");
Check(w.BusyReleases==1&&!w.IsBusy&&w.Failure is null,"Canceled picker releases its guard exactly once");

f=Create("corrupt-picker");w=f.Window();original=State(w.Project);var corrupt=Path.Combine(f.Root,"broken.png");File.WriteAllText(corrupt,"not an image");w.Choice=Task.FromResult<string?>(corrupt);w.ChooseSurface();await Wait(w);
Check(w.Failure is not null&&State(w.Project)==original&&w.HasBuilt,"Malformed texture is rejected while project and compiled output survive");
Check(w.Painter.FlushCalls==0&&w.PreviousDraft==""&&w.SessionSaves==0&&!Directory.Exists(Path.Combine(f.Workspace,"recovery")),"Malformed texture fails before flushing or writing recovery");

f=Create("pending-drop");w=f.Window();var storage=new TaskCompletionSource<IReadOnlyList<StorageItem>>(TaskCreationOptions.RunContinuationsAsynchronously);var pending=w.Drop(storage.Task,"surface");
Check(pending.Handled&&w.IsBusy&&pending.DataView.Reads==1,"Drop marks routed event handled and guards pending storage retrieval");
w.ChooseColor();var second=w.Drop(Files(f.Color));w.SelectCar(Contract.Cars[1],true);
Check(w.PickerCalls==0&&second.DataView.Reads==0&&w.BusyReleases==0&&w.IsBusy,"Pending drop rejects picker/drop/studio-car reentry without releasing owner");
storage.SetResult([new(f.Color)]);await Wait(w);
Check(w.Project.Structure==f.Color&&w.Project.Albedo==f.OriginalColor&&w.Failure is null,"Explicit surface drop accepts a real PNG despite its color filename");
Check(w.BusyReleases==1&&!w.HasBuilt&&w.PreviousDraft.EndsWith(".flproject"),"Accepted drop invalidates output, preserves portable recovery and releases once");

f=Create("flush-pending");w=f.Window();var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);w.Painter.Flush=held.Task;w.Choice=Task.FromResult<string?>(f.Surface);w.ChooseColor();await w.Painter.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
Check(w.IsBusy&&!w.HasBuilt&&!w.OutputEnabled&&w.PreviousDraft=="","Accepted texture invalidates output before pending layer flush");
w.ChooseSurface();w.Templates();w.SelectCar(Contract.Cars[1]);
Check(w.BusyReleases==0&&w.PickerCalls==1&&w.Painter.FlushCalls==1&&w.Project.EditorFile==f.Editor,"Pending flush keeps old editor and rejects all replacement reentry");
held.SetResult();await Wait(w);
Check(w.Project.Albedo==f.Surface&&w.Project.Structure==f.OriginalSurface&&w.Project.EditorFile==""&&w.BusyReleases==1,"Explicit color picker accepts TGA and retains untouched surface map");

foreach(var test in new[]{("ambiguous-color",new[]{"one.png","two.png"},(string?)null),("ambiguous-surface",new[]{"body_IOR.png","surface.tga"},(string?)null),("explicit-multiple",new[]{"one.png","surface.tga"},"color"),("unsupported",new[]{"notes.txt"},(string?)null)})
{
    f=Create(test.Item1);w=f.Window();original=State(w.Project);var paths=test.Item2.Select(n=>f.Image(n)).ToArray();w.Drop(Files(paths),test.Item3);await Wait(w);
    Check(w.Failure is InvalidDataException&&w.Painter.FlushCalls==0&&State(w.Project)==original&&w.HasBuilt,test.Item1+" fails before altering a draft");
}
f=Create("named-pair");w=f.Window();var namedSurface=f.Image("body_IOR.TGA");var namedColor=f.Image("Warrior.PNG");w.Drop(Files(namedSurface,namedColor));await Wait(w);
Check(w.Failure is null&&w.Project.Albedo==namedColor&&w.Project.Structure==namedSurface,"Shared drop routes real PNG/TGA by whole surface suffix without confusing Warrior");
Check(FinishMaps.Read(w.Project.Albedo).Width==1&&FinishMaps.Read(w.Project.Structure).Width==2,"Accepted shared-drop files genuinely decode as PNG and TGA");

f=Create("incomplete-recovery");w=f.Window();w.FormDraft=w.FormDraft with{Name="",OuterColor="#ABCDEF",Description="Unfinished but valuable"};original=File.ReadAllText(f.Editor);w.Choice=Task.FromResult<string?>(f.Color);w.ChooseColor();await Wait(w);
Check(w.Failure is null&&DraftRecovery.IsIncomplete(w.PreviousDraft)&&w.Project.EditorFile=="","Accepted map replacement preserves an incomplete draft before clearing editor");
var recovered=DraftRecovery.Open(w.PreviousDraft,f.Workspace);
Check(recovered.Name==""&&recovered.OuterColor=="#ABCDEF"&&recovered.Description=="Unfinished but valuable"&&recovered.Albedo!=f.OriginalColor&&CoreWorkshop.Hash(recovered.Albedo)==CoreWorkshop.Hash(f.OriginalColor)&&CoreWorkshop.Hash(recovered.Structure)==CoreWorkshop.Hash(f.OriginalSurface),"Incomplete recovery retains unsaved form values and independently copies original maps");
var recoveredEditor=LayerDocument.Read(recovered.EditorFile);var originalEditor=LayerDocument.Read(f.Editor);
string NormalizeEditor(LayerDocument document)=>JsonSerializer.Serialize(document,LayerDocument.Json).Replace(JsonSerializer.Serialize(document.BaseAlbedo),"\"COLOR\"").Replace(JsonSerializer.Serialize(document.BaseStructure),"\"SURFACE\"");
Check(recovered.EditorFile!=f.Editor&&File.ReadAllText(f.Editor)==original&&NormalizeEditor(recoveredEditor)==NormalizeEditor(originalEditor)&&recoveredEditor.BaseAlbedo!=f.OriginalColor&&CoreWorkshop.Hash(recoveredEditor.BaseAlbedo)==CoreWorkshop.Hash(f.OriginalColor),"Incomplete recovery retains independent editor history and original source bytes while rebasing asset paths");

f=Create("car-recovery");w=f.Window();w.FormDraft=w.FormDraft with{Name="Unsaved car design",OuterColor="#FEDCBA",Description="Keep this old car"};var flushed=f.Image("flushed.tga");w.Painter.AfterFlush=()=>w.MapsSaved(flushed,f.OriginalSurface);w.SelectCar(Contract.Cars[1]);await Wait(w);
recovered=DraftRecovery.Open(w.PreviousDraft,f.Workspace);
Check(w.Failure is null&&recovered.CarIndex==0&&recovered.Name=="Unsaved car design"&&recovered.OuterColor=="#FEDCBA"&&recovered.Description=="Keep this old car","Changing car records previous car identity with unsaved form fields");
Check(CoreWorkshop.Hash(recovered.Albedo)==CoreWorkshop.Hash(flushed)&&CoreWorkshop.Hash(recovered.Albedo)!=CoreWorkshop.Hash(f.OriginalColor)&&LayerDocument.Read(recovered.EditorFile).CarIndex==0,"Changing car captures flushed maps together with the old car editor");
Check(w.Project.CarIndex==1&&w.Project.EditorFile==""&&w.Project.Albedo==f.SampleColor(1)&&w.Page==0&&w.Painter.Loads==0,"Design car selection installs the new car templates and stays in Design");

f=Create("studio-car");w=f.Window();w.SelectCar(Contract.Cars[1],true);await Wait(w);
Check(w.Failure is null&&w.Page==3&&w.Painter.Loads==1&&w.Project.CarIndex==1,"Studio car selection reloads the preview exactly once and remains in Studio");
f=Create("bad-car-sample");w=f.Window();original=State(w.Project);File.WriteAllText(f.SampleColor(1),"corrupt");w.SelectCar(Contract.Cars[1]);await Wait(w);
Check(w.Failure is not null&&w.Painter.FlushCalls==0&&w.HasBuilt&&State(w.Project)==original,"Bad car templates fail before current design flush or invalidation");
Check((w.CarSelector.SelectedItem as Car)?.Index==0&&(w.PaintCarSelector.SelectedItem as Car)?.Index==0&&w.BusyReleases==1,"Failed car replacement restores selection and releases one guard");

f=Create("recovery-failure");w=f.Window();original=State(w.Project);var blocked=Path.Combine(f.Root,"workspace-is-a-file");File.WriteAllText(blocked,"block directory creation");HostWorkshop.DefaultWorkspace=blocked;w.Previous("older-recovery.flproject");w.Choice=Task.FromResult<string?>(f.Color);w.ChooseColor();await Wait(w);
Check(w.Failure is IOException&&State(w.Project)==original&&w.Project.EditorFile==f.Editor,"Recovery write failure prevents replacing the live project or editor");
Check(w.PreviousDraft=="older-recovery.flproject"&&w.SessionSaves==0&&w.BusyReleases==1&&w.Painter.FlushCalls==1,"Recovery failure preserves prior recovery pointer and releases only its own operation");

f=Create("templates");w=f.Window();w.FormDraft=w.FormDraft with{Name=""};w.Templates();await Wait(w);
Check(w.Failure is null&&DraftRecovery.IsIncomplete(w.PreviousDraft)&&w.Project.Albedo==f.SampleColor(0)&&w.Project.EditorFile==""&&w.Information.Contains("Previous draft"),"Use car templates preserves unfinished draft and explains recovery");
f=Create("drag-contract");w=f.Window();Check(w.Drag().AcceptedOperation==DataPackageOperation.Copy&&w.Drag(false).AcceptedOperation==DataPackageOperation.None,"Drag advertises copy only for storage items");choice=new(TaskCreationOptions.RunContinuationsAsynchronously);w.Choice=choice.Task;w.ChooseColor();Check(w.Drag().AcceptedOperation==DataPackageOperation.None,"Busy editor rejects drag affordance");choice.SetResult(null);await Wait(w);
var ignored=w.Drop(Files(f.Color),storage:false);Check(ignored.Handled&&ignored.DataView.Reads==0&&!w.IsBusy&&w.BusyReleases==1,"Non-storage drop is handled without starting or releasing an operation");
Console.WriteLine($"{checks} draft replacement checks passed. Scratch: {scratch}");

sealed class Fixture
{
    public readonly string Root,Workspace,Tools,OriginalColor,OriginalSurface,Color,Surface,Editor;
    private static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");
    public Fixture(string root)
    {
        Root=root;Workspace=Path.Combine(root,"workspace");Tools=Path.Combine(root,"tools");Directory.CreateDirectory(root);
        OriginalColor=Image("original.png");OriginalSurface=Image("original.tga");Color=Image("color.png");Surface=Image("surface.tga");Editor=Path.Combine(root,"editor.json");
        new LayerDocument{CarIndex=0,BaseAlbedo=OriginalColor,BaseStructure=OriginalSurface}.Save(Editor);
        foreach(var car in Contract.Cars.Take(2)){var dir=Path.Combine(Tools,"Doc",car.Category);Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,car.Template+"_Diff.png"),Png);File.WriteAllBytes(Path.Combine(dir,car.Template+"_IOR.png"),Png);}
    }
    public string SampleColor(int index){var car=Contract.Cars[index];return Path.Combine(Tools,"Doc",car.Category,car.Template+"_Diff.png");}
    public string Image(string name)
    {
        var path=Path.Combine(Root,name);
        if(Path.GetExtension(path).Equals(".tga",StringComparison.OrdinalIgnoreCase))FinishMaps.WriteTga(new(2,1,[10,20,30,255,40,50,60,255]),path);
        else File.WriteAllBytes(path,Png);
        return path;
    }
    public MainWindow Window()
    {
        HostWorkshop.DefaultWorkspace=Workspace;
        var result=new MainWindow{Tools=Tools,Workspace=Workspace};result.Initialize(new(){Name="Original draft",CarIndex=0,Albedo=OriginalColor,Structure=OriginalSurface,EditorFile=Editor});return result;
    }
}
