using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="BeamAppearance.ParticleBeamHue"/>: the beam profile turned blue for PBW and green for PBW2, and left alone
/// for any other beam.
/// </summary>
public class BeamHueTests {
	private static readonly byte[] FireRamp = { 184, 92, 20, 255, 252, 248, 228, 255 };

	[Fact]
	public void PbwTurnsBlue() {
		Assert.Equal(new byte[] { 20, 112, 184, 255, 228, 232, 252, 255 },
			BeamAppearance.ParticleBeamHue(BeamAppearance.PbwSubtype, FireRamp));
	}

	[Fact]
	public void Pbw2TurnsGreen() {
		Assert.Equal(new byte[] { 20, 184, 92, 255, 228, 252, 248, 255 },
			BeamAppearance.ParticleBeamHue(BeamAppearance.Pbw2Subtype, FireRamp));
	}

	[Fact]
	public void AnotherBeamIsLeftAlone() {
		Assert.Equal(FireRamp, BeamAppearance.ParticleBeamHue(1, FireRamp));
	}

	[Theory]
	[InlineData(BeamAppearance.PbwSubtype)]
	[InlineData(BeamAppearance.Pbw2Subtype)]
	public void GreyAndAlphaAreKept(int subtypeId) {
		byte[] grey = { 128, 128, 128, 7 };

		Assert.Equal(grey, BeamAppearance.ParticleBeamHue(subtypeId, grey));
	}
}
