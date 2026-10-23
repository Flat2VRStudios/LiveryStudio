using System.Text.Json;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    private CancellationTokenSource? photoLifetime;
    private PhotoLighting CurrentPhotoLighting()
    {
        var environment=PreviewEnvironment.SelectedItem as PreviewChoice;
        string Probe(string? relative)=>string.IsNullOrEmpty(relative)?"":Path.Combine(AppContext.BaseDirectory,"Preview",relative);
        return new(){Exposure=(float)PreviewExposure.Value,LightAngle=(float)PreviewLight.Value,SunStrength=sunStrength,NormalStrength=(float)NormalStrength.Value,Normals=NormalEnabled.IsOn,Dirt=(float)DirtAmount.Value,Damage=(float)DamageAmount.Value,DirtType=Math.Clamp(DirtType.SelectedIndex,0,3),EnvironmentMode=environment?.Mode??0,Specular=Probe(environment?.Specular),Irradiance=Probe(environment?.Irradiance)};
    }
    private async void ExportPresentation(object sender,RoutedEventArgs e)
    {
        if(photoLifetime is not null||repeatLifetime is not null||mesh is null||document is null||draft is null)return;
        if(meshCarIndex!=draft.CarIndex||document.CarIndex!=draft.CarIndex){CanvasStatus.Text="Wait for the car to finish loading before exporting.";return;}
        using var lifetime=new CancellationTokenSource();photoLifetime=lifetime;var token=lifetime.Token;
        Task<StudioPhotography>? contextTask=null;Image? preview=null;
        var cache=Path.Combine(WorkshopService.DefaultWorkspace,"photo-previews",Guid.NewGuid().ToString("N"));
        using var gate=new SemaphoreSlim(1,1);var previews=new List<Task>();long revision=0;bool dialogClosed=false;
        var boundDocument=document;var boundGeneration=generation;
        try
        {
        await FlushAsync(preserveInspection:true);token.ThrowIfCancellationRequested();
        if(!ReferenceEquals(document,boundDocument)||generation!=boundGeneration||draft is null||mesh is null||meshCarIndex!=draft.CarIndex)return;
        var snapshot=document.Snapshot("Photo export",document.Layers);
        var project=JsonSerializer.Deserialize<LiveryProject>(JsonSerializer.Serialize(draft,Contract.Json),Contract.Json)!;
        var model=mesh;var camera=CameraPose;var aspect=CameraAspect;
        var lighting=CurrentPhotoLighting();
        Directory.CreateDirectory(cache);
        contextTask=Task.Run(()=>new StudioPhotography(AppContext.BaseDirectory,model,snapshot,project,lighting,token),token);
        var kind=new DialogComboBox{Header="Make",ItemsSource=new[]{"Photo · PNG","Four-angle sheet · PNG","Turntable · animated GIF"},SelectedIndex=simplePhotoKind??0,HorizontalAlignment=HorizontalAlignment.Stretch};simplePhotoKind=null;
        var size=new DialogComboBox{Header="Image size",ItemsSource=new[]{"HD · 1,280 px","Full HD · 1,920 px","4K · 3,840 px"},SelectedIndex=1,HorizontalAlignment=HorizontalAlignment.Stretch};
        var shape=new DialogComboBox{Header="Shape",ItemsSource=new[]{"Wide · 16:9","Square · 1:1","Portrait · 4:5"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var fit=new CheckBox{Content="Frame the whole car",IsChecked=true};
        preview=new Image{Height=Math.Max(100,Math.Min(210,ActualHeight*.3)),Stretch=Stretch.Uniform};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(preview,"Export preview of the complete design");
        var details=new TextBlock{Text="Preparing your design…",TextWrapping=TextWrapping.Wrap,FontSize=12};
        var body=new StackPanel{Spacing=12};body.Children.Add(new TextBlock{Text=project.Name,FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        body.Children.Add(preview);body.Children.Add(details);if(AdvancedMode){body.Children.Add(kind);body.Children.Add(shape);body.Children.Add(size);body.Children.Add(fit);}
        body.Children.Add(new TextBlock{Text="Uses your complete design and current lighting. Your camera, selection and editing tools stay in the studio.",TextWrapping=TextWrapping.Wrap,FontSize=12});
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(160,Math.Min(620,XamlRoot.Size.Height-220))};
        var dialog=new ContentDialog{Title="Show your design",Content=scroll,XamlRoot=XamlRoot,PrimaryButtonText="Save…",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Primary,IsPrimaryButtonEnabled=false};
        (int Width,int Height) Dimensions(int mode,int selectedSize,int selectedShape,bool small=false)
        {
            var edge=small?480:mode==2?960:new[]{1280,1920,3840}[selectedSize];
            return selectedShape switch{1=>(edge,edge),2=>(edge*4/5,edge),_=>(edge,edge*9/16)};
        }
        async Task UpdatePreview()
        {
            var version=++revision;dialog.IsPrimaryButtonEnabled=false;var mode=kind.SelectedIndex;var selectedSize=size.SelectedIndex;var selectedShape=shape.SelectedIndex;var whole=fit.IsChecked==true;
            size.IsEnabled=mode!=2;fit.IsEnabled=mode==0;var dimensions=Dimensions(mode,selectedSize,selectedShape);var thumbnail=Dimensions(mode,selectedSize,selectedShape,true);
            details.Text=$"{dimensions.Width:N0} × {dimensions.Height:N0} pixels"+(mode==2?" · 6-second loop":"");
            await gate.WaitAsync();
            try
            {
                if(dialogClosed||version!=revision||token.IsCancellationRequested)return;var context=await contextTask;
                var path=Path.Combine(cache,version+(mode==2?".gif":".png"));
                if(mode==2)
                {
                    var poses=PhotoLayout.Turntable(model,camera.Yaw,Math.Clamp(camera.Pitch,-.6f,.9f),thumbnail.Width,thumbnail.Height,16);
                    await PhotoEncoding.WriteAsync(path,true,poses.Count,i=>Task.Run(()=>context.Capture(poses[i],thumbnail.Width,thumbnail.Height,token),token),token);
                }
                else
                {
                    var image=await Task.Run(()=>mode==1?PhotoSheet.Render(project.Name,project.SelectedCar.Display,thumbnail.Width,thumbnail.Height,PhotoLayout.Sheet(model,thumbnail.Width,thumbnail.Height),tile=>context.Capture(tile.Camera,tile.Width,tile.Height,token),token):context.Capture(PhotoLayout.Current(model,camera,aspect,thumbnail.Width,thumbnail.Height,whole),thumbnail.Width,thumbnail.Height,token),token);
                    await PhotoEncoding.WriteAsync(path,false,1,_=>Task.FromResult(image),token);
                }
                if(dialogClosed||version!=revision)return;preview.Source=new BitmapImage{UriSource=new Uri(path)};dialog.IsPrimaryButtonEnabled=true;
            }
            catch(OperationCanceledException){}
            catch(Exception x){if(!dialogClosed&&version==revision){details.Text=x.Message;dialog.IsPrimaryButtonEnabled=false;}}
            finally{gate.Release();}
        }
        void SchedulePreview(){if(!dialogClosed)previews.Add(UpdatePreview());}
        kind.SelectionChanged+=(_,_)=>SchedulePreview();size.SelectionChanged+=(_,_)=>SchedulePreview();shape.SelectionChanged+=(_,_)=>SchedulePreview();fit.Click+=(_,_)=>SchedulePreview();
            SchedulePreview();var result=await dialog.ShowAsync();dialogClosed=true;revision++;if(result!=ContentDialogResult.Primary)lifetime.Cancel();await Task.WhenAll(previews);
            if(result!=ContentDialogResult.Primary)return;
            var mode=kind.SelectedIndex;var dimensions=Dimensions(mode,size.SelectedIndex,shape.SelectedIndex);var whole=fit.IsChecked==true;
            var suffix=mode==1?"-four-angles":mode==2?"-turntable":"-photo";
            var picker=new FileSavePicker{SuggestedFileName=string.Concat(project.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))+suffix};
            picker.FileTypeChoices.Add(mode==2?"Animated GIF":"PNG image",new[]{mode==2?".gif":".png"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(HostWindow!));
            if(await picker.PickSaveFileAsync() is not {} file)return;
            var progress=new ProgressBar{Minimum=0,Maximum=mode==2?48:1,Value=0};var status=new TextBlock{Text="Rendering your design…",TextWrapping=TextWrapping.Wrap};
            var progressBody=new StackPanel{Spacing=12};progressBody.Children.Add(status);progressBody.Children.Add(progress);
            var savingDialog=new ContentDialog{Title=mode==2?"Creating your turntable":"Creating your image",Content=progressBody,XamlRoot=XamlRoot,CloseButtonText="Cancel"};
            using var savingCancellation=CancellationTokenSource.CreateLinkedTokenSource(token);savingDialog.CloseButtonClick+=(_,_)=>savingCancellation.Cancel();
            var savingToken=savingCancellation.Token;var shown=savingDialog.ShowAsync();
            try
            {
                var context=await contextTask;var poses=mode==2?PhotoLayout.Turntable(model,camera.Yaw,Math.Clamp(camera.Pitch,-.6f,.9f),dimensions.Width,dimensions.Height):[];
                await PhotoEncoding.WriteAsync(file.Path,mode==2,mode==2?poses.Count:1,async i=>await Task.Run(()=>mode==1?PhotoSheet.Render(project.Name,project.SelectedCar.Display,dimensions.Width,dimensions.Height,PhotoLayout.Sheet(model,dimensions.Width,dimensions.Height),tile=>context.Capture(tile.Camera,tile.Width,tile.Height,savingToken),savingToken):context.Capture(mode==2?poses[i]:PhotoLayout.Current(model,camera,aspect,dimensions.Width,dimensions.Height,whole),dimensions.Width,dimensions.Height,savingToken),savingToken),savingToken,new Progress<int>(n=>{progress.Value=n;status.Text=mode==2?$"Frame {n} of 48":"Saving image…";}));
                CanvasStatus.Text="Saved "+Path.GetFileName(file.Path)+" · your editable design is unchanged.";
            }
            catch(OperationCanceledException){CanvasStatus.Text="Export canceled · your editable design is unchanged.";}
            finally{savingDialog.Hide();await shown;}
        }
        catch(OperationCanceledException){}
        catch(Exception x){Fail(x);}
        finally
        {
            dialogClosed=true;lifetime.Cancel();await Task.WhenAll(previews);if(preview is not null)preview.Source=null;
            try{if(contextTask is not null)(await contextTask).Dispose();}catch(OperationCanceledException){}catch(Exception){}
            photoLifetime=null;
            try{Directory.Delete(cache,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
