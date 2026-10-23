using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace LiveryStudio;

public partial class MainWindow
{
    private async void CarChanged(object sender,SelectionChangedEventArgs e)
    {
        if(loading){UpdateCar();return;}
        if(CarSelector.SelectedItem is Car car)await ChangeCarAsync(car,false);
    }

    private async void PaintCarChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!loading&&PaintCarSelector.SelectedItem is Car car)await ChangeCarAsync(car,true);
    }

    private void RestoreCarSelection()
    {
        var before=loading;loading=true;
        try{CarSelector.SelectedItem=PaintCarSelector.SelectedItem=project.SelectedCar;UpdateCar();}
        finally{loading=before;}
    }

    private async Task ChangeCarAsync(Car car,bool studio)
    {
        // The selection event arrives after the combo changes. Keep ReadDraft on the old car
        // until its maps and layers have been flushed and preserved together.
        RestoreCarSelection();
        if(car.Index==project.CarIndex||!BeginOutputOperation("Opening "+car.Name+"…"))return;
        try
        {
            var service=Service();var maps=await Task.Run(()=>service.Sample(car));
            var replacement=new TextureImport(maps.Albedo,maps.Structure);await Task.Run(replacement.Validate);
            var current=await PreserveDraftForReplacement();
            project=current with{CarIndex=car.Index,Albedo=maps.Albedo,Structure=maps.Structure,EditorFile="",CameraBookmarks=[]};
            ApplyProject();View(studio?3:0);SaveSession();
            if(studio)await Painter.LoadAsync(project);
            SaveSession();Status.Text=car.Name+" ready · restore your last design from Previous draft.";
        }
        catch(Exception error){RestoreCarSelection();Error(error);}
        finally{EndOutputOperation();}
    }

    private async Task<LiveryProject> PreserveDraftForReplacement()
    {
        built=null;EnableOutput(false);Status.Text="Preserving your current draft…";
        await Painter.FlushAsync();var current=ReadDraft();
        previousDraft=await Task.Run(()=>DraftRecovery.Save(current,WorkshopService.DefaultWorkspace));SaveSession();
        return current;
    }

    private async void PickAlbedo(object sender,RoutedEventArgs e)=>await PickTexture(false);
    private async void PickStructure(object sender,RoutedEventArgs e)=>await PickTexture(true);

    private async Task PickTexture(bool surface)
    {
        if(!BeginOutputOperation(surface?"Choose a surface map…":"Choose a color map…"))return;
        try
        {
            BuildProgress.Visibility=Visibility.Collapsed;
            if(await Pick([".png",".tga"]) is not {} file){Status.Text="Texture choice canceled · your draft is unchanged.";return;}
            await ReplaceTextureMaps(surface?new(Structure:file):new(Albedo:file));
        }
        catch(Exception error){Error(error);}
        finally{EndOutputOperation();}
    }

    private async void UseTemplates(object sender,RoutedEventArgs e)
    {
        if(!BeginOutputOperation("Loading this car's texture templates…"))return;
        try
        {
            var car=ReadDraft().SelectedCar;var service=Service();var maps=await Task.Run(()=>service.Sample(car));
            await ReplaceTextureMaps(new(maps.Albedo,maps.Structure));
            Info("Car templates loaded","Open the 3D paint studio to create editable artwork. Your last design is available from More options → Previous draft.");
        }
        catch(Exception error){Error(error);}
        finally{EndOutputOperation();}
    }

    private async Task ReplaceTextureMaps(TextureImport maps)
    {
        BuildProgress.Visibility=Visibility.Visible;Status.Text="Checking your texture maps…";
        await Task.Run(maps.Validate);
        var current=await PreserveDraftForReplacement();
        project=current with{Albedo=maps.Albedo??current.Albedo,Structure=maps.Structure??current.Structure,EditorFile=""};
        ApplyProject();SaveSession();Status.Text="Texture maps updated · your last editable design is in Previous draft.";
    }

    private void TextureDragOver(object sender,DragEventArgs e)
    {
        e.Handled=true;
        e.AcceptedOperation=!busy&&e.DataView.Contains(StandardDataFormats.StorageItems)?DataPackageOperation.Copy:DataPackageOperation.None;
    }

    private async void TextureDrop(object sender,DragEventArgs e)
    {
        e.Handled=true;
        if(!e.DataView.Contains(StandardDataFormats.StorageItems)||!BeginOutputOperation("Reading dropped textures…"))return;
        try
        {
            var target=(sender as FrameworkElement)?.Tag as string;
            var files=await e.DataView.GetStorageItemsAsync();
            await ReplaceTextureMaps(TextureImport.FromDrop(files.Select(f=>f.Path),target));
        }
        catch(Exception error){Error(error);}
        finally{EndOutputOperation();}
    }
}
