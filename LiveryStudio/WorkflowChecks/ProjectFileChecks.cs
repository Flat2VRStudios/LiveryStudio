using System.Text.Json;
using System.Numerics;
using LiveryStudio;
using LiveryStudio.Core;

internal static class ProjectFileChecks
{
    internal static async Task Run(Action<bool,string> check,string root)
    {
        var fixture=new ProjectFixture(Path.Combine(root,"picker-open"));var window=fixture.Window();
        var choice=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);window.OpenChoice=choice.Task;
        window.OpenFilePicker();
        check(window.IsBusy&&window.OpenPickerCalls==1&&window.Painter.FlushCalls==0&&window.HasBuilt,"Open owns the operation before awaiting its picker and keeps the draft/build intact");
        window.SaveFilePicker();window.OpenFilePicker();await window.OpenPath("must-not-be-read.json");
        check(window.IsBusy&&window.BusyReleases==0&&window.OpenPickerCalls==1&&window.SavePickerCalls==0,"Open/Save/template reentry cannot open another picker or release the pending Open owner");
        choice.SetResult(null);await Finished(window);
        check(!window.IsBusy&&window.HasBuilt&&window.Painter.FlushCalls==0&&window.ProjectApplications==0&&window.PreviousDraft.Length==0&&window.BusyReleases==1,"Canceling Open leaves the design, compiled output and recovery untouched");

        fixture=new(Path.Combine(root,"picker-save"));window=fixture.Window();choice=new(TaskCreationOptions.RunContinuationsAsynchronously);window.SaveChoice=choice.Task;
        window.SaveFilePicker();window.OpenFilePicker();window.SaveFilePicker();await window.OpenPath("must-not-be-read.json");
        check(window.IsBusy&&window.SavePickerCalls==1&&window.OpenPickerCalls==0&&window.Painter.FlushCalls==0&&window.BusyReleases==0,"Save owns its picker and denied reentry leaves that owner busy");
        choice.SetResult(null);await Finished(window);
        check(window.HasBuilt&&window.Painter.FlushCalls==0&&window.ProjectApplications==0&&window.PreviousDraft.Length==0&&window.BusyReleases==1,"Canceling Save preserves pending edits, compiled output and previous draft");

        foreach(var kind in new[]{"json","archive","fields","editor","wrong-car","texture","hidden-artwork"})
        {
            fixture=new(Path.Combine(root,"invalid-"+kind));window=fixture.Window();var incoming=fixture.Valid with{Name="Incoming"};var file=fixture.Raw(incoming);
            switch(kind)
            {
                case "json":File.WriteAllText(file,"{ broken");break;
                case "archive":file=Path.Combine(fixture.Root,"broken.flproject");File.WriteAllText(file,"not an archive");break;
                case "fields":file=fixture.Raw(incoming with{InnerColor="#12"});break;
                case "editor":var brokenEditor=Path.Combine(fixture.Root,"broken-editor.json");File.WriteAllText(brokenEditor,"{}");file=fixture.Raw(incoming with{EditorFile=brokenEditor});break;
                case "wrong-car":file=fixture.Raw(incoming with{EditorFile=fixture.Editor(1)});break;
                case "texture":File.WriteAllText(incoming.Albedo,"truncated pixels");break;
                case "hidden-artwork":
                    var badArt=Path.Combine(fixture.Root,"broken-art.tga");File.WriteAllText(badArt,"truncated pixels");
                    var document=new LayerDocument{BaseAlbedo=fixture.Valid.Albedo,BaseStructure=fixture.Valid.Structure};
                    document.Commit("Hidden bad image",layers=>layers.Add(new(){Name="Hidden decal",Kind=LayerKind.Sticker,Visible=false,Asset=badArt,Samples=[new(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY,1,1)]}));
                    var editor=Path.Combine(fixture.Root,"editor.json");document.Save(editor);file=fixture.Raw(incoming with{EditorFile=editor});break;
            }
            window.OpenChoice=Task.FromResult<string?>(file);window.OpenFilePicker();await Finished(window);
            check(window.Failure is not null&&window.Project.Name=="Current design"&&window.ProjectApplications==0&&window.Painter.FlushCalls==0&&window.HasBuilt&&window.PreviousDraft.Length==0&&!window.IsBusy,$"Malformed {kind} is rejected before flushing or replacing the current project");
        }

        fixture=new(Path.Combine(root,"incomplete-preservation"));window=fixture.Window();
        var incomplete=fixture.Valid with{Name="",InnerColor="#",Albedo=Path.Combine(fixture.Root,"missing.png"),EditorFile=fixture.Editor()};window.Initialize(incomplete);
        var savedEditorHash=WorkshopService.Hash(incomplete.EditorFile);var validFile=fixture.Raw(fixture.Valid with{Name="New raw project"});await window.OpenPath(validFile);
        var recovered=LiveryProject.Read(window.PreviousDraft);
        check(window.Project.Name=="New raw project"&&window.WorkspacePage==0&&window.Painter.Loads==0&&window.ProjectApplications==1,"Opening a valid texture project deliberately selects Design without loading Studio");
        check(DraftRecovery.IsIncomplete(window.PreviousDraft)&&recovered.Name==""&&recovered.InnerColor=="#"&&recovered.Albedo==incomplete.Albedo,"Opening a valid file preserves incomplete name, color and missing-map references");
        var recoveredHistory=LayerDocument.Read(recovered.EditorFile);
        check(recovered.EditorFile!=incomplete.EditorFile&&WorkshopService.Hash(incomplete.EditorFile)==savedEditorHash&&recoveredHistory.Cursor==LayerDocument.Read(incomplete.EditorFile).Cursor&&WorkshopService.Hash(recoveredHistory.BaseAlbedo)==WorkshopService.Hash(fixture.Valid.Albedo),"Open snapshots incomplete editor history and original-map bytes before switching projects");
        check(window.Information.Contains(validFile)&&window.Status.Text.Contains("previous draft")&&!window.IsBusy,"Successful Open reports the chosen path and previous-draft recovery");

        fixture=new(Path.Combine(root,"portable-open"));window=fixture.Window();var previousHash=WorkshopService.Hash(window.Project.Albedo);
        var incomingEditor=fixture.Editor();var portable=Path.Combine(fixture.Root,"editable.flproject");ProjectBundle.Save(fixture.Valid with{Name="Portable incoming",EditorFile=incomingEditor},portable);
        await window.OpenPath(portable);
        check(window.WorkspacePage==3&&window.Painter.Loads==1&&window.Project.EditorFile!=incomingEditor&&File.Exists(window.Project.EditorFile),"Editable portable Open creates an independent working project and navigates to Studio");
        var previous=ProjectBundle.Open(window.PreviousDraft,Path.Combine(fixture.Root,"inspect-recovery"));
        check(!DraftRecovery.IsIncomplete(window.PreviousDraft)&&previous.Name=="Current design"&&WorkshopService.Hash(previous.Albedo)==previousHash&&!window.HasBuilt,"Opening portable content preserves the valid previous design as a portable archive and invalidates old output");

        fixture=new(Path.Combine(root,"legacy-xml"));window=fixture.Window();var xml=Path.Combine(fixture.Root,"legacy.xml");File.WriteAllText(xml,(fixture.Valid with{Name="Legacy tool project"}).ToLegacyXml());await window.OpenPath(xml);
        check(window.Project.Name=="Legacy tool project"&&window.WorkspacePage==0&&window.Painter.Loads==0,"Legacy XML project opening remains supported through the guarded recovery path");
        fixture=new(Path.Combine(root,"optional-nulls"));window=fixture.Window();var nullable=fixture.Raw(fixture.Valid with{EditorFile=null!,Description=null!,Banner=null!});await window.OpenPath(nullable);
        check(window.Project.EditorFile==""&&window.Project.Description==""&&window.Project.Banner=="","Raw legacy JSON optional null strings become usable empty fields before replacement");

        fixture=new(Path.Combine(root,"open-flush-capture"));window=fixture.Window();var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);window.Painter.Flush=held.Task;
        var enteredFlush=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);window.Painter.BeforeFlush=()=>enteredFlush.TrySetResult();
        window.Painter.AfterFlush=()=>window.FormDraft=window.FormDraft with{Name="Finished pending edit"};var opening=window.OpenPath(fixture.Raw(fixture.Valid with{Name="Replacement"}));await enteredFlush.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(window.IsBusy&&!window.HasBuilt&&window.ProjectApplications==0,"Validated Open invalidates stale output before holding a pending edit flush");
        window.OpenFilePicker();window.SaveFilePicker();check(window.BusyReleases==0&&window.OpenPickerCalls==0&&window.SavePickerCalls==0,"Reentry during Open flush cannot release ownership or start a picker");
        held.SetResult();await opening;previous=ProjectBundle.Open(window.PreviousDraft,Path.Combine(fixture.Root,"inspect-recovery"));
        check(previous.Name=="Finished pending edit"&&window.Project.Name=="Replacement","Open recovery captures the completed pending edit before replacing the project");

        fixture=new(Path.Combine(root,"recovery-failure"));var blockedWorkspace=Path.Combine(fixture.Root,"blocked-workspace");File.WriteAllText(blockedWorkspace,"file, not directory");window=fixture.Window(blockedWorkspace);
        window.OpenChoice=Task.FromResult<string?>(fixture.Raw(fixture.Valid with{Name="Cannot replace"}));window.OpenFilePicker();await Finished(window);
        check(window.Failure is not null&&window.Project.Name=="Current design"&&window.ProjectApplications==0&&window.PreviousDraft.Length==0&&!window.IsBusy,"If preserving the current draft fails, Open leaves the current project selected");

        fixture=new(Path.Combine(root,"save-flush"));window=fixture.Window();var output=Path.Combine(fixture.Root,"saved.flproject");window.SaveChoice=Task.FromResult<string?>(output);
        window.FormDraft=window.FormDraft with{Name="Bad:/ file name."};held=new(TaskCreationOptions.RunContinuationsAsynchronously);window.Painter.Flush=held.Task;
        var finalMap=Path.Combine(fixture.Root,"latest-map.tga");FinishMaps.WriteTga(new(1,1,[201,31,70,255]),finalMap);
        window.Painter.AfterFlush=()=>{window.MapsSaved(finalMap,fixture.Valid.Structure,"");window.FormDraft=window.FormDraft with{Name="Saved after flush"};};
        window.SaveFilePicker();
        check(window.IsBusy&&!window.HasBuilt&&window.Painter.FlushCalls==1&&!File.Exists(output),"Save invalidates old compiled output before awaiting pending edits and does not write early");
        check(window.SuggestedFile=="Bad__ file name","Save gives the native picker a safe filename suggestion");
        window.OpenFilePicker();await window.OpenPath("must-not-be-read.json");check(window.IsBusy&&window.BusyReleases==0&&window.OpenPickerCalls==0,"Save retains its operation guard while a pending edit is completing");
        held.SetResult();await Finished(window);var saved=ProjectBundle.Open(output,Path.Combine(fixture.Root,"inspect-save"));
        check(saved.Name=="Saved after flush"&&WorkshopService.Hash(saved.Albedo)==WorkshopService.Hash(finalMap),"Saved portable project uses the draft and generated map paths captured after Flush completes");
        check(window.Failure is null&&window.Information.Contains(output)&&window.Status.Text.Contains("saved.flproject")&&!window.IsBusy&&!window.BuildProgress.IsIndeterminate,"Save reports its completed destination and releases progress state");

        fixture=new(Path.Combine(root,"save-invalid"));window=fixture.Window();output=Path.Combine(fixture.Root,"keep.flproject");ProjectBundle.Save(fixture.Valid,output);var originalHash=WorkshopService.Hash(output);
        window.FormDraft=window.FormDraft with{Name=""};window.SaveChoice=Task.FromResult<string?>(output);window.SaveFilePicker();await Finished(window);
        check(window.Failure is ArgumentException&&WorkshopService.Hash(output)==originalHash&&window.FormDraft.Name==""&&!window.IsBusy,"Invalid Save preserves the existing destination and unfinished current form");
        check(window.SuggestedFile=="My livery"&&!window.HasBuilt&&window.BusyReleases==1,"Blank-name Save has a usable picker suggestion and cleans up ownership after validation failure");

        fixture=new(Path.Combine(root,"independent-draft"));var originalEditor=fixture.Editor();var originalEditorHash=WorkshopService.Hash(originalEditor);
        var sourceDraft=fixture.Valid with{EditorFile=originalEditor,Name="Other window",ArtworkGloss=.17};
        var recoveryFile=DraftRecovery.Save(sourceDraft,fixture.Workspace);var independent=DraftRecovery.Open(recoveryFile,fixture.Workspace);
        check(independent.EditorFile!=originalEditor&&independent.Albedo!=sourceDraft.Albedo&&independent.Structure!=sourceDraft.Structure,"Resuming another window's complete draft gives editor history and generated maps independent paths");
        check(independent.Name==sourceDraft.Name&&independent.ArtworkGloss==sourceDraft.ArtworkGloss&&WorkshopService.Hash(independent.Albedo)==WorkshopService.Hash(sourceDraft.Albedo),"Independent recovery preserves form settings and source pixels");
        var independentDocument=LayerDocument.Read(independent.EditorFile);independentDocument.SeamPadding=8;independentDocument.Save(independent.EditorFile);
        FinishMaps.WriteTga(new(1,1,[1,2,3,255]),independent.Albedo);
        check(WorkshopService.Hash(originalEditor)==originalEditorHash&&WorkshopService.Hash(sourceDraft.Albedo)!=WorkshopService.Hash(independent.Albedo),"Editing the recovered copy cannot overwrite the other window's editor or generated map");
        recoveryFile=DraftRecovery.Save(sourceDraft with{Name="",OuterColor="#"},fixture.Workspace);independent=DraftRecovery.Open(recoveryFile,fixture.Workspace);
        check(DraftRecovery.IsIncomplete(recoveryFile)&&independent.Name==""&&independent.OuterColor=="#"&&independent.EditorFile!=originalEditor,"Unfinished forms recover their exact fields with a separate editor history");
        var originalColorHash=WorkshopService.Hash(sourceDraft.Albedo);var originalSurfaceHash=WorkshopService.Hash(sourceDraft.Structure);
        var recoveredDocument=LayerDocument.Read(independent.EditorFile);
        check(independent.Albedo!=sourceDraft.Albedo&&independent.Structure!=sourceDraft.Structure&&recoveredDocument.BaseAlbedo!=sourceDraft.Albedo&&recoveredDocument.BaseStructure!=sourceDraft.Structure,"Incomplete recovery isolates existing output maps and original textures too");
        FinishMaps.WriteTga(new(1,1,[4,5,6,255]),independent.Albedo);FinishMaps.WriteTga(new(1,1,[7,8,9,255]),independent.Structure);
        check(WorkshopService.Hash(sourceDraft.Albedo)==originalColorHash&&WorkshopService.Hash(sourceDraft.Structure)==originalSurfaceHash&&WorkshopService.Hash(originalEditor)==originalEditorHash,"Writing an incomplete recovered copy leaves the source window's output maps and editor unchanged");

        fixture=new(Path.Combine(root,"picker-failure"));window=fixture.Window();window.OpenChoice=Task.FromException<string?>(new IOException("picker unavailable"));window.OpenFilePicker();await Finished(window);
        check(window.Failure is IOException&&window.HasBuilt&&window.Painter.FlushCalls==0&&!window.IsBusy,"Picker failure releases the operation without changing draft or output");
    }

    private static Task Finished(MainWindow window)=>window.Done.Task.WaitAsync(TimeSpan.FromSeconds(15));

    private sealed class ProjectFixture
    {
        internal readonly string Root,Workspace;
        internal readonly LiveryProject Valid;
        internal ProjectFixture(string root)
        {
            Root=root;Workspace=Path.Combine(root,"workspace");Directory.CreateDirectory(root);
            var color=Path.Combine(root,"color.tga");var surface=Path.Combine(root,"surface.tga");
            FinishMaps.WriteTga(new(1,1,[20,40,60,255]),color);FinishMaps.WriteTga(new(1,1,[160,100,0,255]),surface);
            Valid=new(){Name="Current design",Albedo=color,Structure=surface};
        }
        internal MainWindow Window(string? workspace=null)
        {
            var window=new MainWindow(workspace??Workspace){Workspace=Workspace,Tools=Root};
            window.Initialize(Valid with{},new("prior",Valid,"","","","",[]));return window;
        }
        internal string Raw(LiveryProject project){var file=Path.Combine(Root,"incoming.json");File.WriteAllText(file,JsonSerializer.Serialize(project,Contract.Json));return file;}
        internal string Editor(int carIndex=0){var file=Path.Combine(Root,"editor.json");new LayerDocument{CarIndex=carIndex,BaseAlbedo=Valid.Albedo,BaseStructure=Valid.Structure}.Save(file);return file;}
    }
}
