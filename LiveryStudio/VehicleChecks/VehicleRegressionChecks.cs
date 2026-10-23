using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiveryStudio.Core;

namespace LiveryStudio.VehicleChecks;

// Runs against synthetic inputs and a fresh owned directory. It never uses an
// installed game, the user's artwork, or native tool processes.
public static class VehicleRegressionChecks
{
    private sealed record Result(string Name, bool Passed, string? Error);
    private sealed record Fixture(VehicleAssemblyProject Project, string Root, Dictionary<string, string> SourceHashes);
    private static readonly JsonSerializerOptions Json = new(Contract.Json) { IncludeFields = true };

    public static async Task<int> RunAsync(string workspace)
    {
        var root = Path.GetFullPath(workspace);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Use a new regression workspace.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "vehicle-regression-owned.txt"), "Synthetic vehicle regression fixtures only");
        var results = new List<Result>();
        async Task Check(string name, Func<Task> test)
        {
            try { await test(); results.Add(new(name, true, null)); Console.WriteLine("PASS " + name); }
            catch (Exception error) { results.Add(new(name, false, error.ToString())); Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        Task Sync(Action action) { action(); return Task.CompletedTask; }
        var compiledScene=new VehicleSceneReport(1,1,1,false,100,[],["Body"],[]);
        string Reader(int sections,int[]? channels,string name="Body")=>JsonSerializer.Serialize(new{resourceLoaded=true,nodes=new[]{new{name,mesh=true,sections,uvChannels=channels}}});
        await Check("Native reader accepts complete textured base-LOD sections",()=>Sync(()=>compiledScene.ValidateCompiled(Reader(2,[1,2]))));
        foreach(var invalid in new[]{(Name:"zero UVs",Data:Reader(1,[0])),(Name:"empty sections",Data:Reader(0,[])),(Name:"missing UV channels",Data:Reader(1,null)),(Name:"truncated section declarations",Data:Reader(2,[1])),(Name:"extra section declarations",Data:Reader(1,[1,1])),(Name:"lost assembled mesh",Data:Reader(1,[1],"Other"))})
            await Check("Native reader rejects "+invalid.Name,()=>Sync(()=>Reject<Exception>(()=>compiledScene.ValidateCompiled(invalid.Data))));
        var glb = Path.Combine(root, "glb"); Directory.CreateDirectory(glb);
        string Model(string name, Action<JsonObject>? change = null, Action<byte[]>? changeBuffer = null)
        {
            var (model, buffer) = Triangle(); change?.Invoke(model); changeBuffer?.Invoke(buffer);
            var path = Path.Combine(glb, name + ".glb"); WriteGlb(path, model, buffer); return path;
        }
        string ConvertGlb(string path) { var output = path + ".obj"; VehicleGlb.WriteObj(path, output); return File.ReadAllText(output); }
        await Check("GLB static triangle and node translation", () => Sync(() =>
        {
            var path = Model("translated", m => m["nodes"]![0]!["translation"] = new JsonArray(3, 4, 5));
            var output = ConvertGlb(path); var vertices = ObjVectors(output, "v");
            Require(vertices.Count == 3 && Vector3.Distance(vertices[0], new(3, 4, 5)) < 1e-6f && output.Split('\n').Count(l => l.StartsWith("f ")) == 1, "The scene transform/triangle was lost.");
        }));
        await Check("GLB preserves existing UVs", () => Sync(() =>
        {
            var path = Model("uv"); var output = ConvertGlb(path); var uv = ObjVectors(output, "vt");
            Require(uv.Count == 3 && Math.Abs(uv[0].X - .2f) < 1e-6f && Math.Abs(uv[0].Y - .7f) < 1e-6f, "Existing texture coordinates changed.");
        }));
        await Check("GLB reflected winding agrees with normal", () => Sync(() =>
        {
            var output = ConvertGlb(Model("mirrored", m => m["nodes"]![0]!["scale"] = new JsonArray(-2, 3, 1)));
            var positions = ObjVectors(output, "v"); var normals = ObjVectors(output, "vn");
            var face = Vector3.Normalize(Vector3.Cross(positions[1] - positions[0], positions[2] - positions[0]));
            Require(normals.Count == 3 && normals.All(n => Vector3.Dot(face, n) > .999f), "Reflected faces disagree with their normals.");
        }));
        await Check("GLB supports distinct nodes sharing one mesh", () => Sync(() =>
        {
            var output = ConvertGlb(Model("mesh-instances", m => { m["nodes"]!.AsArray().Add(new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray(2, 0, 0) }); m["scenes"]![0]!["nodes"] = new JsonArray(0, 1); }));
            Require(output.Split('\n').Count(l => l.StartsWith("f ")) == 2, "A legal mesh instance was lost.");
        }));
        await Check("GLB rejects repeated child references", () => Sync(() =>
        {
            var path = Model("repeated-child", m => { m["nodes"] = new JsonArray(new JsonObject { ["children"] = new JsonArray(1, 1) }, new JsonObject { ["mesh"] = 0 }); });
            Reject<InvalidDataException>(() => ConvertGlb(path));
        }));
        await Check("GLB rejects multiple parents", () => Sync(() =>
        {
            var path = Model("multiple-parents", m => { m["nodes"] = new JsonArray(new JsonObject { ["children"] = new JsonArray(2) }, new JsonObject { ["children"] = new JsonArray(2) }, new JsonObject { ["mesh"] = 0 }); m["scenes"]![0]!["nodes"] = new JsonArray(0, 1); });
            Reject<InvalidDataException>(() => ConvertGlb(path));
        }));
        await Check("GLB rejects cyclic scene", () => Sync(() =>
        {
            var path = Model("cycle", m => m["nodes"]![0]!["children"] = new JsonArray(0));
            Reject<InvalidDataException>(() => ConvertGlb(path));
        }));
        await Check("GLB rejects truncated chunk", () => Sync(() =>
        {
            var path = Model("truncated"); var bytes = File.ReadAllBytes(path); File.WriteAllBytes(path, bytes[..^4]); Reject<InvalidDataException>(() => ConvertGlb(path));
        }));
        await Check("GLB rejects external buffer", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("external", m => m["buffers"]![0]!["uri"] = "https://invalid.example/mesh.bin")))));
        await Check("GLB rejects accessor overrun", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("overrun", m => m["accessors"]![0]!["count"] = 4)))));
        await Check("GLB rejects compressed geometry", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("compressed", m => m["extensionsRequired"] = new JsonArray("KHR_draco_mesh_compression"))))));
        await Check("GLB rejects sparse accessor", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("sparse", m => m["accessors"]![0]!["sparse"] = new JsonObject())))));
        await Check("GLB rejects invalid index", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("index", changeBuffer: b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(96), 9))))));
        await Check("GLB rejects nonfinite position", () => Sync(() => Reject<InvalidDataException>(() => ConvertGlb(Model("nan", changeBuffer: b => BinaryPrimitives.WriteSingleLittleEndian(b, float.NaN))))));
        await Check("GLB observes pre-cancellation", () => Sync(() =>
        {
            var path = Model("cancel"); Reject<OperationCanceledException>(() => VehicleGlb.WriteObj(path, path + ".obj", new CancellationToken(true)));
        }));

        var fixture = CreateFixture(Path.Combine(root, "portable-source"));
        var archive = Path.Combine(root, "roundtrip.flcar");
        await Check("FLCar stores a complete uniquely hashed inventory", () => Sync(() =>
        {
            VehicleProjectBundle.Save(fixture.Project, archive); using var zip = ZipFile.OpenRead(archive);
            Require(zip.Entries.Count == 8 && zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 8, "Unexpected bundle inventory.");
            var manifest = ReadJson(zip, "Manifest.json")["Hashes"]!.AsObject();
            foreach (var entry in zip.Entries.Where(e => e.FullName != "Manifest.json")) { using var stream = entry.Open(); Require(Convert.ToHexString(SHA256.HashData(stream)) == manifest[entry.FullName]!.GetValue<string>(), "Bundle hash differs: " + entry.FullName); }
            Require(ReadJson(zip, "Car.json")["ExteriorFile"]!.GetValue<string>() == "Exterior.obj", "The source reference is not local.");
        }));
        await Check("FLCar reopens editable history including undone future", () => Sync(() =>
        {
            var opened = VehicleProjectBundle.Open(archive, Path.Combine(root, "open-one")); var original = fixture.Project;
            Require(opened.Id != original.Id && opened.Prepared && opened.RecipeHash() == original.RecipeHash(), "The opened design lost its recipe or independence.");
            Require(opened.Paint.FlatExteriorNormals && opened.Paint.Name == original.Paint.Name && opened.Paint.OuterColor == original.Paint.OuterColor, "Paint settings changed.");
            var a = LayerDocument.Read(original.Paint.EditorFile); var b = LayerDocument.Read(opened.Paint.EditorFile);
            Require(a.Cursor == 1 && b.Cursor == 1 && b.History.Count == 3 && b.CanRedo, "The undone future checkpoint was lost.");
            Require(NormalizeHistory(a) == NormalizeHistory(b), "Editable layers/history/retained source bytes changed.");
            Require(Hash(opened.ExteriorFile) == original.ExteriorHash && Hash(opened.AssemblyFile) == Hash(original.AssemblyFile) && Hash(opened.Paint.Albedo) == Hash(original.Paint.Albedo), "A retained vehicle or paint file changed.");
        }));
        await Check("FLCar opens independent file copies", () => Sync(() =>
        {
            var one = VehicleProjectBundle.Open(archive, Path.Combine(root, "copy-one")); var two = VehicleProjectBundle.Open(archive, Path.Combine(root, "copy-two"));
            File.WriteAllText(one.ExteriorFile, "changed opened copy"); File.AppendAllText(one.AssemblyFile, "changed opened assembly");
            var first = LayerDocument.Read(one.Paint.EditorFile).History[2].Layers[1]; File.WriteAllText(first.RasterArtwork!.SourceFile, "changed opened artwork");
            Require(Hash(two.ExteriorFile) == fixture.Project.ExteriorHash && Hash(two.AssemblyFile) == Hash(fixture.Project.AssemblyFile), "Two opened projects alias files.");
            VerifySources(fixture);
        }));
        await Check("FLCar rejects a changed payload hash", () => Sync(() =>
        {
            var bad = RewriteArchive(archive, root, "bad-payload", entries => entries["Exterior.obj"] = Encoding.UTF8.GetBytes("tampered source"));
            Reject<InvalidDataException>(() => VehicleProjectBundle.Open(bad, Path.Combine(root, "reject-hash"))); VerifySources(fixture);
        }));
        await Check("FLCar rejects an unsafe entry", () => Sync(() =>
        {
            var bad = RewriteArchive(archive, root, "bad-path", entries => entries["../escaped.txt"] = [1]);
            Reject<InvalidDataException>(() => VehicleProjectBundle.Open(bad, Path.Combine(root, "reject-path"))); Require(!File.Exists(Path.Combine(root, "escaped.txt")), "An unsafe entry escaped.");
        }));
        await Check("FLCar rejects external generated-file references", () => Sync(() =>
        {
            var bad = RewriteArchive(archive, root, "bad-reference", entries => { var car = JsonNode.Parse(entries["Car.json"])!.AsObject(); car["AssemblyFile"] = fixture.Project.AssemblyFile; entries["Car.json"] = JsonSerializer.SerializeToUtf8Bytes(car, Json); UpdateManifest(entries, "Car.json"); });
            Reject<InvalidDataException>(() => VehicleProjectBundle.Open(bad, Path.Combine(root, "reject-reference")));
        }));
        await Check("FLCar rejects source/recipe mismatch despite valid ZIP hash", () => Sync(() =>
        {
            var bad = RewriteArchive(archive, root, "bad-source-recipe", entries => { entries["Exterior.obj"] = Encoding.UTF8.GetBytes("changed but rehashed"); UpdateManifest(entries, "Exterior.obj"); });
            Reject<InvalidDataException>(() => VehicleProjectBundle.Open(bad, Path.Combine(root, "reject-recipe")));
        }));
        await Check("FLCar failed save preserves previous destination", () => Sync(() =>
        {
            var before = Hash(archive); var invalid = Clone(fixture.Project); invalid.Paint.Albedo = Path.Combine(root, "missing.tga");
            Reject<ArgumentException>(() => VehicleProjectBundle.Save(invalid, archive)); Require(Hash(archive) == before, "Failed save replaced the previous archive.");
        }));
        await Check("Prepared snapshot is independent and nonrecursive", () => Sync(() =>
        {
            var active = Clone(fixture.Project); VehicleAssemblyService.RememberPrepared(active);
            var previous = JsonSerializer.Deserialize<VehicleAssemblyProject>(active.PreparedDesign, Contract.Json)!;
            active.Fit.Width = 1.4;
            Require(previous.PreparedDesign.Length == 0 && previous.Fit.Width == 1 && previous.Prepared && !active.Prepared, "Prepared recipe aliases the pending fit or contains recursive snapshots.");
            Require(Hash(previous.ExteriorFile) == fixture.Project.ExteriorHash && Hash(previous.PreviewFile) == Hash(fixture.Project.PreviewFile), "The previous model/source snapshot changed.");
        }));
        await Check("Pending-fit car bundle rebases its previous prepared model", () => Sync(() =>
        {
            var active = Clone(fixture.Project); VehicleAssemblyService.RememberPrepared(active); active.Fit.Width = 1.3; active.DonorIndex = 1;
            var saved = Path.Combine(root, "pending-fit.flcar"); VehicleProjectBundle.Save(active, saved);
            var opened = VehicleProjectBundle.Open(saved, Path.Combine(root, "pending-fit-open"));
            var previous = JsonSerializer.Deserialize<VehicleAssemblyProject>(opened.PreparedDesign, Contract.Json)!;
            var openedRoot = Path.GetDirectoryName(opened.ExteriorFile)! + Path.DirectorySeparatorChar;
            Require(!opened.Prepared && opened.Fit.Width == 1.3 && opened.DonorIndex == 1 && opened.Paint.CarIndex == 0, "Opening a pending recipe changed its prepared paint/model ownership.");
            Require(previous.Prepared && previous.DonorIndex == 0 && previous.Fit.Width == 1 && previous.Id == opened.Id, "The previous prepared recipe was lost.");
            foreach (var path in new[] { previous.ExteriorFile, previous.AssemblyFile, previous.BindingFile, previous.ReportFile, previous.PreviewFile, previous.Paint.EditorFile, previous.Paint.Albedo, previous.Paint.Structure })
                Require(File.Exists(path) && Path.GetFullPath(path).StartsWith(openedRoot, StringComparison.OrdinalIgnoreCase), "The portable prepared snapshot retains an external path: " + path);
            Require(Hash(previous.AssemblyFile) == Hash(fixture.Project.AssemblyFile) && NormalizeHistory(LayerDocument.Read(previous.Paint.EditorFile)) == NormalizeHistory(LayerDocument.Read(fixture.Project.Paint.EditorFile)), "Prepared geometry/history changed during rebasing.");
            using var zip = ZipFile.OpenRead(saved); var snapshot = JsonNode.Parse(ReadJson(zip, "Car.json")["PreparedDesign"]!.GetValue<string>())!;
            Require(snapshot["ExteriorFile"]!.GetValue<string>() == "Exterior.obj" && snapshot["PreviewFile"]!.GetValue<string>() == "Preview.flsm", "The portable snapshot embeds absolute model paths.");
        }));
        await Check("Previous prepared car can recover latest paint with original geometry", () => Sync(() =>
        {
            var active = Clone(fixture.Project); VehicleAssemblyService.RememberPrepared(active); active.Fit.Length = 1.5; active.Paint.OuterColor = "#F08020";
            var previous = JsonSerializer.Deserialize<VehicleAssemblyProject>(active.PreparedDesign, Contract.Json)!; previous.Paint = active.Paint;
            var recovery = Path.Combine(root, "previous-prepared.flcar"); VehicleProjectBundle.Save(previous, recovery);
            var opened = VehicleProjectBundle.Open(recovery, Path.Combine(root, "previous-prepared-open"));
            Require(opened.Prepared && opened.Fit.Length == 1 && opened.Paint.OuterColor == "#F08020", "Recovery lost the old geometry recipe or latest paint values.");
            Require(Hash(opened.ExteriorFile) == fixture.Project.ExteriorHash && Hash(opened.PreviewFile) == Hash(fixture.Project.PreviewFile) && NormalizeHistory(LayerDocument.Read(opened.Paint.EditorFile)) == NormalizeHistory(LayerDocument.Read(fixture.Project.Paint.EditorFile)), "Recovery lost retained source/model/history.");
            VerifySources(fixture);
        }));

        var runtime = Path.Combine(root, "synthetic-runtime"); var bin = Path.Combine(runtime, "Bin"); Directory.CreateDirectory(bin);
        foreach (var name in new[] { "Compiler_r.exe", "VehicleSceneBridge.exe", "VehicleSceneProbe.exe" }) File.WriteAllText(Path.Combine(bin, name), "Synthetic placeholder; must never execute");
        var donor = new VehicleDonor(0, "Test donor", "DERBY", "BUICK", "DATA:/GRAPH/CARS/DERBY/BUICK/BUICK.PSI", "Common/Archetypes/GAMEPLAY/CARS/DERBY/BUICK/BUICK.PPF", "Common/Tuning/Car/derby/BUICK/VehicleBreakables.cfg", "", "", [0, 0, 0], [], []);
        Directory.CreateDirectory(Path.Combine(runtime, "Donors")); File.WriteAllText(Path.Combine(runtime, "Donors/catalog.json"), JsonSerializer.Serialize(new[] { donor }, Contract.Json));
        var service = new VehicleAssemblyService(runtime, Path.Combine(root, "import-workspace"), Path.Combine(root, "models"));
        await Check("Invalid replacement preserves project and retained source", async () =>
        {
            var project = Clone(fixture.Project); var before = JsonSerializer.Serialize(project, Json); var corrupt = Path.Combine(root, "invalid.glb"); File.WriteAllText(corrupt, "not a GLB");
            await RejectAsync<InvalidDataException>(() => service.ImportAsync(project, corrupt));
            Require(JsonSerializer.Serialize(project, Json) == before, "Failed import mutated the active project."); VerifySources(fixture);
        });
        await Check("Canceled replacement preserves project and retained source", async () =>
        {
            var project = Clone(fixture.Project); var before = JsonSerializer.Serialize(project, Json); var valid = Model("canceled-import");
            await RejectAsync<OperationCanceledException>(() => service.ImportAsync(project, valid, new CancellationToken(true)));
            Require(JsonSerializer.Serialize(project, Json) == before, "Canceled import mutated the active project."); VerifySources(fixture);
        });
        await Check("Unsupported replacement preserves project and retained source", async () =>
        {
            var project = Clone(fixture.Project); var before = JsonSerializer.Serialize(project, Json); var invalid = Path.Combine(root, "not-a-car.txt"); File.WriteAllText(invalid, "unsupported");
            await RejectAsync<ArgumentException>(() => service.ImportAsync(project, invalid));
            Require(JsonSerializer.Serialize(project, Json) == before, "Unsupported import mutated the active project."); VerifySources(fixture);
        });
        await Check("Invalid project identity is rejected before path writes", async () =>
        {
            var project = Clone(fixture.Project); var escape = Path.Combine(root, "outside-owned-projects"); project.Id = escape;
            await RejectAsync<InvalidDataException>(() => service.ImportAsync(project, fixture.Project.ExteriorFile));
            Require(!Directory.Exists(escape), "Invalid identity created an external folder."); VerifySources(fixture);
        });
        foreach (var test in VehicleTransactionChecks.Cases(Path.Combine(root, "transactions"), runtime, donor))
            await Check(test.Name, () => Sync(test.Test));
        var report = new { Passed = results.All(r => r.Passed), Checks = results.Count, Results = results, Workspace = root, CoreSha256 = Hash(typeof(VehicleAssemblyProject).Assembly.Location), Scope = "Synthetic GLB, portable project, failed source import and transaction recovery checks; no installed game or native process executed" };
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report, Contract.Json));
        Console.WriteLine($"{results.Count(r => r.Passed)}/{results.Count} vehicle regression checks passed; {Path.Combine(root, "results.json")}");
        return report.Passed ? 0 : 1;
    }

    private static Fixture CreateFixture(string root)
    {
        Directory.CreateDirectory(root); string FileAt(string name, string content) { var path = Path.Combine(root, name); File.WriteAllText(path, content); return path; }
        var color = Path.Combine(root, "color.tga"); var surface = Path.Combine(root, "surface.tga"); var artwork = Path.Combine(root, "artwork.tga"); var stencil = Path.Combine(root, "stencil.tga");
        foreach (var path in new[] { color, surface, artwork, stencil }) FinishMaps.WriteTga(new(2, 2, [12, 34, 56, 255, 78, 90, 123, 255, 200, 150, 100, 255, 240, 230, 220, 255]), path);
        var paint = new LiveryProject { Name = "Editable car paint", CarIndex = 0, Albedo = color, Structure = surface, OuterColor = "#124578", FlatExteriorNormals = true };
        var document = LayerDocument.Create(paint, Path.Combine(root, "originals"));
        var sample = new SurfaceProjection(Vector3.Zero, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 1, 1);
        document.Commit("Body color", layers => layers.Add(new() { Name = "Body color", Kind = LayerKind.Color, Color = "#335577", Samples = [sample] }));
        document.Commit("Future retained image", layers => layers.Add(new() { Name = "Prepared image", Asset = artwork, Samples = [sample], RasterArtwork = new(artwork) { Preparation = new() }, Effects = new() { Mask = DecalMask.Image, StencilFile = stencil, Blend = DecalBlend.Screen } }));
        document.Undo(); paint.EditorFile = Path.Combine(root, "editor.json"); document.Save(paint.EditorFile);
        var exterior = FileAt("Exterior.obj", "o Body\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        var project = new VehicleAssemblyProject { Name = "Portable geometry fixture", ExteriorFile = exterior, ExteriorHash = Hash(exterior), DonorHash = new string('A', 64), AssemblyFile = FileAt("Assembly.FBX", "Retained assembly fixture"), BindingFile = FileAt("Layout.tsv", "Body\t0\t-1\tpaint\t__EXTERIOR__\n"), ReportFile = FileAt("Report.json", "{}"), PreviewFile = FileAt("Preview.flsm", "Retained preview fixture"), Paint = paint };
        project.PreparedRecipeHash = project.RecipeHash();
        return new(project, root, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash));
    }
    private static string NormalizeHistory(LayerDocument document)
    {
        var copy = JsonSerializer.Deserialize<LayerDocument>(JsonSerializer.Serialize(document, LayerDocument.Json), LayerDocument.Json)!;
        copy.BaseAlbedo = Hash(copy.BaseAlbedo); copy.BaseStructure = Hash(copy.BaseStructure);
        foreach (var state in copy.History) foreach (var layer in state.Layers)
        {
            if (layer.Asset.Length > 0) layer.Asset = Hash(layer.Asset);
            if (layer.RasterArtwork is { } raster) layer.RasterArtwork = raster with { SourceFile = Hash(raster.SourceFile) };
            if (layer.Effects is { StencilFile.Length: > 0 } effects) layer.Effects = effects with { StencilFile = Hash(effects.StencilFile) };
        }
        return JsonSerializer.Serialize(copy, LayerDocument.Json);
    }
    private static void VerifySources(Fixture fixture) { foreach (var source in fixture.SourceHashes) Require(File.Exists(source.Key) && Hash(source.Key) == source.Value, "Original source changed: " + source.Key); }
    private static VehicleAssemblyProject Clone(VehicleAssemblyProject project) => JsonSerializer.Deserialize<VehicleAssemblyProject>(JsonSerializer.Serialize(project, Contract.Json), Contract.Json)!;
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static JsonObject ReadJson(ZipArchive zip, string name) { using var stream = zip.GetEntry(name)!.Open(); return JsonNode.Parse(stream)!.AsObject(); }
    private static void UpdateManifest(Dictionary<string, byte[]> entries, string name) { var manifest = JsonNode.Parse(entries["Manifest.json"])!.AsObject(); manifest["Hashes"]![name] = Convert.ToHexString(SHA256.HashData(entries[name])); entries["Manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, Json); }
    private static string RewriteArchive(string original, string root, string name, Action<Dictionary<string, byte[]>> edit)
    {
        var entries = new Dictionary<string, byte[]>(); using (var zip = ZipFile.OpenRead(original)) foreach (var entry in zip.Entries) { using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); entries.Add(entry.FullName, memory.ToArray()); }
        edit(entries); var path = Path.Combine(root, name + ".flcar"); using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) foreach (var entry in entries) { using var stream = zip.CreateEntry(entry.Key).Open(); stream.Write(entry.Value); } return path;
    }
    private static List<Vector3> ObjVectors(string output, string prefix) => output.Split('\n').Where(l => l.StartsWith(prefix + " ")).Select(l => { var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray(); return new Vector3(p[0], p[1], p.Length > 2 ? p[2] : 0); }).ToList();
    private static (JsonObject Model, byte[] Buffer) Triangle()
    {
        // One triangle: positions at 0, normals at 36, UVs at 72, ushort indices at 96.
        var buffer = new byte[104]; float[] values = [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1, .2f, .3f, .8f, .3f, .2f, .9f];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i * 4), values[i]);
        for (ushort i = 0; i < 3; i++) BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(96 + i * 2), i);
        var model = JsonNode.Parse("""
        {"asset":{"version":"2.0"},"buffers":[{"byteLength":104}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":24},{"buffer":0,"byteOffset":96,"byteLength":6}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"},{"bufferView":3,"componentType":5123,"count":3,"type":"SCALAR"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
        """)!.AsObject(); return (model, buffer);
    }
    private static void WriteGlb(string path, JsonObject model, byte[] buffer)
    {
        var text = Encoding.UTF8.GetBytes(model.ToJsonString()); var jsonSize = (text.Length + 3) & ~3; var binSize = (buffer.Length + 3) & ~3;
        using var output = new BinaryWriter(File.Create(path)); output.Write(0x46546c67u); output.Write(2u); output.Write(checked((uint)(12 + 8 + jsonSize + 8 + binSize))); output.Write(jsonSize); output.Write(0x4e4f534au); output.Write(text); for (var i = text.Length; i < jsonSize; i++) output.Write((byte)' '); output.Write(binSize); output.Write(0x004e4942u); output.Write(buffer); for (var i = buffer.Length; i < binSize; i++) output.Write((byte)0);
    }
}
