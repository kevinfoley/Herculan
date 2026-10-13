using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Disc;
using HercWorks.Vol;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The disc image support against the retail data beside the repo, each test returning at once when its files are
/// absent: <see cref="RetailInstaller"/> against the v1.10 installs <c>tools/scripts/es2_build_v110_installs.py</c>
/// builds, archives mounted from the v1.10 image against the same disc's files, and
/// <see cref="ImageMusicSource"/>'s silence split against the lengths docs/retail/retail-builds.md measured.
/// </summary>
public class DiscImageInstallTests : IDisposable {
	private const string V110Image = "EarthSiege2_Freeware_GoldGames_1r11_withAudio.iso";

	private readonly string _root = Directory.CreateTempSubdirectory("herculan-install-").FullName;

	public void Dispose() => Directory.Delete(_root, recursive: true);

	/// <summary>
	/// What the installer leaves is, file for file and byte for byte, what the v1.10 script leaves — but for
	/// <c>drive.cfg</c>, which names the disc the install was made from.
	/// </summary>
	[Theory]
	[InlineData("Install-ENG", RetailInstaller.Language.English, RetailInstaller.Size.Maximum, false)]
	[InlineData("Install-FRE", RetailInstaller.Language.French, RetailInstaller.Size.Maximum, false)]
	[InlineData("Install-GER", RetailInstaller.Language.German, RetailInstaller.Size.Maximum, false)]
	[InlineData("Install-ENG-Min", RetailInstaller.Language.English, RetailInstaller.Size.Minimum, false)]
	[InlineData("Install-ENG-Min", RetailInstaller.Language.English, RetailInstaller.Size.Minimum, true)]
	public void InstallsWhatTheV110ScriptInstalls(string oracleName, RetailInstaller.Language language, RetailInstaller.Size size, bool fromImage) {
		if (FindBesideRepo(Path.Combine("ES2v110", oracleName)) is not { } oracle
				|| FindBesideRepo(Path.Combine("ES2v110", "CD")) is not { } folder
				|| (fromImage ? FindBesideRepo(V110Image) : folder) is not { } sourcePath) {
			return;
		}

		using var source = fromImage ? GameDisc.OpenImage(sourcePath) : GameDisc.OpenFolder(sourcePath);
		var installer = RetailInstaller.Identify(source, out _, out _);
		Assert.NotNull(installer);
		Assert.Equal(RetailInstaller.RetailBuild.V110, installer.Build);

		string install = Path.Combine(_root, "install");
		installer.Install(install, size, language, discFiles: false);

		// An oracle install that has been played holds saves, and the game has rewritten its preferences and save
		// list; those two are checked against the disc's own copies instead.
		var expected = Files(oracle)
			.Where(file => !IsSave(file.Key) || file.Key.Equals(SaveList, StringComparison.OrdinalIgnoreCase))
			.ToDictionary(StringComparer.OrdinalIgnoreCase);
		var actual = Files(install);
		Assert.Equal(expected.Keys.Order(StringComparer.OrdinalIgnoreCase), actual.Keys.Order(StringComparer.OrdinalIgnoreCase),
			StringComparer.OrdinalIgnoreCase);
		foreach (var (relative, path) in expected) {
			if (relative.EndsWith(Drive.FileName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			string reference = relative.Equals(SaveList, StringComparison.OrdinalIgnoreCase)
					|| relative.Equals(Preferences, StringComparison.OrdinalIgnoreCase)
				? Path.Combine(folder, relative)
				: path;
			Assert.True(File.ReadAllBytes(reference).AsSpan().SequenceEqual(File.ReadAllBytes(actual[relative])), relative);
		}

		var drive = new DriveTransformer().Parse(File.ReadAllBytes(GameInstall.DriveCfgPath(install)))!;
		Assert.Equal(Path.GetFullPath(install), drive.InstallDirectory);
		Assert.Equal(fromImage ? "." : Path.GetFullPath(folder), drive.Directory);
		Assert.Equal(fromImage ? Path.GetFullPath(sourcePath) : null, drive.DiscImage);
	}

	/// <summary>
	/// The disc files a v1.10 install adds are every file of <c>AVI</c>, of the language's intro and voice folders, and
	/// its language folder's manual, whether the disc is the folder or the image.
	/// </summary>
	[Theory]
	[InlineData(RetailInstaller.Language.English, false)]
	[InlineData(RetailInstaller.Language.French, false)]
	[InlineData(RetailInstaller.Language.German, true)]
	public void AddsTheV110DiscFilesForTheLanguage(RetailInstaller.Language language, bool fromImage) {
		if (FindBesideRepo(Path.Combine("ES2v110", "CD")) is not { } folder
				|| (fromImage ? FindBesideRepo(V110Image) : folder) is not { } sourcePath) {
			return;
		}

		using var source = fromImage ? GameDisc.OpenImage(sourcePath) : GameDisc.OpenFolder(sourcePath);
		var installer = RetailInstaller.Identify(source, out _, out _)!;
		string letter = ((char)language).ToString();
		string[] folders = language == RetailInstaller.Language.English ? ["AVI", "SIMVOICE"] : ["AVI", "AV" + letter, "SIMVOIC" + letter];
		var expected = folders
			.SelectMany(name => Directory.GetFiles(Path.Combine(folder, name)).Select(file => $"{name}/{Path.GetFileName(file)}"))
			.Append($"{RetailInstaller.LanguageFolder(language).Folder}/{RetailInstaller.ManualFileName}")
			.Order(StringComparer.OrdinalIgnoreCase);

		var without = installer.Plan(RetailInstaller.Size.Minimum, language, discFiles: false);
		var added = installer.Plan(RetailInstaller.Size.Minimum, language, discFiles: true).Except(without).ToList();
		Assert.All(added, file => Assert.Equal(file.Source, file.Destination));
		Assert.Equal(expected, added.Select(file => file.Source).Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The v1.10 image read in place holds the same archives as the disc's files, and a Minimum install mounts the
	/// same ones from either.
	/// </summary>
	[Fact]
	public void MountsTheSameArchivesFromTheV110ImageAsFromItsFiles() {
		if (FindBesideRepo(V110Image) is not { } imagePath
				|| FindBesideRepo(Path.Combine("ES2v110", "CD")) is not { } folderPath
				|| FindBesideRepo(Path.Combine("ES2v110", "Install-ENG-Min")) is not { } install) {
			return;
		}

		using var image = GameDisc.OpenImage(imagePath);
		using var folder = GameDisc.OpenFolder(folderPath);
		var names = folder.ArchiveNames().Order(StringComparer.OrdinalIgnoreCase).ToList();
		Assert.Equal(names, image.ArchiveNames().Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
		foreach (string name in names) {
			string relative = Path.Combine(GameInstall.ArchiveFolderName, name);
			Assert.True(folder.ReadAllBytes(relative, long.MaxValue).AsSpan()
				.SequenceEqual(image.ReadAllBytes(relative, long.MaxValue)), name);
		}

		foreach (uint program in new[] { Voln.DbsimProgram, Voln.VshellProgram }) {
			Assert.Equal(GameContent.MountInstall(install, folder, program).MountedArchives,
				GameContent.MountInstall(install, image, program).MountedArchives);
		}
	}

	/// <summary>
	/// The v1.10 image's audio, which keeps no track boundaries, splits into the six pieces docs/retail/retail-builds.md
	/// ("The v1.10 disc image") measures, each from the start of its music to the next's.
	/// </summary>
	[Fact]
	public void SplitsTheV110ImageAudioIntoItsSixPieces() {
		if (FindBesideRepo(V110Image) is not { } imagePath) {
			return;
		}

		using var image = DiscImage.Open(imagePath);
		var layout = ImageMusicSource.ReadLayout(image);

		Assert.True(layout.Estimated);
		Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, layout.Tracks.Select(track => track.Number));
		Assert.Equal(new[] { 12390, 10864, 12387, 13061, 13001, 10941 }, layout.Tracks.Select(track => track.SectorCount));
	}

	private static readonly string SaveList = Path.Combine("SAV", "GAMEFILE.STR");
	private static readonly string Preferences = Path.Combine("DATA", "PREFS.CFG");

	private static bool IsSave(string relative) => relative.StartsWith("SAV" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

	// Every file under root, by its path relative to it.
	private static Dictionary<string, string> Files(string root) =>
		Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.ToDictionary(path => Path.GetRelativePath(root, path), path => path, StringComparer.OrdinalIgnoreCase);

	// A file or folder at the repo root, found by walking up from the test binary; null when absent.
	private static string? FindBesideRepo(string name) {
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent) {
			string candidate = Path.Combine(directory.FullName, name);
			if (File.Exists(candidate) || Directory.Exists(candidate)) {
				return candidate;
			}
		}

		return null;
	}
}
