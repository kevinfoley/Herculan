using HercWorks.Core.Data.File.Sav;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="PlayerSave"/>'s named fields are views over the raw arrays the transformer fills, so
/// these pin which short or byte each reaches against <c>docs/retail/formats/save-games.md</c>. Every raw
/// short is filled with a value of its own, so a view on the wrong one reads the wrong number, and each
/// setter is checked against the raw array it writes.
/// </summary>
public class PlayerSaveFieldTests {
	/// <summary>
	/// The career block's views. The three line arrays are checked end to end, so each one's length is
	/// pinned against the count that follows it, and the last against the briefing movie at short 75.
	/// </summary>
	[Fact]
	public void CareerViewsReachTheirShorts() {
		var save = new PlayerSave();
		for (int i = 0; i < save.CareerBlock.Length; i++) {
			save.CareerBlock[i] = (short)(i + 100);
		}

		Assert.Equal(100, save.CampaignStage);
		Assert.Equal(101, save.MissionInStage);
		AssertLines(save.CareerObjectives, countAt: 2, length: 10);
		AssertLines(save.CareerBriefing, countAt: 13, length: 30);
		AssertLines(save.CareerIntelligence, countAt: 44, length: 30);
		Assert.Equal(175, save.BriefingMovie);

		save.CampaignStage = 2;
		save.MissionInStage = 5;
		save.BriefingMovie = 7;
		Assert.Equal(new short[] { 2, 5 }, save.CareerBlock[..2]);
		Assert.Equal(7, save.CareerBlock[75]);
	}

	/// <summary>The eight shorts between the squad and the player: three squads' members, then positions and machines.</summary>
	[Fact]
	public void SquadViewsReachTheirShorts() {
		var save = new PlayerSave();
		for (int i = 0; i < save.SquadTailAndPlayerHead.Length; i++) {
			save.SquadTailAndPlayerHead[i] = (short)(i + 10);
		}

		Assert.Equal(new short[] { 10, 11, 12 }, Enumerable.Range(0, 3).Select(save.SquadMemberIndex));
		Assert.Equal(16, save.SquadPositionsInPlay);
		Assert.Equal(17, save.MachinesOnStrength);

		save.SquadPositionsInPlay = 3;
		save.MachinesOnStrength = 2;
		Assert.Equal(new short[] { 3, 2 }, save.SquadTailAndPlayerHead[6..]);
	}

	[Fact]
	public void FlagsAndGameStateLiveInTheTail() {
		var save = new PlayerSave { CampaignStateTail = new byte[2000 + 2 + 20] };

		save.SetCampaignFlag(0x3f, 1);
		save.GameState = 7;

		Assert.Equal(1, BitConverter.ToInt16(save.CampaignStateTail, 0x3f * 2));
		Assert.Equal(7, BitConverter.ToInt16(save.CampaignStateTail, 2000));
		Assert.Equal((short)1, save.GetCampaignFlag(0x3f));
		Assert.Equal(7, save.GameState);
	}

	/// <summary>The game state is the tail's bytes 2000 and 2001, so a tail one byte short of it holds no campaign state.</summary>
	[Fact]
	public void ATailShortOfTheGameStateHasNoCampaignState() {
		var save = new PlayerSave { CampaignStateTail = new byte[2001] };

		Assert.False(save.HasCampaignState);
		Assert.Throws<InvalidOperationException>(() => save.GameState);
	}

	private static void AssertLines((short Count, short[] Lines) view, int countAt, int length) {
		Assert.Equal(countAt + 100, view.Count);
		Assert.Equal(Enumerable.Range(countAt + 101, length).Select(i => (short)i), view.Lines);
	}
}
