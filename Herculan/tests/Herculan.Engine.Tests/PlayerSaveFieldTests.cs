using HercWorks.Core.Data.File.Sav;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="PlayerSave"/>'s named fields are views over the raw arrays the transformer fills, so
/// these pin which short or byte each reaches against <c>docs/retail/formats/save-games.md</c>.
/// </summary>
public class PlayerSaveFieldTests {
	[Fact]
	public void CareerAndSquadFieldsNameTheirShorts() {
		var save = new PlayerSave();
		save.CareerBlock[2] = 3;
		save.CareerBlock[3] = 40;
		save.CareerBlock[13] = 1;
		save.CareerBlock[14] = 50;
		save.CareerBlock[44] = 2;
		save.CareerBlock[45] = 60;

		save.CampaignStage = 2;
		save.MissionInStage = 5;
		save.SquadPositionsInPlay = 3;
		save.MachinesOnStrength = 2;

		Assert.Equal(new short[] { 2, 5 }, save.CareerBlock[..2]);
		Assert.Equal((short)3, save.SquadTailAndPlayerHead[6]);
		Assert.Equal((short)2, save.SquadTailAndPlayerHead[7]);
		Assert.Equal((3, 40), (save.CareerObjectives.Count, save.CareerObjectives.Lines[0]));
		Assert.Equal((1, 50), (save.CareerBriefing.Count, save.CareerBriefing.Lines[0]));
		Assert.Equal((2, 60), (save.CareerIntelligence.Count, save.CareerIntelligence.Lines[0]));
		Assert.Equal((10, 30, 30), (save.CareerObjectives.Lines.Length, save.CareerBriefing.Lines.Length, save.CareerIntelligence.Lines.Length));
	}

	[Fact]
	public void FlagsAndGameStateLiveInTheTail() {
		var save = new PlayerSave { CampaignStateTail = new byte[2000 + 2 + 20] };

		save.SetCampaignFlag(0x3f, 1);
		save.GameState = 7;

		Assert.Equal(1, BitConverter.ToInt16(save.CampaignStateTail, 0x3f * 2));
		Assert.Equal(7, BitConverter.ToInt16(save.CampaignStateTail, 2000));
		Assert.Equal((short)1, save.GetCampaignFlag(0x3f));
	}

	[Fact]
	public void AShortTailHasNoCampaignState() {
		var save = new PlayerSave { CampaignStateTail = new byte[100] };

		Assert.False(save.HasCampaignState);
		Assert.Throws<InvalidOperationException>(() => save.GameState);
	}
}
