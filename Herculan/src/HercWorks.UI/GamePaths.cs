using HercWorks.Vol;
using HercWorks.Vol.Io;

namespace HercWorks.UI;

/// <summary>
/// Resolves paths inside the configured Earthsiege 2 install (see <see cref="AppSettings"/>) and
/// owns the "where is your game?" prompt.
///
/// <para>Lookups are case-insensitive by enumeration, not by relying on the filesystem: the retail
/// install mixes cases freely (<c>DATA\script.dat</c>, <c>VOL\ZONES.VOL</c>) and while NTFS ignores
/// case today, resolving explicitly keeps this correct on a case-sensitive volume or share and
/// means a caller can name a file in whatever case the docs use.</para>
/// </summary>
public static class GamePaths {
	/// <summary>The file whose presence identifies a folder as an ES2 install.</summary>
	public const string GameExeName = "ES.EXE";

	/// <summary>Distinct per-dialog identity — see CampaignResourcesForm's DialogClientGuid.</summary>
	private static readonly Guid LocateGameClientGuid = new("5f0a9c31-8d47-4b2e-9e15-c6b3a04d7f28");

	public static string? GameDirectory => AppSettings.Current.GameDirectory;

	/// <summary>A directory counts as an install only if it actually holds ES.EXE.</summary>
	public static bool IsGameDirectory(string? directory) =>
		!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
		FindChild(directory!, GameExeName) != null;

	public static bool IsConfigured => IsGameDirectory(GameDirectory);

	/// <summary>
	/// Resolves a path under the game directory, matching each segment case-insensitively. Returns
	/// null if no directory is configured or any segment doesn't exist, so callers can treat "the
	/// default file isn't there" as an ordinary skip rather than an error.
	/// </summary>
	public static string? Resolve(params string[] segments) {
		if (!IsConfigured) {
			return null;
		}

		string current = GameDirectory!;
		foreach (string segment in segments) {
			if (FindChild(current, segment) is not { } child) {
				return null;
			}
			current = child;
		}

		return current;
	}

	/// <summary>
	/// Best-effort starting folder for a file dialog: the named subfolder if it exists, otherwise
	/// the install root, otherwise empty (which leaves the dialog's own last-folder memory alone).
	/// </summary>
	public static string InitialDirectoryFor(params string[] segments) =>
		Resolve(segments) ?? (IsConfigured ? GameDirectory! : string.Empty);

	/// <summary>
	/// Where a SHELL <c>GAM\</c> file can live, in the order the editors should prefer: a loose
	/// override at the install root first (it's what the game itself reads in preference to the
	/// packed copy), then an unpacked SHELL0 tree, then the packed VOL as the always-present
	/// fallback.
	/// </summary>
	private static readonly string[][] GamSearchDirectories = {
		new[] { "GAM" },
		new[] { "VOL", "SHELL0", "GAM" }
	};

	/// <summary>
	/// Where a simulator <c>dat\</c> file can live, in the same preference order as
	/// <see cref="GamSearchDirectories"/>: DBSIM opens <c>dat\name.dat</c> as a loose file before it
	/// searches its volumes (<c>VolRStream_Open</c>, see docs/retail/formats/vol-archive.md), then an unpacked
	/// SIMVOL0 tree, then the packed VOL.
	/// </summary>
	private static readonly string[][] SimDatSearchDirectories = {
		new[] { "DAT" },
		new[] { "VOL", "simvol0", "dat" }
	};

	private const string VolDirectory = "VOL";
	private const string ShellVolName = "SHELL0.VOL";
	private const string SimVolName = "SIMVOL0.VOL";
	private const string GamDirectoryLabel = "GAM";
	private const string SimDatDirectoryLabel = "dat";

	/// <summary>
	/// Parsed VOLs, keyed by path. SHELL0.VOL is ~8 MB and every GAM lookup that falls through to it
	/// would otherwise re-read and re-parse the whole archive.
	/// </summary>
	private static readonly Dictionary<string, Voln> VolCache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Finds a SHELL <c>GAM\</c> file by name over <see cref="GamSearchDirectories"/> and then inside
	/// SHELL0.VOL, or null if no game directory is configured and the file is nowhere in it.
	/// </summary>
	public static GameFile? FindGamFile(string fileName) =>
		FindFile(fileName, GamSearchDirectories, ShellVolName, GamDirectoryLabel);

	/// <summary>
	/// Finds a simulator <c>dat\</c> file by name over <see cref="SimDatSearchDirectories"/> and then
	/// inside SIMVOL0.VOL, or null if no game directory is configured and the file is nowhere in it.
	/// </summary>
	public static GameFile? FindSimDatFile(string fileName) =>
		FindFile(fileName, SimDatSearchDirectories, SimVolName, SimDatDirectoryLabel);

	/// <summary>
	/// Starting folder for a GAM file dialog: the first loose GAM directory that exists, falling back
	/// to the install root (the packed VOL isn't a folder a dialog can open into).
	/// </summary>
	public static string GamInitialDirectory => InitialDirectoryOver(GamSearchDirectories);

	/// <summary>Starting folder for a simulator <c>dat\</c> file dialog, as <see cref="GamInitialDirectory"/>.</summary>
	public static string SimDatInitialDirectory => InitialDirectoryOver(SimDatSearchDirectories);

	private static GameFile? FindFile(string fileName, string[][] searchDirectories, string volName, string dirLabel) {
		foreach (string[] directory in searchDirectories) {
			if (Resolve(directory.Append(fileName).ToArray()) is { } path) {
				return GameFile.FromLooseFile(path);
			}
		}

		if (Resolve(VolDirectory, volName) is not { } volPath) {
			return null;
		}

		// Matched on the VOL's own folder label rather than VolEntry.Dir: the label is the archive's
		// literal directory name, while Dir is a FileType the reader maps it onto and is null for any
		// folder that has no matching enum member.
		var entry = ReadVol(volPath)?.Folders.Values
			.FirstOrDefault(dir => string.Equals(dir.Label, dirLabel, StringComparison.OrdinalIgnoreCase))
			?.Files.FirstOrDefault(file => string.Equals(file.FileName, fileName, StringComparison.OrdinalIgnoreCase));

		return entry == null ? null : GameFile.FromVolEntry(entry, Path.GetFileName(volPath), dirLabel);
	}

	private static string InitialDirectoryOver(string[][] searchDirectories) =>
		searchDirectories.Select(Resolve).FirstOrDefault(path => path != null)
		?? (IsConfigured ? GameDirectory! : string.Empty);

	private static Voln? ReadVol(string volPath) {
		if (VolCache.TryGetValue(volPath, out var cached)) {
			return cached;
		}

		try {
			var vol = VolFileReader.ParseVolFile(volPath);
			VolCache[volPath] = vol;
			return vol;
		} catch (Exception) {
			// An unreadable VOL is just a miss for the caller, same as an absent one.
			return null;
		}
	}

	/// <summary>
	/// Ensures a valid game directory is configured, prompting for ES.EXE if it isn't. Returns
	/// whether one is configured on exit — a cancelled prompt is not fatal, it just leaves the
	/// default-file loading and dialog start folders unavailable until the user sets it later.
	/// </summary>
	public static bool EnsureConfigured(IWin32Window? owner) => IsConfigured || Prompt(owner);

	/// <summary>
	/// Asks for the game executable and stores the folder holding it. Loops on a wrong pick rather
	/// than silently accepting some other .exe's folder as the install.
	/// </summary>
	public static bool Prompt(IWin32Window? owner) {
		Show(owner, "Please locate your Earthsiege 2 installation.",
			MessageBoxButtons.OK, MessageBoxIcon.Information);

		while (true) {
			using var dialog = new OpenFileDialog {
				Filter = $"Earthsiege 2 executable ({GameExeName})|{GameExeName}|Programs (*.exe)|*.exe|All files (*.*)|*.*",
				Title = $"Locate your Earthsiege 2 installation — select {GameExeName}",
				FileName = GameExeName,
				CheckFileExists = true,
				ClientGuid = LocateGameClientGuid,
				InitialDirectory = IsConfigured ? GameDirectory! : string.Empty
			};

			if (dialog.ShowDialog(owner) != DialogResult.OK) {
				return false;
			}

			string picked = dialog.FileName;
			if (!string.Equals(Path.GetFileName(picked), GameExeName, StringComparison.OrdinalIgnoreCase)) {
				if (ShowRetry(owner, $"That's {Path.GetFileName(picked)}, not {GameExeName}.") != DialogResult.Retry) {
					return false;
				}
				continue;
			}

			string directory = Path.GetDirectoryName(picked)!;
			AppSettings.Current.GameDirectory = directory;
			AppSettings.Current.Save();
			return true;
		}
	}

	private static DialogResult ShowRetry(IWin32Window? owner, string message) =>
		Show(owner, $"{message}\n\nSelect {GameExeName} from the folder Earthsiege 2 is installed in.",
			MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);

	/// <summary>
	/// MessageBox with an owner when there is one. An ownerless box is a top-level window of its own,
	/// which is what lets the app fall behind whatever it was launched from once the box closes.
	/// </summary>
	private static DialogResult Show(IWin32Window? owner, string text, MessageBoxButtons buttons, MessageBoxIcon icon) {
		const string title = "Locate Earthsiege 2";
		return owner == null
			? MessageBox.Show(text, title, buttons, icon)
			: MessageBox.Show(owner, text, title, buttons, icon);
	}

	/// <summary>
	/// Case-insensitive single-level lookup. Tries the exact name first so the common case costs one
	/// filesystem probe instead of a full directory enumeration.
	/// </summary>
	private static string? FindChild(string directory, string name) {
		string direct = Path.Combine(directory, name);
		if (File.Exists(direct) || Directory.Exists(direct)) {
			return direct;
		}

		try {
			return Directory.EnumerateFileSystemEntries(directory)
				.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
		} catch (Exception) {
			// An unreadable or vanished directory is just "not found" as far as callers care.
			return null;
		}
	}
}
