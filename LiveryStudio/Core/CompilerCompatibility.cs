using System.Security.Cryptography;
using System.Text;

namespace LiveryStudio.Core;

public static class CompilerCompatibility
{
	public static string? Prepare(string tools,string workspace)
	{
		var fallback=Path.Combine(tools,"Compatibility","d3d9.dll");if(!File.Exists(fallback))return null;
		var bin=Path.Combine(tools,"Bin");var files=Directory.EnumerateFiles(bin,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".log",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
		var hashes=files.ToDictionary(p=>Path.GetRelativePath(bin,p),WorkshopService.Hash,StringComparer.OrdinalIgnoreCase);hashes["d3d9.dll"]=WorkshopService.Hash(fallback);
		var license=Path.Combine(tools,"Compatibility","DXVK-LICENSE.txt");if(File.Exists(license))hashes["DXVK-LICENSE.txt"]=WorkshopService.Hash(license);
		var signature=string.Join("\n",hashes.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Key.ToUpperInvariant()+"="+p.Value));var id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..24];
		var cache=Path.GetFullPath(Path.Combine(workspace,"compiler-cache"));var destination=Path.Combine(cache,id);Directory.CreateDirectory(cache);
		using var mutex=new Mutex(false,"Local\\LiveryStudio-compiler-"+id);var acquired=false;
		try
		{
			try{acquired=mutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){acquired=true;}if(!acquired)throw new IOException("Another window is preparing the compiler. Try again in a moment.");
			bool Valid(string root)=>hashes.All(p=>File.Exists(Path.Combine(root,p.Key))&&WorkshopService.Hash(Path.Combine(root,p.Key))==p.Value);
			if(Directory.Exists(destination)){if(Valid(destination))return destination;destination+="-"+Guid.NewGuid().ToString("N");}
			var staging=Path.Combine(cache,id+"-"+Guid.NewGuid().ToString("N")+".tmp");Directory.CreateDirectory(staging);
			try
			{
				foreach(var file in files){var target=Path.Combine(staging,Path.GetRelativePath(bin,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);}
				File.Copy(fallback,Path.Combine(staging,"d3d9.dll"),true);if(File.Exists(license))File.Copy(license,Path.Combine(staging,"DXVK-LICENSE.txt"),true);
				if(!Valid(staging))throw new IOException("The bundled compatibility runtime changed while being copied. Retry the bake.");
				Directory.Move(staging,destination);return destination;
			}
			finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
		}
		finally{if(acquired)mutex.ReleaseMutex();}
	}
}
