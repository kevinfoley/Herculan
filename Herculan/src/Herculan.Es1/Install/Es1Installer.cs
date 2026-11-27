using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Es1.Flic;

namespace Herculan.Es1.Install;

/// <summary>
/// Installs Metaltech: Earthsiege from its CD — a folder or an image (<see cref="GameDisc"/>) — into a folder: one of the
/// three sizes of the disc's <c>INSTALL\INSTALL.SCR</c>, its <c>minInst</c>, <c>medInst</c> and <c>maxInst</c> branches
/// (docs/es1/shell/launcher.md#the-installers-esbat), then what the script and the <c>SOUNDSET</c> and <c>ADDRES</c> runs
/// after it leave in <c>RESOURCE.CFG</c>. Nothing on the disc is run, and the disc's script is not parsed:
/// <see cref="Plan"/> is this engine's encoding of it.
///
/// <para>This engine's choices, where retail asks the player or names a DOS drive: the sound set-up is the one
/// <see cref="SoundSettings"/> lists, which <c>SOUNDSET.EXE</c> writes for a Sound Blaster 16 with FM music; the CD drive
/// the script's <c>%2</c> stands for is <see cref="CdDrive"/>, in <c>ES.BAT</c>, <c>data\addr.bat</c> and
/// <c>pathFlic</c>; and the hard drive its <c>%1</c> stands for is <see cref="HardDrive"/>, in the minimum and medium
/// sizes' <c>ES.BAT</c>.</para>
/// </summary>
public sealed class Es1Installer {
	/// <summary>The script's three sizes, as its menu lists them.</summary>
	public enum Size {
		/// <summary><c>minInst</c>, "minimum install (4.5 meg)": the archives <c>SIMSOUND</c> and <c>SIMVOICE</c>, the rest left on the CD.</summary>
		Minimum,

		/// <summary><c>medInst</c>, "medium install (11 meg)": the minimum's archives and <c>SIMVOL0</c>.</summary>
		Medium,

		/// <summary><c>maxInst</c>, "full install (32 meg)": every archive and the four programs.</summary>
		Full,
	}

	/// <summary>The disc's installer script, which marks a Metaltech: Earthsiege disc.</summary>
	public const string ScriptPath = @"INSTALL\INSTALL.SCR";

	/// <summary>The DOS drive written for the CD: <c>D:</c>, which <see cref="Es1FlicHost"/> takes as the disc whatever its letter.</summary>
	public const string CdDrive = "D:";

	/// <summary>The DOS drive written for the install in the minimum and medium sizes' <c>ES.BAT</c>: <c>C:</c>.</summary>
	public const string HardDrive = "C:";

	/// <summary>The batch file every size writes for <c>INSTALL.BAT</c> to run, under the install.</summary>
	public const string AddrBatPath = @"DATA\ADDR.BAT";

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

	/// <summary>An installer for the source disc, or null when it holds no <see cref="ScriptPath"/> or no <c>GO.EXE</c>.</summary>
	/// <param name="source">The disc.</param>
	public static Es1Installer? Identify(GameDisc source) =>
		source.FileExists(ScriptPath) && source.FileExists(RetailGameDetector.Es1Marker) ? new Es1Installer(source) : null;

	/// <summary>
	/// What a size's branch copies, each file's path on the disc and under the install: every file of
	/// <c>CAR</c>, <c>DATA</c>, <c>SOS</c>, <c>SSET</c> and <c>TAPES</c>; of <c>VOL</c>, <c>SIMSOUND.VOL</c> and
	/// <c>SIMVOICE.VOL</c>, with <c>SIMVOL0.VOL</c> for the medium size, and every <c>.VOL</c> file for the full one; the
	/// files of <c>INSTALL</c> and then <c>INSTALL\HDSTUFF</c> into <c>INSTALL</c>; those of <c>STUFF</c> into the root;
	/// and for the full size the four programs. A later copy to the same name replaces the earlier, as DOS's <c>copy</c>
	/// does.
	/// </summary>
	/// <param name="size">The size chosen.</param>
	public IReadOnlyList<(string Source, string Destination)> Plan(Size size) {
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

		string archives = GameContent.ArchiveFolderName;
		if (size == Size.Full) {
			AddFolder(archives, archives, name => name.EndsWith(".VOL", StringComparison.OrdinalIgnoreCase));
		} else {
			// The script's copies, in its order.
			foreach (string name in size == Size.Medium
					? new[] { "SIMSOUND.VOL", "SIMVOICE.VOL", "SIMVOL0.VOL" }
					: ["SIMSOUND.VOL", "SIMVOICE.VOL"]) {
				Add($@"{archives}\{name}", $@"{archives}\{name}");
			}
		}

		AddFolder("INSTALL", "INSTALL");
		AddFolder(@"INSTALL\HDSTUFF", "INSTALL");
		AddFolder("STUFF", "");
		// Already there from STUFF; named so that a disc without it is refused before anything is copied.
		Add(ResourceSource, Es1ResourceCfg.FileName.ToUpperInvariant());
		if (size == Size.Full) {
			foreach (string program in new[] { RetailGameDetector.Es1Marker, "DBSIM.EXE", "DETECT.EXE", "ESCD.EXE" }) {
				Add(program, program);
			}
		}

		return files;
	}

	/// <summary>The bytes <see cref="Plan"/> copies for a size (<see cref="DiscCopy.PlanBytes"/>).</summary>
	/// <param name="size">The size chosen.</param>
	public long PlanBytes(Size size) => DiscCopy.PlanBytes(_source, Plan(size));

	/// <summary>
	/// Copies a size's <see cref="Plan"/> into the destination folder (<see cref="DiscCopy.Copy"/>),
	/// then writes its <c>ES.BAT</c> (<see cref="EsBat"/>) and <see cref="AddrBatPath"/> (<see cref="AddrBat"/>), and turns
	/// the copied <c>RESOURCE.CFG</c> into what <c>SOUNDSET</c> and <c>ADDRES</c> leave (<see cref="ResourceCfg"/>). A
	/// failure or a cancellation removes what this wrote and rethrows.
	/// </summary>
	/// <param name="destination">A new or empty folder.</param>
	/// <param name="size">The size chosen.</param>
	/// <param name="progress">Told how far the copy has got.</param>
	/// <param name="cancellation">Stops the copy, which then removes what it wrote.</param>
	/// <exception cref="InvalidOperationException">The destination is refused, a file the plan names is not on the disc,
	/// or there is not room for it.</exception>
	public void Install(string destination, Size size, IProgress<InstallProgress>? progress = null, CancellationToken cancellation = default) =>
		DiscCopy.Copy(_source, destination, Plan(size), (root, written) => WriteConfiguration(root, size, written), progress, cancellation);

	/// <summary>
	/// The <c>ES.BAT</c> that a size's branch writes, each <c>echo</c> line ending CR LF. The minimum and medium
	/// sizes' sets <c>ALT_DRIVE</c> to the hard drive and <c>PREFER_ALT</c>, changes to the CD drive, runs <c>escd</c>
	/// there and changes back; the full size's sets <c>ALT_DRIVE</c> to the CD drive, clears <c>PREFER_ALT</c> and runs
	/// <c>escd</c> in the install.
	/// </summary>
	/// <param name="size">The size chosen.</param>
	public static string EsBat(Size size) => size == Size.Full
		? $"@echo off\r\nmode co80\r\nset ALT_DRIVE={CdDrive}\r\nset PREFER_ALT=\r\nescd\r\n"
		: $"@echo off\r\nmode co80\r\nset ALT_DRIVE={HardDrive}\r\nset PREFER_ALT=1\r\n{CdDrive}\r\nescd\r\n{HardDrive}\r\n";

	/// <summary>
	/// The <c>data\addr.bat</c> the script's <c>:exit</c> writes for every size: <c>echo @echo off &gt;data\addr.bat</c>
	/// and <c>echo install\addres FlicPlayer pathFlic \flic\ %2 resource.cfg &gt;&gt;data\addr.bat</c>, each line keeping
	/// the space before its <c>&gt;</c> and ending CR LF. <c>%2</c> is the CD drive's letter, <see cref="CdDrive"/>'s.
	/// </summary>
	public static string AddrBat() =>
		$"@echo off \r\ninstall\\addres FlicPlayer pathFlic \\flic\\ {CdDrive.TrimEnd(':')} resource.cfg \r\n";

	private static void WriteConfiguration(string root, Size size, List<string> written) {
		string esBat = DiscCopy.Under(root, Es1EsBat.FileName);
		written.Add(esBat);
		File.WriteAllText(esBat, EsBat(size), Encoding.Latin1);

		string addrBat = DiscCopy.Under(root, AddrBatPath);
		written.Add(addrBat);
		File.WriteAllText(addrBat, AddrBat(), Encoding.Latin1);

		string resource = DiscCopy.Under(root, Es1ResourceCfg.FileName.ToUpperInvariant());
		File.WriteAllText(resource, ResourceCfg(File.ReadAllText(resource, Encoding.Latin1)), Encoding.Latin1);
	}

	/// <summary>
	/// The disc's <c>STUFF\RESOURCE.CFG</c>, the text, as the install leaves it: its DOS end-of-file
	/// marks and trailing blank lines dropped, each <see cref="SoundSettings"/> value in its section, and <c>ADDRES</c>'s
	/// <c>[FlicPlayer]</c> <c>pathFlic=</c><see cref="CdDrive"/><c>\flic\</c> after a blank line, every line ending CR LF.
	/// </summary>
	/// <param name="text">The disc's <c>RESOURCE.CFG</c>.</param>
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
