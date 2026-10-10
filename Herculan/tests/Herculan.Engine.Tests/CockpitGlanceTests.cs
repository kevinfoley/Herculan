using Herculan.Engine.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="CockpitGlance"/>: the original's glance gates (docs/retail/simulation/cockpit-views.md, "View
/// switching") and this engine's reach-limited slide.
/// </summary>
public class CockpitGlanceTests {
	private static void Settle(CockpitGlance glance, float reach = 1f) =>
		glance.Advance(CockpitGlance.DurationSeconds * 2, reach);

	[Fact]
	public void GlanceSlidesToTheReachAndStops() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Right);
		Settle(glance, reach: 0.6f);

		Assert.Equal(0.6f, glance.OffsetPanels, 5);
		Assert.False(glance.AtForward);
	}

	[Fact]
	public void OneStepIsAPanelOverTheDuration() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Left);
		glance.Advance(CockpitGlance.DurationSeconds / 4, 1f);

		Assert.Equal(-0.25f, glance.OffsetPanels, 5);
	}

	/// <summary>Command 5 from view 2 is a return, not a glance to view 3; command 4 from view 2 does nothing.</summary>
	[Fact]
	public void OppositeCommandReturnsAndSameCommandIsIgnored() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Right);
		Settle(glance);

		glance.Command(GlanceSide.Right);
		Assert.Equal(GlanceSide.Right, glance.Requested);

		glance.Command(GlanceSide.Left);
		Assert.Equal(GlanceSide.Forward, glance.Requested);
	}

	/// <summary>
	/// A held opposite command — the hat is level-triggered — does not flick through to the other
	/// window until the strip is fully back, which is when the original's current view becomes 0.
	/// </summary>
	[Fact]
	public void NewGlanceWaitsForTheStripToReturn() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Right);
		Settle(glance);

		glance.Command(GlanceSide.Left);
		glance.Advance(CockpitGlance.DurationSeconds / 2, 1f);
		glance.Command(GlanceSide.Left);
		Assert.Equal(GlanceSide.Forward, glance.Requested);

		Settle(glance);
		Assert.True(glance.AtForward);
		glance.Command(GlanceSide.Left);
		Assert.Equal(GlanceSide.Left, glance.Requested);
	}

	[Fact]
	public void NoGlanceWhenTheWindowAlreadyShowsEverything() {
		var glance = new CockpitGlance();
		glance.Advance(0.016, reachPanels: 0f);
		glance.Command(GlanceSide.Right);

		Assert.True(glance.AtForward);
	}

	[Fact]
	public void ShrinkingReachPullsTheStripBackToTheNewStop() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Right);
		Settle(glance, reach: 1f);
		glance.Advance(0, reachPanels: 0.4f);

		Assert.Equal(0.4f, glance.OffsetPanels, 5);
	}

	[Fact]
	public void ReturnComesBackToForward() {
		var glance = new CockpitGlance();
		glance.Command(GlanceSide.Left);
		Settle(glance);
		glance.Return();
		Settle(glance);

		Assert.True(glance.AtForward);
		Assert.Equal(0f, glance.OffsetPanels);
	}
}
