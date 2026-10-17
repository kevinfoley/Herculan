using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionFileLaunch.Open"/> against a real install. Skips silently without one, as the rest of the suite does.
/// </summary>
public class MissionFileLaunchTests {
	/// <summary>
	/// Catches a document whose generation differs from the handoff written beside it — VSHELL's generator copied before
	/// the flag seeding's draws, or the practice header left out — so the scene the editor builds from the document would
	/// not be the mission the handoff's squad was built for. TRAIN5 draws two variant tables.
	/// </summary>
	[Fact]
	public void OpenedDocumentGeneratesTheHandoffItOpened() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", nameof(OpenedDocumentGeneratesTheHandoffItOpened));
		// The shell's writer does not truncate, so a longer file left by an earlier run would keep its tail.
		foreach (string file in new[] { "script.dat", "mission.str" }) {
			if (File.Exists(Path.Combine(directory, file))) {
				File.Delete(Path.Combine(directory, file));
			}
		}

		string? script = MissionFileLaunch.Open(GameContent.MountShell(root), root, "TRAIN5.MSN", directory, out var document,
			out string? failure);

		Assert.True(script != null, failure);
		Assert.Equal(File.ReadAllBytes(script), document!.ScriptBytes);
		Assert.Equal(File.ReadAllBytes(Path.Combine(directory, "mission.str")), document.MissionText);
	}
}
