using LiveryStudio.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiveryStudio;

public partial class MainWindow
{
    private readonly EditorSessionStore sessions=new(Path.Combine(WorkshopService.DefaultWorkspace,"session.json"));
    private readonly EditorShutdown shutdown=new();

    private void PersistSession()=>sessions.Save(new(ReadDraft(),WorkshopService.DefaultTools,GameFolder.Text,previousDraft,currentWorkspace,AdvancedMode:AdvancedModeToggle.IsChecked==true),claim:!shutdown.IsClosing);
    private void SaveSession()
    {
        if(loading||restoringSession)return;
        try{PersistSession();}catch(Exception e){log.Add(e.Message);}
    }

    private async void ClosingWindow(AppWindow sender,AppWindowClosingEventArgs args)
    {
        if(shutdown.Ready)return;
        args.Cancel=true;
        if(shutdown.IsClosing)return;
        if(busy){Status.Text="Finish or cancel the current operation before closing.";return;}
        Busy(true);BuildProgress.Visibility=CancelButton.Visibility=Visibility.Collapsed;Status.Text="Saving your draft…";
        try{await shutdown.CloseAsync(Painter.FlushAsync,PersistSession,Close);}
        catch(Exception e){Busy(false);Error(e);}
    }

    private async Task IsolateResumedDraft()
    {
        // Separate builds can run side by side too. A second window must not edit
        // the first one's editor.json or generated maps after restoring its session.
        if(!File.Exists(project.EditorFile))return;
        var processes=System.Diagnostics.Process.GetProcessesByName("LiveryStudio");
        try{if(processes.Length<2&&Environment.GetEnvironmentVariable("LIVERY_STUDIO_ISOLATE")!="1")return;}finally{foreach(var process in processes)process.Dispose();}
        Status.Text="Opening an independent copy of your draft…";
        var source=project;
        project=await Task.Run(()=>DraftRecovery.Open(DraftRecovery.Save(source,WorkshopService.DefaultWorkspace),WorkshopService.DefaultWorkspace));
        ApplyProject();
    }

    private async void RecoverDraft(object sender,RoutedEventArgs args)
    {
        if(!BeginOutputOperation("Finding your saved drafts…"))return;
        SavedEditorDraft? selected=null;
        try
        {
            BuildProgress.Visibility=Visibility.Collapsed;
            var drafts=await Task.Run(sessions.OtherDrafts);
            if(drafts.Count==0){Info("No other drafts yet","Each editor keeps its draft here automatically. Your current design is already saved.");return;}
            var list=new ListView{SelectionMode=ListViewSelectionMode.Single,MaxHeight=Math.Clamp(Root.ActualHeight-300,100,360),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            foreach(var entry in drafts)
            {
                var name=string.IsNullOrWhiteSpace(entry.Session.Project.Name)?"Untitled draft":entry.Session.Project.Name;
                var row=new StackPanel{Spacing=4,Margin=new Thickness(0,6,0,6)};
                row.Children.Add(new TextBlock{Text=name,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                row.Children.Add(new TextBlock{Text=entry.Session.Project.SelectedCar.Name+" · "+entry.SavedAt.ToLocalTime().ToString("g"),TextWrapping=TextWrapping.Wrap,FontSize=12});
                list.Items.Add(new ListViewItem{Content=row,Tag=entry});
            }
            var content=new StackPanel{Spacing=12,MaxWidth=500};
            content.Children.Add(new TextBlock{Text="Choose a draft to open as an independent copy. Your current design will be preserved.",TextWrapping=TextWrapping.Wrap});content.Children.Add(list);
            var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title="Recover a draft",Content=content,PrimaryButtonText="Open draft",CloseButtonText="Cancel",IsPrimaryButtonEnabled=false,DefaultButton=ContentDialogButton.Close};
            list.SelectionChanged+=(_,_)=>dialog.IsPrimaryButtonEnabled=list.SelectedItem is ListViewItem;
            if(await dialog.ShowAsync()==ContentDialogResult.Primary)selected=(list.SelectedItem as ListViewItem)?.Tag as SavedEditorDraft;
        }
        catch(Exception e){Error(e);}
        finally{EndOutputOperation();}
        if(selected is null||!BeginOutputOperation("Opening your recovered draft…"))return;
        try
        {
            await PreserveDraftForReplacement();
            var source=selected.Session.Project;
            var copied=await Task.Run(()=>
            {
                var file=DraftRecovery.Save(source,WorkshopService.DefaultWorkspace);
                return (Project:DraftRecovery.Open(file,WorkshopService.DefaultWorkspace),Incomplete:DraftRecovery.IsIncomplete(file));
            });
            project=copied.Project;ApplyProject();View(copied.Incomplete||string.IsNullOrWhiteSpace(project.EditorFile)?0:3);
            if(currentWorkspace==3)await Painter.LoadAsync(project);
            SaveSession();Status.Text="Recovered "+project.Name+" · your previous draft is preserved.";
            Info("Draft recovered",copied.Incomplete?"Your fields and available artwork are restored. Finish its details before baking.":"Your design is open as an independent editable copy.");
        }
        catch(Exception e){Error(e);}finally{EndOutputOperation();}
    }
}
