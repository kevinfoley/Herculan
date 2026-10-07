using Herculan.Engine.Numerics;
using Herculan.Engine.Sim.Ai;

namespace Herculan.Engine.Sim;

/// <summary>
/// The transport's tick — <c>StructureTransportVtable</c>'s <c>+0x18</c> slot,
/// <c>Base_TransportThinkTick</c> (<c>004045c8</c>), which type <c>0x22</c> alone installs
/// (docs/retail/simulation/structure-behaviour.md, "The transport").
/// </summary>
public sealed partial class BaseObject {
	/// <summary>What a transport fires. Null leaves one unarmed; no other class reads it.</summary>
	public TransportArmament? TransportArmament { get; init; }

	/// <summary>
	/// <c>base+0x209</c> — the three weapon stations' state, <c>0x1f</c> bytes each. Built on the first
	/// tick, since only the one class has any.
	/// </summary>
	private WeaponStation[]? _stations;

	/// <summary>
	/// <c>base+0xa5</c> as the transport's tick latches it, once every component but the core is at
	/// full damage. See <see cref="Disarmed"/>.
	/// </summary>
	private bool _transportDisarmed;

	/// <summary>
	/// <c>Base_TransportThinkTick</c> (<c>004045c8</c>) — one tick of a transport.
	///
	/// <para>A fallen transport hands the tick to <see cref="ThinkTick"/>. A standing one runs its death
	/// sequence, then turns its own heading <see cref="FirstStationBearing"/> and runs
	/// <see cref="StationTick"/> three times, turning <see cref="StationSpacing"/> after each, and puts
	/// the heading back. So each station acquires, aims and fires in its own frame, and the object is
	/// never seen facing any of them.</para>
	///
	/// <para>The disarm test comes first and does not skip the stations on the tick it latches; from
	/// the next tick on they are skipped altogether.</para>
	/// </summary>
	private void TransportThinkTick(SimWorld world) {
		if (Destroyed) {
			ThinkTick(world);
			return;
		}

		DeathSequenceTick(world);

		int heading = Heading;
		Heading = (heading + FirstStationBearing) & 0xffff;

		if (!_transportDisarmed && TransportArmament is { } armament) {
			if (EveryStationComponentDown()) {
				_transportDisarmed = true;
			}

			_stations ??= new[] { new WeaponStation(), new WeaponStation(), new WeaponStation() };
			for (int station = 0; station < _stations.Length; station++) {
				StationTick(world, armament, station, _stations[station]);
				Heading = (Heading + StationSpacing) & 0xffff;
			}
		}

		Heading = heading;
	}

	/// <summary>
	/// Whether every component after the first, the core, is at full damage — the test that latches
	/// <see cref="_transportDisarmed"/>. On retail data that is the three launcher pods and the three
	/// beam housings on them.
	/// </summary>
	private bool EveryStationComponentDown() {
		int i = 1;
		while (i < Type.Components.Length && _damage[i] >= Type.Components[i].MaxDamage) {
			i++;
		}

		return i == Type.Components.Length;
	}

	/// <summary>
	/// One weapon station: acquire on its own <see cref="StationRetargetInterval"/> countdown, take the
	/// aim error from the structure's origin to the target's aim point against this station's heading,
	/// and run the three <see cref="TransportArmament.Slots"/>.
	///
	/// <para>A slot is live only while its component stands short of full damage: the launcher slot
	/// is component <c>1 + 2s</c> for station <c>s</c>, and both beam slots are component
	/// <c>2 + 2s</c>. A live slot steps its firing window, then fires when its refire countdown has
	/// run out, its window is open, the aim error is inside the slot's arcs on both axes and the
	/// target is inside its range. A dead one steps neither countdown.</para>
	/// </summary>
	private void StationTick(SimWorld world, TransportArmament armament, int index, WeaponStation station) {
		if (SimMath.CountdownTimerTick(ref station.RetargetTimer) == 0) {
			station.Target = AiTargeting.SelectTarget(world, this, TargetFilter.RejectOwnClass, StationCone);
			station.RetargetTimer = StationRetargetInterval;
		}

		if (station.Target is not { } target) {
			return;
		}

		int range = Position.ApproxDistanceTo(target.Position);
		var aim = target.AimPoint;
		var (pitch, _, yaw) = SimTrig.EulerToward(aim, Position);
		short pitchError = (short)(pitch - Pitch);
		short yawError = (short)(yaw - Heading);

		for (int s = 0; s < TransportArmament.SlotCount; s++) {
			int component = index * 2 + (s != 0 ? 1 : 0) + 1;

			// Only type 0x22 builds this class, and it has the seven components the indexing needs;
			// the original does not check.
			if (component >= Type.Components.Length
					|| _damage[component] == Type.Components[component].MaxDamage) {
				continue;
			}

			ref var slot = ref station.Slots[s];
			if (SimMath.CountdownTimerTick(ref slot.WindowTimer) == 0) {
				slot.WindowOpen = !slot.WindowOpen;
				int phase = slot.WindowOpen ? 1 : 0;
				slot.WindowTimer = (short)(WindowBase[phase] + world.Random.NextBelow(WindowSpread[phase]));
			}

			var weapon = armament.Slots[s];
			if (SimMath.CountdownTimerTick(ref slot.RefireTimer) != 0 || !slot.WindowOpen
					|| (ushort)(yawError + weapon.YawArc) >= (ushort)(weapon.YawArc * 2)
					|| (ushort)(pitchError + weapon.PitchArc) >= (ushort)(weapon.PitchArc * 2)
					|| weapon.Range <= range) {
				continue;
			}

			// The muzzle offset turns with this station's heading in the ground plane alone; its height
			// is added as it stands.
			var offset = Transform3.FromEuler(0, 0, (short)Heading).RotateVector(weapon.OffsetX, weapon.OffsetY, 0);
			var muzzle = new Vec3i(Position.X + offset.X, Position.Y + offset.Y, Position.Z + weapon.OffsetZ);

			if (s == 0) {
				FireStationMissile(world, armament, target, muzzle);
			} else {
				FireStationBeam(world, armament, aim, muzzle);
			}

			slot.RefireTimer = weapon.RefireDelay;
		}
	}

	/// <summary>
	/// The launcher slot. The round leaves along the station's own facing, not toward the target: it
	/// is the lock that brings it round. The target is installed as this object's
	/// <see cref="SimObject.Target"/> across the launch and cleared straight after, purely so
	/// <see cref="SimWorld.FireRocket"/> hands it to the round — a transport holds no target of its
	/// own otherwise.
	/// </summary>
	private void FireStationMissile(SimWorld world, TransportArmament armament, SimObject target,
			Vec3i muzzle) {
		if (armament.Missile is not { } missile) {
			return;
		}

		Target = target;
		world.FireRocket(missile, muzzle, (Pitch, Roll, (short)Heading), 0, this);
		Target = null;
	}

	/// <summary>
	/// A beam slot, through the same <c>Bullet_FireBurst</c> path a HERC's lasers take: the ray runs
	/// from the muzzle straight at the target's aim point, so unlike the launcher a beam is aimed.
	/// </summary>
	private void FireStationBeam(SimWorld world, TransportArmament armament, Vec3i aim, Vec3i muzzle) {
		if (armament.Beam is not { } beam) {
			return;
		}

		var (pitch, roll, yaw) = SimTrig.EulerToward(aim, muzzle);
		var frame = Transform3.FromEuler(pitch, roll, yaw);
		frame.X = muzzle.X;
		frame.Y = muzzle.Y;
		frame.Z = muzzle.Z;

		world.FireBeam(new WeaponShot(frame, armament.BeamRange, beam, armament.BeamPower, this));
	}

	/// <summary>The first station's bearing off the object's own heading — 30 degrees.</summary>
	private const int FirstStationBearing = 0x1555;

	/// <summary>The bearing between one station and the next — 120 degrees.</summary>
	private const int StationSpacing = 0x5554;

	/// <summary>
	/// <c>Ai_SelectTarget</c>'s cone for a station, either side of its own heading — about 67 degrees,
	/// so between them the three see all the way round with overlaps.
	/// </summary>
	private const short StationCone = 0x3000;

	/// <summary>Each station's retarget reload, in timer units — about 4.9 s.</summary>
	private const short StationRetargetInterval = 10000;

	/// <summary>
	/// The firing window's reload, by the phase it is entering — the pair at <c>004973f4</c>, 0 for
	/// shut and 1 for open. <see cref="WindowSpread"/> adds a random part.
	/// </summary>
	private static readonly short[] WindowBase = { 5000, 5000 };

	/// <summary>
	/// The bound of the random part of the window's reload — the pair at <c>004973f8</c>. With
	/// <see cref="WindowBase"/>, each phase lasts 5000 to 9999 timer units, about 2.4 to 4.9 s.
	/// </summary>
	private static readonly short[] WindowSpread = { 5000, 5000 };

	/// <summary>One station's <c>0x1f</c>-byte record: three slots, then the retarget countdown and the target.</summary>
	private sealed class WeaponStation {
		/// <summary>Three 8-byte slot records at <c>+0x00</c>.</summary>
		public readonly StationSlot[] Slots = new StationSlot[TransportArmament.SlotCount];

		/// <summary><c>+0x18</c> — the retarget countdown.</summary>
		public short RetargetTimer;

		/// <summary>
		/// <c>+0x1b</c> — what this station is shooting at. A plain pointer, not <c>+0x1a4</c>, so it
		/// takes no part in anything that reads the shared target field.
		/// </summary>
		public SimObject? Target;
	}

	/// <summary>One weapon slot's 8-byte record.</summary>
	private struct StationSlot {
		/// <summary><c>+0x00</c> — the refire countdown, reloaded from <see cref="HercWorks.Core.Data.Struct.Dbsim.LcWeaponSlot.RefireDelay"/>.</summary>
		public short RefireTimer;

		/// <summary><c>+0x03</c> — the firing window's countdown.</summary>
		public short WindowTimer;

		/// <summary><c>+0x06</c> — whether the window is open.</summary>
		public bool WindowOpen;
	}
}
