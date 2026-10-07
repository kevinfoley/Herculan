using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Install;

/// <summary>How far an install has got, for a progress bar.</summary>
public readonly record struct InstallProgress(int FileIndex, int FileCount, string File, long BytesDone, long BytesTotal);

/// <summary>
/// Installs Earthsiege 2 from a retail disc — a directory or an image (<see cref="GameDisc"/>) — into a directory,
/// copying what the disc's <c>SIERRA.INF</c> script copies for a size and language and then writing what its
/// <c>BATCH.EXE</c> writes (docs/retail/retail-builds.md, "The installer"). Nothing on the disc is ever run.
///
/// <para><b>The file lists are this engine's encoding of the two retail scripts</b>, chosen by
/// <c>SIERRA.INF</c>'s <c>[Ident] Version</c>; a disc of any other version is refused rather than interpreted. Of
/// v1.10's two branches this takes the Windows 95 one (<c>VER95\</c>). The disc's own script is never parsed:
/// how Sierra's <c>SETUP.EXE</c> evaluates it is not read, and the script is untrusted input.</para>
///
/// <para>Left out, as none of it is needed to read the game's data: the Indeo codecs and their registry and
/// <c>SYSTEM.INI</c> entries, DirectX, WinG and Win32s, the demos, <c>SIERRA.INI</c>, the Program Manager group,
/// and <c>BATCH.EXE</c>'s <c>prefs.cfg</c> change, which depends on a speed <c>SETUP.EXE</c> measures. The
/// original programs are copied, but the install is not set up to run them.</para>
///
/// <para>Every language is offered for both builds. v1.0's script copies the English voice archive whatever the
/// language, so there a language decides only <c>language.cfg</c>, and with it the manual's folder.</para>
/// </summary>
public sealed class RetailInstaller {
	/// <summary>The script both builds' <c>SETUP.EXE</c> runs, at the disc's root.</summary>
	public const string ScriptFileName = "SIERRA.INF";

	private const string Ver95 = "VER95";

	private readonly GameDisc _source;

	private RetailInstaller(GameDisc source, RetailBuild build) {
		_source = source;
		Build = build;
	}

	/// <summary>Retail's three install sizes (docs/retail/retail-builds.md, "The installer").</summary>
	public enum Size {
		Minimum,
		Medium,
		Maximum,
	}

	/// <summary>The languages a retail disc installs in, by <c>language.cfg</c>'s letter.</summary>
	public enum Language {
		English = 'E',
		French = 'F',
		German = 'G',
	}

	/// <summary>The two retail builds (docs/retail/retail-builds.md).</summary>
	public enum RetailBuild {
		V100,
		V110,
	}

	/// <summary>Why a disc cannot be installed from (<see cref="Identify"/>).</summary>
	public enum Problem {
		NoScript,
		UnknownVersion,
	}

	/// <summary>Which build the disc carries.</summary>
	public RetailBuild Build { get; }

	/// <summary>The build as the docs name it.</summary>
	public string BuildName => Build == RetailBuild.V100 ? "v1.0" : "v1.10";

	/// <summary>The languages offered.</summary>
	public static IReadOnlyList<Language> Languages { get; } =
		[Language.English, Language.French, Language.German];

	/// <summary>
	/// An installer for <paramref name="source"/>, or null with the reason when it holds no <c>SIERRA.INF</c> or one
	/// of a version this does not know. <paramref name="version"/> is the version line's value, when there is one.
	/// </summary>
	public static RetailInstaller? Identify(GameDisc source, out Problem problem, out string? version) {
		problem = Problem.NoScript;
		version = null;
		byte[]? script = source.ReadAllBytes(ScriptFileName, 1 << 20);
		if (script == null) {
			return null;
		}

		bool inIdent = false;
		foreach (string raw in Encoding.Latin1.GetString(script).Split('\n')) {
			string line = raw.Trim();
			if (line.StartsWith('[')) {
				inIdent = line.Equals("[Ident]", StringComparison.OrdinalIgnoreCase);
			} else if (inIdent && line.StartsWith("Version=", StringComparison.OrdinalIgnoreCase)) {
				version = line["Version=".Length..].Trim();
				break;
			}
		}

		problem = Problem.UnknownVersion;
		string? number = version?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		return number switch {
			"100" => new RetailInstaller(source, RetailBuild.V100),
			"110" => new RetailInstaller(source, RetailBuild.V110),
			_ => null,
		};
	}

	/// <summary>
	/// What the script copies for <paramref name="size"/> and <paramref name="language"/>: each file's path on the
	/// disc and under the install, renames already applied.
	/// </summary>
	public IReadOnlyList<(string Source, string Destination)> Plan(Size size, Language language) {
		var files = new List<(string, string)>();
		void Add(params string[] paths) => files.AddRange(paths.Select(path => (path, path)));

		// The files every size copies, then the archives a larger size adds.
		Add("ESII.ICO", "BWCC32.DLL", "SOSLIBS3.DLL", "HMIDRV.WIN", "HMIMDRV.WIN", "SOSLIB.INI", "CW3220.DLL",
			"DATA/EXIT.CFG", "DATA/KEYJOY.CFG", "DATA/PREFS.CFG", "DATA/SOUND.CFG", "DATA/MAT0.DAT", "DATA/MFORMS.DAT",
			"DATA/MAPLABEL.STR", "TAPES/DEMOLIST.STR", "TAPES/DEMO1.TAP", "TAPES/DEMO2.TAP", "TAPES/DEMO3.TAP",
			"VOL/SIMALERT.VOL", "VOL/SIMSOUND.VOL", "VOL/SIMPATCH.VOL", "SAV/GAMEFILE.STR");

		if (Build == RetailBuild.V100) {
			Add("ES.EXE", "VSHELL.EXE", "DBSIM.EXE", "SOS9503.DLL", "DATA/MISSION.STR",
				"ENGLISH/README.WRI", "FRENCH/README.WRI", "GERMAN/README.WRI", "VOL/SIMVOICE.VOL");
		} else {
			var (folder, extension) = LanguageFolder(language);
			Add("VOL/SIMLANG.VOL");
			files.Add(($"{Ver95}/ES.EXE", "ES.EXE"));
			files.Add(($"{Ver95}/VSHELL.EXE", "VSHELL.EXE"));
			files.Add(($"{Ver95}/DBSIM.EXE", "DBSIM.EXE"));
			files.Add(($"{Ver95}/SOS9503.DLL", "SOS9503.DLL"));
			files.Add(($"{folder}/ERROR.{extension}", "ERROR.STR"));
			files.Add(($"{folder}/MISSION.{extension}", "DATA/MISSION.STR"));
			Add($"VOL/SIMVOIC{(char)language}.VOL", $"{folder}/README.WRI");
		}

		if (size == Size.Medium) {
			Add("VOL/SIMVOL0.VOL");
		} else if (size == Size.Maximum) {
			Add("VOL/SHLSOUND.VOL", "VOL/SIMVOL0.VOL", "VOL/SHELL0.VOL", "VOL/ZONES.VOL", "VOL/LANG0.VOL");
			if (Build == RetailBuild.V110) {
				Add("VOL/SHELL1.VOL");
			}
		}

		return files;
	}

	/// <summary>The bytes <paramref name="plan"/> copies; a file missing from the disc counts as nothing.</summary>
	public long PlanBytes(IReadOnlyList<(string Source, string Destination)> plan) =>
		plan.Sum(file => {
			using var stream = _source.OpenRead(file.Source);
			return stream?.Length ?? 0;
		});

	/// <summary>Why a directory cannot be installed into (<see cref="CheckDestination"/>).</summary>
	public enum DestinationProblem {
		IsAFile,
		NotEmpty,
		InsideDisc,
	}

	/// <summary>
	/// Why <paramref name="destination"/> cannot be installed into, or null when it can: it must be a new or empty
	/// directory, outside the source when the source is a directory.
	/// </summary>
	public DestinationProblem? CheckDestination(string destination) {
		string full = Path.GetFullPath(destination);
		if (File.Exists(full)) {
			return DestinationProblem.IsAFile;
		}

		if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) {
			return DestinationProblem.NotEmpty;
		}

		if (_source.Image == null) {
			string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_source.Location)) + Path.DirectorySeparatorChar;
			if ((full + Path.DirectorySeparatorChar).StartsWith(source, StringComparison.OrdinalIgnoreCase)) {
				return DestinationProblem.InsideDisc;
			}
		}

		return null;
	}

	/// <summary>
	/// Why the source cannot be written into <c>drive.cfg</c> as the install's disc, or null when it can: a disc
	/// folder must pass <see cref="GameInstall.CheckDiscDirectory"/>. An image always can.
	/// </summary>
	public GameInstall.DiscDirectoryProblem? CheckSource() =>
		_source.Image == null ? GameInstall.CheckDiscDirectory(_source.Location) : null;

	/// <summary>The bytes free on the drive holding <paramref name="destination"/>, or null when that cannot be told.</summary>
	public static long? FreeSpace(string destination) {
		try {
			return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
		} catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>
	/// Copies <see cref="Plan"/> into <paramref name="destination"/>, then writes <c>BATCH.EXE</c>'s three files:
	/// <list type="number">
	/// <item><c>data\drive.cfg</c>: the disc folder and the install, one per line. From an image, the disc line is
	/// <c>.</c> and the image goes on HERCULAN's own line (<see cref="Drive.DiscImage"/>).</item>
	/// <item><c>data\language.cfg</c>: the language's letter.</item>
	/// <item>A copy of the language folder's <c>README.WRI</c>: <c>esreadme.txt</c> from v1.0, <c>readme.txt</c>
	/// from v1.10 (docs/retail/formats/winhelp.md, "Macros").</item>
	/// </list>
	/// A failure or a cancellation removes what this wrote and rethrows.
	/// </summary>
	/// <exception cref="InvalidOperationException">The destination is refused, a file the plan names is not on the
	/// disc, or there is not room for it.</exception>
	public void Install(string destination, Size size, Language language,
			IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) {
		string root = Path.GetFullPath(destination);
		if (CheckDestination(root) is { } refusal) {
			throw new InvalidOperationException($"{root} cannot be installed into: {refusal}.");
		}

		if (CheckSource() is { } discProblem) {
			throw new InvalidOperationException($"{_source.Location} cannot go into {Drive.FileName}: {discProblem}.");
		}

		var plan = Plan(size, language);
		if (plan.FirstOrDefault(file => !_source.FileExists(file.Source)) is { Source: not null } missing) {
			throw new InvalidOperationException($"The disc has no {missing.Source}.");
		}

		long total = PlanBytes(plan);
		if (FreeSpace(root) is { } free && free < total) {
			throw new InvalidOperationException($"{root} has {free:N0} bytes free; the install needs {total:N0}.");
		}

		bool created = !Directory.Exists(root);
		var written = new List<string>();
		try {
			Directory.CreateDirectory(root);
			var buffer = new byte[1 << 20];
			long done = 0;
			for (int i = 0; i < plan.Count; i++) {
				var (source, relative) = plan[i];
				string target = Under(root, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				using var input = _source.OpenRead(source)
					?? throw new InvalidOperationException($"The disc has no {source}.");
				written.Add(target);
				using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write)) {
					for (int read; (read = input.Read(buffer)) > 0;) {
						cancellation.ThrowIfCancellationRequested();
						output.Write(buffer, 0, read);
						done += read;
						progress?.Report(new InstallProgress(i, plan.Count, relative, done, total));
					}
				}
			}

			WriteConfiguration(root, language, written);
			progress?.Report(new InstallProgress(plan.Count, plan.Count, "", total, total));
		} catch {
			Undo(root, created, written);
			throw;
		}
	}

	private void WriteConfiguration(string root, Language language, List<string> written) {
		var drive = _source.Image == null
			? new Drive { Directory = Path.GetFullPath(_source.Location), InstallDirectory = root }
			: new Drive { Directory = ".", InstallDirectory = root, DiscImage = _source.Location };
		string driveCfg = GameInstall.DriveCfgPath(root);
		Directory.CreateDirectory(Path.GetDirectoryName(driveCfg)!);
		written.Add(driveCfg);
		File.WriteAllBytes(driveCfg, new DriveTransformer().Write(drive)!);

		string languageCfg = Under(root, Path.Combine(MissionLoader.DataFolderName, GameInstall.LanguageCfgName));
		written.Add(languageCfg);
		File.WriteAllBytes(languageCfg, [(byte)language]);

		string readme = Under(root, Path.Combine(LanguageFolder(language).Folder, "README.WRI"));
		string copy = Under(root, Build == RetailBuild.V100 ? "esreadme.txt" : "readme.txt");
		written.Add(copy);
		File.Copy(readme, copy);
	}

	// Every destination is one of Plan's constants; this keeps it so should one ever be wrong.
	private static string Under(string root, string relative) {
		string full = Path.GetFullPath(Path.Combine(root, relative));
		return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			? full
			: throw new InvalidOperationException($"{relative} is outside the install.");
	}

	/// <summary>
	/// <paramref name="language"/>'s folder on the disc, which holds its manual and readme (and in v1.10 its
	/// <c>ERROR</c> and <c>MISSION</c> text), and that text's extension.
	/// </summary>
	public static (string Folder, string Extension) LanguageFolder(Language language) => language switch {
		Language.French => ("FRENCH", "FRE"),
		Language.German => ("GERMAN", "GER"),
		_ => ("ENGLISH", "ENG"),
	};

	// Removes what an install that failed wrote: its files, then the directories left empty, then the
	// destination itself when the install made it.
	private static void Undo(string root, bool created, List<string> written) {
		try {
			foreach (string file in written.Where(File.Exists)) {
				File.Delete(file);
			}

			foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
					.OrderByDescending(path => path.Length)) {
				if (!Directory.EnumerateFileSystemEntries(directory).Any()) {
					Directory.Delete(directory);
				}
			}

			if (created && !Directory.EnumerateFileSystemEntries(root).Any()) {
				Directory.Delete(root);
			}
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			Console.Error.WriteLine($"Could not remove the partial install in {root}: {ex.Message}");
		}
	}
}
