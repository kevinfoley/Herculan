using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// An object's two per-slot byte tables and two countdowns (docs/es1/simulation/sensor-model.md#the-fields). Each table
/// has 80 rows (docs/es1/simulation/sim-object-base.md#the-live-object-list). What sets the countdowns at a machine's
/// construction is Open (docs/es1/simulation/sensor-model.md#open), as is what a pool slot holds before its constructor
/// (docs/es1/simulation/mech.md#open); here they and the tables start at 0, as every field the constructor does not
/// write does.
/// </summary>
public sealed class Es1SensorTables {
	/// <summary>The rows of each table.</summary>
	public const int Rows = 80;

	/// <summary><c>+0xbc</c>, the contacts: a slot's byte is set while this object knows of that slot's object.</summary>
	public byte[] Contacts { get; } = new byte[Rows];

	/// <summary><c>+0x10c</c>, the line-of-sight cache: a slot's byte is set while the line to that slot's object strikes no zone shape.</summary>
	public byte[] LineOfSight { get; } = new byte[Rows];

	/// <summary><c>+0x179</c>, the counter of the line-of-sight countdown <c>+0x178</c>.</summary>
	public short LineOfSightCountdown;

	/// <summary><c>+0x17c</c>, the counter of the decay countdown <c>+0x17b</c>.</summary>
	public short DecayCountdown;

	/// <summary>
	/// A row of <paramref name="table"/>. Retail does not bound the slot, so a slot past the 80 rows reads the next
	/// table's bytes; here it reads 0, an engine guard.
	/// </summary>
	public static bool Get(byte[] table, short slot) => slot >= 0 && slot < Rows && table[slot] != 0;

	/// <summary>A row of <paramref name="table"/> set or cleared; past the 80 rows, where retail writes the next table's bytes, nothing, an engine guard.</summary>
	public static void Set(byte[] table, short slot, bool value) {
		if (slot >= 0 && slot < Rows) {
			table[slot] = value ? (byte)1 : (byte)0;
		}
	}
}

/// <summary>
/// The enemy-sighted callout's globals (docs/es1/simulation/sensor-model.md#globals). Each lies past the image's file bytes
/// (<c>0xadcd8</c> of <c>0xbdd40</c>), so it starts at 0.
/// </summary>
public sealed class Es1SensorGlobals {
	/// <summary>The latch's bytes, the <c>0x64</c> from <c>000af200</c> to <c>000af264</c>.</summary>
	public const int LatchSlots = 0x64;

	/// <summary><c>EnemySighted_CalledInLatch</c> (<c>000af200</c>), a byte per slot: the object has been called in.</summary>
	public byte[] CalledInLatch { get; } = new byte[LatchSlots];

	/// <summary>The byte <c>000af264</c>, which the callout sets; what reads it is Open (docs/es1/simulation/sensor-model.md#open).</summary>
	public bool Sighted { get; set; }

	/// <summary><c>000af269</c>, the counter of <c>EnemySighted_CalloutRateLimit</c> (<c>000af268</c>).</summary>
	public short CalloutRateLimit;
}

/// <summary>
/// The sensor model: <c>Sim_DetectionTick</c> (<c>ES1DBSIM 0000e240</c>) and its file, <c>Detection_Sweep</c>,
/// <c>Detection_LineOfSight</c>, <c>Detection_ShareContact</c>, <c>Detection_DecayContacts</c> and the HERC's
/// <c>Mech_AiEnemySighted</c> (docs/es1/simulation/sensor-model.md). Each walk is over <c>LiveObjectList</c>.
///
/// <para>An object with no group, through which retail reads (every walk reads the group's action <c>+0x16</c> and side
/// <c>+0x14</c>), takes no part in any walk here, an engine guard.</para>
/// </summary>
public static class Es1SensorModel {
	/// <summary><c>SimObject_BearingInSensorArc</c>'s half-width, ±78.75°.</summary>
	public const int SensorArcHalfWidth = 0x3800;

	/// <summary>An active radar's reach in <c>Detection_Sweep</c>.</summary>
	public const int RadarRange = 100000;

	/// <summary>The reach within which <c>Detection_ShareContact</c> passes a contact on.</summary>
	public const int ShareRange = 120000;

	/// <summary>The ground-plane reach beyond which <c>Detection_DecayContacts</c> drops a contact, the first distance dropped.</summary>
	public const int DecayRange = 120001;

	/// <summary>The reach within which a known enemy engages.</summary>
	public const int EngagementRange = 50000;

	/// <summary><c>Detection_ContactScanRange</c>'s two ranges.</summary>
	public const int ShortScanRange = 30000, LongScanRange = 60000;

	/// <summary>The reloads of the two countdowns and the callout's rate limit, before their draw below <see cref="ReloadJitter"/>.</summary>
	private const short LineOfSightReload = 5000, DecayReload = 10000, CalloutReload = 10000;

	/// <summary>The bound of the draw each countdown's reload adds.</summary>
	private const short ReloadJitter = 1000;

	/// <summary>The class <c>+0x162</c> the callout never calls in.</summary>
	private const short UncalledClass = 1;

	/// <summary>The callout's message id on the pilot channel.</summary>
	public const short EnemySightedMessage = 1;

	/// <summary>
	/// <c>Sim_DetectionTick</c>, in its three passes over <c>LiveObjectList</c>
	/// (docs/es1/simulation/sensor-model.md#the-tick--sim_detectiontick-0000e240): the countdowns of each object whose
	/// group's action is clear, an expired decay countdown reloaded and its contacts decayed; the sweep of each such object
	/// on side 0 neither immobilized nor destroyed, the player's machine held back to sweep last; and every object's
	/// <c>+0x9c</c> cleared.
	/// </summary>
	public static void Tick(Es1SimGlobals globals, Es1Mech playerMech) {
		var live = globals.LiveObjects;
		var clock = globals.Clock;
		foreach (var simObject in live) {
			if (!ActionClear(simObject)) {
				continue;
			}

			var tables = simObject.Sensors;
			clock.CountdownTimerTick(ref tables.LineOfSightCountdown);
			if (clock.CountdownTimerTick(ref tables.DecayCountdown) == 0) {
				tables.DecayCountdown = (short)(DecayReload + globals.Random.NextBelow(ReloadJitter));
				DecayContacts(simObject, globals);
			}
		}

		// The last object with +0x9d that passes is the one held back.
		IEs1SimObject? held = null;
		foreach (var simObject in live) {
			if (!ActionClear(simObject) || simObject.Immobilized || simObject.Destroyed || simObject.Group!.Side != 0) {
				continue;
			}

			if (simObject.IsPlayer) {
				held = simObject;
			} else {
				Sweep(simObject, globals, playerMech);
			}
		}

		if (held != null) {
			Sweep(held, globals, playerMech);
		}

		foreach (var simObject in live) {
			simObject.EngagementGate = false;
		}
	}

	/// <summary>
	/// <c>Detection_Sweep</c> (<c>0000e85c</c>)(<paramref name="machine"/>), over every object neither immobilized nor
	/// destroyed whose group's action is clear and whose side is 1 (docs/es1/simulation/sensor-model.md#looking-around--detection_sweep-0000e85c):
	/// the radar's paint within <see cref="RadarRange"/> when either emits and the two are not both radar-visible; then
	/// within <see cref="ContactScanRange"/>, unless each knows of the other, each one's look through its sensor arc on the
	/// bearing between them, the machine's turret twist added and the other's half a turn, or with each knowing of the
	/// other, the engagement within <see cref="EngagementRange"/>.
	/// </summary>
	public static void Sweep(IEs1SimObject machine, Es1SimGlobals globals, Es1Mech playerMech) {
		int scanRange = ContactScanRange(globals);
		foreach (var other in globals.LiveObjects) {
			if (other.Immobilized || other.Destroyed || !ActionClear(other) || other.Group!.Side != 1) {
				continue;
			}

			int distance = other.Position.ApproxDistanceTo(machine.Position);
			if ((!machine.RadarVisible || !other.RadarVisible) && (machine.RadarOn || other.RadarOn) && distance < RadarRange
					&& LineOfSight(machine, other, globals)) {
				machine.AiEnemySighted(other, globals, playerMech);
				machine.RadarVisible = true;
				other.RadarVisible = true;
			}

			if (distance >= scanRange) {
				continue;
			}

			if (!Es1SensorTables.Get(machine.Sensors.Contacts, other.ListSlot)
					|| !Es1SensorTables.Get(other.Sensors.Contacts, machine.ListSlot)) {
				short bearing = Es1MissionGroup.HeadingToward(machine.Position, other.Position);
				if (machine.BearingInSensorArc(unchecked((short)(machine.TorsoTwistAngle + bearing)))
						&& LineOfSight(machine, other, globals)) {
					machine.AiEnemySighted(other, globals, playerMech);
					if (!machine.IsPlayer) {
						ShareContact(machine, other, globals);
					} else {
						Es1SensorTables.Set(machine.Sensors.Contacts, other.ListSlot, true);
					}
				}

				if (other.BearingInSensorArc(unchecked((short)(other.TorsoTwistAngle + bearing - 0x8000)))
						&& LineOfSight(machine, other, globals)) {
					ShareContact(other, machine, globals);
				}
			} else if (distance < EngagementRange) {
				other.Engaged = true;
				ActivateEngagementAction(machine);
				machine.Engaged = true;
				ActivateEngagementAction(other);
			}
		}
	}

	/// <summary>
	/// <c>Detection_LineOfSight</c> (<c>0000e55c</c>)(<paramref name="machine"/>, <paramref name="other"/>): when the
	/// <i>other's</i> line-of-sight counter is 0, the <i>machine's</i> reloaded with 5000 + a draw below 1000 and the
	/// machine's cache byte for the other set to whether <c>Sim_RaycastShapes</c>(0, the other's position, the machine's)
	/// strikes no zone shape; then the cached byte (docs/es1/simulation/sensor-model.md#line-of-sight--detection_lineofsight-0000e55c).
	/// </summary>
	public static bool LineOfSight(IEs1SimObject machine, IEs1SimObject other, Es1SimGlobals globals) {
		if (other.Sensors.LineOfSightCountdown == 0) {
			machine.Sensors.LineOfSightCountdown = (short)(LineOfSightReload + globals.Random.NextBelow(ReloadJitter));
			bool struck = globals.ZoneShapes.RaycastShapes(0, other.Position, machine.Position, out _);
			Es1SensorTables.Set(machine.Sensors.LineOfSight, other.ListSlot, !struck);
		}

		return Es1SensorTables.Get(machine.Sensors.LineOfSight, other.ListSlot);
	}

	/// <summary>
	/// <c>Detection_ShareContact</c> (<c>0000e618</c>)(<paramref name="spotter"/>, <paramref name="contact"/>): when the two
	/// are on different sides, each object whose group's action is clear on the spotter's side, the spotter included, within
	/// <see cref="ShareRange"/> of the contact takes it as a contact, and within <see cref="EngagementRange"/> engages the
	/// contact and runs its own engagement action (docs/es1/simulation/sensor-model.md#passing-a-contact-on--detection_sharecontact-0000e618).
	/// <c>Mech_ShareContact</c> (<c>0000d760</c>), the base's slot <c>+0x54</c>, calls it for a hit object with the shooter.
	/// </summary>
	public static void ShareContact(IEs1SimObject spotter, IEs1SimObject contact, Es1SimGlobals globals) {
		byte side = spotter.Group!.Side;
		if (side == contact.Group!.Side) {
			return;
		}

		foreach (var simObject in globals.LiveObjects) {
			if (!ActionClear(simObject) || simObject.Group!.Side != side) {
				continue;
			}

			int distance = contact.Position.ApproxDistanceTo(simObject.Position);
			if (distance < ShareRange) {
				Es1SensorTables.Set(simObject.Sensors.Contacts, contact.ListSlot, true);
				if (distance < EngagementRange) {
					contact.Engaged = true;
					ActivateEngagementAction(simObject);
				}
			}
		}
	}

	/// <summary>
	/// <c>Detection_DecayContacts</c> (<c>0000e41c</c>)(<paramref name="simObject"/>): its <c>+0x8f</c> cleared, then each
	/// object whose group's action is clear, on another side, that it holds as a contact, dropped from both objects'
	/// contacts unless it lies within <see cref="DecayRange"/> on the ground plane and the line of sight to it is clear
	/// (docs/es1/simulation/sensor-model.md#decay--detection_decaycontacts-0000e41c).
	/// </summary>
	public static void DecayContacts(IEs1SimObject simObject, Es1SimGlobals globals) {
		simObject.RadarVisible = false;
		byte side = simObject.Group!.Side;
		foreach (var other in globals.LiveObjects) {
			if (!ActionClear(other) || other.Group!.Side == side
					|| !Es1SensorTables.Get(simObject.Sensors.Contacts, other.ListSlot)) {
				continue;
			}

			var (a, b) = (other.Position, simObject.Position);
			if (SimMath.FastMagnitude2D(b.X - a.X, b.Y - a.Y) < DecayRange && LineOfSight(simObject, other, globals)) {
				continue;
			}

			Es1SensorTables.Set(simObject.Sensors.Contacts, other.ListSlot, false);
			Es1SensorTables.Set(other.Sensors.Contacts, simObject.ListSlot, false);
		}
	}

	/// <summary>
	/// <c>Mech_AiEnemySighted</c> (<c>0000e760</c>)(<paramref name="machine"/>, <paramref name="sighted"/>), the HERC's slot
	/// <c>+0x4c</c> (docs/es1/simulation/sensor-model.md#the-enemy-sighted-callout--mech_aienemysighted-0000e760): the byte
	/// <c>000af264</c> set; for the player's machine the sighted object's latch set; for another machine the rate limit
	/// ticked, then, when the sighted object's class is not 1, the machine is in <c>PlayerMech</c>'s group, the latch is
	/// clear, the object is not radar-visible and neither the machine nor <c>PlayerMech</c> knows of it, the latch set and,
	/// with the rate limit run out, message <see cref="EnemySightedMessage"/> posted on the pilot channel
	/// (<see cref="Messages.Es1PilotChannel.PostSquadMessage"/>, not forced) and the limit reloaded with 10000.
	/// </summary>
	public static void MechAiEnemySighted(Es1Mech machine, IEs1SimObject sighted, Es1SimGlobals globals, Es1Mech playerMech) {
		var callout = globals.Sensors;
		callout.Sighted = true;
		short slot = sighted.ListSlot;
		if (machine.IsPlayer) {
			SetLatch(callout, slot);
			return;
		}

		globals.Clock.CountdownTimerTick(ref callout.CalloutRateLimit);
		if (sighted.TargetClass == UncalledClass || !ReferenceEquals(machine.Group, playerMech.Group) || Latched(callout, slot)
				|| sighted.RadarVisible || Es1SensorTables.Get(machine.Sensors.Contacts, slot)
				|| Es1SensorTables.Get(playerMech.Sensors.Contacts, slot)) {
			return;
		}

		SetLatch(callout, slot);
		if (callout.CalloutRateLimit == 0) {
			Messages.Es1PilotChannel.PostSquadMessage(globals, machine, false, EnemySightedMessage);
			callout.CalloutRateLimit = CalloutReload;
		}
	}

	/// <summary>
	/// <c>SimObject_BearingInSensorArc</c> (<c>0000d738</c>): whether <paramref name="bearing"/> lies within
	/// ±<see cref="SensorArcHalfWidth"/> of <paramref name="heading"/>, the two sign-extended to 32 bits, so the sum does
	/// not wrap (docs/es1/simulation/sim-object-base.md#the-sensor-arc).
	/// </summary>
	public static bool BearingInSensorArc(short heading, short bearing) =>
		(uint)(bearing - heading + SensorArcHalfWidth) < SensorArcHalfWidth * 2;

	/// <summary>
	/// <c>Detection_ContactScanRange</c> (<c>0001f814</c>): <see cref="ShortScanRange"/> when header word 8 is 1, otherwise
	/// <see cref="LongScanRange"/> (docs/es1/simulation/world.md#small-functions).
	/// </summary>
	public static int ContactScanRange(Es1SimGlobals globals) => globals.HeaderWord8 == 1 ? ShortScanRange : LongScanRange;

	/// <summary>The range within which a radar-visible object is known.</summary>
	public const int KnownRange = 1000000;

	/// <summary>
	/// <c>Ai_KnowsObject</c> (<c>0000d808</c>)(<paramref name="machine"/>, <paramref name="simObject"/>)
	/// (docs/es1/simulation/sim-object-base.md#what-an-ai-machine-knows-and-may-target): the object radar-visible within
	/// <see cref="KnownRange"/> of the machine, or the machine's contact byte for the object's slot set.
	/// </summary>
	public static bool KnowsObject(IEs1SimObject machine, IEs1SimObject simObject) =>
		simObject.RadarVisible && simObject.Position.ApproxDistanceTo(machine.Position) < KnownRange
		|| Es1SensorTables.Get(machine.Sensors.Contacts, simObject.ListSlot);

	/// <summary>Whether the object's group holds no action (<c>+0x16</c>); an object with no group never, an engine guard.</summary>
	private static bool ActionClear(IEs1SimObject simObject) => simObject.Group is { Action: null };

	/// <summary>The object's engagement action run unless it is null or the object's gate is set.</summary>
	private static void ActivateEngagementAction(IEs1SimObject simObject) {
		if (simObject.EngagementAction != null && !simObject.EngagementGate) {
			// PLACEHOLDER: Action_Activate (0001cc5c)(action), the actions, plan step 8.
		}
	}

	private static bool Latched(Es1SensorGlobals callout, short slot) =>
		slot >= 0 && slot < Es1SensorGlobals.LatchSlots && callout.CalledInLatch[slot] != 0;

	/// <summary>The latch's byte for a slot set; a slot past its bytes, which retail does not bound, sets nothing, an engine guard.</summary>
	private static void SetLatch(Es1SensorGlobals callout, short slot) {
		if (slot >= 0 && slot < Es1SensorGlobals.LatchSlots) {
			callout.CalledInLatch[slot] = 1;
		}
	}
}
