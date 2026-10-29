using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The form a custom mission is kept in.</summary>
public enum CustomMissionForm {
	/// <summary>A loose <c>.MSN</c> and its text in the install's mission folder (<see cref="SavedMissions"/>).</summary>
	Loose,

	/// <summary>A <c>.hcm</c> bundle in the install's mission folder (<see cref="MissionBundle"/>).</summary>
	Bundle,

	/// <summary>A <c>.hcm</c> bundle that comes with HERCULAN (<see cref="CustomMissions.IncludedFolder"/>), never deleted.</summary>
	Included,
}

/// <summary>
/// One mission the shell's Custom Missions window lists. <see cref="Name"/> is its <c>.MSN</c>'s name without the
/// extension; <see cref="Path"/> the file it is kept in, the <c>.MSN</c> itself or the bundle. <see cref="About"/> is its
/// about file, beside the <c>.MSN</c> or in the bundle, or null without one. <see cref="Problem"/> is why the mission cannot be played, or null when it can.
/// </summary>
public sealed record CustomMission(string Name, string Path, CustomMissionForm Form, MissionAbout? About, string? Problem);

/// <summary>A bundle read for import, before it is written: its mission's name, where it goes, its bytes, and whether a file is there already.</summary>
public sealed record CustomMissionImport(string Name, string Destination, byte[] Bytes, bool Replaces);

/// <summary>
/// The missions the shell's Custom Missions window offers, and what it does with them: every loose <c>.MSN</c> in the
/// install's mission folder, as the mission editor saves them, every <c>.hcm</c> bundle there, and every bundle that comes
/// with HERCULAN (<see cref="IncludedFolder"/>). The window, the bundle and this whole path are this engine's: retail plays
/// a mission only through a career.
///
/// <para>An included mission is also found by name where the archives and the install have none of that name
/// (<see cref="FindIncluded"/>), so the mission editor opens it, and Play and the game's command line play it, as they
/// do an archive mission; a mission the editor saves under its name goes into the install and is found first.</para>
///
/// <para>A mission plays as the mission editor's Play plays one, on its own theater and time of day
/// (<see cref="MissionFileLaunch.Write(GameContent, string, string, string, bool, out string?)"/> with its own world): a
/// loose one by its name, read through the shell's mount as any mission is, so one that takes a career mission's name
/// plays as that career mission, on its stage's theater; and a bundle from the bytes it holds, read
/// again as it is played, loaded as a mission <c>gam\career.dat</c> does not list
/// (<see cref="MissionFileLaunch.WriteHeld"/>). A bundle's text is the shell language's, else English, else the first
/// it holds. Nothing in a bundle is ever extracted to disk.</para>
/// </summary>
public static class CustomMissions {
	/// <summary>The extensions a mission's text takes, one per language the shell runs in.</summary>
	public static IReadOnlyList<string> TextExtensions { get; } =
		Enum.GetValues<GameLanguage>().Select(ShellTrainingLaunch.MissionTextExtension).Distinct().ToList();

	/// <summary>The install's mission folder.</summary>
	public static string Folder(string installRoot) => CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder);

	/// <summary>The folder beside the executable that holds the mission bundles HERCULAN comes with.</summary>
	public static string IncludedFolder => Path.Combine(AppContext.BaseDirectory, "Missions");

	/// <summary>The names of the missions HERCULAN comes with, as their bundles name them; those that do not read are left out.</summary>
	public static IReadOnlyList<string> IncludedNames() =>
		Bundles(IncludedFolder).Select(path => ReadBundle(path, out _)?.Name).OfType<string>().ToList();

	/// <summary>The bundle HERCULAN comes with whose mission is named <paramref name="name"/> (no extension, any case), or null.</summary>
	public static MissionBundle? FindIncluded(string name) =>
		Bundles(IncludedFolder).Select(path => ReadBundle(path, out _))
			.FirstOrDefault(bundle => bundle != null && bundle.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// What mission <paramref name="name"/>'s about file says, from the install's saved files when it has them, else from the
	/// bundle HERCULAN comes with of that name; null with neither.
	/// </summary>
	public static MissionAbout? About(string installRoot, string name) =>
		SavedMissions.Exists(installRoot, name) ? SavedMissions.ReadAbout(installRoot, name) : FindIncluded(name)?.About;

	/// <summary>
	/// Mission <paramref name="name"/>'s own zone, from the install's saved files when it has them
	/// (<see cref="SavedMissions.ReadZone"/>, which gives the reason in <paramref name="failure"/> when they do not read), else
	/// from the bundle HERCULAN comes with of that name; null without one.
	/// </summary>
	public static ZoneFiles? Zone(string installRoot, string name, out string? failure) {
		failure = null;
		return SavedMissions.Exists(installRoot, name) ? SavedMissions.ReadZone(installRoot, name, out failure) : FindIncluded(name)?.Zone;
	}

	/// <summary>Every custom mission in the install and every one HERCULAN comes with, by name, with what keeps each from being played.</summary>
	public static IReadOnlyList<CustomMission> List(string installRoot) {
		var missions = new List<CustomMission>();
		foreach (string file in SavedMissions.List(installRoot)) {
			string path = CaseInsensitivePath.Combine(Folder(installRoot), file);
			string name = Path.GetFileNameWithoutExtension(file);
			string? problem = SavedMissions.WhyNotName(name) ?? WhyNotReadable(path) ?? WhyNotZone(installRoot, name);
			missions.Add(new CustomMission(name, path, CustomMissionForm.Loose, SavedMissions.ReadAbout(installRoot, name), problem));
		}

		AddBundles(Folder(installRoot), CustomMissionForm.Bundle);
		AddBundles(IncludedFolder, CustomMissionForm.Included);
		return missions
			.OrderBy(mission => mission.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(mission => mission.Form)
			.ToList();

		void AddBundles(string folder, CustomMissionForm form) {
			foreach (string path in Bundles(folder)) {
				missions.Add(ReadBundle(path, out string? failure) is { } bundle
					? new CustomMission(bundle.Name, path, form, bundle.About, WhyNotPlayable(bundle))
					: new CustomMission(Path.GetFileNameWithoutExtension(path), path, form, null, failure));
			}
		}
	}

	/// <summary>
	/// The files <see cref="Delete"/> removes for <paramref name="mission"/>: none for one HERCULAN comes with; a bundle's own
	/// file; a loose mission's <c>.MSN</c>, its text in every language, its about file, its own zone's header and heightmap,
	/// and the mission editor's backup of each (<see cref="SavedMissions.BackupExtension"/>), those that exist.
	/// </summary>
	public static IReadOnlyList<string> Files(string installRoot, CustomMission mission) {
		if (mission.Form == CustomMissionForm.Included) {
			return [];
		}

		if (mission.Form == CustomMissionForm.Bundle) {
			return [mission.Path];
		}

		string folder = Folder(installRoot);
		return new[] { MissionFileLaunch.MissionExtension }.Concat(TextExtensions)
			.Concat([MissionAbout.Extension, ZoneFiles.HeaderExtension, ZoneFiles.HeightmapExtension])
			.Select(extension => CaseInsensitivePath.Combine(folder, mission.Name + extension))
			.SelectMany(path => new[] { path, path + SavedMissions.BackupExtension })
			.Where(File.Exists)
			.ToList();
	}

	/// <summary>Deletes <paramref name="mission"/>'s <see cref="Files"/>, and returns them.</summary>
	public static IReadOnlyList<string> Delete(string installRoot, CustomMission mission) {
		var files = Files(installRoot, mission);
		foreach (string file in files) {
			File.Delete(file);
		}

		return files;
	}

	/// <summary>
	/// The bundle at <paramref name="source"/>, read and checked for import into the install's mission folder under its own
	/// file name, or null with the reason it cannot be: it is not a <c>.hcm</c>, is already in that folder, or is not a
	/// bundle that could be played. The bytes checked are the bytes <see cref="Import"/> writes, so the file changing
	/// meanwhile changes nothing.
	/// </summary>
	public static CustomMissionImport? ReadImport(string installRoot, string source, out string? failure) {
		string file = Path.GetFileName(source);
		if (!Path.GetExtension(file).Equals(MissionBundle.Extension, StringComparison.OrdinalIgnoreCase)) {
			failure = $"{file} is not a {MissionBundle.Extension} mission bundle.";
			return null;
		}

		string destination = CaseInsensitivePath.Combine(Folder(installRoot), file);
		if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) {
			failure = $"{file} is already in the install's {MissionFileLaunch.MissionFolder} folder.";
			return null;
		}

		if (MissionBundle.ReadFile(source, out failure) is not { } bytes
				|| MissionBundle.Read(bytes, TextExtensions, out failure) is not { } bundle) {
			return null;
		}

		if (WhyNotPlayable(bundle) is { } problem) {
			failure = problem;
			return null;
		}

		return new CustomMissionImport(bundle.Name, destination, bytes, File.Exists(destination));
	}

	/// <summary>Writes the bundle <paramref name="import"/> read into the install's mission folder, creating the folder.</summary>
	public static void Import(CustomMissionImport import) {
		Directory.CreateDirectory(Path.GetDirectoryName(import.Destination)!);
		File.WriteAllBytes(import.Destination, import.Bytes);
	}

	/// <summary>
	/// Writes the handoff for <paramref name="mission"/> into <paramref name="directory"/>, on the mission's own theater and
	/// time of day, and returns its <c>script.dat</c>, or null with the reason. <paramref name="shellContent"/> is VSHELL's
	/// mount of the install. A mission's file is anyone's, so a load it trips up is a failure to report, not the shell's crash.
	/// </summary>
	public static string? WriteHandoff(GameContent shellContent, string installRoot, CustomMission mission, string directory,
			out string? failure) {
		try {
			Directory.CreateDirectory(directory);
			MissionFileLaunch.ClearHandoff(directory);
			if (mission.Form == CustomMissionForm.Loose) {
				return MissionFileLaunch.Write(shellContent, installRoot, mission.Name, directory, ownWorld: true, out failure);
			}

			if (ReadBundle(mission.Path, out failure) is not { } bundle) {
				return null;
			}

			if (WhyNotPlayable(bundle) is { } problem) {
				failure = problem;
				return null;
			}

			return MissionFileLaunch.WriteHeld(shellContent, installRoot, bundle.Name, bundle.Msn, Text(bundle, shellContent.Language),
				directory, ownWorld: true, out failure);
		} catch (Exception ex) when (IsLoadFailure(ex)) {
			failure = ex.Message;
			return null;
		}
	}

	/// <summary>The text the shell running in <paramref name="language"/> reads from <paramref name="bundle"/>: its language's, else English, else the first held.</summary>
	public static byte[]? Text(MissionBundle bundle, GameLanguage language) =>
		bundle.Text(ShellTrainingLaunch.MissionTextExtension(language))
		?? bundle.Text(ShellTrainingLaunch.MissionTextExtension(GameLanguage.English))
		?? bundle.TextExtensions.Select(bundle.Text).FirstOrDefault();

	// Every .hcm in <folder>.
	private static IEnumerable<string> Bundles(string folder) {
		if (!Directory.Exists(folder)) {
			return [];
		}

		var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
		return Directory.GetFiles(folder, "*" + MissionBundle.Extension, options)
			.Where(path => Path.GetExtension(path).Equals(MissionBundle.Extension, StringComparison.OrdinalIgnoreCase));
	}

	private static MissionBundle? ReadBundle(string path, out string? failure) => MissionBundle.Read(path, TextExtensions, out failure);

	// Why a bundle that reads cannot be played: its mission's name is not one a saved mission can have, or its .MSN does not parse.
	private static string? WhyNotPlayable(MissionBundle bundle) =>
		SavedMissions.WhyNotName(bundle.Name) is { } why
			? $"{bundle.Name}{MissionFileLaunch.MissionExtension}: {why}"
			: WhyNotMission(bundle.Msn);

	// Why the loose .MSN at <path> cannot be played: it cannot be read, or does not parse.
	private static string? WhyNotReadable(string path) {
		try {
			return WhyNotMission(File.ReadAllBytes(path));
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return ex.Message;
		}
	}

	// Why loose mission <name>'s own zone cannot be played, when it has one.
	private static string? WhyNotZone(string installRoot, string name) {
		SavedMissions.ReadZone(installRoot, name, out string? failure);
		return failure;
	}

	// Why <msn> is not a mission file: the reader comes to its end early or reads a count it cannot hold.
	private static string? WhyNotMission(byte[] msn) {
		try {
			return new MissionFileTransformer().Parse(msn) == null ? "The mission file is empty." : null;
		} catch (Exception ex) when (IsLoadFailure(ex)) {
			return $"The mission file cannot be read: {ex.Message}";
		}
	}

	// What reading or loading a malformed mission throws: running off the end of its bytes, a negative or oversized count, or
	// a file that cannot be read or written.
	private static bool IsLoadFailure(Exception ex) =>
		ex is IOException or UnauthorizedAccessException or InvalidDataException or IndexOutOfRangeException or ArgumentException
			or OverflowException;
}
