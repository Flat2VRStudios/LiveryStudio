namespace LiveryStudio.Core;

internal static class PathIdentity
{
	// MSIX app-data redirection is not a filesystem junction. Native activation
	// contexts (notably VC90 private assemblies) need the real opened-file path.
	[System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
	private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,System.Text.StringBuilder path,uint length,uint flags);
	public static string PhysicalFile(string path)
	{
		if(!OperatingSystem.IsWindows())return Resolve(path);
		using var handle=File.OpenHandle(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var result=new System.Text.StringBuilder(1024);var length=GetFinalPathNameByHandle(handle,result,(uint)result.Capacity,0);if(length==0)throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());if(length>=result.Capacity){result.Capacity=checked((int)length+1);length=GetFinalPathNameByHandle(handle,result,(uint)result.Capacity,0);if(length==0||length>=result.Capacity)throw new IOException("Could not resolve the native tool path.");}var value=result.ToString();return value.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)?@"\\"+value[8..]:value.StartsWith(@"\\?\",StringComparison.Ordinal)?value[4..]:value;
	}
	// Resolve directory junctions as well as final-file links without changing the user's saved paths.
	public static string Resolve(string path)
	{
		var full=Path.GetFullPath(path);var root=Path.GetPathRoot(full)!;var resolved=root;
		foreach(var segment in full[root.Length..].Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
		{
			resolved=Path.Combine(resolved,segment);
			FileSystemInfo info=Directory.Exists(resolved)?new DirectoryInfo(resolved):new FileInfo(resolved);
			if(info.Exists&&info.LinkTarget is not null)resolved=info.ResolveLinkTarget(true)?.FullName??resolved;
		}
		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved));
	}
}
