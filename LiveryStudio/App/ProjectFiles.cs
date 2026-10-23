using LiveryStudio.Core;
using Microsoft.UI.Xaml;

namespace LiveryStudio;

public partial class MainWindow
{
    private readonly string projectFileWorkspace=WorkshopService.DefaultWorkspace;

    private async void OpenProject(object sender,RoutedEventArgs e)
    {
        if(!BeginOutputOperation("Choose a project to open…"))return;
        try
        {
            BuildProgress.Visibility=Visibility.Collapsed;
            if(await Pick([".flproject",".json",".xml"]) is not {} file){Status.Text="Open canceled · your current draft is unchanged.";return;}
            await OpenProjectFile(file);
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }

    // Templates use the same ownership and recovery path as the file picker.
    private async Task OpenProjectPath(string path)
    {
        if(!BeginOutputOperation("Opening "+Path.GetFileName(path)+"…"))return;
        try{await OpenProjectFile(path);}finally{EndOutputOperation();}
    }

    private async Task OpenProjectFile(string file)
    {
        BuildProgress.Visibility=Visibility.Visible;Status.Text="Checking "+Path.GetFileName(file)+"…";
        var opened=await Task.Run(()=>
        {
            var candidate=file.EndsWith(".flproject",StringComparison.OrdinalIgnoreCase)
                ?ProjectBundle.Open(file,projectFileWorkspace):LiveryProject.Read(file);
            candidate=candidate with {EditorFile=candidate.EditorFile??"",Banner=candidate.Banner??"",Description=candidate.Description??""};
            candidate.Validate();
            var images=new HashSet<string>(StringComparer.OrdinalIgnoreCase){candidate.Albedo,candidate.Structure};
            if(!string.IsNullOrWhiteSpace(candidate.EditorFile))
            {
                var document=LayerDocument.Read(candidate.EditorFile);
                if(document.CarIndex!=candidate.CarIndex)throw new InvalidDataException("This project's editable design belongs to a different car.");
                images.Add(document.BaseAlbedo);images.Add(document.BaseStructure);
                foreach(var layer in document.Layers)
                {
                    if(layer.Kind==LayerKind.Sticker)images.Add(layer.Asset);
                    if(layer.Effects is {Mask:DecalMask.Image} effects)images.Add(effects.StencilFile);
                }
            }
            // Existence alone does not prove that a texture can be shown in the editor.
            foreach(var image in images)FinishMaps.Read(image).Validate();
            return candidate;
        });

        // A malformed selection must not flush or replace the design already open.
        // A successful flush can save pending artwork, so invalidate its old build first.
        built=null;EnableOutput(false);Status.Text="Preserving your current draft…";
        await Painter.FlushAsync();var current=ReadDraft();
        previousDraft=await Task.Run(()=>DraftRecovery.Save(current,projectFileWorkspace));SaveSession();
        project=opened;ApplyProject();View(string.IsNullOrWhiteSpace(opened.EditorFile)?0:3);
        if(currentWorkspace==3)await Painter.LoadAsync(project);
        SaveSession();Status.Text="Opened "+project.Name+" · your previous draft is preserved.";
        Info("Project opened",file+"\nYour previous design is available in Previous draft.");
    }

    private async void SaveProject(object sender,RoutedEventArgs e)
    {
        if(!BeginOutputOperation("Choose where to save your project…"))return;
        try
        {
            BuildProgress.Visibility=Visibility.Collapsed;
            var name=ReadDraft().Name;
            var suggested=string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Trim().TrimEnd('.');
            if(string.IsNullOrWhiteSpace(suggested))suggested="My livery";
            if(await SavePick(".flproject",suggested) is not {} file){Status.Text="Save canceled · your current draft is unchanged.";return;}

            built=null;EnableOutput(false);BuildProgress.Visibility=Visibility.Visible;Status.Text="Saving your editable project…";
            await Painter.FlushAsync();var draft=ReadDraft();project=draft;
            await Task.Run(()=>ProjectBundle.Save(draft,file));
            SaveSession();Status.Text="Project saved · "+Path.GetFileName(file);
            Info("Portable project saved",file+"\nIncludes original textures, artwork, editable layers and history.");
        }
        catch(Exception error){Error(error);}finally{EndOutputOperation();}
    }
}
