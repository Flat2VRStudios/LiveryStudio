using System.Text;
using Microsoft.Win32;

namespace LiveryStudio.Core;

public enum GameInstallationKind { Steam, Editable }
public sealed record GameInstallation(string Root,string Data,GameInstallationKind Kind,bool CanImport,string Executable)
{
    public string LaunchUri=>"steam://run/"+GameInstallationDiscovery.SteamAppId;
}

public static class GameInstallationDiscovery
{
    public const string SteamAppId="402130";
    public static IReadOnlyList<GameInstallation> FindSteam(IEnumerable<string>? steamRoots=null)
    {
        var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in steamRoots??DefaultSteamRoots())
        {
            if(!TryFullPath(root,out var full))continue;libraries.Add(full);
            foreach(var file in new[]{Path.Combine(full,"steamapps","libraryfolders.vdf"),Path.Combine(full,"config","libraryfolders.vdf")})
            {
                try
                {
                    var tree=ReadVdf(file);if(!tree.TryGetValue("libraryfolders",out var folderRoot)||folderRoot is not Dictionary<string,object> folders)continue;
                    foreach(var (key,value) in folders)
                    {
                        if(!int.TryParse(key,out _))continue;
                        var path=value as string??(value is Dictionary<string,object> entry&&entry.TryGetValue("path",out var field)?field as string:null);
                        if(path is not null&&TryFullPath(path,out var library))libraries.Add(library);
                    }
                }
                catch(Exception e)when(IsDiscoveryError(e)){}
            }
        }
        var found=new Dictionary<string,GameInstallation>(StringComparer.OrdinalIgnoreCase);
        foreach(var library in libraries.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var manifest=ReadVdf(Path.Combine(library,"steamapps","appmanifest_"+SteamAppId+".acf"));
                if(!manifest.TryGetValue("AppState",out var state)||state is not Dictionary<string,object> app||!app.TryGetValue("appid",out var id)||id as string!=SteamAppId||!app.TryGetValue("installdir",out var folder)||folder is not string directory||!SafeDirectoryName(directory))continue;
                var installation=Inspect(Path.Combine(library,"steamapps","common",directory));
                if(installation is not null)found[installation.Root]=installation;
            }
            catch(Exception e)when(IsDiscoveryError(e)){}
        }
        return found.Values.ToArray();
    }
    public static GameInstallation? Inspect(string? selected)
    {
        if(!TryFullPath(selected,out var full))return null;
        try
        {
            foreach(var data in new[]{full,Path.Combine(full,"Data"),Path.Combine(full,"Flatout","Data")}.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var root=Directory.GetParent(data)?.Parent?.FullName??"";
                var launcher=Path.Combine(root,"PlayAll","Libs","vs2022","x64","DllLauncher64_r.exe");var dll=Path.Combine(root,"Flatout","Bin","x64","Flatout64_r.dll");
                if(!File.Exists(launcher)||!File.Exists(dll)||!HasEditableSettings(data))continue;
                return new(root,Path.GetFullPath(data),GameInstallationKind.Editable,true,launcher);
            }
            foreach(var root in new[]{full,Directory.GetParent(full)?.FullName}.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var exe=Path.Combine(root,"Flatout.exe");
                if(File.Exists(exe)&&File.Exists(Path.Combine(root,"steam_api64.dll"))&&Directory.Exists(Path.Combine(root,"WIN32")))return new(root,root,GameInstallationKind.Steam,false,exe);
            }
        }
        catch(Exception e)when(IsDiscoveryError(e)){}
        return null;
    }
    public static bool HasEditableSettings(string data)=>File.Exists(Path.Combine(data,"Common","Tuning","Factories","CarsSkins.cfg"))&&File.Exists(Path.Combine(data,"Common","Tuning","Factories","Rewards.cfg"));
    private static bool SafeDirectoryName(string value)=>value.Length is >0 and <=255&&value is not ("." or "..")&&!value.Any(c=>char.IsControl(c)||c is '/' or '\\' or ':');
    private static bool TryFullPath(string? value,out string full)
    {
        full="";if(string.IsNullOrWhiteSpace(value)||!Path.IsPathFullyQualified(value))return false;try{full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));return true;}catch(Exception e)when(IsDiscoveryError(e)){return false;}
    }
    private static bool IsDiscoveryError(Exception e)=>e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    private static IReadOnlyList<string> DefaultSteamRoots()
    {
        var roots=new List<string>();
        if(OperatingSystem.IsWindows())
        {
            foreach(var (hive,key,name) in new[]{(Registry.CurrentUser,@"Software\Valve\Steam","SteamPath"),(Registry.LocalMachine,@"SOFTWARE\WOW6432Node\Valve\Steam","InstallPath"),(Registry.LocalMachine,@"SOFTWARE\Valve\Steam","InstallPath")})
            {try{using var entry=hive.OpenSubKey(key);if(entry?.GetValue(name) is string path)roots.Add(path);}catch(Exception e)when(IsDiscoveryError(e)){}}
        }
        foreach(var programFiles in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})if(programFiles.Length>0)roots.Add(Path.Combine(programFiles,"Steam"));
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static Dictionary<string,object> ReadVdf(string file)
    {
        if(new FileInfo(file).Length>4*1024*1024)throw new InvalidDataException("Steam library metadata is too large.");return new Vdf(File.ReadAllText(file)).Read();
    }
    private sealed class Vdf(string text)
    {
        private int at,tokens;
        public Dictionary<string,object> Read()=>Object(false,0);
        private Dictionary<string,object> Object(bool nested,int depth)
        {
            if(depth>24)throw new InvalidDataException("Invalid Steam library metadata.");var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
            while(true)
            {
                var key=Token();if(key is null){if(nested)throw new InvalidDataException("Incomplete Steam library metadata.");return result;}
                if(key=="}"){if(!nested)throw new InvalidDataException("Unexpected Steam metadata ending.");return result;}
                if(key=="{")throw new InvalidDataException("Invalid Steam metadata key.");var value=Token()??throw new InvalidDataException("Missing Steam metadata value.");
                if(value=="}")throw new InvalidDataException("Missing Steam metadata value.");result[key]=value=="{"?Object(true,depth+1):value;
            }
        }
        private string? Token()
        {
            while(at<text.Length)
            {
                if(char.IsWhiteSpace(text[at])||text[at]=='\uFEFF'){at++;continue;}
                if(text[at]=='/'&&at+1<text.Length&&text[at+1]=='/'){while(at<text.Length&&text[at]!='\n')at++;continue;}break;
            }
            if(at==text.Length)return null;if(++tokens>200000)throw new InvalidDataException("Steam metadata contains too many entries.");
            if(text[at] is '{' or '}')return text[at++].ToString();var quoted=text[at]=='"';if(quoted)at++;var token=new StringBuilder();
            while(at<text.Length)
            {
                var c=text[at++];if(quoted&&c=='"')return token.ToString();if(!quoted&&(char.IsWhiteSpace(c)||c is '{' or '}')){at--;return token.ToString();}
                if(quoted&&c=='\\'&&at<text.Length&&text[at] is '\\' or '"')c=text[at++];token.Append(c);if(token.Length>4096)throw new InvalidDataException("Steam metadata value is too long.");
            }
            if(quoted)throw new InvalidDataException("Unterminated Steam metadata string.");return token.ToString();
        }
    }
}
