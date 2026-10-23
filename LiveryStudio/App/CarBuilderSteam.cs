using LiveryStudio.Core;
using Microsoft.UI.Xaml;

namespace LiveryStudio;

public sealed partial class CarBuilderWindow
{
    private SteamCarReceipt? steamInstalled;
    private SteamCarInstall SteamImports=>new(service.Workspace);
    private void RefreshInstalled()
    {
        var game=GameInstallationDiscovery.Inspect(GameData.Text);
        var steam=game?.Kind==GameInstallationKind.Steam;
        var compatible=game?.CanImport==true;
        var steamReady=steam&&project.Paint.IndependentPaintSides&&project.DonorIndex==0;
        BuildButton.Content=steam?"Build & drive":"Build & install car";
        BuildButton.IsEnabled=!busy&&project.Prepared&&(compatible||steamReady);
        LaunchButton.IsEnabled=!busy&&game is not null;
        DriveIntro.Text=steam?"Install your finished look locally, then drive it in FlatOut 4.":"Your donor files are backed up before installing the car.";
        DriveHint.Text=steam?"Choose Grosse pointe in Garage, then select the first livery in Customize → Liveries.":"Select the donor in Garage, then choose this car’s WK_ livery in Customize → Liveries.";
        DriveDetails.Text=steam?"Restore original car removes the local package and restores its loading list. Your editable car project stays saved.":"Custom exteriors use the donor's handling, collision and wheel rig.";
        RestoreButton.Content=steam?"Restore original car":"Restore original donor";
        installed=null;steamInstalled=null;RestoreButton.IsEnabled=false;
        CarGameStatus.Text=steamReady?"Steam connected · independent bumper paint is included with the car.":compatible?"Game connected · your original car is backed up before installation.":steam?"Steam connected · independent-side installation currently supports Grosse pointe.":"Save and paint without the game, or choose your game installation.";
        InstalledLabel.Text="";
        try
        {
            if(steam){steamInstalled=SteamImports.Current(game!.Root);if(steamInstalled?.State=="Removed")steamInstalled=null;InstalledLabel.Text=steamInstalled is null?"Grosse pointe's original first livery is available.":steamInstalled.Name+" · "+steamInstalled.State;RestoreButton.IsEnabled=!busy&&steamInstalled is not null;if(steamInstalled?.State is "Installing" or "Restoring")BuildButton.IsEnabled=false;}
            else if(compatible){installed=imports.List(GameData.Text).FirstOrDefault(r=>r.Receipt.DonorIndex==project.DonorIndex&&r.Receipt.State is not ("Removed" or "Rolled back"));InstalledLabel.Text=installed is null?"Original donor is available.":installed.Receipt.Name+" · "+installed.Receipt.State;RestoreButton.IsEnabled=!busy&&installed is not null;}
        }
        catch(Exception e){InstalledLabel.Text=e.Message;BuildButton.IsEnabled=false;}
    }
    private void LaunchSteam(GameInstallation game)
    {
        var current=SteamImports.Current(game.Root);
        if(current?.State is "Installing" or "Restoring")throw new InvalidOperationException("Restore the interrupted car installation before launching the game.");
        if(current is {State:"Installed"})SteamImports.Verify(current);
        using var opened=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(game.LaunchUri){UseShellExecute=true});
        Status.Text="FlatOut 4 is opening · choose Grosse pointe's first livery.";
    }
    private async Task BuildSteam(GameInstallation game,CancellationToken token)
    {
        SteamCarInstall.RequireClosed(game.Root);
        var built=await service.BuildSteamAsync(project,paintService,game.Root,token);
        token.ThrowIfCancellationRequested();
        steamInstalled=await Task.Run(()=>SteamImports.Install(built));
        SteamImports.Verify(steamInstalled);LaunchSteam(game);
    }
}
