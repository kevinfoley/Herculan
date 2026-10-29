using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Anim;
using Herculan.Engine.Terrain;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// End-to-end checks on HERC locomotion, run against the real game files on flat ground.
///
/// <para>The load-bearing one is <see cref="TopSpeedMatchesTheHudReadout"/>. A HERC has no velocity
/// vector — it moves because its walk and run animations carry root motion — so getting top speed
/// right requires the animation clock, the root-motion accumulator, the load-time speed rescale and
/// the world scale all to be right at once. There is nothing to tune: the predicted speed either
/// lands on the machine's own HUD readout or it does not.</para>
///
/// <para>Every test skips silently when no Earthsiege 2 install can be found, so a checkout without
/// the game still passes its suite.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class MechLocomotionTests {
	/// <summary>Every HERC the retail game ships, in <c>nam\HERCS.NAM</c> order.</summary>
	public static TheoryData<string> Hercs => new(
		"OUTLAW", "RAPTOR2", "TOMAHAWK", "SAMSON", "COLOSSUS", "APOCA", "OGRE", "MAVERICK",
		"DIABLO", "CERBERUS", "HYPERION", "ACHILLES", "SCARAB", "STINGRAY", "MONGOOSE",
		"HEADHUNT", "MIRIMAC", "RAMSES");

	private const int SettleTicks = 500;
	private const int MeasureTicks = 500;

	[Theory]
	[MemberData(nameof(Hercs))]
	public void TopSpeedMatchesTheHudReadout(string herc) {
		if (Content() is not { } content) {
			return;
		}

		var mech = Spawn(content, herc);
		if (mech == null) {
			return;
		}

		var world = FlatWorld(mech);
		var full = new MechControls(0, -MechControls.AxisFull);

		for (int i = 0; i < SettleTicks; i++) {
			mech.Controls = full;
			world.Tick();
		}

		var start = mech.Position;
		for (int i = 0; i < MeasureTicks; i++) {
			mech.Controls = full;
			world.Tick();
		}

		double travelled = System.Math.Sqrt(
			System.Math.Pow(mech.Position.X - start.X, 2) + System.Math.Pow(mech.Position.Y - start.Y, 2));
		double seconds = MeasureTicks / (double)SimWorld.TicksPerSecond;
		double kph = travelled / seconds / WorldScale.WorldUnitsPerMeter * 3.6;
		double hud = mech.DisplaySpeedKph;

		Assert.True(hud > 0, $"{herc} never reached a nonzero speed");
		Assert.InRange(kph / hud, 0.80, 1.10);
	}

	[Theory]
	[MemberData(nameof(Hercs))]
	public void TurningInPlaceCoversSeventyDegreesPerCycle(string herc) {
		if (Content() is not { } content) {
			return;
		}

		var mech = Spawn(content, herc);
		if (mech?.Animation == null) {
			return;
		}

		var world = FlatWorld(mech);
		var stick = new MechControls(MechControls.AxisFull, 0);

		// Accumulated per tick, since a quarter of a minute of turning laps the circle.
		int turned = 0;
		int previous = mech.Heading;
		var startPosition = mech.Position;

		for (int i = 0; i < 250; i++) {
			mech.Controls = stick;
			world.Tick();
			turned += BinaryAngle.Delta(previous, mech.Heading);
			previous = mech.Heading;
		}

		// The turn-in-place cycle is uniform across the fleet: 7 frames of 1820 BAM at 100 ticks a
		// frame, played at Q10(350, stick) — about 27 degrees a second once the step-off animation
		// has handed over, and no translation at all. Full right stick turns clockwise, which plays
		// the sequence backwards.
		double degrees = turned * 360.0 / BinaryAngle.FullTurn;
		Assert.InRange(degrees, -300, -200);

		Assert.InRange(mech.Position.X - startPosition.X, -50, 50);
		Assert.InRange(mech.Position.Y - startPosition.Y, -50, 50);
	}

	/// <summary>
	/// The pilot's eye rides the model node the type record names, moved off it by the type's own eye
	/// offset, and lands somewhere a cockpit plausibly is: 7-15 m up rather than at the machine's
	/// waist. The node chain is docs/retail/simulation/mech-locomotion.md, "Cockpit eye and bob".
	/// </summary>
	[Theory]
	[MemberData(nameof(Hercs))]
	public void TheEyeRidesTheCameraNode(string herc) {
		if (Content() is not { } content || Spawn(content, herc) is not { } mech) {
			return;
		}

		FlatWorld(mech);

		float eye = (mech.EyePosition.Z - mech.Position.Z) / WorldScale.WorldUnitsPerMeter;
		Assert.InRange(eye, 7f, 15f);
		// ACHILLES's offset is zero, so its eye is its node; every other type's moves it off.
		var node = mech.CameraNodeTransform;
		if (mech.Type.EyeOffsetY != 0 || mech.EyeLift != 0) {
			Assert.NotEqual(new Vec3i(node.X, node.Y, node.Z), mech.EyePosition);
		}
	}

	/// <summary>
	/// There is no cockpit-bob code in DBSIM: the eye node hangs off a node the walk cycle animates,
	/// so walking moves it and standing still does not. A stride swings it a few tenths of a metre,
	/// which is the whole of the effect.
	/// </summary>
	[Theory]
	[MemberData(nameof(Hercs))]
	public void WalkingBobsTheEyeAndStandingDoesNot(string herc) {
		if (Content() is not { } content || Spawn(content, herc) is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);

		int resting = mech.EyePosition.Z - mech.Position.Z;
		for (int i = 0; i < 100; i++) {
			mech.Controls = MechControls.Neutral;
			world.Tick();
		}

		Assert.Equal(resting, mech.EyePosition.Z - mech.Position.Z);

		int low = int.MaxValue;
		int high = int.MinValue;
		for (int i = 0; i < SettleTicks + MeasureTicks; i++) {
			mech.Controls = new MechControls(0, -MechControls.AxisFull);
			world.Tick();
			if (i < SettleTicks) {
				continue;
			}

			int eye = mech.EyePosition.Z - mech.Position.Z;
			low = System.Math.Min(low, eye);
			high = System.Math.Max(high, eye);
		}

		Assert.InRange((high - low) / WorldScale.WorldUnitsPerMeter, 0.1f, 1.0f);
	}

	/// <summary>
	/// Holding the throttle axis against its stop runs a machine from full forward on into full
	/// reverse — there is no gear to select — and the machine walks backwards. The clamp that would
	/// stop it at zero is gated on a joystick throttle lever being configured
	/// (<c>Input_SetThrottleLeverMode</c>, <c>00459d20</c>), and the keyboard has none. The ramp and
	/// the one-tick pause at zero on the way are <see cref="ThrottleRampsAndReleases"/>'.
	/// </summary>
	[Fact]
	public void HoldingTheThrottleDownReachesReverse() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);

		for (int i = 0; i < 20; i++) {
			mech.Controls = new MechControls(0, -MechControls.AxisFull);
			world.Tick();
		}

		for (int i = 0; i < 20; i++) {
			mech.Controls = new MechControls(0, MechControls.AxisFull);
			world.Tick();
		}

		Assert.Equal(-0x400, mech.Throttle);

		// And the machine actually walks backwards once the gait has settled into it.
		var start = mech.Position;
		for (int i = 0; i < MeasureTicks; i++) {
			mech.Controls = new MechControls(0, MechControls.AxisFull);
			world.Tick();
		}

		Assert.True(mech.Speed < 0);

		// And backwards, not just somewhere: a HERC faces (-sin h, cos h), so at heading 0 reversing
		// walks it toward -Y.
		Assert.Equal(0, mech.Heading);
		Assert.True(mech.Position.Y < start.Y,
			$"expected to reverse toward -Y, moved {mech.Position.Y - start.Y}");
	}

	/// <summary>
	/// A retail throttle lever reads the axis as a position rather than a rate, measured from its idle
	/// stop at <see cref="MechControls.AxisFull"/>: mid-travel is half throttle at once, not a ramp,
	/// and the idle stop is zero. Its setting is never negative, so it cannot reach reverse.
	/// </summary>
	[Fact]
	public void AThrottleLeverIsAbsoluteAndOneSided() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);

		// Mid-travel: half throttle in one tick.
		mech.Controls = new MechControls(0, 0, ThrottleLever: MechControls.ThrottleLeverUnipolar);
		world.Tick();
		Assert.Equal(0x200, mech.Throttle);

		// The idle stop, the end the keyboard's reverse is at.
		mech.Controls = new MechControls(0, MechControls.AxisFull, ThrottleLever: MechControls.ThrottleLeverUnipolar);
		world.Tick();
		Assert.Equal(0, mech.Throttle);
	}

	/// <summary>
	/// The throttle is a rate control: holding the axis runs it to its stop in about seven ticks, and
	/// it stays there when the axis is released. Backing the axis off runs it down again, and the
	/// sign-crossing guard parks it at exactly zero on the way past — for one tick, not for good.
	/// </summary>
	[Fact]
	public void ThrottleRampsAndReleases() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);

		for (int i = 0; i < 10; i++) {
			mech.Controls = new MechControls(0, -MechControls.AxisFull);
			world.Tick();
		}

		Assert.Equal(0x400, mech.Throttle);
		Assert.True(mech.Speed > 0);

		// Releasing the axis leaves the setting where it is — "once the throttle is set, it stays set".
		for (int i = 0; i < 10; i++) {
			mech.Controls = MechControls.Neutral;
			world.Tick();
		}

		Assert.Equal(0x400, mech.Throttle);

		// Backing it off runs it back down through the one-tick pause at zero.
		int atZero = 0;
		for (int i = 0; i < 10; i++) {
			mech.Controls = new MechControls(0, MechControls.AxisFull);
			world.Tick();
			if (mech.Throttle == 0) {
				atZero++;
			}
		}

		Assert.Equal(1, atZero);
		Assert.True(mech.Throttle < 0);
	}

	[Fact]
	public void StandingStillWithNoInputDoesNotDrift() {
		if (Content() is not { } content || Spawn(content, "SAMSON") is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);
		var start = mech.Position;

		for (int i = 0; i < 200; i++) {
			mech.Controls = MechControls.Neutral;
			world.Tick();
		}

		Assert.Equal(0, mech.Speed);
		Assert.Equal(start.X, mech.Position.X);
		Assert.Equal(start.Y, mech.Position.Y);
	}

	/// <summary>
	/// The accurate-speed readout leaves the run gait at retail's figure and brings the walk gait to
	/// the ground the machine actually covers — the gait retail's readout overstates about twofold.
	/// The walk is held at four fifths of the machine's own gait threshold, clear of the crawl below 60
	/// where the playback rate is held up. The margin is retail's own run calibration error, up to
	/// about 10%, plus the motion each tick's whole-animation-tick step drops at walking rates — APOCA
	/// at speed 66 advances 7 animation ticks where 7.99 are due, and reads 26% high. Three
	/// chassis span the fleet: OUTLAW, the fastest; APOCA, the longest stride; SCARAB, the slowest.
	/// </summary>
	[Theory]
	[InlineData("OUTLAW", false)]
	[InlineData("OUTLAW", true)]
	[InlineData("APOCA", false)]
	[InlineData("SCARAB", false)]
	public void GroundSpeedMatchesTheGroundCovered(string herc, bool running) {
		if (Content() is not { } content || Spawn(content, herc) is not { } mech) {
			return;
		}

		double throttleFraction = running ? 1.0 : 0.8 * mech.Type.GaitThreshold / mech.Type.MaxForward;
		double kph = SteadySpeed(mech, throttleFraction)
			* SimWorld.TicksPerSecond / WorldScale.WorldUnitsPerMeter * 3.6;

		// Running is a branch of the readout, not a property of the chassis, so one machine shows it.
		if (running) {
			Assert.Equal(mech.DisplaySpeedKph, mech.GroundSpeedKph);
			return;
		}

		Assert.True(mech.Speed >= 60, $"{herc} walked at speed {mech.Speed}, inside the crawl");
		Assert.InRange(mech.GroundSpeedKph / kph, 0.8, 1.3);

		// And the walk cases really are walking: retail's figure is well over the real one.
		Assert.True(mech.DisplaySpeedKph > 1.5 * mech.GroundSpeedKph,
			$"{herc} reads {mech.DisplaySpeedKph} against a ground speed of {mech.GroundSpeedKph}");
	}

	/// <summary>
	/// Easing the throttle up from a standstill never makes the accurate-speed readout fall, though
	/// the machine itself slows as its speed rises through 45 — the playback rate is held at 60 below
	/// that (docs/retail/simulation/mech-locomotion.md, "Control law").
	/// </summary>
	[Fact]
	public void GroundSpeedRisesSteadilyAtACrawl() {
		if (Content() is not { } content || Spawn(content, "OUTLAW") is not { } mech) {
			return;
		}

		var world = FlatWorld(mech);
		int previous = 0;
		int previousRate = 0;
		bool rateFell = false;
		for (int i = 0; i < 400; i++) {
			// One throttle step every fourth tick, held otherwise, up to under a third of the range.
			short axis = i % 4 == 0 && mech.Throttle < 0x140 ? (short)-0x10 : (short)0;
			mech.Controls = new MechControls(0, axis);
			world.Tick();

			int reading = mech.GroundSpeedKph;
			Assert.True(reading >= previous, $"tick {i}: {previous} fell to {reading} at speed {mech.Speed}");
			previous = reading;

			rateFell |= mech.AnimRate < previousRate;
			previousRate = mech.AnimRate;
		}

		// The run up did cross the stretch where the machine itself slows.
		Assert.True(rateFell, "the playback rate never fell");

		Assert.True(mech.Speed > 60, $"only reached speed {mech.Speed}");
	}

	private static double SteadySpeed(MechObject mech, double throttleFraction) {
		var world = FlatWorld(mech);
		short axis = (short)-(MechControls.AxisFull * throttleFraction);
		short cap = (short)(0x400 * throttleFraction);

		for (int i = 0; i < SettleTicks; i++) {
			mech.Controls = new MechControls(0, mech.Throttle >= cap ? (short)0 : axis);
			world.Tick();
		}

		var start = mech.Position;
		for (int i = 0; i < MeasureTicks; i++) {
			mech.Controls = new MechControls(0, mech.Throttle >= cap ? (short)0 : axis);
			world.Tick();
		}

		return System.Math.Sqrt(
			System.Math.Pow(mech.Position.X - start.X, 2) + System.Math.Pow(mech.Position.Y - start.Y, 2))
			/ MeasureTicks;
	}

	/// <summary>
	/// A featureless 256x256 grid with a single machine standing in the middle of it — flat ground
	/// so the terrain-slope term is exactly zero and nothing else can collide.
	/// </summary>
	private static SimWorld FlatWorld(MechObject mech) {
		const int widthShift = 8;
		const int cellSizeShift = 12;
		const int cellCount = 1 << (widthShift * 2);

		var heights = new byte[cellCount];
		System.Array.Fill(heights, (byte)40);

		var terrain = new HeightGrid(widthShift, widthShift, cellSizeShift, 16, 10, heights, new byte[cellCount]);
		var world = new SimWorld(terrain);

		int middle = (1 << (widthShift + cellSizeShift)) / 2;
		mech.Position = new Vec3i(middle, middle, terrain.HeightAtWorld(middle, middle));
		world.Add(mech);
		BecomePlayer(world, mech);
		return world;
	}

	/// <summary>
	/// Makes <paramref name="mech"/> the player's machine, in a group of its own: a machine moves only
	/// from its group's AI tick, and only the player's takes <see cref="MechObject.Controls"/>.
	/// </summary>
	internal static void BecomePlayer(SimWorld world, MechObject mech) {
		mech.IsPlayer = true;
		var group = new MissionGroup(0, World.MissionUnitKind.Mech, World.MissionSide.Human,
			System.Array.Empty<World.MissionOrder?>());
		group.Add(mech);
		world.AddGroup(group);
		world.PlayerMech = mech;
	}

	private static MechObject? Spawn(GameContent content, string herc) {
		if (content.Read("dat", herc + ".DAT") is not { } datBytes
			|| new HercSimDataTransformer().Parse(datBytes) is not HercSimDat data) {
			return null;
		}

		var animation = content.Read("dts", herc + ".DTS") is { } dtsBytes
			? ShapeAnimation.FromModel(
				new DTSModelTransformer().Parse(dtsBytes) as HercWorks.Core.Data.File.Dyn.DynamixThreeSpaceModel)
			: null;

		return new MechObject(herc, data, 0, MechLoadout.None, animation);
	}

	private static GameContent? Content() {
		string? root = GameInstall.Locate(null);
		return root != null ? GameContent.MountSimulator(root) : null;
	}
}
