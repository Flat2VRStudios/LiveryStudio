using LiveryStudio.Core;

internal static class GameInstallationChecks
{
    public static void Run(string temp,Action<bool,string> check)
    {
        var root=Path.Combine(temp,"game-discovery");var steam=Path.Combine(root,"Steam");var extra=Path.Combine(root,"Library with spaces");
        void FileAt(string file,string text=""){Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,text);}
        string Quote(string value)=>"\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
        string Retail(string library,string name)
        {
            var game=Path.Combine(library,"steamapps","common",name);
            FileAt(Path.Combine(game,"Flatout.exe"));FileAt(Path.Combine(game,"steam_api64.dll"));Directory.CreateDirectory(Path.Combine(game,"WIN32"));return game;
        }
        void Manifest(string library,string name,string id="402130")=>FileAt(Path.Combine(library,"steamapps","appmanifest_402130.acf"),"\"AppState\" { \"appid\" "+Quote(id)+" \"installdir\" "+Quote(name)+" }");
        var primary=Retail(steam,"FlatOut 4 Total Insanity");Manifest(steam,"FlatOut 4 Total Insanity");
        var secondary=Retail(extra,"FlatOut 4 alternate");Manifest(extra,"FlatOut 4 alternate");
        FileAt(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { // actual Steam uses nested entries\n \"0\" { \"path\" "+Quote(steam)+" \"apps\" { \"402130\" \"100\" } } \"1\" { \"path\" "+Quote(extra)+" } \"2\" { \"path\" "+Quote(extra)+" } }");
        var found=GameInstallationDiscovery.FindSteam([steam]);
        check(found.Count==2&&found.Any(x=>x.Root==primary)&&found.Any(x=>x.Root==secondary),"Find FlatOut 4 across modern Steam libraries with escaped paths and spaces, removing duplicates");
        check(found.All(x=>x.Kind==GameInstallationKind.Steam&&!x.CanImport&&x.LaunchUri=="steam://run/402130"),"Retail Steam installs support launching without claiming editable local imports");
        FileAt(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"LibraryFolders\" { \"1\" "+Quote(extra)+" }");
        check(GameInstallationDiscovery.FindSteam([steam]).Count==2,"Read older Steam library lists and the primary library's own manifest");
        FileAt(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"1\" { \"path\"");
        check(GameInstallationDiscovery.FindSteam([steam,extra]).Count==2,"Malformed library metadata does not hide another valid Steam root");
        Manifest(extra,"FlatOut 4 alternate","999999");
        check(GameInstallationDiscovery.FindSteam([extra]).Count==0,"Ignore a manifest for a different Steam app");
        Manifest(extra,"..\\..\\FlatOut 4 alternate");
        check(GameInstallationDiscovery.FindSteam([extra]).Count==0,"Reject a manifest install directory that escapes Steam's common folder");
        Manifest(extra,"FlatOut 4 alternate");System.IO.File.Delete(Path.Combine(secondary,"Flatout.exe"));
        check(GameInstallationDiscovery.FindSteam([extra]).Count==0,"Ignore stale manifests when the game is no longer installed");
        check(GameInstallationDiscovery.Inspect(Path.Combine(primary,"WIN32"))?.Root==primary,"Folder fallback accepts a game folder or a folder directly inside it");
        check(GameInstallationDiscovery.Inspect(Path.Combine(root,"unknown")) is null&&GameInstallationDiscovery.Inspect("") is null,"Unknown folders remain disconnected rather than guessed");
        var editable=Path.Combine(root,"Editable game");var data=Path.Combine(editable,"Flatout","Data");
        FileAt(Path.Combine(editable,"PlayAll","Libs","vs2022","x64","DllLauncher64_r.exe"));FileAt(Path.Combine(editable,"Flatout","Bin","x64","Flatout64_r.dll"));
        FileAt(Path.Combine(data,"Common","Tuning","Factories","CarsSkins.cfg"));FileAt(Path.Combine(data,"Common","Tuning","Factories","Rewards.cfg"));
        check(new[]{editable,Path.Combine(editable,"Flatout"),data}.All(path=>GameInstallationDiscovery.Inspect(path) is {CanImport:true,Kind:GameInstallationKind.Editable} game&&game.Data==data),"Normalize editable installation root and Data selections to the supported import target");
        System.IO.File.Delete(Path.Combine(data,"Common","Tuning","Factories","Rewards.cfg"));
        check(GameInstallationDiscovery.Inspect(data) is null,"Do not offer direct imports when required editable game settings are absent");
    }
}
