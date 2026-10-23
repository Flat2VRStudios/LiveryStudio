using System.Text.Json;
using LiveryStudio;
using LiveryStudio.Core;
using Microsoft.UI.Xaml.Controls;

var scratch=Path.Combine(AppContext.BaseDirectory,"scratch",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
async Task<Exception> Reject(Func<Task> action){try{await action();}catch(Exception e){return e;}throw new Exception("Expected failure.");}
Fixture Create(string name)=>new(Path.Combine(scratch,name));

// Exercise the linked production workflow, with only WinUI/Painter replaced by a minimal host.
var guard=Create("guard");var w=guard.Window();var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);w.Painter.Flush=held.Task;
var first=w.Bake();var owner=w.TokenSource;
Check(w.IsBusy&&owner is not null,"Bake holds guard and cancellation before pending Flush");
Check(!w.HasBuilt,"Starting Bake invalidates previously compiled output before pending edit flush");
Check(await Reject(()=>w.Bake()) is InvalidOperationException,"Second Bake is rejected during pending Flush");
w.Test();w.SelectImport(guard.Receipt());w.Update();w.Remove();
Check(w.IsBusy&&ReferenceEquals(owner,w.TokenSource)&&w.BusyReleases==0,"Denied Bake/Test/Update/Remove reentry preserves owning guard and token");
w.Cancel();held.SetResult();Check(await Reject(()=>first) is OperationCanceledException,"Cancellation while flushing stops before preparation/build");
Check(!w.IsBusy&&w.TokenSource is null&&w.BusyReleases==1,"Owning cancellation releases guard exactly once");

var invalid=Create("invalid-name");w=invalid.Window();w.FormDraft=w.FormDraft with{Name=""};w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is ArgumentException&&!w.IsBusy&&!w.HasBuilt,"Invalid draft reports error and leaves output invalidated/unlocked");
Check(ContentDialog.Count==0&&!Directory.Exists(Path.Combine(invalid.Workspace,"builds")),"Invalid draft reaches neither close prompt nor compilation");

var absent=Create("missing-settings");File.Delete(absent.Rewards);w=absent.Window();w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure?.Message.Contains("Rewards.cfg")==true&&ContentDialog.Count==0,"Missing game settings fail before close/compile");

var readOnly=Create("read-only-settings");File.SetAttributes(readOnly.Factory,FileAttributes.ReadOnly);w=readOnly.Window();w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));File.SetAttributes(readOnly.Factory,FileAttributes.Normal);
Check(w.Failure?.Message.Contains("read-only")==true&&ContentDialog.Count==0,"Read-only registration file fails preflight without modifying it");

var missingGlass=Create("missing-glass");File.Delete(missingGlass.Glass);w=missingGlass.Window();w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure?.Message.Contains("glass material")==true&&ContentDialog.Count==0,"Missing car/common glass fails before game close");

var stale=Create("stale-receipt");var receipt=stale.Receipt();File.AppendAllText(receipt.Receipt.InstalledFiles[0],"external edit");w=stale.Window();w.SelectImport(receipt);w.Update();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is InvalidDataException&&w.Painter.Rebuilds==0&&ContentDialog.Count==0,"Changed installed asset blocks Update before rebuilding maps or showing confirmation");
w=stale.Window();w.SelectImport(receipt);w.ReplaceOnTest.IsChecked=true;w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is InvalidDataException&&ContentDialog.Count==0,"Update-on-test preflights replacement receipt");
w=stale.Window();w.SelectImport(receipt);w.Remove();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is InvalidDataException&&w.HasBuilt&&ContentDialog.Count==0,"Remove preserves compiled output and rejects stale receipt before confirmation");

var missingOriginal=Create("missing-original");var editor=missingOriginal.Editor();File.Delete(missingOriginal.Albedo);w=missingOriginal.Window();w.Initialize(w.FormDraft with{EditorFile=editor});w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is FileNotFoundException&&w.Failure.Message.Contains("original textures")&&ContentDialog.Count==0,"Missing editable originals remain an error; no stock fallback or game close");

var wrongCar=Create("wrong-car");editor=wrongCar.Editor(carIndex:1);w=wrongCar.Window();w.Initialize(w.FormDraft with{EditorFile=editor});w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is InvalidDataException&&w.Failure.Message.Contains("different car")&&ContentDialog.Count==0,"Editor car mismatch is caught before rebuild/close");

var recovery=Create("recover-maps");editor=recovery.Editor();w=recovery.Window();w.Initialize(w.FormDraft with{EditorFile=editor,Albedo=Path.Combine(recovery.Root,"missing.png"),Structure=Path.Combine(recovery.Root,"missing.tga")});
w.Painter.OnRebuild=p=>w.MapsSaved(recovery.Albedo,recovery.Surface,editor);w.Begin();var prepared=await w.Prepare(w.FormDraft with{},DesktopGame.Resolve(recovery.Data));w.End();
Check(w.Painter.Rebuilds==1&&prepared.Albedo==recovery.Albedo&&prepared.Structure==recovery.Surface,"Missing generated maps can rebuild from existing editable originals");
Check(w.SessionSaves==1&&prepared.Name=="Captured draft","Prepared captured draft is saved after successful validation");

var capture=Create("capture");w=capture.Window();held=new(TaskCreationOptions.RunContinuationsAsynchronously);w.Painter.Flush=held.Task;w.Begin();var snapshot=w.FormDraft with{};var preparing=w.Prepare(snapshot,DesktopGame.Resolve(capture.Data));w.FormDraft=w.FormDraft with{Name="Later form value",InnerColor="#123456"};w.MapsSaved(capture.Albedo,capture.Surface,"");held.SetResult();prepared=await preparing;w.End();
Check(prepared.Name=="Captured draft"&&prepared.InnerColor==snapshot.InnerColor,"Authoring fields remain captured across pending Flush");

var noSelection=Create("no-selection");w=noSelection.Window();w.ReplaceOnTest.IsChecked=true;w.Test();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure?.Message.Contains("Choose an installed livery")==true&&ContentDialog.Count==0,"Replace-on-test requires selection before any game-close prompt");

var decline=Create("decline");w=decline.Window();w.SelectImport(decline.Receipt());w.Update();await w.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));
Check(w.Failure is null&&!w.IsBusy&&w.Status.Text.Contains("canceled")&&!Directory.Exists(Path.Combine(decline.Workspace,"builds")),"Declined Update leaves guard released and skips compilation");
Check(w.InstalledList.SelectedItem is ManagedImport declinedSelection&&File.Exists(declinedSelection.Receipt.InstalledFiles[0]),"Declined Update preserves installed assets");

var library=Create("installed-library");w=library.Window();await w.RefreshImports();
Check(w.Imports.Count==0&&w.InstalledStatus.Text.StartsWith("No imports recorded")&&!w.InstalledUpdateButton.IsEnabled&&!w.InstalledRemoveButton.IsEnabled,"Empty current-game history has an honest empty state and disabled selection actions");
w.ChangeGameFolder("");await w.RefreshImports();
Check(w.InstalledStatus.Text.StartsWith("Connect a game")&&w.Imports.Count==0&&!w.InstalledRefreshButton.IsEnabled,"No connected game is distinct from an empty import history and disables import actions");
var readyImport=library.Receipt();var changedImport=library.Receipt();var removedImport=library.Receipt();
File.AppendAllText(changedImport.Receipt.InstalledFiles[0],"changed elsewhere");File.WriteAllText(removedImport.ReceiptPath,JsonSerializer.Serialize(removedImport.Receipt with{RemovedAt=DateTimeOffset.UtcNow},Contract.Json));
w.ChangeGameFolder(library.Data);await w.RefreshImports();
Check(w.Imports.Count==3&&w.InstalledStatus.Text.StartsWith("1 installed · 1 need attention · 1 removed"),"History accurately counts installed, changed and removed records");
w.SelectListed(w.Imports.Single(x=>x.Id==readyImport.Id));w.ReplaceOnTest.IsChecked=true;
Check(w.InstalledUpdateButton.IsEnabled&&w.InstalledRemoveButton.IsEnabled&&w.ReplaceOnTest.IsEnabled,"Verified current-list selection enables update/remove/update-on-test");
var previousSelection=w.InstalledList.SelectedItem;await w.RefreshImports();
Check(w.InstalledList.SelectedItem is ManagedImport keep&&keep.Id==readyImport.Id&&!ReferenceEquals(keep,previousSelection)&&w.ReplaceOnTest.IsChecked==true,"Same-target refresh selects the newly read matching receipt and preserves update-on-test");
w.Begin(false);Check(!w.InstalledUpdateButton.IsEnabled&&!w.InstalledRefreshButton.IsEnabled&&w.ReplaceOnTest.IsChecked==true,"Busy disables installed actions without silently changing update-on-test intent");w.End();
Check(w.InstalledUpdateButton.IsEnabled&&w.InstalledRefreshButton.IsEnabled,"Ending busy restores only eligible installed actions");
w.SelectListed(w.Imports.Single(x=>x.Id==changedImport.Id));
Check(!w.InstalledUpdateButton.IsEnabled&&!w.InstalledRemoveButton.IsEnabled&&!w.ReplaceOnTest.IsEnabled&&w.ReplaceOnTest.IsChecked==false&&w.InstalledSelectionHint.Text.Contains("changed outside"),"Changed selection explains why actions are disabled and clears update-on-test");
w.Update();w.Remove();Check(!w.IsBusy&&w.TokenSource is null,"Unavailable selection cannot invoke update/remove through handlers");
w.SelectListed(w.Imports.Single(x=>x.Id==removedImport.Id));
Check(!w.InstalledUpdateButton.IsEnabled&&w.InstalledSelectionHint.Text.Contains("archived import"),"Removed entry is shown as history with a specific explanation");
w.SelectListed(w.Imports.Single(x=>x.Id==readyImport.Id));w.ReplaceOnTest.IsChecked=true;File.AppendAllText(readyImport.Receipt.InstalledFiles[0],"new external edit");await w.RefreshImports();
Check(w.InstalledList.SelectedItem is ManagedImport nowChanged&&nowChanged.Status=="Files changed"&&!w.InstalledRemoveButton.IsEnabled&&w.ReplaceOnTest.IsChecked==false,"Refresh preserves changed receipt for explanation but disables its formerly valid actions");
var oldRevision=w.ImportRevision;var oldEntries=w.Imports;var oldSelected=w.InstalledList.SelectedItem as ManagedImport;
var otherGame=Create("other-library-target");w.ChangeGameFolder(otherGame.Data);
Check(w.Imports.Count==0&&w.InstalledList.SelectedItem is null&&w.ReplaceOnTest.IsChecked==false&&!w.InstalledUpdateButton.IsEnabled,"Changing game folder immediately clears old list, selection and update mode");
w.ApplyImportScan(oldRevision,library.Data,oldEntries,oldSelected);
Check(w.Imports.Count==0&&w.InstalledList.SelectedItem is null,"A late scan from the previous folder cannot restore its entries or selection");
await w.RefreshImports();w.ApplyImportScan(oldRevision,library.Data,oldEntries,oldSelected);
Check(w.Imports.Count==0&&w.InstalledStatus.Text.StartsWith("No imports recorded"),"An obsolete scan cannot overwrite a newer completed folder refresh");
w.SelectListed(readyImport);w.Update();w.Remove();
Check(!w.IsBusy&&!w.InstalledUpdateButton.IsEnabled,"A stale receipt outside the current list cannot enable or invoke installed actions");
w.ChangeGameFolder(Path.Combine(library.Root,"missing-folder"));await w.RefreshImports();
Check(w.Imports.Count==0&&w.InstalledList.SelectedItem is null&&w.InstalledStatus.Text.Contains("does not exist")&&!w.InstalledRefreshButton.IsEnabled,"Missing game folder clears stale results, explains the target error and disables import actions");
var unknownGame=Path.Combine(library.Root,"unrecognized-game");Directory.CreateDirectory(unknownGame);w.ChangeGameFolder(unknownGame);await w.RefreshImports();
Check(w.Imports.Count==0&&w.InstalledStatus.Text.Contains("wasn't found")&&!w.InstalledRefreshButton.IsEnabled,"An unrecognized folder explains game detection instead of suggesting Steam Workshop");
DraftRecoveryChecks.Run(Check,Path.Combine(scratch,"draft-recovery"));
await ProjectFileChecks.Run(Check,Path.Combine(scratch,"project-files"));
Console.WriteLine($"{checks} workflow checks passed. Scratch: {scratch}");

sealed class Fixture
{
    public string Root,Workspace,Tools,Data,Factory,Rewards,Glass,Albedo,Surface;
    public Fixture(string root)
    {
        Root=root;Workspace=Path.Combine(root,"workspace");Tools=Path.Combine(root,"tools");Data=Path.Combine(root,"game","Flatout","Data");
        Factory=Path.Combine(Data,"Common","Tuning","Factories","CarsSkins.cfg");Rewards=Path.Combine(Path.GetDirectoryName(Factory)!,"Rewards.cfg");Glass=Path.Combine(Data,"WIN32","GRAPH","CARS","COMMON","GLASS.PMA");Albedo=Path.Combine(root,"original.png");Surface=Path.Combine(root,"original.tga");
        foreach(var file in new[]{Factory,Rewards,Glass,Albedo,Surface,Path.Combine(root,"game","PlayAll","Libs","vs2022","x64","DllLauncher64_r.exe"),Path.Combine(root,"game","Flatout","Bin","x64","Flatout64_r.dll")}.Concat(new[]{"Compiler_r.exe","KtPkg.exe","SteamWorkShopSender.exe"}.Select(n=>Path.Combine(Tools,"Bin",n))))Write(file,"fixture");
    }
    static void Write(string file,string text){Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,text);}
    public MainWindow Window()
    {
        var p=new LiveryProject{Name="Captured draft",Albedo=Albedo,Structure=Surface};var w=new MainWindow(Workspace){Tools=Tools,Workspace=Workspace};w.GameFolder.Text=Data;w.Initialize(p,new("prior",p,"","","","",[]));return w;
    }
    public string Editor(int carIndex=0){var path=Path.Combine(Root,"editor.json");new LayerDocument{CarIndex=carIndex,BaseAlbedo=Albedo,BaseStructure=Surface}.Save(path);return path;}
    public ManagedImport Receipt()
    {
        var id="BODY_"+Guid.NewGuid().ToString("N");var car=Contract.Cars[0];var prefix=Path.Combine(Data,"WIN32","GRAPH","CARS",car.Class,car.CarId);
        var files=new[]{Path.Combine(prefix,id+".PMA"),Path.Combine(prefix,"TEXTURES",id+"_DIFF.PTX"),Path.Combine(prefix,"TEXTURES",id+"_IOR.PTX"),Path.Combine(Data,"WIN32","PACLOG","CUSTOMPAC","LIVERY_STUDIO",id+".PACLOG")};foreach(var file in files)Write(file,"owned");
        var block=$"-- LIVERY_STUDIO_BEGIN {id}\nfixture\n-- LIVERY_STUDIO_END {id}\n";File.AppendAllText(Factory,"\n"+block);File.AppendAllText(Rewards,"\n"+block);
        var receipt=new ImportReceipt("Installed",Data,"key","",files,"",InstalledHashes:files.ToDictionary(f=>f,WorkshopService.Hash),FactoryBlock:block,RewardsBlock:block);
        var path=Path.Combine(Workspace,"imports",id,"receipt","receipt.json");Write(path,JsonSerializer.Serialize(receipt,Contract.Json));return new(id,path,receipt,"Installed",DateTimeOffset.UtcNow);
    }
}
