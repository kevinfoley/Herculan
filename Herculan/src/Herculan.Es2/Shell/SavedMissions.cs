using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es2.Content;

namespace Herculan.Es2.Shell;

/// <summary>
/// Missions the editor saves into the install: content-only loose files <c>MSN\&lt;NAME&gt;.MSN</c> and its text,
/// which <see cref="GameContent"/>, like retail's <c>VolRStream_Open</c>, reads in place of an archive's entry of that
/// name (docs/retail/formats/vol-archive.md#loose-files-on-disk-carry-no-prefix). The text file takes the extension
/// <c>Msn_LoadEngText</c> looks for in the shell's language (<see cref="ShellCareerMissions.ReadMission"/>), so the shell
/// that reads the mission finds it. A name no archive holds loads as <see cref="MissionFileLaunch"/> loads any mission
/// <c>gam\career.dat</c> does not list. Beside them goes the mission's about file (<see cref="MissionAbout"/>), which
/// only the editor and the Custom Missions window read, and the header and heightmap of the mission's own zone when it has
/// one (<see cref="ZoneFiles"/>), named for the mission, never in the install's <c>DAT\</c> and <c>DBA\</c> folders, where
/// a loose zone stands in for an archive's in every mission (docs/retail/formats/zone-terrain.md#loose-zone-files).
///
/// <para>A write first copies each file it replaces to <c>&lt;file&gt;.bak</c>, one generation, overwritten by the
/// next write. That backup, and the whole save path, is this engine's: retail writes no mission file.</para>
/// </summary>
public static class SavedMissions {
	/// <summary>What a replaced file's copy is named after: the file's own name, this appended.</summary>
	public const string BackupExtension = ".bak";

	/// <summary>The longest name a mission can be saved under: a DOS 8.3 file name's stem.</summary>
	public const int MaxNameLength = 8;

	/// <summary>
	/// Why a name cannot name a saved mission, or null when it can: one to
	/// <see cref="MaxNameLength"/> characters, each an ASCII letter, a digit or <c>_</c>.
	/// </summary>
	/// <param name="name">The name to check.</param>
	public static string? WhyNotName(string name) {
		if (name.Length == 0) {
			return "Type a name.";
		}

		if (name.Length > MaxNameLength) {
			return $"A mission name is at most {MaxNameLength} characters.";
		}

		return name.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
			? null
			: "A mission name holds only letters, digits and _.";
	}

	/// <summary>The loose <c>.MSN</c> of a mission under the install.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name (no extension).</param>
	public static string MissionPath(string installRoot, string name) =>
		CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder, name + MissionFileLaunch.MissionExtension);

	/// <summary>The loose text file of a mission, as a shell running in a language reads it.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	/// <param name="language">The shell's language.</param>
	public static string TextPath(string installRoot, string name, GameLanguage language) =>
		CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder,
			name + ShellCareerMissions.MissionTextExtension(language));

	/// <summary>The about file of a mission under the install.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	public static string AboutPath(string installRoot, string name) =>
		CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder, name + MissionAbout.Extension);

	/// <summary>
	/// What a mission's about file says, or null when it has none, or one that cannot be read or is
	/// larger than <see cref="MissionAbout.MaxSize"/>.
	/// </summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	public static MissionAbout? ReadAbout(string installRoot, string name) {
		string path = AboutPath(installRoot, name);
		try {
			return File.Exists(path) && new FileInfo(path).Length <= MissionAbout.MaxSize
				? MissionAbout.Parse(File.ReadAllBytes(path))
				: null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>The header of a mission's own zone under the install.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	public static string ZoneHeaderPath(string installRoot, string name) =>
		CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder, name + ZoneFiles.HeaderExtension);

	/// <summary>The heightmap of a mission's own zone under the install.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	public static string ZoneHeightmapPath(string installRoot, string name) =>
		CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder, name + ZoneFiles.HeightmapExtension);

	/// <summary>
	/// A mission's own zone, or null with the reason the pair is not a zone, prefixed with the file it is about: one file of
	/// the two missing, a file larger than a zone's or one that cannot be read, or <see cref="ZoneFiles.Read"/>'s reason,
	/// prefixed with both. A mission with neither file has no zone and no reason.
	/// </summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	/// <param name="failure">The reason the pair is not a zone, or null when it is one or the mission has neither file.</param>
	public static ZoneFiles? ReadZone(string installRoot, string name, out string? failure) {
		string headerPath = ZoneHeaderPath(installRoot, name);
		string heightmapPath = ZoneHeightmapPath(installRoot, name);
		bool hasHeader = File.Exists(headerPath);
		bool hasHeightmap = File.Exists(heightmapPath);
		failure = null;
		if (!hasHeader && !hasHeightmap) {
			return null;
		}

		if (!hasHeader || !hasHeightmap) {
			var (missing, present) = hasHeader ? (heightmapPath, headerPath) : (headerPath, heightmapPath);
			failure = $"{Path.GetFileName(missing)}: There is no such file beside {Path.GetFileName(present)}.";
			return null;
		}

		if (ReadZoneFile(headerPath, ZoneDatTransformer.Size, out failure) is not { } header
				|| ReadZoneFile(heightmapPath, ZoneFiles.MaxHeightmapSize, out failure) is not { } heightmap) {
			return null;
		}

		if (ZoneFiles.Read(header, heightmap, out string? why) is not { } zone) {
			failure = $"{Path.GetFileName(headerPath)} and {Path.GetFileName(heightmapPath)}: {why}";
			return null;
		}

		return zone;
	}

	// The bytes of the zone file at <path>, or null with the reason: it is over <maxSize> bytes, or cannot be read.
	private static byte[]? ReadZoneFile(string path, int maxSize, out string? failure) {
		string file = Path.GetFileName(path);
		try {
			long size = new FileInfo(path).Length;
			if (size > maxSize) {
				failure = $"{file}: The file is {size} bytes; a zone's is at most {maxSize}.";
				return null;
			}

			failure = null;
			return File.ReadAllBytes(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			failure = $"{file}: {ex.Message}";
			return null;
		}
	}

	/// <summary>Whether the install has a loose <c>.MSN</c> for a mission.</summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	public static bool Exists(string installRoot, string name) => File.Exists(MissionPath(installRoot, name));

	/// <summary>Every loose <c>.MSN</c> in the install's mission folder, by file name.</summary>
	public static IReadOnlyList<string> List(string installRoot) {
		string folder = CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder);
		if (!Directory.Exists(folder)) {
			return [];
		}

		var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
		return Directory.GetFiles(folder, "*" + MissionFileLaunch.MissionExtension, options)
			.Select(path => Path.GetFileName(path))
			.Where(file => Path.GetExtension(file).Equals(MissionFileLaunch.MissionExtension, StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Writes a mission's <c>.MSN</c>, unless null its text, unless blank its about file, and unless null its own zone's
	/// header and heightmap into the install's mission folder, creating the folder, each replaced
	/// file copied to its <see cref="BackupExtension"/> first. A blank about file removes the about file there,
	/// and a null zone the zone's files, after the same copy. Returns the paths written.
	/// </summary>
	/// <param name="installRoot">The install's root.</param>
	/// <param name="name">The mission's name.</param>
	/// <param name="language">The language of the text.</param>
	/// <param name="msn">The mission's <c>.MSN</c>.</param>
	/// <param name="text">The mission's text, or null.</param>
	/// <param name="about">The mission's about file, blank to remove it.</param>
	/// <param name="zone">The mission's own zone, or null to remove its files.</param>
	public static IReadOnlyList<string> Write(string installRoot, string name, GameLanguage language, byte[] msn, byte[]? text,
			MissionAbout about, ZoneFiles? zone) {
		if (WhyNotName(name) is { } why) {
			throw new ArgumentException(why, nameof(name));
		}

		Directory.CreateDirectory(CaseInsensitivePath.Combine(installRoot, MissionFileLaunch.MissionFolder));
		var written = new List<string>();
		WriteBackedUp(MissionPath(installRoot, name), msn, written);
		if (text != null) {
			WriteBackedUp(TextPath(installRoot, name, language), text, written);
		}

		string aboutPath = AboutPath(installRoot, name);
		if (!about.IsBlank) {
			WriteBackedUp(aboutPath, about.Write(), written);
		} else {
			RemoveBackedUp(aboutPath);
		}

		string headerPath = ZoneHeaderPath(installRoot, name);
		string heightmapPath = ZoneHeightmapPath(installRoot, name);
		if (zone != null) {
			WriteBackedUp(headerPath, zone.WriteHeader(), written);
			WriteBackedUp(heightmapPath, zone.WriteHeightmap(), written);
		} else {
			RemoveBackedUp(headerPath);
			RemoveBackedUp(heightmapPath);
		}

		return written;
	}

	private static void RemoveBackedUp(string path) {
		if (File.Exists(path)) {
			File.Copy(path, path + BackupExtension, overwrite: true);
			File.Delete(path);
		}
	}

	private static void WriteBackedUp(string path, byte[] bytes, List<string> written) {
		if (File.Exists(path)) {
			File.Copy(path, path + BackupExtension, overwrite: true);
		}

		File.WriteAllBytes(path, bytes);
		written.Add(path);
	}
}
