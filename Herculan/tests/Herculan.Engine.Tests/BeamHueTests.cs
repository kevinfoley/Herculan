using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary><see cref="BeamAppearance.TurnHue"/>: the PBW and PBW2 beam profile with its hue turned 180 degrees.</summary>
public class BeamHueTests {
	[Fact]
	public void TheFireRampTurnsBlue() {
		byte[] fireRamp = { 184, 92, 20, 255, 252, 248, 228, 255 };

		Assert.Equal(new byte[] { 20, 112, 184, 255, 228, 232, 252, 255 }, BeamAppearance.TurnHue(fireRamp));
	}

	[Fact]
	public void GreyAndAlphaAreKept() {
		byte[] grey = { 128, 128, 128, 7 };

		Assert.Equal(grey, BeamAppearance.TurnHue(grey));
	}

	[Fact]
	public void OnlyPbwAndPbw2AreParticleBeams() {
		Assert.True(BeamAppearance.IsParticleBeam(0));
		Assert.True(BeamAppearance.IsParticleBeam(6));
		Assert.False(BeamAppearance.IsParticleBeam(2));
		Assert.False(BeamAppearance.IsParticleBeam(3));
	}
}
