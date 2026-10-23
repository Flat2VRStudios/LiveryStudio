using System.Diagnostics;

namespace LiveryStudio.Core;
public sealed record DesktopGameTarget(string Data,string Root,string Launcher,string GameDll);
public static class DesktopGame
{
	public static DesktopGameTarget Resolve(string gameData)
	{
		var data=PathIdentity.Resolve(gameData);var root=Directory.GetParent(data)?.Parent?.FullName??"";
		var launcher=Path.Combine(root,"PlayAll","Libs","vs2022","x64","DllLauncher64_r.exe");var gameDll=Path.Combine(root,"Flatout","Bin","x64","Flatout64_r.dll");
		if(!File.Exists(launcher)||!File.Exists(gameDll))throw new FileNotFoundException("This installation does not support direct livery testing. Use Steam Workshop, or choose an installation that supports direct imports in Setup.");
		return new(data,root,launcher,gameDll);
	}
	public static IReadOnlyList<Process> Running(DesktopGameTarget target)
	{
		var result=new List<Process>();var launcher=PathIdentity.Resolve(target.Launcher);foreach(var process in Process.GetProcesses())
		{
			try{if(process.MainModule?.FileName is {} file&&PathIdentity.Resolve(file).Equals(launcher,StringComparison.OrdinalIgnoreCase))result.Add(process);else process.Dispose();}
			catch(System.ComponentModel.Win32Exception){process.Dispose();}catch(InvalidOperationException){process.Dispose();}catch(IOException){process.Dispose();}catch(UnauthorizedAccessException){process.Dispose();}
		}
		return result;
	}
	public static async Task CloseAsync(IReadOnlyList<Process> processes,CancellationToken cancellation=default)
	{
		foreach(var process in processes)
		{
			if(process.HasExited)continue;if(!process.CloseMainWindow())throw new InvalidOperationException("The running game could not close normally. Close it before testing this livery.");
			using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(15));
			try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException)when(!cancellation.IsCancellationRequested){throw new InvalidOperationException("The game is still closing. Finish closing it, then test again.");}
		}
	}
	public static Process Launch(DesktopGameTarget target)
	{
		var start=new ProcessStartInfo(target.Launcher){WorkingDirectory=target.Data,UseShellExecute=false};start.ArgumentList.Add(target.GameDll);start.ArgumentList.Add("-novr");start.ArgumentList.Add("-silent");
		start.Environment["PATH"]=Path.Combine(target.Root,"Flatout","Bin","x64")+";"+Path.GetDirectoryName(target.Launcher)+";"+Environment.GetEnvironmentVariable("PATH");return Process.Start(start)??throw new InvalidOperationException("The desktop game did not start.");
	}
}
