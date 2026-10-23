using LiveryStudio.Core;
using Microsoft.UI.Xaml;

namespace LiveryStudio;

public sealed partial class CarPaintView
{
    public event Action? IndependentSidesRequested;
    private void RequestIndependentSides(object sender,RoutedEventArgs e)
    {
        if(draft?.IndependentPaintSides==true){CanvasStatus.Text="Each side already has its own paint mapping.";return;}
        if(IndependentSidesRequested is null){CanvasStatus.Text="Create independent sides from the original car in the main studio.";return;}
        IndependentSidesRequested.Invoke();
    }
}

public partial class MainWindow
{
    private async void MakeIndependentSides()
    {
        if(busy)return;
        Busy(true);
        using var operationCancellation=new CancellationTokenSource();cancellation=operationCancellation;var token=operationCancellation.Token;
        try
        {
            await Painter.FlushAsync();var source=ReadDraft();
            Status.Text="Preparing independent sides · keeping your original design…";
            var service=new VehicleAssemblyService();
            var corrected=await Task.Run(()=>service.CreateIndependentSidesAsync(source,token));
            var saved=Path.Combine(service.Workspace,"working-cars",corrected.Id+".flcar");await Task.Run(()=>VehicleProjectBundle.Save(corrected,saved),token);token.ThrowIfCancellationRequested();
            var window=new CarBuilderWindow(GameFolder.Text,WorkshopService.DefaultTools,corrected,AdvancedModeToggle.IsChecked==true);window.Activate();
            Status.Text="Independent sides are ready in Car Builder. Your original design is preserved.";
        }
        catch(OperationCanceledException){Status.Text="Canceled. Your original design is preserved.";}
        catch(Exception x){Error(x);}
        finally{cancellation=null;Busy(false);}
    }
}
