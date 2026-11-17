using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Es1.Flic;

namespace Herculan.Es1.Install;

/// <summary>
/// Installs Metaltech: Earthsiege from its CD — a folder or an image (<see cref="GameDisc"/>) — into a folder: the full
/// install of the disc's <c>INSTALL\INSTALL.SCR</c>, its <c>maxInst</c> branch (docs/es1/shell/launcher.md#the-installers-esbat),
/// then what the script and the <c>SOUNDSET</c> and <c>ADDRES</c> runs after it leave in <c>RESOURCE.CFG</c>. Nothing on
/// the disc is run, and the disc's script is not parsed: <see cref="Plan"/> is this engine's encoding of it.
///
/// <para>Only the full install is offered. The minimum and medium installs leave <c>GO.EXE</c> and some of the archives
/// on the CD, where the programs reach them through <c>ALT_DRIVE</c>; this engine reads the data from the install alone,
/// and tells an install by its <c>GO.EXE</c> (<see cref="RetailGameDetector.Es1Marker"/>).</para>
///
/// <para>This engine's choices, where retail asks the player or names a DOS drive: the sound set-up is the one
/// <see cref="SoundSettings"/> lists, which <c>SOUNDSET.EXE</c> writes for a Sound Blaster 16 with FM music, and the CD
/// drive the script's <c>%2</c> stands for is <see cref="CdDrive"/>, in <c>ES.BAT</c> and in <c>pathFlic</c>. The
/// script's own <c>data\addr.bat</c>, the batch file it writes for <c>INSTALL.BAT</c> to run <c>ADDRES</c>, is left out.</para>
/// </summary>
public sealed class Es1Installer {
	/// <summary>The disc's installer script, which marks a Metaltech: Earthsiege disc.</summary>
	public const string ScriptPath = @"INSTALL\INSTALL.SCR";

	/// <summary>The DOS drive written for the CD: <c>D:</c>, which <see cref="Es1FlicHost"/> takes as the disc whatever its letter.</summary>
	public const string CdDrive = "D:";

	private const string ResourceSource = @"STUFF\RESOURCE.CFG";

	private readonly GameDisc _source;

	private Es1Installer(GameDisc source) {
		_source = source;
	}

	/// <summary>
	/// The values written into <c>RESOURCE.CFG</c> in place of the disc's empty ones: a Sound Blaster 16 (8-bit stereo,
	/// <c>E015</c>) at port <c>220</c>, IRQ 7, DMA 1, and FM music (<c>A002</c>) at <c>388</c>, by section and key.
	/// </summary>
	public static IReadOnlyList<(string Section, string Key, string Value)> SoundSettings { get; } = [
		(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Driver, "E015"),
		(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Dma, "1"),
		(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Irq, "7"),
		(Es1ResourceCfg.SoundFx, Es1ResourceCfg.Port, "220"),
		(Es1ResourceCfg.Music, Es1ResourceCfg.Driver, "A002"),
		(Es1ResourceCfg.Music, Es1ResourceCfg.Port, "388"),
	];

	/// <summary>An installer for <paramref name="source"/>, or null when it holds no <see cref="ScriptPath"/> or no <c>GO.EXE</c>.</summary>
	public static Es1Installer? Identify(GameDisc source) =>
		source.FileExists(ScriptPath) && source.FileExists(RetailGameDetector.Es1Marker) ? new Es1Installer(source) : null;

	/// <summary>
	/// What <c>maxInst</c> copies, each file's path on the disc and under the install: every file of <c>CAR</c>,
	/// <c>DATA</c>, <c>SOS</c>, <c>SSET</c> and <c>TAPES</c>, the <c>.VOL</c> files of <c>VOL</c>, the files of
	/// <c>INSTALL</c> and then <c>INSTALL\HDSTUFF</c> into <c>INSTALL</c>, those of <c>STUFF</c> into the root, and the
	/// four programs. A later copy to the same name replaces the earlier, as DOS's <c>copy</c> does.
	/// </summary>
	public IReadOnlyList<(string Source, string Destination)> Plan() {
		var files = new List<(string Source, string Destination)>();
		void Add(string source, string destination) {
			files.RemoveAll(file => file.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase));
			files.Add((source, destination));
		}

		void AddFolder(string folder, string into, Func<string, bool>? keep = null) {
			foreach (string name in _source.FileNames(folder).Where(keep ?? (_ => true)).Order(StringComparer.OrdinalIgnoreCase)) {
				Add($@"{folder}\{name}", into.Length == 0 ? name : $@"{into}\{name}");
			}
		}

		foreach (string folder in new[] { "CAR", "DATA", "SOS", "SSET", "TAPES" }) {
			AddFolder(folder, folder);
		}

		AddFolder(GameContent.ArchiveFolderName, GameContent.ArchiveFolderName,
			name => name.EndsWith(".VOL", StringComparison.OrdinalIgnoreCase));
		AddFolder("INSTALL", "INSTALL");
		AddFolder(@"INSTALL\HDSTUFF", "INSTALL");
		AddFolder("STUFF", "");
		// Already there from STUFF; named so that a disc without it is refused before anything is copied.
		Add(ResourceSource, Es1ResourceCfg.FileName.ToUpperInvariant());
		foreach (string program in new[] { RetailGameDetector.Es1Marker, "DBSIM.EXE", "DETECT.EXE", "ESCD.EXE" }) {
			Add(program, program);
		}

		return files;
	}

	/// <summary>The bytes <see cref="Plan"/> copies (<see cref="DiscCopy.PlanBytes"/>).</summary>
	public long PlanBytes() => DiscCopy.PlanBytes(_source, Plan());

	/// <summary>Why <paramref name="destination"/> cannot be installed into (<see cref="DiscCopy.CheckDestination"/>), or null when it can.</summary>
	public DiscCopy.DestinationProblem? CheckDestination(string destination) => DiscCopy.CheckDestination(_source, destination);

	/// <summary>
	/// Copies <see cref="Plan"/> into <paramref name="destination"/> (<see cref="DiscCopy.Copy"/>), then writes
	/// <c>maxInst</c>'s <c>ES.BAT</c> and turns the copied <c>RESOURCE.CFG</c> into what <c>SOUNDSET</c> and
	/// <c>ADDRES</c> leave (<see cref="ResourceCfg"/>). A failure or a cancellation removes what this wrote and rethrows.
	/// </summary>
	/// <exception cref="InvalidOperationException">The destination is refused, a file the plan names is not on the disc,
	/// or there is not room for it.</exception>
	public void Install(string destination, IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) =>
		DiscCopy.Copy(_source, destination, Plan(), WriteConfiguration, progress, cancellation);

	private static void WriteConfiguration(string root, List<string> written) {
		// The script's lines, each echo appending CR LF.
		string esBat = DiscCopy.Under(root, "ES.BAT");
		written.Add(esBat);
		File.WriteAllText(esBat, $"@echo off\r\nmode co80\r\nset ALT_DRIVE={CdDrive}\r\nset PREFER_ALT=\r\nescd\r\n", Encoding.Latin1);

		string resource = DiscCopy.Under(root, Es1ResourceCfg.FileName.ToUpperInvariant());
		File.WriteAllText(resource, ResourceCfg(File.ReadAllText(resource, Encoding.Latin1)), Encoding.Latin1);
	}

	/// <summary>
	/// The disc's <c>STUFF\RESOURCE.CFG</c>, <paramref name="text"/>, as the install leaves it: its DOS end-of-file
	/// marks and trailing blank lines dropped, each <see cref="SoundSettings"/> value in its section, and <c>ADDRES</c>'s
	/// <c>[FlicPlayer]</c> <c>pathFlic=</c><see cref="CdDrive"/><c>\flic\</c> after a blank line, every line ending CR LF.
	/// </summary>
	public static string ResourceCfg(string text) {
		var lines = text.Replace("\x1a", "").Replace("\r\n", "\n").Split('\n').ToList();
		while (lines.Count > 0 && lines[^1].Trim().Length == 0) {
			lines.RemoveAt(lines.Count - 1);
		}

		string section = "";
		for (int i = 0; i < lines.Count; i++) {
			string line = lines[i].Trim();
			if (line.StartsWith('[') && line.EndsWith(']')) {
				section = line[1..^1];
				continue;
			}

			int equals = line.IndexOf('=');
			if (equals <= 0) {
				continue;
			}

			string key = line[..equals].Trim();
			foreach (var (settingSection, settingKey, value) in SoundSettings) {
				if (settingSection.Equals(section, StringComparison.OrdinalIgnoreCase) && settingKey.Equals(key, StringComparison.OrdinalIgnoreCase)) {
					lines[i] = $"{key}={value}";
				}
			}
		}

		lines.Add("");
		lines.Add($"[{Es1FlicHost.FlicPlayerSection}]");
		lines.Add($@"{Es1FlicHost.PathFlicKey}={CdDrive}\flic\");
		return string.Join("\r\n", lines) + "\r\n";
	}
}
