using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LiveryStudio.Core;

public sealed record WorkshopPublication(uint AppId, ulong PublishedFileId, bool Published, bool NeedsLegalAgreement, DateTimeOffset UpdatedAt)
{
    public string Url => "https://steamcommunity.com/sharedfiles/filedetails/?id=" + PublishedFileId;
}

// Uses the current x64 Steamworks runtime, with an explicit consumer app. The
// original x86 sender silently chooses the original FlatOut 4 app instead of VR.
public static class SteamWorkshop
{
    public const uint FlatOutVR = 3844750, FlatOut4 = 402130;
    private static readonly SemaphoreSlim Gate = new(1);
    private static nint runtime;
    public static string GameName(uint appId) => appId switch
    {
        FlatOutVR => "FlatOut 4: Total Insanity VR",
        FlatOut4 => "FlatOut 4: Total Insanity",
        _ => throw new ArgumentException("Choose the FlatOut VR or original FlatOut 4 Workshop.")
    };
    public static string ReceiptPath(BuiltLivery livery, uint appId)
    {
        _ = GameName(appId);
        return Path.Combine(livery.BuildRoot, $"workshop-{appId}.json");
    }
    public static void ValidatePackage(BuiltLivery livery)
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(livery.BuildRoot, "Workshop"));
        if (!string.Equals(expectedRoot, Path.GetFullPath(livery.PackageDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Workshop content folder does not match this saved build.");
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [livery.Id + "/" + livery.Id + ".PKG"] = Path.Combine(livery.BuildRoot, "Data/WIN32/Pkg/CustomPkg/Pkg.pkg"),
            [livery.Id + ".PACLOG"] = Path.Combine(livery.BuildRoot, "Data/WIN32/Paclog.PACLOG")
        };
        if ((File.GetAttributes(expectedRoot) & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateDirectories(expectedRoot, "*", SearchOption.AllDirectories).Any(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("The Workshop content folder must not contain links.");
        var files = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories);
        if (files.Length != expected.Count) throw new InvalidDataException("The Workshop package contains unexpected or missing files.");
        foreach (var file in files)
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                !expected.TryGetValue(Path.GetRelativePath(expectedRoot, file).Replace('\\', '/'), out var source) ||
                !File.Exists(source) || WorkshopService.Hash(file) != WorkshopService.Hash(source))
                throw new InvalidDataException("The Workshop package is missing or changed. Bake the livery again before publishing.");
    }
    public static async Task<WorkshopPublication> PublishAsync(BuiltLivery livery, string toolsRoot, uint appId, Action<string>? log, CancellationToken cancellation)
    {
        _ = GameName(appId);
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Workshop publishing requires the Windows x64 app.");
        livery.Project.Validate(publishing: true, textureSources: false);
        WorkshopService.ValidateBuild(livery);
        ValidatePackage(livery);
        if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(livery.Project.Banner).ToLowerInvariant()))
            throw new ArgumentException("Choose a PNG or JPEG Workshop preview image.");
        if (new FileInfo(livery.Project.Banner).Length is 0 or > 1024 * 1024)
            throw new ArgumentException("Choose a Workshop preview image smaller than 1 MB.");
        _ = FinishMaps.Read(livery.Project.Banner);
        await Gate.WaitAsync(cancellation);
        try { return await Task.Run(() => Publish(livery, toolsRoot, appId, log, cancellation), CancellationToken.None); }
        finally { Gate.Release(); }
    }
    private static WorkshopPublication Publish(BuiltLivery livery, string toolsRoot, uint appId, Action<string>? log, CancellationToken cancellation)
    {
        var oldApp = Environment.GetEnvironmentVariable("SteamAppId");
        var oldGame = Environment.GetEnvironmentVariable("SteamGameId");
        var initialized = false;
        var receiptFile = ReceiptPath(livery, appId);
        WorkshopPublication? receipt = null;
        if (File.Exists(receiptFile))
        {
            receipt = JsonSerializer.Deserialize<WorkshopPublication>(File.ReadAllText(receiptFile), Contract.Json)
                ?? throw new InvalidDataException("The Workshop publication receipt is damaged.");
            if (receipt.AppId != appId || receipt.PublishedFileId == 0)
                throw new InvalidDataException("The Workshop receipt does not match the selected game.");
        }
        void Save(WorkshopPublication value)
        {
            var temp = receiptFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Contract.Json));
            File.Move(temp, receiptFile, true);
            receipt = value;
        }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (runtime == 0)
            {
                var dll = Path.Combine(Path.GetFullPath(toolsRoot), "Bin", "steam_api64.dll");
                runtime = NativeLibrary.Load(dll);
                NativeLibrary.SetDllImportResolver(typeof(SteamWorkshop).Assembly, (name, _, _) => name == "steam_api64" ? runtime : 0);
            }
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
            var message = new StringBuilder(1024);
            if (Native.SteamAPI_InitFlat(message) != 0)
                throw new InvalidOperationException("Steam could not connect. Start Steam, sign in to an account that owns the selected game, and retry. " + message);
            initialized = true;
            var utils = Native.SteamAPI_SteamUtils_v010();
            var ugc = Native.SteamAPI_SteamUGC_v021();
            if (utils == 0 || ugc == 0 || Native.SteamAPI_ISteamUtils_GetAppID(utils) != appId)
                throw new InvalidOperationException("Steam initialized for a different game; nothing was uploaded.");
            var user = Native.SteamAPI_SteamUser_v023();
            var connecting = Stopwatch.StartNew();
            while (user != 0 && !Native.SteamAPI_ISteamUser_BLoggedOn(user) && connecting.Elapsed < TimeSpan.FromSeconds(30))
            {
                cancellation.ThrowIfCancellationRequested();
                Native.SteamAPI_RunCallbacks();
                Thread.Sleep(100);
            }
            if (user == 0 || !Native.SteamAPI_ISteamUser_BLoggedOn(user))
                throw new InvalidOperationException("Sign in to Steam before publishing.");
            log?.Invoke($"Publishing to {GameName(appId)} ({appId}).");
            if (receipt is null)
            {
                var created = Wait<CreateResult>(utils, Native.SteamAPI_ISteamUGC_CreateItem(ugc, appId, 0), 3403, CancellationToken.None);
                CheckResult(created.Result, "Creating Workshop item");
                if (created.Id == 0) throw new InvalidDataException("Steam returned an empty Workshop item ID.");
                // Persist before uploading, so a failure or timeout can resume the
                // same item instead of creating a duplicate on the next attempt.
                Save(new(appId, created.Id, false, created.NeedsAgreement, DateTimeOffset.UtcNow));
            }
            var id = receipt!.PublishedFileId;
            log?.Invoke("Workshop item: " + receipt.Url);
            cancellation.ThrowIfCancellationRequested();
            var update = Native.SteamAPI_ISteamUGC_StartItemUpdate(ugc, appId, id);
            if (update == ulong.MaxValue) throw new InvalidOperationException("Steam rejected the Workshop update.");
            void Set(bool ok, string field) { if (!ok) throw new InvalidOperationException("Steam rejected the livery's " + field + "."); }
            Set(Native.SteamAPI_ISteamUGC_SetItemTitle(ugc, update, livery.Project.Name), "title");
            Set(Native.SteamAPI_ISteamUGC_SetItemDescription(ugc, update, livery.Project.Description), "description");
            Set(Native.SteamAPI_ISteamUGC_SetItemMetadata(ugc, update, JsonSerializer.Serialize(new { format = "FlatOutLiveryPackage", version = 1, car = livery.Project.CarIndex, body = livery.Id })), "metadata");
            Set(Native.SteamAPI_ISteamUGC_SetItemContent(ugc, update, Path.GetFullPath(livery.PackageDirectory)), "content folder");
            Set(Native.SteamAPI_ISteamUGC_SetItemPreview(ugc, update, Path.GetFullPath(livery.Project.Banner)), "preview image");
            Set(Native.SteamAPI_ISteamUGC_SetItemVisibility(ugc, update, 0), "public visibility");
            cancellation.ThrowIfCancellationRequested();
            log?.Invoke("Uploading livery package and preview…");
            // Steam cannot cancel a submitted upload. Keep pumping it to a
            // definite result even if the caller cancels after submission.
            var submitted = Wait<SubmitResult>(utils, Native.SteamAPI_ISteamUGC_SubmitItemUpdate(ugc, update, "Published from Livery Studio"), 3404, CancellationToken.None);
            CheckResult(submitted.Result, "Uploading Workshop item");
            if (submitted.Id != id) throw new InvalidDataException("Steam returned a different Workshop item ID.");
            Save(new(appId, id, !submitted.NeedsAgreement, submitted.NeedsAgreement, DateTimeOffset.UtcNow));
            log?.Invoke((receipt!.Published ? "Publication confirmed: " : "Workshop agreement required: ") + receipt.Url);
            return receipt;
        }
        finally
        {
            if (initialized) Native.SteamAPI_Shutdown();
            Environment.SetEnvironmentVariable("SteamAppId", oldApp);
            Environment.SetEnvironmentVariable("SteamGameId", oldGame);
        }
    }
    private static T Wait<T>(nint utils, ulong call, int callback, CancellationToken cancellation) where T : struct
    {
        if (call == 0) throw new InvalidOperationException("Steam could not start the Workshop request.");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            Native.SteamAPI_RunCallbacks();
            if (Native.SteamAPI_ISteamUtils_IsAPICallCompleted(utils, call, out var failed))
            {
                if (failed) throw new IOException("Steam's Workshop request failed. Check the connection and retry the same build.");
                var size = Marshal.SizeOf<T>();
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!Native.SteamAPI_ISteamUtils_GetAPICallResult(utils, call, buffer, size, callback, out failed) || failed)
                        throw new IOException("Steam could not confirm the Workshop request.");
                    return Marshal.PtrToStructure<T>(buffer);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            if (timer.Elapsed > TimeSpan.FromMinutes(5))
                throw new TimeoutException("Steam has not confirmed the upload. Retry this saved build to resume its existing Workshop item.");
            Thread.Sleep(100);
        }
    }
    private static void CheckResult(int result, string operation)
    {
        if (result != 1) throw new IOException($"{operation} failed (Steam result {result}). Check Workshop configuration, account ownership, and the Steam client connection.");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct CreateResult { public int Result; public ulong Id; [MarshalAs(UnmanagedType.I1)] public bool NeedsAgreement; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SubmitResult { public int Result; [MarshalAs(UnmanagedType.I1)] public bool NeedsAgreement; public ulong Id; }
    private static class Native
    {
        private const string Dll = "steam_api64";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int SteamAPI_InitFlat(StringBuilder error);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void SteamAPI_Shutdown();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void SteamAPI_RunCallbacks();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SteamAPI_SteamUtils_v010();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SteamAPI_SteamUGC_v021();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SteamAPI_SteamUser_v023();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SteamAPI_ISteamUtils_GetAppID(nint self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUser_BLoggedOn(nint self);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(nint self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUtils_GetAPICallResult(nint self, ulong call, nint buffer, int size, int callback, [MarshalAs(UnmanagedType.I1)] out bool failed);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong SteamAPI_ISteamUGC_CreateItem(nint self, uint appId, int type);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong SteamAPI_ISteamUGC_StartItemUpdate(nint self, uint appId, ulong id);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemTitle(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemDescription(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemMetadata(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemContent(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemPreview(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SteamAPI_ISteamUGC_SetItemVisibility(nint self, ulong update, int visibility);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong SteamAPI_ISteamUGC_SubmitItemUpdate(nint self, ulong update, [MarshalAs(UnmanagedType.LPUTF8Str)] string note);
    }
}
