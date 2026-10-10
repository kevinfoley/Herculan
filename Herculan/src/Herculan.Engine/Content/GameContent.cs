using HercWorks.Vol;
using HercWorks.Vol.Io;
using Herculan.Engine.Install;

namespace Herculan.Engine.Content;

/// <summary>
/// The engine's resource layer: mounts the game's own <c>.VOL</c> archives and resolves
/// <c>folder\name</c> lookups against them, which is how DBSIM addresses everything it loads
/// (<c>dat\zone504</c>, <c>dba\zone504.dba</c>, <c>dts\samson.dts</c>, ...). DBSIM builds each such
/// path with <c>ResourcePath_BuildFolderName</c> (<c>00492ae0</c>).
///
/// <para><see cref="MountInstall"/> mounts what the program would: every <c>vol\*.vol</c> in the
/// install and then in the <c>drive.cfg</c> directory whose program mask shares a bit with the
/// program's, in descending order of search precedence (<see cref="Voln.VolOrderNum"/>), the first
/// archive holding a <c>folder\name</c> answering for it (docs/retail/formats/vol-archive.md, "Which
/// archives are mounted").</para>
///
/// <para>It also carries the program's <see cref="Language"/>, which the readers of translated resources take
/// their folder or extension from.</para>
///
/// Parsing is delegated wholesale to <see cref="VolFileReader"/> in HercWorks.Vol; this type adds
/// only the index and the load-order rule. Per docs/herculan/planning.md's repo-structure decision
/// the engine talks to HercWorks.Core/HercWorks.Vol directly.
/// </summary>
public sealed class GameContent {
	/// <summary>The most archives a program's volume group holds (<c>VolumeGroup_SetGroup</c>, <c>00467bd4</c>).</summary>
	public const int MaxArchives = 0x1e;

	/// <summary>The archive file pattern both programs scan for.</summary>
	public const string ArchivePattern = "*.vol";

	private readonly Dictionary<string, VolEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<Voln> _mounted;

	private GameContent(List<Voln> searchOrder, GameLanguage language, bool isV110) {
		_mounted = searchOrder;
		Language = language;
		IsV110 = isV110;
		foreach (var vol in searchOrder) {
			foreach (var entry in vol.FilesSet) {
				// Folder labels come out of the VOL header already stripped of their '\' separator
				// (see VolFileReader.GenerateFolderList), so the key is just "dir\name".
				if (entry.FileName != null && vol.Folders.TryGetValue(entry.DirIdx, out var folder)) {
					_entries.TryAdd(Key(folder.Label, entry.FileName), entry);
				}
			}
		}
	}

	/// <summary>
	/// The language the program runs in: <see cref="LauncherLanguage.Simulator"/> for DBSIM's mount and
	/// <see cref="LauncherLanguage.Shell"/> for VSHELL's.
	/// </summary>
	public GameLanguage Language { get; }

	/// <summary>
	/// Whether the install is v1.10's (<see cref="LauncherLanguage.IsV110"/>). v1.10's shell takes the folder of
	/// <c>campaign.str</c> and of the intro movies from <see cref="Language"/>, where v1.0's names English ones whatever
	/// its language (docs/retail/retail-builds.md#v110s-shell-reads-the-language-twice-more). The cockpit reads it for
	/// the simulator changes it follows (docs/retail/retail-builds.md#how-v110s-programs-differ): the MFD status
	/// screen's range in metres (<see cref="Cockpit.MfdStatusSubject.Range"/>).
	/// </summary>
	public bool IsV110 { get; }

	/// <summary>Names of the archives mounted, in the order they are searched.</summary>
	public IReadOnlyList<string> MountedArchives =>
		_mounted.Select(v => v.FileName ?? "<unnamed>").ToList();

	/// <summary>What DBSIM mounts of the install; see <see cref="MountInstall"/>.</summary>
	public static GameContent MountSimulator(string installRoot) => MountInstall(installRoot, Voln.DbsimProgram);

	/// <summary>What VSHELL mounts of the install; see <see cref="MountInstall"/>.</summary>
	public static GameContent MountShell(string installRoot) => MountInstall(installRoot, Voln.VshellProgram);

	/// <summary>What DBSIM mounts of the install and its open disc; see <see cref="MountInstall(string, GameDisc?, uint)"/>.</summary>
	public static GameContent MountSimulator(string installRoot, GameDisc? disc) =>
		MountInstall(installRoot, disc, Voln.DbsimProgram);

	/// <summary>What VSHELL mounts of the install and its open disc; see <see cref="MountInstall(string, GameDisc?, uint)"/>.</summary>
	public static GameContent MountShell(string installRoot, GameDisc? disc) =>
		MountInstall(installRoot, disc, Voln.VshellProgram);

	/// <summary>
	/// Mounts what <paramref name="program"/> — <see cref="Voln.DbsimProgram"/> or
	/// <see cref="Voln.VshellProgram"/> — would of the install at <paramref name="installRoot"/>.
	///
	/// <para><c>VolumeGroup_SetGroup</c> (<c>00467bd4</c>) scans <c>vol\*.vol</c> under the current
	/// directory, which is the install, and then under the <c>drive.cfg</c> directory, here the disc
	/// <see cref="GameInstall.OpenDisc"/> opens: both programs pass 0 for its directory-first
	/// argument. It skips a file whose name an archive already loaded carries, and
	/// <c>VolumeGroup_AddVolume</c> (<c>00467e90</c>) inserts each loaded archive after every one of
	/// equal or higher precedence, so of two equal archives the first loaded is searched first.
	/// <c>findfirst</c> lists a directory in the file system's order; this lists it by name, which
	/// changes nothing for retail data: every <c>folder\name</c> two equal-precedence archives of one
	/// program share has the same content in both, in v1.0 and in v1.10. Names are compared ignoring
	/// case, where retail's comparison is exact.</para>
	///
	/// <para>Retail stops before scanning when the group already holds <see cref="MaxArchives"/>, but
	/// adding past that during a scan writes beyond its array; this stops adding at the limit and says
	/// so.</para>
	/// </summary>
	public static GameContent MountInstall(string installRoot, uint program) {
		using var disc = GameInstall.OpenDisc(installRoot);
		return MountInstall(installRoot, disc, program);
	}

	/// <summary>
	/// <see cref="MountInstall(string, uint)"/> with the disc already open, which may be an image
	/// (<see cref="GameDisc"/>); null mounts the install alone. Every archive is read into memory, so the disc
	/// can be closed once this returns.
	/// </summary>
	public static GameContent MountInstall(string installRoot, GameDisc? disc, uint program) {
		var loaded = new List<Voln>();
		var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };

		bool Admit(string name, string where) {
			if (loaded.Any(vol => string.Equals(vol.FileName, name, StringComparison.OrdinalIgnoreCase))) {
				return false;
			}

			if (loaded.Count == MaxArchives) {
				Console.Error.WriteLine($"Not mounting {where}: a program holds at most {MaxArchives} archives.");
				return false;
			}

			return true;
		}

		string installArchives = GameInstall.ArchiveDirectory(installRoot);
		if (Directory.Exists(installArchives)) {
			foreach (string path in Directory.GetFiles(installArchives, ArchivePattern, options)
					.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)) {
				if ((VolFileReader.ReadProgramMask(path) & program) is not (null or 0) && Admit(Path.GetFileName(path), path)) {
					loaded.Add(VolFileReader.ParseVolFile(path));
				}
			}
		}

		if (disc != null) {
			foreach (string name in disc.ArchiveNames().Order(StringComparer.OrdinalIgnoreCase)) {
				string relative = Path.Combine(GameInstall.ArchiveFolderName, name);
				using var stream = disc.OpenRead(relative);
				if (stream == null || (VolFileReader.ReadProgramMask(stream) & program) is null or 0
						|| !Admit(name, disc.Describe(relative))) {
					continue;
				}

				var bytes = new byte[stream.Length];
				stream.Position = 0;
				stream.ReadExactly(bytes);
				loaded.Add(VolFileReader.ParseVolBytes(name, bytes, disc.Describe(relative)));
			}
		}

		if (loaded.Count == 0) {
			throw new FileNotFoundException(
				$"No game archives for program mask 0x{program:x} in {installArchives}"
				+ (disc != null ? $" or {disc.Describe(GameInstall.ArchiveFolderName)}." : "."));
		}

		// OrderByDescending is stable, which keeps load order among equal precedences.
		var language = (program & Voln.DbsimProgram) != 0
			? LauncherLanguage.Simulator(installRoot)
			: LauncherLanguage.Shell(installRoot);
		return new GameContent(loaded.OrderByDescending(vol => vol.VolOrderNum).ToList(), language,
			LauncherLanguage.IsV110(installRoot));
	}

	/// <summary>
	/// Reads one resource's bytes, or null if no mounted archive has it. <paramref name="folder"/>
	/// and <paramref name="name"/> are matched case-insensitively, matching the original's own
	/// <c>_stricmp</c>-based resource resolution.
	/// </summary>
	public byte[]? Read(string folder, string name) =>
		_entries.TryGetValue(Key(folder, name), out var entry) ? entry.RawBytes : null;

	/// <summary>
	/// Same as <see cref="Read"/> but throws with the resolved resource path when the entry is
	/// missing — for callers where a missing file means the install is wrong, not a condition to
	/// handle.
	/// </summary>
	public byte[] ReadRequired(string folder, string name) =>
		Read(folder, name) ?? throw new FileNotFoundException(
			$"Resource '{Key(folder, name)}' not present in any mounted archive " +
			$"({string.Join(", ", MountedArchives)}).");

	/// <summary>Whether any mounted archive contains this resource.</summary>
	public bool Contains(string folder, string name) => _entries.ContainsKey(Key(folder, name));

	/// <summary>Every resource name in one folder, in mount order. Useful for tooling and probes.</summary>
	public IEnumerable<string> ListFolder(string folder) {
		string prefix = folder + "\\";
		return _entries.Keys
			.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			.Select(k => k.Substring(prefix.Length))
			.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
	}

	private static string Key(string folder, string name) => $"{folder}\\{name}";
}
