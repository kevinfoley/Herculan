using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;

namespace HercWorks.Query;

/// <summary>One retail mission: its <c>.MSN</c> and the <c>.ENG</c> beside it.</summary>
/// <param name="Name">The file name without its extension, <c>C4_09</c>.</param>
/// <param name="MsnBytes">The <c>.MSN</c> as stored, for a query that runs the mission load over it.</param>
/// <param name="EngBytes">The <c>.ENG</c> as stored, or null when the mission has none.</param>
internal sealed record Mission(string Name, MissionFile File, MissionStringFile? Text, byte[] MsnBytes, byte[]? EngBytes);

/// <summary>One mission's record counts, for <c>missions</c>.</summary>
/// <param name="TextRecords">The <c>.ENG</c>'s record count, null when the mission has none.</param>
internal sealed record MissionSummary(string Mission, int Conditions, int Mechs, int Flyers, int Bases, int Groups, int? TextRecords);

/// <summary>
/// Everything a query reads out of an install, parsed by HercWorks.Core: every <c>MSN\*.MSN</c> and its
/// <c>.ENG</c> out of <c>ZONES.VOL</c>, and the type tables that name what the rosters place —
/// <c>dat\BASES.DAT</c>, <c>nam\MECHS.NAM</c>, <c>nam\FLYERS.NAM</c> and <c>str\STRINGS0.STR</c> — out
/// of <c>SIMVOL0.VOL</c>.
///
/// <para>Each archive is opened by name, from the install's <c>VOL</c> folder and then from the
/// <c>VOL</c> folder of the directory its <c>DATA\drive.cfg</c> names, which is where a v1.10 Minimum
/// install leaves them. This is not the programs' own mount (docs/retail/formats/vol-archive.md, "Which
/// archives are mounted"): no other archive is consulted, so a v1.10 install's <c>SIMLANG.VOL</c>
/// copy of <c>STRINGS0.STR</c> is not the one read.</para>
/// </summary>
internal sealed class RetailData {
	public const string MissionArchive = "ZONES.VOL";
	public const string SimulatorArchive = "SIMVOL0.VOL";
	public const string ArchiveFolder = "VOL";

	/// <summary>The <c>STRINGS0.STR</c> groups holding the structure and vehicle type names.</summary>
	public const int StructureNameGroup = 23;
	public const int VehicleNameGroup = 24;

	private RetailData(string installRoot, string missionArchive, IReadOnlyList<Mission> missions,
			BasesDat? bases, NameList? mechs, NameList? flyers, StringFile? strings) {
		InstallRoot = installRoot;
		MissionArchivePath = missionArchive;
		Missions = missions;
		Bases = bases;
		MechNames = mechs;
		FlyerNames = flyers;
		SimStrings = strings;
	}

	public string InstallRoot { get; }
	public string MissionArchivePath { get; }

	/// <summary>The missions, ordered by name.</summary>
	public IReadOnlyList<Mission> Missions { get; }

	public BasesDat? Bases { get; }
	public NameList? MechNames { get; }
	public NameList? FlyerNames { get; }
	public StringFile? SimStrings { get; }

	/// <summary>
	/// The install to read when none is given: the nearest <c>ES2</c> folder holding
	/// <c>VOL\ZONES.VOL</c>, looking up from the working directory and then from this binary.
	/// </summary>
	public static string? FindDefaultInstall() {
		foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }) {
			for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent) {
				string candidate = Path.Combine(directory.FullName, "ES2");
				if (FindArchive(candidate, MissionArchive) != null) {
					return candidate;
				}
			}
		}

		return null;
	}

	/// <exception cref="FileNotFoundException">The install has no <c>ZONES.VOL</c>.</exception>
	public static RetailData Load(string installRoot) {
		string zones = FindArchive(installRoot, MissionArchive)
			?? throw new FileNotFoundException($"No {ArchiveFolder}\\{MissionArchive} in {installRoot} or the directory its drive.cfg names.");

		var zonesVol = VolFileReader.ParseVolFile(zones);
		var msnTransformer = new MissionFileTransformer();
		var engTransformer = new MissionStringFileTransformer();
		var missions = new List<Mission>();
		foreach (var entry in EntriesIn(zonesVol, "MSN")) {
			string fileName = entry.FileName!.Trim();
			if (!fileName.EndsWith(".MSN", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			string name = Path.GetFileNameWithoutExtension(fileName);
			byte[] msn = entry.RawBytes ?? throw new InvalidDataException($"{fileName} is empty.");
			var file = msnTransformer.Parse(msn)
				?? throw new InvalidDataException($"{fileName} is empty.");
			byte[]? eng = Find(zonesVol, "MSN", name + ".ENG")?.RawBytes;
			var text = eng != null ? engTransformer.Parse(eng) : null;
			missions.Add(new Mission(name.ToUpperInvariant(), file, text, msn, eng));
		}

		missions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

		BasesDat? bases = null;
		NameList? mechs = null, flyers = null;
		StringFile? strings = null;
		if (FindArchive(installRoot, SimulatorArchive) is { } sim) {
			var simVol = VolFileReader.ParseVolFile(sim);
			bases = Find(simVol, "DAT", "BASES.DAT") is { RawBytes: { } b } ? new BasesDatTransformer().Parse(b) : null;
			mechs = Find(simVol, "NAM", "MECHS.NAM") is { RawBytes: { } m } ? new NameListTransformer().Parse(m) : null;
			flyers = Find(simVol, "NAM", "FLYERS.NAM") is { RawBytes: { } f } ? new NameListTransformer().Parse(f) : null;
			strings = Find(simVol, "STR", "STRINGS0.STR") is { RawBytes: { } s } ? new StringFileTransformer().Parse(s) : null;
		}

		return new RetailData(Path.GetFullPath(installRoot), Path.GetFullPath(zones), missions, bases, mechs, flyers, strings);
	}

	/// <summary>
	/// The name a structure type's MFD readout gives it: <c>STRINGS0.STR</c> group 23 or 24, picked
	/// by its texture selector, at its silhouette index (docs/retail/simulation/mfd.md#viewport-and-condition-per-class).
	/// </summary>
	public string? StructureName(int typeIndex) {
		if (Bases is not { } bases || SimStrings is not { } strings || typeIndex < 0 || typeIndex >= bases.Types.Length) {
			return null;
		}

		var type = bases.Types[typeIndex];
		return strings.Text(type.TextureSelector == 0 ? StructureNameGroup : VehicleNameGroup, type.SilhouetteIndex);
	}

	/// <summary>How many types a roster's type table holds, or 0 when it was not read.</summary>
	public int TypeCount(RosterKind kind) => kind switch {
		RosterKind.Mech => MechNames?.Names.Length ?? 0,
		RosterKind.Flyer => FlyerNames?.Names.Length ?? 0,
		_ => Bases?.Types.Length ?? 0,
	};

	/// <summary>A roster type's name, or null when the table has none for it.</summary>
	public string? TypeName(RosterKind kind, int typeIndex) => kind switch {
		RosterKind.Mech => MechNames?[typeIndex],
		RosterKind.Flyer => FlyerNames?[typeIndex],
		_ => StructureName(typeIndex),
	};

	/// <summary><paramref name="archive"/> in the install's <c>VOL</c> folder, else in the drive.cfg directory's; null when neither has it.</summary>
	private static string? FindArchive(string installRoot, string archive) {
		foreach (string? root in new[] { installRoot, DiscDirectory(installRoot) }) {
			if (root == null) {
				continue;
			}

			string path = Path.Combine(root, ArchiveFolder, archive);
			if (File.Exists(path)) {
				return path;
			}
		}

		return null;
	}

	/// <summary>The first token of <c>DATA\drive.cfg</c>, resolved against the install; null when there is none.</summary>
	private static string? DiscDirectory(string installRoot) {
		string path = Path.Combine(installRoot, "DATA", Drive.FileName);
		if (!File.Exists(path) || new DriveTransformer().Parse(File.ReadAllBytes(path))?.Directory is not { } directory) {
			return null;
		}

		return Path.GetFullPath(Path.Combine(installRoot, directory));
	}

	private static IEnumerable<VolEntry> EntriesIn(Voln vol, string folder) =>
		vol.FilesSet.Where(e => e.FileName != null
			&& vol.Folders.TryGetValue(e.DirIdx, out var dir)
			&& string.Equals(dir.Label, folder, StringComparison.OrdinalIgnoreCase));

	private static VolEntry? Find(Voln vol, string folder, string name) =>
		EntriesIn(vol, folder).FirstOrDefault(e => string.Equals(e.FileName!.Trim(), name, StringComparison.OrdinalIgnoreCase));
}
