using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Herculan.Engine.World;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The editor's campaign state (<see cref="CampaignState"/>) over a mission opened as the editor opens one, against a real
/// install. Skips silently without one, as the rest of the suite does.
/// </summary>
public class CampaignStateTests {
	private const int GroupRow = 16;

	/// <summary>
	/// Catches a step holding a block index (a side change's group) undone in place after a flag change moved that index:
	/// it would set the side of the group now at the old index and leave its own group on the changed side. In
	/// <c>C2_07</c>, flag 402 above 0 brings in group 123, which comes before Cybrid group 128 in the file.
	/// </summary>
	[Fact]
	public void SideChangeUndoneInAnotherCampaignStateRestoresItsOwnGroup() {
		const short Gated = 123, Changed = 128;
		if (Open("C2_07", nameof(SideChangeUndoneInAnotherCampaignStateRestoresItsOwnGroup), campaign: true) is not { } mission) {
			return;
		}

		var source = mission.Source!;
		Assert.Null(source.ExportedIndex(GroupRow, Gated));
		int group = source.ExportedIndex(GroupRow, Changed)!.Value;
		Assert.NotEmpty(mission.Index.GroupMembers[group]);
		Assert.Equal(MissionSide.Cybrid, mission.Index.SideOf(group));
		mission.Sides.Set(group, MissionSide.Human);

		mission.Campaign.RequestFlag(402, 1);
		mission.Campaign.RunRequest();
		Assert.Null(mission.Campaign.Failure);
		int moved = source.ExportedIndex(GroupRow, Changed)!.Value;
		Assert.NotEqual(group, moved);

		mission.Mover.Undo();

		Assert.Equal(MissionSide.Cybrid, mission.Index.SideOf(moved));
		Assert.All(mission.Index.GroupMembers[moved], member => Assert.Equal(MissionSide.Cybrid, member.Object.Side));
	}

	/// <summary>
	/// Catches a flag edit that writes <c>mission.var</c> from the flags typed rather than as the load leaves them (every
	/// load clears flag 0), a Draw again that drops the flags typed, and either one added to the undo history.
	/// </summary>
	[Fact]
	public void TypedFlagsSurviveDrawAgainOutsideTheHistory() {
		if (Open("TRAIN5", nameof(TypedFlagsSurviveDrawAgainOutsideTheHistory), campaign: false) is not { } mission) {
			return;
		}

		mission.Campaign.RequestFlag(0, 5);
		mission.Campaign.RunRequest();
		Assert.Null(mission.Campaign.Failure);
		Assert.Equal(5, mission.Campaign.Flag(0));
		byte[] counters = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(mission.Mission.SourcePath)!, MissionLoader.CountersFileName));
		Assert.Equal(0, BitConverter.ToInt16(counters, 0));

		mission.Campaign.RequestDrawAgain();
		mission.Campaign.RunRequest();

		Assert.Null(mission.Campaign.Failure);
		Assert.Equal(5, mission.Campaign.Flag(0));
		Assert.False(mission.Mover.CanUndo);
	}

	// <name> opened as the editor's Load from VOL opens it, into a scratch folder of the test's own; null without an
	// install, or for a campaign mission without the DATA\player.mec its open takes the squad from.
	private static EditorMission? Open(string name, string test, bool campaign) {
		if (GameInstall.Locate(null) is not { } root
				|| (campaign && !File.Exists(Path.Combine(ShellWorkingFiles.DataDirectory(root), MissionLoader.PlayerFileName)))) {
			return null;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", test);
		if (Directory.Exists(directory)) {
			Directory.Delete(directory, recursive: true);
		}

		var shell = GameContent.MountShell(root);
		GameContent Shell() => shell;
		string? script = MissionOpener.Open(shell, root, name, directory, out var source, out var squad, out string? failure);
		Assert.True(script != null, failure);
		return EditorMission.Open(GameContent.MountSimulator(root), script, name, ShellWorkingFiles.DataDirectory(root), source,
			squad, Shell, MissionOpener.ModeOf(Shell, root, name, unlisted: false));
	}
}
