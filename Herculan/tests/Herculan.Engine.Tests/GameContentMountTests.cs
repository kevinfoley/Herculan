using System.Text;
using HercWorks.Vol;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="GameContent.MountInstall"/>'s scan over the install and the <c>drive.cfg</c> directory, on
/// hand-built archives, and what it mounts of the v1.10 test installs when they are present.
/// </summary>
public class GameContentMountTests : IDisposable {
	private readonly string _root = Directory.CreateTempSubdirectory("herculan-mount-").FullName;
	private string Install => Path.Combine(_root, "install");
	private string Disc => Path.Combine(_root, "disc");

	public void Dispose() => Directory.Delete(_root, recursive: true);

	[Fact]
	public void ScansTheInstallFirstAndSkipsADiscArchiveOfTheSameName() {
		WriteVol(Install, "A.VOL", Voln.DbsimProgram, 0x05, ("X.DAT", "install"));
		WriteVol(Disc, "A.VOL", Voln.DbsimProgram, 0x05, ("X.DAT", "disc"));
		WriteVol(Disc, "B.VOL", Voln.DbsimProgram, 0x05, ("X.DAT", "b"), ("Y.DAT", "b"));
		WriteDriveCfg(Disc);

		var content = GameContent.MountSimulator(Install);

		Assert.Equal(new[] { "A.VOL", "B.VOL" }, content.MountedArchives);
		Assert.Equal("install", Text(content, "X.DAT"));
		Assert.Equal("b", Text(content, "Y.DAT"));
	}

	[Fact]
	public void LoadsOnlyArchivesWhoseMaskSharesTheProgramsBit() {
		WriteVol(Install, "SIM.VOL", Voln.DbsimProgram, 0x05, ("S.DAT", "sim"));
		WriteVol(Install, "SHELL.VOL", Voln.VshellProgram, 0x05, ("H.DAT", "shell"));
		WriteVol(Install, "BOTH.VOL", Voln.DbsimProgram | Voln.VshellProgram, 0x05, ("Z.DAT", "both"));

		Assert.Equal(new[] { "BOTH.VOL", "SIM.VOL" }, GameContent.MountSimulator(Install).MountedArchives);
		Assert.Equal(new[] { "BOTH.VOL", "SHELL.VOL" }, GameContent.MountShell(Install).MountedArchives);
	}

	[Fact]
	public void SearchesHigherPrecedenceFirstWhereverItWasFound() {
		WriteVol(Install, "BASE.VOL", Voln.DbsimProgram, 0x05, ("X.DAT", "base"));
		WriteVol(Disc, "PATCH.VOL", Voln.DbsimProgram, 0x0A, ("X.DAT", "patch"));
		WriteDriveCfg(Disc);

		var content = GameContent.MountSimulator(Install);

		Assert.Equal(new[] { "PATCH.VOL", "BASE.VOL" }, content.MountedArchives);
		Assert.Equal("patch", Text(content, "X.DAT"));
	}

	[Fact]
	public void WithoutDriveCfgMountsTheInstallAlone() {
		WriteVol(Install, "A.VOL", Voln.DbsimProgram, 0x05, ("X.DAT", "install"));
		WriteVol(Disc, "B.VOL", Voln.DbsimProgram, 0x05, ("Y.DAT", "disc"));

		Assert.Equal(new[] { "A.VOL" }, GameContent.MountSimulator(Install).MountedArchives);
	}

	[Fact]
	public void DiscFilePrefersTheDiscAndFallsBackToTheInstall() {
		WriteFile(Path.Combine(Disc, "AVI", "PT1.AVI"), "disc");
		WriteFile(Path.Combine(Install, "AVI", "PT1.AVI"), "install");
		WriteFile(Path.Combine(Install, "AVI", "PT2.AVI"), "install");
		WriteDriveCfg(Disc);

		using var disc = GameInstall.OpenDisc(Install);
		Assert.Equal("disc"u8.ToArray(), GameInstall.ReadDiscFile(Install, disc, Path.Combine("AVI", "PT1.AVI"), 100));
		Assert.Equal("install"u8.ToArray(), GameInstall.ReadDiscFile(Install, disc, Path.Combine("AVI", "PT2.AVI"), 100));
		Assert.Null(GameInstall.ReadDiscFile(Install, disc, Path.Combine("AVI", "PT3.AVI"), 100));
	}

	/// <summary>
	/// A v1.10 French install holds only its own voice archive; the English and German ones, and
	/// everything a Minimum install leaves behind, are mounted from the disc.
	/// </summary>
	[Fact]
	public void MountsAV110InstallWithItsDisc() {
		if (FindTestInstall("Install-FRE") is not { } install) {
			return;
		}

		var simulator = GameContent.MountSimulator(install).MountedArchives;
		Assert.Equal("SIMPATCH.VOL", simulator[0]);
		Assert.Equal(
			new[] { "SIMALERT.VOL", "SIMLANG.VOL", "SIMPATCH.VOL", "SIMSOUND.VOL", "SIMVOICE.VOL", "SIMVOICF.VOL",
				"SIMVOICG.VOL", "SIMVOL0.VOL", "ZONES.VOL" },
			simulator.Order(StringComparer.OrdinalIgnoreCase));

		var shell = GameContent.MountShell(install).MountedArchives;
		Assert.Equal("SHELL1.VOL", shell[0]);
		Assert.Equal(new[] { "LANG0.VOL", "SHELL0.VOL", "SHELL1.VOL", "SHLSOUND.VOL", "ZONES.VOL" },
			shell.Order(StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>A Minimum install leaves the main archives on the disc, and the shell and simulator still find them.</summary>
	[Fact]
	public void MountsAV110MinimumInstallFromItsDisc() {
		if (FindTestInstall("Install-ENG-Min") is not { } install) {
			return;
		}

		var simulator = GameContent.MountSimulator(install);
		Assert.Contains("SIMVOL0.VOL", simulator.MountedArchives);
		Assert.Contains("ZONES.VOL", simulator.MountedArchives);
		Assert.False(File.Exists(Path.Combine(GameInstall.ArchiveDirectory(install), "SIMVOL0.VOL")));

		var shell = GameContent.MountShell(install);
		Assert.Equal(new[] { "LANG0.VOL", "SHELL0.VOL", "SHELL1.VOL", "SHLSOUND.VOL", "ZONES.VOL" },
			shell.MountedArchives.Order(StringComparer.OrdinalIgnoreCase));
		Assert.NotNull(shell.Read("ENG", "ESTEXT.BIN"));
	}

	// ES2v110\ beside the repo, as tools/scripts/es2_build_v110_installs.py builds it; null when absent.
	private static string? FindTestInstall(string name) {
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
			string candidate = Path.Combine(directory.FullName, "ES2v110", name);
			if (GameInstall.IsInstallRoot(candidate)) {
				return candidate;
			}
		}

		return null;
	}

	private static string? Text(GameContent content, string name) =>
		content.Read("DAT", name) is { } bytes ? Encoding.ASCII.GetString(bytes) : null;

	private void WriteDriveCfg(string disc) =>
		WriteFile(Path.Combine(Install, "DATA", "drive.cfg"), $"{disc}\r\n{Install}");

	private static void WriteFile(string path, string text) {
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, text);
	}

	/// <summary>
	/// A one-folder (<c>DAT</c>) archive in <paramref name="root"/>\VOL, laid out as
	/// docs/retail/formats/vol-archive.md describes: header, folder list, entry list, then each entry's
	/// nine-byte prefix, content and trailer byte.
	/// </summary>
	private static void WriteVol(string root, string fileName, uint mask, byte precedence,
			params (string Name, string Text)[] files) {
		byte[] folders = Encoding.ASCII.GetBytes("DAT\\\0");
		var bytes = new List<byte>();
		bytes.AddRange("VOLN"u8.ToArray());
		bytes.AddRange(BitConverter.GetBytes(mask));
		bytes.Add(precedence);
		bytes.Add(1);
		bytes.AddRange(BitConverter.GetBytes((ushort)folders.Length));
		bytes.AddRange(folders);
		bytes.AddRange(BitConverter.GetBytes((ushort)files.Length));
		bytes.AddRange(BitConverter.GetBytes(files.Length * 18));

		int offset = bytes.Count + files.Length * 18;
		var data = new List<byte>();
		foreach (var (name, text) in files) {
			byte[] nameBytes = new byte[13];
			Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
			bytes.AddRange(nameBytes);
			bytes.Add(0);
			bytes.AddRange(BitConverter.GetBytes(offset + data.Count));

			byte[] content = Encoding.ASCII.GetBytes(text);
			data.Add(0x02);
			data.AddRange(BitConverter.GetBytes(content.Length));
			data.AddRange(new byte[4]);
			data.AddRange(content);
			data.Add(content[^1]);
		}

		bytes.AddRange(data);
		string path = Path.Combine(root, GameInstall.ArchiveFolderName, fileName);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, bytes.ToArray());
	}
}
