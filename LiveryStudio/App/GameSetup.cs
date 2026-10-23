using System.Diagnostics;
using LiveryStudio.Core;
using Microsoft.UI.Xaml;

namespace LiveryStudio;

public partial class MainWindow
{
    private GameInstallation? connectedGame;
    private bool findingGame;

    private void UpdateGameSetup()
    {
        if(GameDetectionTitle is null)return;
        connectedGame=GameInstallationDiscovery.Inspect(GameFolder.Text);
        GameDetectionTitle.Text=findingGame?"Finding FlatOut 4…":connectedGame is null?"FlatOut 4 wasn't found":connectedGame.CanImport?"Game connected":"FlatOut 4 found in Steam";
        GameDetectionDetail.Text=findingGame?"Checking your Steam libraries. You can keep creating while we look.":connectedGame is null?"You can keep creating without the game. If it is installed, choose its folder below.":connectedGame.CanImport?"Build, import and launch your livery in one click. Installed liveries appear below.":"Launch the game here. To play with your finished livery, build it and publish it to Steam Workshop.";
        GameLocation.Text=connectedGame?.Root??"No installation selected";
        LaunchGameButton.IsEnabled=!busy&&!findingGame&&connectedGame is not null;
        FindGameButton.IsEnabled=ChooseGameButton.IsEnabled=!busy&&!findingGame;
        SteamWorkshopButton.Visibility=connectedGame is {CanImport:false}?Visibility.Visible:Visibility.Collapsed;
        InstalledImportsCard.Visibility=connectedGame is {CanImport:true}?Visibility.Visible:Visibility.Collapsed;
    }
    private async Task DetectGameAsync(bool replaceSelection=false)
    {
        if(findingGame)return;
        findingGame=true;UpdateGameSetup();
        try
        {
            var found=await Task.Run(()=>GameInstallationDiscovery.FindSteam().FirstOrDefault()??FindNearbyGame());
            if(replaceSelection||GameInstallationDiscovery.Inspect(GameFolder.Text) is null)GameFolder.Text=found?.Data??"";
            SaveSession();
        }
        catch(Exception e){log.Add("Game detection: "+e.Message);}
        finally{findingGame=false;UpdateGameSetup();RefreshInstalled();}
    }
    private static GameInstallation? FindNearbyGame()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(GameInstallationDiscovery.Inspect(directory.FullName) is {CanImport:true} game)return game;
        return null;
    }
    private async void FindGameAgain(object sender,RoutedEventArgs e)=>await DetectGameAsync(replaceSelection:true);
    private async void PickGame(object sender,RoutedEventArgs e)
    {
        try
        {
            if(await FolderPick() is not {} path)return;
            var installation=GameInstallationDiscovery.Inspect(path)??throw new InvalidOperationException("FlatOut 4 wasn't found in that folder. Choose the game's installation folder.");
            GameFolder.Text=installation.Data;UpdateGameSetup();SaveSession();RefreshInstalled();
        }
        catch(Exception x){Error(x);}
    }
    private void LaunchGame(object sender,RoutedEventArgs e)
    {
        try
        {
            UpdateGameSetup();
            var game=connectedGame??throw new InvalidOperationException("Find or choose your FlatOut 4 installation in Setup first.");
            if(game.Kind==GameInstallationKind.Steam)
            {
                using var launched=Process.Start(new ProcessStartInfo(game.LaunchUri){UseShellExecute=true});
                Info("FlatOut 4 is opening","Finished liveries can be added to the game through Steam Workshop.");
            }
            else
            {
                using var launched=DesktopGame.Launch(DesktopGame.Resolve(game.Data));
                Info("Game launched","Imported liveries are in Garage → Customize → Liveries.");
            }
        }
        catch(Exception x){Error(x);}
    }
    private void OpenSteamWorkshop(object sender,RoutedEventArgs e)
    {
        try{using var opened=Process.Start(new ProcessStartInfo("https://steamcommunity.com/app/"+SteamWorkshop.FlatOutVR+"/workshop/"){UseShellExecute=true});}catch(Exception x){Error(x);}
    }
}
