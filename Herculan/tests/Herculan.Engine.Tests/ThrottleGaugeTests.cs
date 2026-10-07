using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The console throttle slider: the track geometry that turns a setting into a knob position and a
/// pointer back into a setting, and the once-a-frame exchange that keeps the gauge and the machine
/// holding the same number.
///
/// <para>The geometry tests run against every herc's real <c>.GAU</c> where one is installed, and
/// against a synthetic track otherwise, so a checkout without the game still exercises the maths.</para>
/// </summary>
public class ThrottleGaugeTests {
	/// <summary>Every herc with cockpit art of its own — the eight <c>.GAU</c> files retail ships.</summary>
	public static TheoryData<string> Cockpits => new(
		"OUTLAW", "RAPTOR2", "TOMAHAWK", "SAMSON", "COLOSSUS", "APOCA", "OGRE", "MAVERICK");

	/// <summary>
	/// Full forward puts the knob at the top of the track and full reverse at the bottom, which is
	/// the manual's own description of the control and the direction the original's two sign flips
	/// settle on.
	/// </summary>
	[Theory]
	[MemberData(nameof(Cockpits))]
	public void TheKnobSpansTheTrackForwardsUp(string herc) {
		if (Track(herc) is not { } track) {
			return;
		}

		Assert.Equal(track.Top, track.KnobTopFor(ThrottleTrack.Full));
		Assert.Equal(track.Bottom, track.KnobBottomFor(-ThrottleTrack.Full));
		Assert.True(track.KnobTopFor(0) > track.Top && track.KnobBottomFor(0) < track.Bottom);
	}

	/// <summary>
	/// A zero setting centres the knob on the track — <c>00447e24</c> seeds the knob's bottom edge at
	/// <c>trackBottom - (trackHeight - knobHeight)/2</c>, which is the same place. "Centered is
	/// stopped", as the manual puts it.
	/// </summary>
	[Theory]
	[MemberData(nameof(Cockpits))]
	public void NeutralCentresTheKnob(string herc) {
		if (Track(herc) is not { } track) {
			return;
		}

		int constructorSeed = track.Bottom - (track.Bottom - track.Top - track.KnobHeight) / 2;
		Assert.InRange(track.KnobBottomFor(0), constructorSeed - 1, constructorSeed + 1);
	}

	/// <summary>
	/// Dragging to a position and reading the setting back off it round-trips: the pointer maps to
	/// the knob's bottom edge and the setting maps back from its top, and the two are inverses.
	/// </summary>
	[Theory]
	[MemberData(nameof(Cockpits))]
	public void PointerAndSettingAreInverses(string herc) {
		if (Track(herc) is not { } track) {
			return;
		}

		for (short setting = -ThrottleTrack.Full; setting < ThrottleTrack.Full; setting += 64) {
			short round = track.ThrottleAt(track.KnobBottomFor(setting));

			// One knob position covers several settings — the track is under 100 pixels for a 2048-unit
			// range — so the round trip lands within a pixel's worth of the original, not on it.
			int perPixel = (ThrottleTrack.Full * 2) / Math.Max(track.Travel, 1);
			Assert.InRange(round, setting - perPixel, setting + perPixel);
		}
	}

	/// <summary>A pointer past either end of the track pins to that end rather than running past it.</summary>
	[Theory]
	[MemberData(nameof(Cockpits))]
	public void DraggingPastTheEndsPins(string herc) {
		if (Track(herc) is not { } track) {
			return;
		}

		Assert.Equal(ThrottleTrack.Full, track.ThrottleAt(track.Top - 500));
		Assert.Equal(-ThrottleTrack.Full, track.ThrottleAt(track.Bottom + 500));
	}

	/// <summary>
	/// <c>Player_PerFrameCockpitUpdate</c>'s arbitration with the flag clear: the gauge is the one
	/// that moved, so the machine takes its value.
	/// </summary>
	[Fact]
	public void AnUntouchedThrottleFollowsTheGauge() {
		var mech = Stationary();
		mech.Throttle = 100;

		Assert.Equal(700, mech.ExchangeCockpitThrottle(700));
		Assert.Equal(700, mech.Throttle);
	}

	/// <summary>
	/// The same with the flag set: the machine moved its own throttle this frame, so the gauge is
	/// handed the machine's value and the flag clears ready for the next frame.
	/// </summary>
	[Fact]
	public void AMovedThrottleWinsOverTheGaugeOnce() {
		var mech = Stationary();
		mech.Throttle = 250;
		mech.ThrottleDirty = true;

		Assert.Equal(250, mech.ExchangeCockpitThrottle(700));
		Assert.Equal(250, mech.Throttle);
		Assert.False(mech.ThrottleDirty);

		// Flag cleared, so the next frame is the gauge's again — which is what makes a drag stick.
		Assert.Equal(700, mech.ExchangeCockpitThrottle(700));
	}

	/// <summary>All stop zeroes the throttle and claims the frame, so the gauge follows it down.</summary>
	[Fact]
	public void AllStopZeroesTheThrottleAndTheGauge() {
		var mech = Stationary();
		mech.Throttle = 1024;

		mech.AllStop();

		Assert.Equal(0, mech.ExchangeCockpitThrottle(1024));
		Assert.Equal(0, mech.Throttle);
	}

	/// <summary>
	/// A herc's real track, or a synthetic one standing in for it when the game is not installed. The
	/// stand-in uses OUTLAW's own numbers, so the arithmetic under test is the same either way.
	/// </summary>
	private static ThrottleTrack? Track(string herc) {
		if (GameInstall.Locate(null) is { } root
			&& GameContent.MountSimulator(root) is { } content
			&& CockpitArt.Load(content, herc, "WORLD1") is { } art) {
			return ThrottleTrack.From(art);
		}

		return herc == "OUTLAW" ? SyntheticOutlawTrack() : null;
	}

	/// <summary>OUTLAW's <c>.GAU</c> track and knob, in device pixels, built without a game install.</summary>
	private static ThrottleTrack SyntheticOutlawTrack() => new(268, 350, 296, 448, 12, -6);

	private static MechObject Stationary() =>
		new("TEST", new HercWorks.Core.Data.File.Dat.Sim.HercSimDat(), 100, MechLoadout.None);
}
