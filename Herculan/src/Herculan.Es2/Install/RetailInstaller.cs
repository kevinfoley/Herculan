using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Es2.Audio;
using Herculan.Es2.Shell;
using Herculan.Es2.World;

namespace Herculan.Es2.Install;

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
	///
	/// <para>With <paramref name="discFiles"/>, also what both programs read from the disc at every size, for the
	/// language the install runs in: the <c>AVI</c> folder, a v1.10 French or German install's intro folder, the
	/// training instructor's clip folder, and the language folder's on-line manual. No retail script copies these
	/// (docs/retail/retail-builds.md, "The installer"); this engine's option, so an install plays them without its
	/// disc, through <see cref="GameInstall.OpenDiscFile"/>'s fallback to the install.</para>
	/// </summary>
	public IReadOnlyList<(string Source, string Destination)> Plan(Size size, Language language, bool discFiles) {
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

		if (discFiles) {
			var game = RunsIn(language);
			foreach (string folder in new[] { ShellMovieQueue.MovieFolder, ShellMovieQueue.IntroFolder(game), ComputerVoice.VoiceFolder(game) }.Distinct()) {
				Add(_source.FileNames(folder).Order(StringComparer.OrdinalIgnoreCase).Select(name => $"{folder}/{name}").ToArray());
			}

			string manual = $"{LanguageFolder(language).Folder}/{ManualFileName}";
			if (_source.FileExists(manual)) {
				Add(manual);
			}
		}

		return files;
	}

	/// <summary>The on-line manual's name inside each language folder (docs/retail/formats/winhelp.md).</summary>
	public const string ManualFileName = "ES2GUIDE.HLP";

	// The language an install of this build in language runs in: v1.10's launcher passes the installed language,
	// v1.0's passes none (docs/retail/retail-builds.md, "How a language is chosen"; LauncherLanguage).
	private GameLanguage RunsIn(Language language) => Build == RetailBuild.V100 ? GameLanguage.English : language switch {
		Language.French => GameLanguage.French,
		Language.German => GameLanguage.German,
		_ => GameLanguage.English,
	};

	/// <summary>The bytes <paramref name="plan"/> copies (<see cref="DiscCopy.PlanBytes"/>).</summary>
	public long PlanBytes(IReadOnlyList<(string Source, string Destination)> plan) => DiscCopy.PlanBytes(_source, plan);

	/// <summary>
	/// Why the source cannot be written into <c>drive.cfg</c> as the install's disc, or null when it can: a disc
	/// folder must pass <see cref="GameInstall.CheckDiscDirectory"/>. An image always can.
	/// </summary>
	public GameInstall.DiscDirectoryProblem? CheckSource() =>
		_source.Image == null ? GameInstall.CheckDiscDirectory(_source.Location) : null;

	/// <summary>
	/// Copies <see cref="Plan"/>, with or without <paramref name="discFiles"/>, into <paramref name="destination"/>
	/// (<see cref="DiscCopy.Copy"/>), then writes <c>BATCH.EXE</c>'s three files:
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
	public void Install(string destination, Size size, Language language, bool discFiles,
			IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) {
		if (CheckSource() is { } discProblem) {
			throw new InvalidOperationException($"{_source.Location} cannot go into {Drive.FileName}: {discProblem}.");
		}

		DiscCopy.Copy(_source, destination, Plan(size, language, discFiles),
			(root, written) => WriteConfiguration(root, language, written), progress, cancellation);
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

	private static string Under(string root, string relative) => DiscCopy.Under(root, relative);

	/// <summary>
	/// <paramref name="language"/>'s folder on the disc, which holds its manual and readme (and in v1.10 its
	/// <c>ERROR</c> and <c>MISSION</c> text), and that text's extension.
	/// </summary>
	public static (string Folder, string Extension) LanguageFolder(Language language) => language switch {
		Language.French => ("FRENCH", "FRE"),
		Language.German => ("GERMAN", "GER"),
		_ => ("ENGLISH", "ENG"),
	};
}
