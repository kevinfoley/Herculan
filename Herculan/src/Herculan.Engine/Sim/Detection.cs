using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// Who knows about whom — the sensor model the whole of target selection stands on.
///
/// <para>There are <b>two separate notions of "seen"</b> in the original and they are not
/// interchangeable, which is the thing to get right before reading anything below:</para>
/// <list type="bullet">
/// <item><b>Radar visibility</b> (<see cref="SimObject.RadarVisible"/>, <c>obj+0x95</c>) is a
/// property of one object. Something with an active scanner painted it, so it is showing on
/// everyone's screen. It is set by <see cref="Sweep"/> and cleared wholesale by
/// <see cref="DecayContacts"/>.</item>
/// <item><b>A contact</b> (<see cref="SimObject.Detects"/>, <c>obj+0xc2</c>) is a property of a
/// <i>pair</i>: this object knows where that one is. Contacts are made by looking — a bearing inside
/// the sensor arc with clear line of sight — and they are shared sideways to everything on the same
/// side within 100000 units, which is how a lance fights as a unit.</item>
/// </list>
///
/// <para><see cref="TargetSelection.CanTarget"/> accepts either one, at a different range each: a
/// radar-visible object out to <see cref="RadarTargetingRange"/>, a known contact out to the
/// scanner's own setting.</para>
///
/// <para><b>The sweep is asymmetric and that is deliberate in the original.</b>
/// <see cref="Tick"/> runs <see cref="Sweep"/> for human-side objects only, and each sweep looks
/// only at Cybrid ones — but the pass writes both objects' tables, so a Cybrid machine ends up
/// knowing about the human that spotted it without ever running a sweep of its own. Half the loops
/// are missing because they are not needed, not because they were dropped here.</para>
/// </summary>
public static class Detection {
	/// <summary>
	/// Sight height for an object whose shape record is missing — <c>Detection_LineOfSight</c> (<c>00412608</c>)'s literal 500,
	/// and the default for <see cref="SimObject.SightHeight"/>.
	/// </summary>
	public const int DefaultSightHeight = 500;

	/// <summary>
	/// How far a <i>radar-visible</i> object can be selected from — <c>DAT_004d1cfc</c>, which is
	/// the last entry of the scanner's own three-range table (<c>MfdDisplay_Ctor</c> writes
	/// 50000/100000/200000). <c>TargetSelect_CanTarget</c> (<c>00433174</c>) reads that third entry directly rather than the
	/// player's current setting, so the radar-visible branch always uses the longest range.
	/// </summary>
	public const int RadarTargetingRange = 200000;

	/// <summary>
	/// The scan range a <i>known contact</i> can be selected from — <c>Detection_ContactScanRange</c> (<c>00426aec</c>), which is
	/// 30000 on the short setting and 60000 otherwise. The setting is <c>DAT_004a9ee2</c>, the
	/// manual's Alt+R; nothing changes it here, so the long range applies.
	/// </summary>
	public const int ContactTargetingRange = 60000;

	/// <inheritdoc cref="ContactTargetingRange"/>
	public const int ShortContactTargetingRange = 30000;

	/// <summary>
	/// Outer limit of the radar-painting pass — beyond this, an active scanner does nothing for
	/// either object in the pair.
	/// </summary>
	private const int ScannerRange = 200000;

	/// <summary>
	/// Range inside which a scanner paints the <i>other</i> object even though that object is not
	/// itself emitting. Past it, only an object with its own scanner running lights up.
	/// </summary>
	private const int PassiveRadarRange = 140000;

	/// <summary>Range inside which the looking half of <see cref="Sweep"/> runs at all.</summary>
	private const int VisualRange = 80000;

	/// <summary>How far a new contact is passed along to the spotter's own side.</summary>
	private const int ContactShareRange = 100000;

	/// <summary>
	/// How far a contact stays a contact. <see cref="DecayContacts"/> drops anything past it —
	/// measured on the ground plane alone, unlike every other range here.
	/// </summary>
	private const int ContactHoldRange = 100001;

	/// <summary>
	/// Half-width of the sensor arc, about <see cref="SimObject.Heading"/> plus
	/// <see cref="SimObject.AimTwist"/> — <c>SimObject_BearingInSensorArc</c> (<c>00411acc</c>, mech vtable <c>+0x44</c>), whose test
	/// is <c>(bearing - heading + 0x3800) &lt; 0x7000</c> unsigned. About 78.75° each side.
	/// </summary>
	public const int SensorArcHalfWidth = 0x3800;

	/// <summary>Base reload for <see cref="SimObject.SightCacheTimer"/>, before its roll.</summary>
	private const short SightCacheInterval = 5000;

	/// <summary>Base reload for <see cref="SimObject.ContactDecayTimer"/>, before its roll.</summary>
	private const short ContactDecayInterval = 10000;

	/// <summary>The spread both reloads add, as the bound of the roll they take.</summary>
	private const short TimerJitter = 1000;

	/// <summary>
	/// The 50000 inside which <see cref="Sweep"/> and <see cref="ShareContact"/> mark an object
	/// <see cref="SimObject.Engaged"/> and activate engagement actions — well inside
	/// <see cref="VisualRange"/>. Which objects each one marks is in
	/// docs/retail/simulation/mission-deployment.md, "An object's own two actions".
	/// </summary>
	public const int EngagementRange = 50000;

	/// <summary>
	/// Whether an object takes part in the sensor model at all.
	///
	/// <para>Only the arrival test is the original's: it skips anything whose group is still waiting
	/// on its arrival action (<c>group+0x14</c>). The other two exclude what DBSIM's live-object list
	/// never holds. Removal has no listed state there, and the list only ever holds the three combat
	/// classes, because <c>ObjectList_Add</c> is called from their constructors and nothing else's,
	/// whereas <see cref="SimWorld"/> also carries the observer camera. An object with no
	/// <see cref="Sim.TargetClass"/> is therefore not in the list as far as this file is concerned:
	/// it neither sees nor is seen, and — the part that matters — it does not sweep, which would
	/// otherwise have a free camera spotting for the player's side.</para>
	/// </summary>
	private static bool InSensorModel(SimObject simObject) =>
		!simObject.Removed && !simObject.AwaitingDeployment
			&& simObject.TargetClass != TargetClass.None;

	/// <summary>
	/// <c>Sim_DetectionTick</c> (<c>004123ac</c>) — the whole sensor model for one simulation step, in the original's own
	/// three passes.
	///
	/// <list type="number">
	/// <item><b>Timers.</b> Every object's line-of-sight cache and contact-decay countdowns tick, and
	/// an expired decay countdown re-examines that object's contact list on the spot.</item>
	/// <item><b>The sweeps.</b> Every live human-side object looks for Cybrids — except the machine
	/// the player is flying, which is held back and swept last, so that contacts its squadmates make
	/// this tick have already been shared to it by the time it looks.</item>
	/// <item><b>The ECM gate</b> (<see cref="SimObject.EngagementActionHeld"/>) cleared on
	/// everything, for the per-mech systems pass that follows to raise again.</item>
	/// </list>
	/// </summary>
	public static void Tick(SimWorld world) {
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			var self = objects[i];
			if (!InSensorModel(self)) {
				continue;
			}

			SimMath.CountdownTimerTick(ref self.SightCacheTimer);

			if (SimMath.CountdownTimerTick(ref self.ContactDecayTimer) == 0) {
				self.ContactDecayTimer =
					(short)(world.Random.NextBelow(TimerJitter) + ContactDecayInterval);
				DecayContacts(world, self);
			}
		}

		SimObject? player = null;
		for (int i = 0; i < objects.Count; i++) {
			var self = objects[i];
			if (!InSensorModel(self) || self.Neutralised || self.Side != MissionSide.Human) {
				continue;
			}

			if (self.LocallyPiloted) {
				player = self;
				continue;
			}

			Sweep(world, self);
		}

		if (player != null) {
			Sweep(world, player);
		}

		for (int i = 0; i < objects.Count; i++) {
			objects[i].EngagementActionHeld = false;
		}
	}

	/// <summary>
	/// <c>Detection_Sweep</c> (<c>004128f8</c>) — one human-side object's look around, over every Cybrid in the world.
	/// Two independent things happen per candidate and the second is not conditional on the first.
	///
	/// <list type="number">
	/// <item><b>Radar painting.</b> Within <see cref="ScannerRange"/>, if at least one of the pair has
	/// a scanner running and they are not <i>both</i> already painted, a clear line of sight lights up
	/// whichever of them the range and the emitter allow —
	/// <see cref="PassiveRadarRange"/> is where a scanner starts painting something that is not
	/// emitting back.</item>
	/// <item><b>Looking.</b> Within <see cref="VisualRange"/> only. A pair that already knows about
	/// each other is finished; otherwise each object's own sensor arc is tested against the bearing
	/// between them, and a hit with clear line of sight makes the contact. A contact made by an AI
	/// machine is shared to its side by <see cref="ShareContact"/>; one made by the player's own
	/// machine is kept to itself.</item>
	/// </list>
	///
	/// <para>Just before it paints the Cybrid, and just before it makes a contact of its own, the sweep
	/// calls the sweeping object's <see cref="SimObject.EnemySighted"/> — the "enemy detected"
	/// callout, which tests the state those two writes are about to change.</para>
	/// </summary>
	private static void Sweep(SimWorld world, SimObject self) {
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (!InSensorModel(other) || other.Neutralised || other.Side != MissionSide.Cybrid) {
				continue;
			}

			int distance = self.Position.ApproxDistanceTo(other.Position);

			// "Not both painted, and at least one emitting" — the original's own pair of tests, which
			// stop a pair that is already lit from being re-tested every tick.
			if ((!self.RadarVisible || !other.RadarVisible)
					&& (self.ScannerActive || other.ScannerActive)
					&& distance < ScannerRange
					&& LineOfSight(world, self, other)) {
				if (other.ScannerActive || distance < PassiveRadarRange) {
					self.EnemySighted(world, other);
					other.RadarVisible = true;
				}

				if (self.ScannerActive || distance < PassiveRadarRange) {
					self.RadarVisible = true;
				}
			}

			if (distance >= VisualRange) {
				continue;
			}

			// A pair that already sees each other has nothing left to establish -- except that closing
			// to engagement range is itself a mission event. Both parties latch it and both fire their
			// own engagement action, which is the third of the four ways a mission action fires.
			if (self.Detects(other) && other.Detects(self)) {
				if (distance < EngagementRange) {
					other.Engaged = true;
					self.ActivateEngagementAction(world);
					self.Engaged = true;
					other.ActivateEngagementAction(world);
				}

				continue;
			}

			short bearing = HeadingToward(other.Position, self.Position);

			if (InSensorArc(self, (short)(bearing + self.AimTwist))
					&& LineOfSight(world, self, other)) {
				self.EnemySighted(world, other);

				if (self.LocallyPiloted) {
					self.SetDetects(other, true);
				} else {
					ShareContact(world, self, other);
				}
			}

			// And the same test from the other object's point of view, on the reciprocal bearing.
			// This is the only reason a Cybrid ever notices anything: nothing sweeps on its behalf.
			if (InSensorArc(other, (short)(bearing + other.AimTwist + BinaryAngle.HalfTurn))
					&& LineOfSight(world, self, other)) {
				ShareContact(world, other, self);
			}
		}
	}

	/// <summary>
	/// <c>Detection_ShareContact</c> (<c>00412704</c>) — a new contact passed to everyone on the spotter's side within
	/// <see cref="ContactShareRange"/>, the spotter included. Nothing is shared to the other side, and
	/// a spotter and a contact on the same side is not a contact at all. Within
	/// <see cref="EngagementRange"/> the <i>contact</i> is marked engaged and the <i>ally</i>
	/// activates its own engagement action — docs/retail/simulation/mission-deployment.md, "An object's own
	/// two actions".
	/// </summary>
	internal static void ShareContact(SimWorld world, SimObject spotter, SimObject contact) {
		if (spotter.Side == contact.Side) {
			return;
		}

		var objects = world.Objects;
		for (int i = 0; i < objects.Count; i++) {
			var ally = objects[i];
			if (!InSensorModel(ally) || ally.Side != spotter.Side) {
				continue;
			}

			int distance = ally.Position.ApproxDistanceTo(contact.Position);
			if (distance < ContactShareRange) {
				ally.SetDetects(contact, true);

				if (distance < EngagementRange) {
					contact.Engaged = true;
					ally.ActivateEngagementAction(world);
				}
			}
		}
	}

	/// <summary>
	/// <c>Detection_DecayContacts</c> (<c>0041251c</c>) — one object forgets what it can no longer justify knowing. Its radar
	/// visibility is dropped outright at the top (the sweep re-establishes it the same tick if it is
	/// still being painted), and every enemy contact it holds is dropped unless it is both inside
	/// <see cref="ContactHoldRange"/> and still in line of sight.
	///
	/// <para>The range here is measured <b>on the ground plane only</b>
	/// (<see cref="SimMath.FastMagnitude2D"/>), where every other range in this file is the 3D
	/// approximation. That is the original's own choice of helper, not an oversight — a flyer
	/// directly overhead stays a contact.</para>
	///
	/// <para>Dropping is mutual: the contact loses its row for this object too.</para>
	/// </summary>
	private static void DecayContacts(SimWorld world, SimObject self) {
		self.RadarVisible = false;

		var objects = world.Objects;
		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (!InSensorModel(other) || other.Side == self.Side || !self.Detects(other)) {
				continue;
			}

			int distance = SimMath.FastMagnitude2D(
				self.Position.X - other.Position.X, self.Position.Y - other.Position.Y);

			if (distance >= ContactHoldRange || !LineOfSight(world, self, other)) {
				self.SetDetects(other, false);
				other.SetDetects(self, false);
			}
		}
	}

	/// <summary>
	/// <c>Detection_LineOfSight</c> (<c>00412608</c>) — is there clear ground between these two, cached per pair.
	///
	/// <para>The ray runs between the two objects' shape centres
	/// (<see cref="SimObject.SightHeight"/>) and a terrain hit anywhere along it means no sight. The
	/// result is stored on <paramref name="self"/> in the row belonging to
	/// <paramref name="other"/>, and returned from there on every call until the cache expires.</para>
	///
	/// <para><b>The cache gate is the original's, oddity included.</b> It tests
	/// <i><paramref name="other"/>'s</i> countdown to decide whether to recompute, and then reloads
	/// <i><paramref name="self"/>'s</i>. Verified against the raw disassembly at <c>00412617</c> and
	/// <c>0041263f</c> — <c>CMP word ptr [ESI + 0x1e3], 0x0</c> against
	/// <c>MOV word ptr [EBX + 0x1e3], AX</c> — rather than taken from the decompiler, because it
	/// reads like a transcription slip and is not one. The practical effect is that the reload rarely
	/// suppresses anything, so most pairs are re-walked whenever they are asked about.</para>
	/// </summary>
	public static bool LineOfSight(SimWorld world, SimObject self, SimObject other) {
		if (other.SightCacheTimer == 0) {
			self.SightCacheTimer = (short)(world.Random.NextBelow(TimerJitter) + SightCacheInterval);

			var from = new Vec3i(self.Position.X, self.Position.Y, self.Position.Z + self.SightHeight);
			var to = new Vec3i(other.Position.X, other.Position.Y, other.Position.Z + other.SightHeight);

			self.SetLineOfSightTo(other, !world.Terrain.RayWalk(from, to, out _));
		}

		return self.LineOfSightTo(other);
	}

	/// <summary>
	/// Vtable <c>+0x44</c>, which <c>Detection_Sweep</c> dispatches on both objects of a pair —
	/// whether a bearing falls inside an object's sensor arc. A machine or an aircraft runs
	/// <c>SimObject_BearingInSensorArc</c> (<c>00411acc</c>); the caller has already folded in the aim
	/// twist, so that is purely a comparison against <see cref="SimObject.Heading"/>. All five
	/// structure classes install <c>Base_BearingInSensorArc_Always</c> (<c>00405308</c>) instead, so a
	/// <see cref="BaseObject"/> sees all round.
	/// </summary>
	public static bool InSensorArc(SimObject self, short bearing) =>
		self is BaseObject
		|| (ushort)(bearing - self.Heading + SensorArcHalfWidth) < SensorArcHalfWidth * 2;

	/// <summary>
	/// <c>Math_HeadingToward</c> (<c>00492828</c>) — the ground-plane bearing from
	/// <paramref name="from"/> to <paramref name="to"/>, less the quarter turn every bearing in the
	/// simulation carries because a machine's forward axis is model Y.
	/// </summary>
	/// <remarks>
	/// The original's argument order is the destination first; this takes them the way round the name
	/// reads. The degenerate guard is <c>Math_Atan2Guarded</c> (<c>00492800</c>)'s own, which nudges the <b>x</b> delta so a
	/// pair standing on the same spot reads as a bearing of zero — the same guard
	/// <see cref="World.MissionLoader"/> applies to a route with no length.
	/// </remarks>
	public static short HeadingToward(Vec3i to, Vec3i from) => (short)(
		SimTrig.Atan2Guarded(to.Y - from.Y, to.X - from.X) - BinaryAngle.QuarterTurn);
}
