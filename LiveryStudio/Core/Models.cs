using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace LiveryStudio.Core;

public record Car(int Index, string Name, string Class, string CarId, string Template, string Category)
{
	[System.Text.Json.Serialization.JsonIgnore] public string ThumbnailPath=>Path.Combine(AppContext.BaseDirectory,"Assets","Cars",Index.ToString("00")+".png");
	public string Display => $"{Name} · {Category}";
	public string EngineType => Class + "_" + CarId.Replace("_EVO", "").Replace("_G850", "").Replace("_V8", "") switch
	{
		var v when v.EndsWith("ICECREAM_VAN") => v.Replace("ICECREAM_VAN", "IceCreamVan"),
		var v => v[..(v.IndexOf('_') + 1)] + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v[(v.IndexOf('_') + 1)..].ToLowerInvariant())
	};
}

public static class Contract
{
	public static string Template(string name)
	{
		using var stream = typeof(Contract).Assembly.GetManifestResourceStream($"LiveryStudio.Core.Templates.{name}") ?? throw new FileNotFoundException(name);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
	public static IReadOnlyList<Car> Cars { get; } = JsonDocument.Parse(Template("catalog.json")).RootElement.EnumerateArray().Select(e => new Car(
		e.GetProperty("Index").GetInt32(), e.GetProperty("Name").GetString()!, e.GetProperty("Class").GetString()!,
		e.GetProperty("Car").GetString()!, e.GetProperty("Template").GetString()!, e.GetProperty("Category").GetString()!)).ToArray();
	public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

public sealed record LiveryProject
{
	public string Name { get; set; } = "Midnight Current";
	public int CarIndex { get; set; } = 0;
	public string Albedo { get; set; } = "";
	public string Structure { get; set; } = "";
	public string Banner { get; set; } = "";
	public string EditorFile { get; set; } = "";
	public string OuterColor { get; set; } = "#21DCC1";
	public string InnerColor { get; set; } = "#123543";
	public double OuterGloss { get; set; } = .85;
	public double InnerGloss { get; set; } = .65;
	public double Metalness { get; set; } = .8;
	public double ClearCoat { get; set; } = .9;
	public bool UseSrgbPaintColors { get; set; }
	[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public bool FlatExteriorNormals { get; set; }
	[System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public bool IndependentPaintSides { get; set; }
	public bool OverrideArtworkFinish { get; set; }
	public double ArtworkGloss { get; set; } = .65;
	public double ArtworkMetalness { get; set; }
	public double DamageGloss { get; set; } = 1;
	public double DamageMetalness { get; set; } = .784314;
	public string Description { get; set; } = "A fresh custom livery for FlatOut 4.";
	public List<CameraBookmark> CameraBookmarks { get; set; } = [];
	public Car SelectedCar => Contract.Cars.Single(c => c.Index == CarIndex);

	public static string Float(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
	public static string ColorVector(string hex,bool useSrgb=false)
	{
		if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Paint colors must be six-digit hex colors, such as #21DCC1.");
		var c=PaintFinish.Color(hex);if(useSrgb)c=PaintFinish.ToLinear(c);
		return string.Join(';',new[]{c.X,c.Y,c.Z}.Select(v=>v.ToString("G9",CultureInfo.InvariantCulture)))+";1.0";
	}
	public void Validate(bool publishing = false, bool textureSources = true)
	{
		_ = SelectedCar;
		if(CameraBookmarks is null||CameraBookmarks.Count>81||CameraBookmarks.Any(v=>v is null)||CameraBookmarks.Select(v=>(v.CarIndex,v.Slot)).Distinct().Count()!=CameraBookmarks.Count)throw new ArgumentException("Invalid camera bookmark collection.");
		foreach(var view in CameraBookmarks)view.Validate();
		if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl)) throw new ArgumentException("Give your livery a name of 1–100 characters.");
		foreach (var file in textureSources ? new[] { Albedo, Structure } : Array.Empty<string>())
		{
			if (!File.Exists(file) || !new[] { ".png", ".tga" }.Contains(Path.GetExtension(file).ToLowerInvariant())) throw new ArgumentException("Choose an existing PNG or TGA for both texture maps.");
		}
		_ = ColorVector(OuterColor); _ = ColorVector(InnerColor);
		if (new[] { OuterGloss, InnerGloss, Metalness, ClearCoat, ArtworkGloss, ArtworkMetalness, DamageGloss, DamageMetalness }.Any(v => !double.IsFinite(v) || v < 0 || v > 1)) throw new ArgumentException("Paint values must be between 0 and 1.");
		if (publishing && (string.IsNullOrWhiteSpace(Description) || !File.Exists(Banner))) throw new ArgumentException("Workshop publishing requires a description and banner image.");
	}
	public string ToLegacyXml() => new XDocument(new XElement("BakeConfig",
		new XElement("sClass", SelectedCar.Class), new XElement("sCar", SelectedCar.CarId), new XElement("sLiveryName", Name),
		new XElement("sDiffuseTexture", Albedo), new XElement("sStructureTexture", Structure), new XElement("sColor1", ColorVector(OuterColor,UseSrgbPaintColors)),
		new XElement("sColor2", ColorVector(InnerColor,UseSrgbPaintColors)), new XElement("sGloss1", Float(OuterGloss)), new XElement("sGloss2", Float(InnerGloss)),
		new XElement("sMetalness", Float(Metalness)), new XElement("sClearCoat", Float(ClearCoat)), new XElement("sDescription", Description), new XElement("sPreview", Banner),
		new XElement("sUseSrgbPaintColors",UseSrgbPaintColors),new XElement("sOverrideArtworkFinish",OverrideArtworkFinish),
		new XElement("sArtworkGloss",Float(ArtworkGloss)),new XElement("sArtworkMetalness",Float(ArtworkMetalness)),
		new XElement("sDamageGloss",Float(DamageGloss)),new XElement("sDamageMetalness",Float(DamageMetalness)))).ToString();
	public static LiveryProject Read(string file)
	{
		if (!file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return JsonSerializer.Deserialize<LiveryProject>(File.ReadAllText(file), Contract.Json) ?? throw new InvalidDataException("Invalid project.");
		var xml = XDocument.Load(file).Root ?? throw new InvalidDataException("Invalid config.");
		string Str(string name) => xml.Element(name)?.Value ?? "";
		double Num(string name) => double.TryParse(Str(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1;
		bool Flag(string name)=>bool.TryParse(Str(name),out var value)&&value;
		string Hex(string name) { var v = Str(name).Split(';'); return v.Length < 3 ? "#FFFFFF" : "#" + string.Concat(v.Take(3).Select(s => {var n=float.Parse(s,CultureInfo.InvariantCulture);if(Flag("sUseSrgbPaintColors"))n=PaintFinish.ToSrgb(n);return ((byte)Math.Clamp(Math.Round(n*255),0,255)).ToString("X2");})); }
		return new() { Name=Str("sLiveryName"), CarIndex=Contract.Cars.FirstOrDefault(c=>c.Class==Str("sClass") && c.CarId==Str("sCar"))?.Index ?? 0,
			Albedo=Str("sDiffuseTexture"), Structure=Str("sStructureTexture"), Banner=Str("sPreview"), Description=Str("sDescription"), OuterColor=Hex("sColor1"), InnerColor=Hex("sColor2"), OuterGloss=Num("sGloss1"), InnerGloss=Num("sGloss2"), Metalness=Num("sMetalness"), ClearCoat=Num("sClearCoat"),UseSrgbPaintColors=Flag("sUseSrgbPaintColors"),OverrideArtworkFinish=Flag("sOverrideArtworkFinish"),ArtworkGloss=xml.Element("sArtworkGloss") is null?.65:Num("sArtworkGloss"),ArtworkMetalness=xml.Element("sArtworkMetalness") is null?0:Num("sArtworkMetalness"),DamageGloss=xml.Element("sDamageGloss") is null?1:Num("sDamageGloss"),DamageMetalness=xml.Element("sDamageMetalness") is null?.784314:Num("sDamageMetalness") };
	}
}

public sealed record BuiltLivery(string Id, LiveryProject Project, string BuildRoot, string PackageDirectory, string BodyResource, string PaclogResource, Dictionary<string,string> Hashes);
public sealed record ImportReceipt(string Name, string GameData, string Key, string Backup, string[] InstalledFiles, string FactoryHash, string? RewardsBackup=null, string? RewardsHash=null,Dictionary<string,string>? InstalledHashes=null,string? FactoryBlock=null,string? RewardsBlock=null,DateTimeOffset? RemovedAt=null,string? ReplacedBy=null);
