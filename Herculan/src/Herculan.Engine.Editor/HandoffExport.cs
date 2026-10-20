using HercWorks.Core.Io;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// File &gt; Export script.dat: the edited mission as a simulator handoff in a folder the user names, for
/// Herculan.Engine.Host or DBSIM to run directly. It writes the document's generated <c>script.dat</c> and
/// <c>mission.str</c>, and copies the rest of the handoff from the editor's scratch folder as it is: <c>player.mec</c>, as
/// the open wrote it or an edit built it again (<see cref="MissionRebuild"/>), and <c>mission.var</c>, as the open or the last
/// change of the preview (<see cref="MissionPreview"/>) wrote it. It never writes the install's own <c>DATA\</c>,
/// which holds the game's own handoff (docs/herculan/handoff-editor-document.md#what-a-saved-scriptdat-reaches).
/// </summary>
internal static class HandoffExport {
	// The handoff files the document does not hold.
	private static readonly string[] Copied = [MissionLoader.PlayerFileName, MissionLoader.CountersFileName];

	/// <summary>Why the handoff cannot be exported into <paramref name="folder"/>, or null when it can.</summary>
	/// <param name="handoffDirectory">The scratch folder the open wrote the handoff into.</param>
	public static string? WhyNotFolder(string folder, string installRoot, string handoffDirectory) {
		if (folder.Trim().Length == 0) {
			return "Type a folder.";
		}

		if (!Path.IsPathFullyQualified(folder)) {
			return "Type a full path, from the drive.";
		}

		if (Same(folder, ShellWorkingFiles.DataDirectory(installRoot))) {
			return "That is the install's own DATA folder, which holds the game's handoff. Choose another.";
		}

		return Same(folder, handoffDirectory) ? "That is the editor's own scratch folder. Choose another." : null;
	}

	/// <summary>
	/// Writes <paramref name="script"/> and <paramref name="missionText"/> into <paramref name="folder"/>, creating it,
	/// with the other handoff files <paramref name="handoffDirectory"/> holds. Returns the paths written. Throws
	/// <see cref="ArgumentException"/> for a folder <see cref="WhyNotFolder"/> refuses.
	/// </summary>
	public static IReadOnlyList<string> Write(string folder, string installRoot, string handoffDirectory, byte[] script,
			byte[] missionText) {
		if (WhyNotFolder(folder, installRoot, handoffDirectory) is { } why) {
			throw new ArgumentException(why, nameof(folder));
		}

		Directory.CreateDirectory(folder);
		var written = new List<string>();
		void Put(string name, byte[] bytes) {
			string path = CaseInsensitivePath.Combine(folder, name);
			File.WriteAllBytes(path, bytes);
			written.Add(path);
		}

		Put(MissionLoader.ScriptFileName, script);
		Put(MissionLoader.TextFileName, missionText);
		foreach (string name in Copied) {
			string from = CaseInsensitivePath.Combine(handoffDirectory, name);
			if (File.Exists(from)) {
				Put(name, File.ReadAllBytes(from));
			}
		}

		return written;
	}

	// Whether two paths name one folder, compared ignoring case and a trailing separator, as the file systems retail ran
	// on compare them.
	private static bool Same(string a, string b) =>
		string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
			StringComparison.OrdinalIgnoreCase);
}
