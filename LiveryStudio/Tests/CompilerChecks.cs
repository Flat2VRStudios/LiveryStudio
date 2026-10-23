using LiveryStudio.Core;

internal static class CompilerChecks
{
	public static void Run(string temp,Action<bool,string> check)
	{
		var tools=Path.Combine(temp,"compiler-tools");var bin=Path.Combine(tools,"Bin");var plugins=Path.Combine(bin,"ContentPipeline");var fallback=Path.Combine(tools,"Compatibility");Directory.CreateDirectory(plugins);Directory.CreateDirectory(fallback);
		var compiler=Path.Combine(bin,"Compiler_r.exe");var plugin=Path.Combine(plugins,"Texture.dll");File.WriteAllText(compiler,"original compiler");File.WriteAllText(plugin,"original plugin");File.WriteAllText(Path.Combine(fallback,"d3d9.dll"),"bundled compatibility runtime");File.WriteAllText(Path.Combine(fallback,"DXVK-LICENSE.txt"),"license");var workspace=Path.Combine(temp,"compiler-workspace");
		var prepared=CompilerCompatibility.Prepare(tools,workspace)!;check(File.ReadAllText(Path.Combine(prepared,"Compiler_r.exe"))=="original compiler"&&File.ReadAllText(Path.Combine(prepared,"ContentPipeline","Texture.dll"))=="original plugin"&&File.Exists(Path.Combine(prepared,"d3d9.dll")),"Compatibility staging preserves original compiler and plugin bytes beside the bundled graphics runtime");
		check(!File.Exists(Path.Combine(bin,"d3d9.dll"))&&File.ReadAllText(compiler)=="original compiler","Compatibility startup never modifies the shipped native compiler directory");
		check(CompilerCompatibility.Prepare(tools,workspace)==prepared,"Verified compiler cache is reusable without installation");
		File.WriteAllText(Path.Combine(prepared,"Compiler_r.exe"),"interrupted or changed cache");var repaired=CompilerCompatibility.Prepare(tools,workspace)!;check(repaired!=prepared&&File.ReadAllText(Path.Combine(repaired,"Compiler_r.exe"))=="original compiler","Changed compiler cache recovers into a fresh verified directory");
		File.WriteAllText(plugin,"updated plugin");var updated=CompilerCompatibility.Prepare(tools,workspace)!;check(updated!=repaired&&File.ReadAllText(Path.Combine(updated,"ContentPipeline","Texture.dll"))=="updated plugin","Compiler dependency changes select a new cache identity");
	}
}
