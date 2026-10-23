using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveryStudio.Core;

namespace LiveryStudio.VehicleChecks;

// Materializes interrupted states from production Install-produced journals.
// Every target is inside a new synthetic game-layout directory, never the game.
public static class VehicleTransactionChecks
{
    private sealed record Fixture(string Root, string Data, string PaintWorkspace, VehicleDonor Donor,
        VehicleImportManager Manager, BuiltVehicle Built, Dictionary<string, string> Originals);

    public static IEnumerable<(string Name, Action Test)> Cases(string root, string runtime, VehicleDonor donor)
    {
        Fixture New(string name) => Create(Path.Combine(root, name), runtime, donor);
        yield return ("Vehicle journal owns all ten files before removal", () =>
        {
            var f = New("normal"); var installed = f.Manager.Install(f.Built, f.Data);
            Require(installed.Receipt.Version == 2 && installed.Receipt.Files.Length == 10 && installed.Receipt.State == "Installed", "Incomplete vehicle journal.");
            f.Manager.Verify(installed, f.Data);
            Require(!Directory.Exists(Path.Combine(f.PaintWorkspace, "imports")), "The vehicle depends on an unjournaled nested paint import.");
            f.Manager.Restore(installed, f.Data); OriginalState(f);
            Require(f.Manager.Read(installed.ReceiptPath, f.Data).Receipt.State == "Removed", "Successful restore was not committed.");
            f.Manager.Restore(installed, f.Data); OriginalState(f);
        });
        foreach (var count in new[] { 0, 2, 6, 8, 9, 10 })
        {
            var applied = count;
            yield return ($"Vehicle recovers interrupted Installing after {applied}/10 writes", () =>
            {
                var f = New("install-prefix-" + applied); var item = f.Manager.Install(f.Built, f.Data);
                var installedBytes = item.Receipt.Files.ToDictionary(v => v.Relative, v => File.ReadAllBytes(Target(f, v.Relative)));
                foreach (var file in item.Receipt.Files) BeforeFile(f, item, file);
                foreach (var file in item.Receipt.Files.Take(applied)) Write(Target(f, file.Relative), installedBytes[file.Relative]);
                SetState(item, "Installing"); var resumed = f.Manager.Read(item.ReceiptPath, f.Data);
                f.Manager.Restore(resumed, f.Data, rolledBack: true); OriginalState(f);
                Require(f.Manager.Read(item.ReceiptPath, f.Data).Receipt.State == "Rolled back", "Partial install recovery was not committed.");
                f.Manager.Restore(resumed, f.Data); OriginalState(f);
            });
        }
        yield return ("Vehicle resumes interrupted asset and registration restore", () =>
        {
            var f = New("restore-interrupted"); var item = f.Manager.Install(f.Built, f.Data);
            // Geometry and two paint files have already been restored/deleted,
            // while the PACLOG and registration still contain the install.
            foreach (var file in item.Receipt.Files.Take(6)) BeforeFile(f, item, file);
            SetState(item, "Restoring"); f.Manager.Restore(f.Manager.Read(item.ReceiptPath, f.Data), f.Data); OriginalState(f);
        });
        yield return ("Vehicle resumes after one registration was already restored", () =>
        {
            var f = New("restore-one-config"); var item = f.Manager.Install(f.Built, f.Data);
            foreach (var file in item.Receipt.Files.Take(9)) BeforeFile(f, item, file);
            SetState(item, "Restoring"); f.Manager.Restore(f.Manager.Read(item.ReceiptPath, f.Data), f.Data); OriginalState(f);
        });
        yield return ("Vehicle registration removal preserves unrelated edits", () =>
        {
            var f = New("external-registration"); var item = f.Manager.Install(f.Built, f.Data);
            const string suffix = "\r\n-- Third party café 🌟\r\nUnrelatedMod = true\r\n";
            foreach (var file in item.Receipt.Files.Where(v => v.Block.Length > 0)) File.AppendAllText(Target(f, file.Relative), suffix, new UTF8Encoding(false));
            f.Manager.Verify(item, f.Data); f.Manager.Restore(item, f.Data);
            foreach (var old in f.Originals)
            {
                var config = item.Receipt.Files.SingleOrDefault(v => v.Relative.Equals(old.Key, StringComparison.OrdinalIgnoreCase) && v.Block.Length > 0);
                if (config is null) Require(Hash(Target(f, old.Key)) == old.Value, "An unrelated donor file changed.");
                else
                {
                    var before = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(item.ReceiptPath)!, config.Backup));
                    Require(File.ReadAllBytes(Target(f, old.Key)).AsSpan().SequenceEqual(before.Concat(Encoding.UTF8.GetBytes(suffix)).ToArray()), "Unrelated registration bytes changed.");
                }
            }
            foreach (var file in item.Receipt.Files.Where(v => v.BeforeHash is null)) Require(!File.Exists(Target(f, file.Relative)), "An owned resource remains after restore.");
        });
        yield return ("Vehicle refuses external compiled-file edits atomically", () =>
        {
            var f = New("external-native"); var item = f.Manager.Install(f.Built, f.Data);
            File.AppendAllText(Target(f, item.Receipt.Files[0].Relative), "external change"); var before = Snapshot(f.Data);
            Reject<InvalidDataException>(() => f.Manager.Restore(item, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
            Require(f.Manager.Read(item.ReceiptPath, f.Data).Receipt.State == "Installed", "Refused restore changed transaction state.");
        });
        yield return ("Vehicle refuses edited owned registration atomically", () =>
        {
            var f = New("edited-owned-block"); var item = f.Manager.Install(f.Built, f.Data); var config = item.Receipt.Files.First(v => v.Block.Length > 0);
            var path = Target(f, config.Relative); File.WriteAllText(path, File.ReadAllText(path).Replace("Available = true", "Available = false", StringComparison.Ordinal), new UTF8Encoding(false));
            var before = Snapshot(f.Data); Reject<InvalidDataException>(() => f.Manager.Restore(item, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
        });
        yield return ("Vehicle rejects changed recovery backup before game writes", () =>
        {
            var f = New("bad-backup"); var item = f.Manager.Install(f.Built, f.Data); var backup = item.Receipt.Files.First(v => v.BeforeHash is not null).Backup;
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(item.ReceiptPath)!, backup), "damaged backup"); var before = Snapshot(f.Data);
            Reject<InvalidDataException>(() => f.Manager.Restore(item, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
        });
        yield return ("Vehicle refuses a second active donor replacement", () =>
        {
            var f = New("second-donor"); var item = f.Manager.Install(f.Built, f.Data); var before = Snapshot(f.Data); var second = New("second-donor-input");
            Reject<InvalidOperationException>(() => f.Manager.Install(second.Built, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
        });
        yield return ("Vehicle rejects changed compiled input before installation", () =>
        {
            var f = New("bad-build"); File.AppendAllText(f.Built.Files.First().Value, "changed build"); var before = Snapshot(f.Data);
            Reject<InvalidDataException>(() => f.Manager.Install(f.Built, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
        });
        yield return ("Vehicle receipt cannot redirect a target outside game Data", () =>
        {
            var f = New("bad-receipt-target"); var item = f.Manager.Install(f.Built, f.Data);
            var entries = item.Receipt.Files.ToArray(); entries[0] = entries[0] with { Relative = "../../escaped.p3d" };
            File.WriteAllText(item.ReceiptPath, JsonSerializer.Serialize(item.Receipt with { Files = entries }, Contract.Json));
            var before = Snapshot(f.Data); Reject<InvalidDataException>(() => f.Manager.Restore(item, f.Data)); EqualSnapshot(before, Snapshot(f.Data));
            Require(!File.Exists(Path.Combine(f.Root, "escaped.p3d")), "The receipt escaped its game folder.");
        });
    }

    private static Fixture Create(string root, string runtime, VehicleDonor donor)
    {
        Directory.CreateDirectory(root); var game = Path.Combine(root, "Game"); var data = Path.Combine(game, "Flatout", "Data"); Directory.CreateDirectory(data);
        Write(Path.Combine(game, "PlayAll/Libs/vs2022/x64/DllLauncher64_r.exe"), "Synthetic launcher placeholder, never executed");
        var dll = Path.Combine(game, "Flatout/Bin/x64/Flatout64_r.dll"); Write(dll, "Synthetic game identity");
        Write(Path.Combine(data, donor.PrefabPath), "<donor-prefab>original</donor-prefab>\r\n");
        Write(Path.Combine(data, donor.BreakablesPath), "-- Original donor damage table\r\n");
        var originalScene = "WIN32/" + donor.BodyResource[6..]; Write(Path.Combine(data, originalScene), "Original donor PSI bytes");
        Write(Path.Combine(data, "WIN32/GRAPH/CARS/COMMON/GLASS.PMA"), "Unrelated glass bytes");
        Write(Path.Combine(data, "Common/Tuning/Factories/CarsSkins.cfg"), "-- Original café skins 🌟\r\nSkins = {}\r\n");
        Write(Path.Combine(data, "Common/Tuning/Factories/Rewards.cfg"), "-- Original rewards\r\nRewards = { CarSkins = {} }\r\n");
        var workspace = Path.Combine(root, "manager"); var paintWorkspace = Path.Combine(root, "paint-manager");
        var assembly = new VehicleAssemblyService(runtime, workspace, Path.Combine(root, "unused-models")); var paintService = new WorkshopService(Path.Combine(root, "unused-tools"), paintWorkspace);
        var car = Contract.Cars.Single(c => c.Index == donor.Index); var paintId = "BODY_" + Guid.NewGuid().ToString("N"); var id = "CAR_" + Guid.NewGuid().ToString("N");
        var prefix = $"GRAPH/CARS/{car.Class}/{car.CarId}/"; var build = Path.Combine(root, "paint-build"); var package = Path.Combine(root, "paint-package");
        var paintHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { prefix + paintId + ".PMA", prefix + "TEXTURES/" + paintId + "_DIFF.PTX", prefix + "TEXTURES/" + paintId + "_IOR.PTX" })
        {
            var source = Path.Combine(build, "Data/WIN32", path); Write(source, "Synthetic native paint " + path); paintHashes.Add(path, Hash(source));
        }
        Write(Path.Combine(package, paintId + ".PACLOG"), "Synthetic package log");
        var project = new VehicleAssemblyProject { Name = "Transaction fixture", DonorIndex = donor.Index };
        var paintProject = new LiveryProject { Name = project.Name, CarIndex = donor.Index };
        var paint = new BuiltLivery(paintId, paintProject, build, package, "DATA:/" + prefix + paintId + ".PMA", "DATA:/PACLOG/CUSTOMPAC/LIVERY_STUDIO/" + paintId + ".PACLOG", paintHashes);
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in new[] { "WIN32/GRAPH/CARS/LIVERY_STUDIO/" + id[4..20] + "/BODY.P3D", originalScene, donor.PrefabPath, "Common/TUNING/LIVERY_STUDIO/" + id + "/BREAKABLES.CFG" })
        {
            var source = Path.Combine(root, "compiled-output", outputs.Count + ".bin"); Write(source, "Synthetic native vehicle " + relative); outputs.Add(relative, source);
        }
        var originals = new[] { donor.PrefabPath, donor.BreakablesPath, originalScene }.ToDictionary(p => p, p => Hash(Path.Combine(data, p)), StringComparer.OrdinalIgnoreCase);
        var scene = new VehicleSceneReport(1, 1, 1, false, 100, [[0, 0, 0], [1, 1, 1]], ["Body", "Interior"], []);
        var built = new BuiltVehicle(id, project, Path.Combine(root, "compiled-output"), paint, outputs, outputs.ToDictionary(p => p.Key, p => Hash(p.Value), StringComparer.OrdinalIgnoreCase), scene, originals, Hash(dll));
        return new(root, data, paintWorkspace, donor, new(assembly), built, Snapshot(data));
    }
    private static string Target(Fixture fixture, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(fixture.Data, relative));
        Require(path.StartsWith(Path.GetFullPath(fixture.Data) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Invalid synthetic fixture target."); return path;
    }
    private static void BeforeFile(Fixture f, ManagedVehicle item, VehicleInstalledFile file)
    {
        var target = Target(f, file.Relative);
        if (file.BeforeHash is not null) Write(target, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(item.ReceiptPath)!, file.Backup)));
        else if (File.Exists(target)) File.Delete(target);
    }
    private static void OriginalState(Fixture f) => EqualSnapshot(f.Originals, Snapshot(f.Data));
    private static void EqualSnapshot(Dictionary<string, string> before, Dictionary<string, string> after) => Require(before.Count == after.Count && before.All(p => after.GetValueOrDefault(p.Key) == p.Value), "The game file set or original bytes changed.");
    private static Dictionary<string, string> Snapshot(string data) => Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(data, p).Replace('\\', '/'), Hash, StringComparer.OrdinalIgnoreCase);
    private static void SetState(ManagedVehicle item, string state) => File.WriteAllText(item.ReceiptPath, JsonSerializer.Serialize(item.Receipt with { State = state }, Contract.Json));
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Write(string path, string text) => Write(path, new UTF8Encoding(false).GetBytes(text));
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
