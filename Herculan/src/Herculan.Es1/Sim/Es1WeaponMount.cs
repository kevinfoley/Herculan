using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Part;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Input;
using Herculan.Es1.Projectiles;
using Herculan.Es1.View;
using Herculan.Es1.WeaponRows;

namespace Herculan.Es1.Sim;

/// <summary>The five mount classes the factory builds by weapon id (docs/es1/simulation/weapon-mount-classes.md#the-classes).</summary>
public enum Es1MountKind {
	/// <summary><c>WeaponMount_CtorEnergy</c>, weapons 4, 6–8, 12, 14 and 15, <c>0x7f</c> bytes.</summary>
	Energy,

	/// <summary><c>WeaponMount_CtorEnergy</c> with <c>ElfMountVtable</c>, weapon 3, <c>0x82</c> bytes.</summary>
	Elf,

	/// <summary><c>WeaponMount_CtorAmmunition</c>, weapons 0–2, 9–11 and 16, <c>0x81</c> bytes.</summary>
	Ammunition,

	/// <summary><c>WeaponMount_CtorAmmunition</c> with <c>GrenadeMountVtable</c>, weapon 5, <c>0x85</c> bytes.</summary>
	Grenade,

	/// <summary><c>EcmMount_Ctor</c>, weapon 13, <c>0x7f</c> bytes.</summary>
	Ecm,
}

/// <summary>
/// What <c>WeaponMount_PrepareShot</c> leaves for its dispatch, three globals in retail
/// (docs/es1/simulation/weapon-mount-classes.md#firing): the shot transform <c>WeaponMount_ShotTransform</c>
/// (<c>000af144</c>), the muzzle's world point <c>WeaponMount_ShotMuzzlePoint</c> (<c>000af164</c>) and the firing angles
/// <c>WeaponMount_ShotEuler</c> (<c>000af170</c>).
/// </summary>
public readonly record struct Es1MountShot(Transform3 Transform, Vec3i Muzzle, (short Pitch, short Roll, short Heading) Angles);

/// <summary>
/// One fitted weapon, a <c>CONFIG_PART</c> whose model is the weapon's shape (docs/es1/simulation/weapon-mount-classes.md),
/// as its class's constructor leaves it, with the slots the manager calls (docs/es1/simulation/weapon-mount-classes.md#the-slots):
/// ready, trigger and fire (docs/es1/simulation/weapon-mount-classes.md#firing), the pool turn
/// (docs/es1/simulation/weapon-mount-classes.md#charging), the slots that build and feed its cockpit row
/// (docs/es1/simulation/weapon-mount-classes.md#gauges) and the two the chain's toggle calls. Each slot switches on
/// <see cref="Kind"/> where retail dispatches through the class's table.
/// </summary>
public sealed class Es1WeaponMount {
	/// <summary>The charge target and capacitor an energy mount starts with, <c>Math_Q10Multiply</c>(1200, 820).</summary>
	public const short InitialCharge = 960;

	/// <summary>The full charge, which the energy gauge push scales the Q10 fractions by, and the power of every ELF shot.</summary>
	public const int FullCharge = 1200;

	/// <summary>The charge target <c>WeaponMount_WakeCapacitor</c> sets.</summary>
	public const short WakeCharge = 0x334;

	/// <summary>What the ammunition subtype slot, <see cref="AmmunitionSubtype"/>, answers for a mount that fires no rocket.</summary>
	public const short NotALauncher = 5;

	/// <summary><c>WeaponMount_ChargeCapacitor</c>'s rate, and its rate while <see cref="MidCharge"/> is set.</summary>
	public const short ChargeRate = 20, MidChargeRate = 60;

	/// <summary>The most <c>WeaponMount_ChargeCapacitor</c> gives back from a capacitor over its target.</summary>
	public const short GiveBack = 5;

	/// <summary>The rate, per <c>Math_IntegrateRateOverTick</c>, at which the ammunition row's count rolls down to the rounds.</summary>
	public const short RoundsRollRate = 250;

	/// <summary><c>GrenadeMount_TriggerHeld</c>'s throw distance at a press, its growth a call, and the distance past which it throws.</summary>
	public const int ThrowStart = 20000, ThrowGrowth = 1000, ThrowLimit = 100000;

	/// <summary>The barrel count <c>WeaponMount_FireDispatch_GunBeam</c> fires three shots for.</summary>
	public const short ThreeBarrels = 3;

	/// <summary>
	/// The templates' names, the 17-entry table at <c>000911fc</c> the loader stores at each template's <c>+0x52</c>
	/// (docs/es1/formats/weapons-dat-sim.md#the-load), by weapon id.
	/// </summary>
	public static readonly string[] TemplateNames = [
		"ATC20", "ATC35", "ATC50", "ELF", "EMP", "GREN", "LAS100", "LAS200", "LAS300", "6", "8", "10", "PBEAM", "ECM", "EMP",
		"PBEAM", "MISSL",
	];

	/// <summary><c>MissileAmmoNameTable</c> (<c>000910ac</c>), the rocket subtypes' names.</summary>
	public static readonly string[] MissileNames = ["SARH", "ARH", "ARM", "EO"];

	// +0x33 and +0x3b, byte 0 of the two flag blocks: fired, held while the mount fires every pool turn, and fired since
	// the last pool turn. Byte 1 of each is the power hand-off, PowerHandOff and _powerHandOffPending. No other byte of
	// the blocks has a writer.
	private bool _fired, _firedPending;
	private bool _powerHandOffPending;

	// +0x31, the refire countdown, and +0x80, the ELF's cell timer.
	private short _refireCountdown;
	private short _cellTimer;

	/// <summary>
	/// <c>WeaponMount_CtorBase</c> (<c>0000a530</c>) and the class constructor <paramref name="kind"/> names.
	/// <paramref name="model"/> is the weapon's model for a mounting code below 4, or null.
	/// </summary>
	public Es1WeaponMount(Es1MountKind kind, short weaponId, GunLayout.HardpointEntry hardpoint,
			Weapons.WeaponMountTemplate template, ProjectileData.Projectile? projectile, TSObject? model,
			Func<int, AnimationThread> addThread) {
		Kind = kind;
		WeaponId = weaponId;
		Hardpoint = hardpoint;
		Template = template;
		Projectile = projectile;
		Model = model;
		// +0x14, the model's private cell frames (ShapeInstance_CtorCellFrames), one per sequence, each at 0.
		CellFrames = new short[(model as TSShape)?.SequenceList?.Length ?? 0];

		// +0x5b and +0x5f: a thread on the machine for each of the hardpoint's two convergence nodes that is not negative.
		PitchThread = hardpoint.ConvergencePitchNode >= 0 ? addThread(hardpoint.ConvergencePitchNode) : null;
		YawThread = hardpoint.ConvergenceYawNode >= 0 ? addThread(hardpoint.ConvergenceYawNode) : null;

		// Step 4: the model moved to the mount point, once, in this mount's own copy.
		if (model != null) {
			var offset = MountPointOffset;
			TranslatePointLists(model, (short)offset.X, (short)offset.Y, (short)offset.Z);
		}

		Selectable = kind != Es1MountKind.Ecm;
		switch (kind) {
			case Es1MountKind.Energy:
			case Es1MountKind.Elf:
				ChargeTarget = InitialCharge;
				Capacitor = InitialCharge;
				PowerHandOff = true;
				_powerHandOffPending = true;
				break;
			case Es1MountKind.Ammunition:
			case Es1MountKind.Grenade:
				ChargeTarget = template.MagazineSize;
				Capacitor = template.MagazineSize << 8;
				break;
		}
	}

	public Es1MountKind Kind { get; }

	/// <summary>The weapon id, the template's index.</summary>
	public short WeaponId { get; }

	/// <summary><c>+0x18</c>, the hardpoint's <c>.GL</c> record.</summary>
	public GunLayout.HardpointEntry Hardpoint { get; }

	/// <summary><c>+0x1c</c>, the weapon's template.</summary>
	public Weapons.WeaponMountTemplate Template { get; }

	/// <summary><c>+0x20</c>, the projectile record; null for the ECM, and for <c>GREN</c>'s index past the table (docs/es1/formats/weapons-dat-sim.md#open).</summary>
	public ProjectileData.Projectile? Projectile { get; }

	/// <summary>
	/// <c>+0x10</c>, the weapon's model, a root of <c>dts\mechwpns</c> read for this mount alone and moved to its mount
	/// point; null for the invisible mounting, and once <see cref="Destroy"/> has cleared it.
	/// </summary>
	public TSObject? Model { get; private set; }

	/// <summary><c>+0x14</c>, the model's private cell frames, one per sequence of the model; the muzzle flash and the ELF's spin-up step the first.</summary>
	public short[] CellFrames { get; }

	/// <summary>The transform id <c>Mech_ConfigureLoadout</c> stamps on the model's parts: the hardpoint bone's part's in the machine's shape.</summary>
	public int ModelTransformId { get; set; } = -1;

	/// <summary><c>+0x5b</c> and <c>+0x5f</c>, the convergence threads.</summary>
	public AnimationThread? PitchThread { get; }

	/// <inheritdoc cref="PitchThread"/>
	public AnimationThread? YawThread { get; }

	/// <summary>
	/// <c>+0x24</c>–<c>+0x29</c>, the convergence angles, pitch, roll and yaw, which <see cref="Es1Turret.ConvergeGuns"/>
	/// writes each pitch tick and the constructor zeroes.
	/// </summary>
	public (short Pitch, short Roll, short Yaw) Convergence { get; set; }

	/// <summary><c>+0x31</c>, the refire countdown, which <see cref="PrepareShot"/> sets to the template's refire delay and the pool turn counts down.</summary>
	public short RefireCountdown => _refireCountdown;

	/// <summary><c>+0x44</c>, the muzzle flash playing: the pool turn steps the first cell frame until it wraps to 0.</summary>
	public bool MuzzleFlash { get; private set; }

	/// <summary><c>+0x46</c>, the grenade launcher's throw being held.</summary>
	public bool Charging { get; private set; }

	/// <summary><c>+0x47</c>, the ELF's spin-up running.</summary>
	public bool SpinningUp { get; private set; }

	/// <summary><c>+0x48</c>, the ELF's spin-up done: the trigger fires until released.</summary>
	public bool SpinUpLatched { get; private set; }

	/// <summary><c>+0x81</c>, the grenade launcher's throw distance (<c>int32</c>).</summary>
	public int ThrowDistance { get; private set; }

	/// <summary><c>+0x49</c>, destroyed, which <see cref="Destroy"/> sets; the cockpit row's push hands it to the row.</summary>
	public bool Destroyed { get; private set; }

	/// <summary>
	/// <c>WeaponMount_Destroy</c> (<c>0000b968</c>)(mount, <paramref name="throwGun"/>, <paramref name="machine"/>)
	/// (docs/es1/simulation/weapon-mount-classes.md#losing-a-mount): for a mount not yet destroyed, the model cleared, so
	/// the hardpoint draws nothing, and <see cref="Destroyed"/> set. The mount point it then works out in the world feeds
	/// the thrown gun alone.
	/// </summary>
	public void Destroy(bool throwGun, Es1Mech machine) {
		if (Destroyed) {
			return;
		}

		Model = null;
		Destroyed = true;
		// PLACEHOLDER: with throwGun and the hardpoint's mounting code (.GL +6) below 4, the gun thrown from the mount point
		// (the muzzle offset through the bone's node and the machine's frame): 00006168(0, the point, the template's +0x26
		// root of WeaponModels_Mechwpn2Roots) given g_TSDetailBiasForMounts at its +0x4a and launched through 00006378 with
		// the third angle of the bearing from the machine's slot +0x28 point and the hardpoint's dword +0x18; the debris,
		// plan step 7e.
		_ = (throwGun, machine);
	}

	/// <summary><c>+0x4b</c>, linked with its partner.</summary>
	public bool Linked { get; set; }

	/// <summary><c>+0x4c</c>, the byte set while the mount can be armed; the ECM's constructor clears it.</summary>
	public bool Selectable { get; set; }

	/// <summary><c>+0x7b</c>: an energy mount's charge target, an ammunition mount's rounds.</summary>
	public short ChargeTarget { get; set; }

	/// <summary>
	/// <c>+0x7d</c>: an energy mount's capacitor (<c>int16</c>), an ammunition mount's rounds in 256ths (<c>int32</c>), the
	/// ECM's switch (<c>int16</c>).
	/// </summary>
	public int Capacitor { get; set; }

	/// <summary>
	/// <c>+0x34</c>, byte 1 of the <c>+0x33</c> block, the power-level hand-off: set, the energy gauge push sends the charge
	/// target out to the charge bar; clear, it reads the bar back. The energy constructor sets it with its twin <c>+0x3c</c>,
	/// and the pool turn ANDs it with the twin and clears the twin, so it survives one pool turn
	/// (docs/es1/simulation/weapon-mount-classes.md#gauges).
	/// </summary>
	public bool PowerHandOff { get; private set; }

	/// <summary><c>+0x43</c>, the mid-charge byte, which <c>WeaponMount_CtorBase</c> clears.</summary>
	public bool MidCharge { get; set; }

	/// <summary><c>+0x77</c>, the mount's cockpit row, which its gauge factory builds; null before <see cref="CreateGauge"/>.</summary>
	public Es1WeaponGauge? Gauge { get; private set; }

	/// <summary>The cell frame of the model's sequence <paramref name="sequence"/>; 0 past the frames.</summary>
	public short CellFrame(int sequence) => sequence >= 0 && sequence < CellFrames.Length ? CellFrames[sequence] : (short)0;

	/// <summary>
	/// <c>WeaponMount_GetDisplayName</c> (<c>0000aa9c</c>): the template's name; for a rocket record (its type 0), the
	/// subtype's name from <see cref="MissileNames"/> with the template's name after it, so a rack of ten <c>ARH</c>
	/// reads <c>ARH10</c>. A subtype past the table, which no retail record has, gives the template's name alone, an
	/// engine guard.
	/// </summary>
	public string DisplayName() {
		string name = WeaponId >= 0 && WeaponId < TemplateNames.Length ? TemplateNames[WeaponId] : "";
		if (Projectile is not { Type.Val: 0 } rocket) {
			return name;
		}

		short subtype = rocket.SubtypeId;
		return subtype >= 0 && subtype < MissileNames.Length ? MissileNames[subtype] + name : name;
	}

	/// <summary>
	/// Slot <c>+0x5c</c>, the gauge match: <c>WeaponMount_EnergyMatchGauge</c> (<c>0000a4f8</c>) and
	/// <c>WeaponMount_AmmoMatchGauge</c> (<c>0000a514</c>) answer whether <paramref name="gauge"/> is the mount's row;
	/// the ECM's, <c>WeaponMount_MatchGaugeNever</c> (<c>0000a4f0</c>), never.
	/// </summary>
	public bool MatchesGauge(Es1WeaponGauge gauge) => Kind != Es1MountKind.Ecm && ReferenceEquals(gauge, Gauge);

	/// <summary>
	/// Slot <c>+0x64</c>, the ammunition subtype (docs/es1/simulation/weapon-mount-classes.md#the-slots): an ammunition or
	/// grenade mount's <c>WeaponMount_GetAmmoType</c> (<c>0000ac70</c>), for a rocket record (its type 0) the record's
	/// subtype with the rounds <see cref="ChargeTarget"/> in <paramref name="rounds"/>; any other mount's, and an
	/// ammunition mount's without a rocket record, <c>WeaponMount_GetEnergyAmmoType</c> (<c>0000ac54</c>),
	/// <see cref="NotALauncher"/> with 0. A mount with no record, where retail reads one through its pointer, takes the
	/// second, an engine guard.
	/// </summary>
	public short AmmunitionSubtype(out short rounds) {
		if (Kind is Es1MountKind.Ammunition or Es1MountKind.Grenade && Projectile is { Type.Val: 0 } rocket) {
			rounds = ChargeTarget;
			return rocket.SubtypeId;
		}

		rounds = 0;
		return NotALauncher;
	}

	/// <summary>
	/// Slot <c>+0x4c</c>, the energy priority: an energy or ELF mount's <c>WeaponMount_GetEnergyPriority</c>
	/// (<c>0000b8c4</c>), 10000 while mid-charge and the charge target otherwise; every other class's
	/// <c>WeaponMount_GetEnergyPriorityZero</c> (<c>0000c6ff</c>), 0.
	/// </summary>
	public int EnergyPriority => Kind is Es1MountKind.Energy or Es1MountKind.Elf ? MidCharge ? 10000 : ChargeTarget : 0;

	/// <summary>
	/// Slot <c>+0x40</c>, the wake: an energy or ELF mount's <c>WeaponMount_WakeCapacitor</c> (<c>0000b884</c>), the charge
	/// target <see cref="WakeCharge"/> unless destroyed; every other class's <c>WeaponMount_WakeCapacitorNoOp</c>
	/// (<c>0000c6f3</c>).
	/// </summary>
	public void Wake() {
		if (Kind is Es1MountKind.Energy or Es1MountKind.Elf && !Destroyed) {
			ChargeTarget = WakeCharge;
		}
	}

	/// <summary>
	/// <c>WeaponMount_RangeAllows</c> (<c>0000ac14</c>)(mount, <paramref name="range"/>): the template's minimum range &lt;
	/// <paramref name="range"/> &lt; its range, both bounds exclusive (docs/es1/simulation/weapon-mounts.md#readiness).
	/// </summary>
	public bool RangeAllows(int range) => Template.MinimumRange < range && range < Template.Range;

	/// <summary>
	/// Slot <c>+0x30</c>, ready (docs/es1/simulation/weapon-mount-classes.md#the-slots): an energy mount's
	/// <c>WeaponMount_EnergyCanFire</c> (<c>0000b278</c>), not destroyed, the refire countdown at 0 and the capacitor at
	/// least max(the template's low threshold, the charge target) when the low threshold is below the cost, or the cost
	/// otherwise; the ELF's <c>ElfMount_CanFire</c> (<c>0000b384</c>), not destroyed and the capacitor at least max(the low
	/// threshold, the charge target), or at least the cost while it fired on the last pool turn; an ammunition or grenade
	/// mount's <c>WeaponMount_AmmoCanFire</c> (<c>0000b340</c>), not destroyed, the countdown at 0 and rounds left; the
	/// ECM's <c>EcmMount_CanFireNever</c> (<c>0000b270</c>).
	/// </summary>
	public bool CanFire() {
		short low = Template.EnergyThresholdLow, cost = Template.ShotCost, charge = (short)Capacitor;
		switch (Kind) {
			case Es1MountKind.Energy: {
				short threshold = low < cost ? Math.Max(low, ChargeTarget) : cost;
				return !Destroyed && _refireCountdown == 0 && threshold <= charge;
			}
			case Es1MountKind.Elf:
				return !Destroyed && (Math.Max(low, ChargeTarget) <= charge || (_fired && cost <= charge));
			case Es1MountKind.Ammunition or Es1MountKind.Grenade:
				return !Destroyed && _refireCountdown == 0 && ChargeTarget != 0;
			default:
				return false;
		}
	}

	/// <summary>
	/// Slot <c>+0x34</c>, the trigger (docs/es1/simulation/weapon-mount-classes.md#triggers), on the input's trigger byte
	/// <c>+0x15</c>: an energy or ammunition mount's <c>WeaponMount_TriggerHeld</c> (<c>0000c768</c>), the byte itself;
	/// the ELF's <c>ElfMount_TriggerHeld</c> (<c>0000acb4</c>) and the grenade launcher's <c>GrenadeMount_TriggerHeld</c>
	/// (<c>0000ad54</c>), below; the ECM's <c>EcmMount_TriggerHeldNever</c> (<c>0000acab</c>).
	/// </summary>
	/// <param name="latches">The press-once latches the grenade launcher's throw latches the trigger on; with none it latches nothing.</param>
	public bool TriggerHeld(Es1PlayerInputBlock input, Es1ButtonLatches? latches) {
		byte trigger = input.Buttons[0];
		return Kind switch {
			Es1MountKind.Energy or Es1MountKind.Ammunition => trigger != 0,
			Es1MountKind.Elf => ElfTriggerHeld(trigger),
			Es1MountKind.Grenade => GrenadeTriggerHeld(trigger, latches),
			_ => false,
		};
	}

	/// <summary>
	/// <c>ElfMount_TriggerHeld</c>, the spin-up: before the latch, a press starts it with the cell timer at 0 and a release
	/// stops it, the first cell frame back at 0, and neither fires; once latched, the trigger byte, a release rewinding the
	/// first cell frame and dropping the latch. Retail writes the cell frame through the model's frames without testing
	/// for a model; a mount with none writes nothing, an engine guard.
	/// </summary>
	private bool ElfTriggerHeld(byte trigger) {
		if (!SpinUpLatched) {
			if (trigger == 0) {
				if (SpinningUp) {
					SetFirstCellFrame(0);
					SpinningUp = false;
				}
			} else if (!SpinningUp) {
				SpinningUp = true;
				_cellTimer = 0;
			}

			return false;
		}

		if (trigger == 0) {
			SetFirstCellFrame(0);
			SpinUpLatched = false;
		}

		return trigger != 0;
	}

	/// <summary>
	/// <c>GrenadeMount_TriggerHeld</c>, the hold-to-throw: a press starts the throw at <see cref="ThrowStart"/> and does not
	/// fire; held, the distance grows <see cref="ThrowGrowth"/> a call and does not fire until it passes
	/// <see cref="ThrowLimit"/>; a release, or passing the limit, fires, and still held latches the trigger,
	/// <c>Input_LatchButton</c> (<c>0004d8c8</c>)(1, 1), so the next build reads it released.
	/// </summary>
	private bool GrenadeTriggerHeld(byte trigger, Es1ButtonLatches? latches) {
		if (!Charging) {
			if (trigger != 0) {
				Charging = true;
				ThrowDistance = ThrowStart;
			}

			return false;
		}

		if (trigger != 0) {
			ThrowDistance += ThrowGrowth;
			if (ThrowDistance <= ThrowLimit) {
				return false;
			}

			latches?.Latch(1, 1);
		}

		return true;
	}

	/// <summary>
	/// Slot <c>+0x2c</c>, fire (docs/es1/simulation/weapon-mount-classes.md#firing), with the free-shot flag
	/// <paramref name="freeShot"/>: the class's dispatch, each opening with <see cref="PrepareShot"/>; the ECM's
	/// <c>EcmMount_FireNoOp</c> (<c>0000af3c</c>) does nothing.
	/// </summary>
	public void Fire(bool freeShot, Es1Mech machine, Es1SimGlobals globals) {
		switch (Kind) {
			case Es1MountKind.Energy:
				FireGunBeam(machine, globals);
				break;
			case Es1MountKind.Elf:
				FireElf(machine, globals);
				break;
			case Es1MountKind.Ammunition:
				FireMissile(freeShot, machine, globals);
				break;
			case Es1MountKind.Grenade:
				FireGrenade(freeShot, machine);
				break;
		}
	}

	/// <summary>The projectile record's subtype <c>+2</c>, the last argument of every shot function; 0 with no record, where retail reads through the pointer.</summary>
	private short ShotSubtype => Projectile?.SubtypeId ?? 0;

	/// <summary>The projectile record's type <c>+0</c>; −1 with no record, which no dispatch's test matches.</summary>
	private short ShotRecordType => Projectile?.Type?.Val ?? -1;

	/// <summary>Whether the hardpoint's mounting code, its signed byte <c>+6</c>, is a visible one, below 4.</summary>
	private bool VisibleHardpoint => (sbyte)Hardpoint.MountingCode < Es1MechType.InvisibleMounting;

	/// <summary>
	/// <c>WeaponMount_FireDispatch_GunBeam</c> (<c>0000b024</c>), the energy mount's: the muzzle flash on a visible
	/// hardpoint; for a beam record (type 4), the shot transform's translation moved to the muzzle and
	/// <see cref="Es1BeamTracer.FireBurst"/> at min(cost, capacitor) over the template's range, the power taken off the
	/// capacitor; otherwise, with the capacitor below the cost, <see cref="Es1Bullet.FirePowered"/> at the capacitor, two more
	/// for <see cref="ThreeBarrels"/> barrels from the muzzle triple with its X at 0 and negated, and the capacitor emptied;
	/// with it at or above the cost, the cost taken off and <see cref="Es1Bullet.Fire"/>.
	/// </summary>
	private void FireGunBeam(Es1Mech machine, Es1SimGlobals globals) {
		var shot = PrepareShot(machine);
		if (VisibleHardpoint) {
			MuzzleFlash = true;
		}

		short cost = Template.ShotCost;
		short charge = (short)Capacitor;
		if (ShotRecordType == Es1BeamTracer.RecordType) {
			var transform = shot.Transform;
			transform.X = shot.Muzzle.X;
			transform.Y = shot.Muzzle.Y;
			transform.Z = shot.Muzzle.Z;
			short power = Math.Min(cost, charge);
			Es1BeamTracer.FireBurst(globals, power, machine, Template.Range, transform, ShotSubtype);
			Capacitor = (short)(charge - power);
		} else if (charge < cost) {
			Es1Bullet.FirePowered(globals, charge, machine, shot.Angles, shot.Muzzle, ShotSubtype);
			if (Template.BarrelCount == ThreeBarrels) {
				var (x, y, z) = Template.MuzzleOffset;
				Es1Bullet.FirePowered(globals, (short)Capacitor, machine, shot.Angles, BarrelMuzzle(shot, 0, y, z), ShotSubtype);
				Es1Bullet.FirePowered(globals, (short)Capacitor, machine, shot.Angles, BarrelMuzzle(shot, (short)-x, y, z),
					ShotSubtype);
			}

			Capacitor = 0;
		} else {
			Capacitor = (short)(charge - cost);
			Es1Bullet.Fire(globals, machine, shot.Angles, shot.Muzzle, ShotSubtype);
		}
	}

	/// <summary>A barrel's world point: the triple (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) plus <see cref="MountPointOffset"/>, as 16-bit sums, through the shot transform.</summary>
	private Vec3i BarrelMuzzle(Es1MountShot shot, short x, short y, short z) {
		var mountPoint = MountPointOffset;
		return shot.Transform.TransformPoint((short)(x + mountPoint.X), (short)(y + mountPoint.Y), (short)(z + mountPoint.Z));
	}

	/// <summary>
	/// <c>ElfMount_FireDispatch</c> (<c>0000b218</c>): the cost taken off the capacitor however little it holds, the shot
	/// transform's translation moved to the muzzle, and <see cref="Es1BeamTracer.FireBurst"/> at <see cref="FullCharge"/>
	/// over the template's range. It raises no muzzle flash: the spin-up drives the model's cells.
	/// </summary>
	private void FireElf(Es1Mech machine, Es1SimGlobals globals) {
		var shot = PrepareShot(machine);
		Capacitor = (short)((short)Capacitor - Template.ShotCost);
		var transform = shot.Transform;
		transform.X = shot.Muzzle.X;
		transform.Y = shot.Muzzle.Y;
		transform.Z = shot.Muzzle.Z;
		Es1BeamTracer.FireBurst(globals, FullCharge, machine, Template.Range, transform, ShotSubtype);
	}

	/// <summary>
	/// <c>WeaponMount_FireDispatch_Missile</c> (<c>0000af40</c>), the ammunition mount's: unless the shot is free, the cost
	/// taken off the rounds, which at 0 or below are cleared with <see cref="Selectable"/>; for a rocket record (type 0)
	/// <see cref="Es1Rocket.Fire"/> at speed 0; otherwise <see cref="Es1Bullet.Fire"/> and the muzzle flash on a visible
	/// hardpoint.
	/// </summary>
	private void FireMissile(bool freeShot, Es1Mech machine, Es1SimGlobals globals) {
		var shot = PrepareShot(machine);
		if (!freeShot) {
			ChargeTarget -= Template.ShotCost;
			if (ChargeTarget < 1) {
				ChargeTarget = 0;
				Selectable = false;
			}
		}

		if (ShotRecordType == Es1Rocket.RecordType) {
			Es1Rocket.Fire(globals, 0, machine, shot.Angles, shot.Muzzle, ShotSubtype);
		} else {
			Es1Bullet.Fire(globals, machine, shot.Angles, shot.Muzzle, ShotSubtype);
			if (VisibleHardpoint) {
				MuzzleFlash = true;
			}
		}
	}

	/// <summary>
	/// <c>GrenadeMount_FireDispatch</c> (<c>0000afd4</c>): unless the shot is free, one round taken off; the throw's hold
	/// cleared; <see cref="Es1Grenade.FireNoOp"/> with the throw distance, which launches nothing.
	/// </summary>
	private void FireGrenade(bool freeShot, Es1Mech machine) {
		var shot = PrepareShot(machine);
		if (!freeShot) {
			ChargeTarget--;
		}

		Charging = false;
		Es1Grenade.FireNoOp(ThrowDistance, machine, shot.Angles, shot.Muzzle, ShotSubtype);
	}

	/// <summary>
	/// <c>WeaponMount_PrepareShot</c> (<c>0000add4</c>), the base fire slot and every dispatch's prologue
	/// (docs/es1/simulation/weapon-mount-classes.md#firing): the node of the hardpoint's bone (<c>.GL +0</c>) in the
	/// machine's shape; when either convergence thread is missing, the rotation of (<see cref="Convergence"/>'s pitch with
	/// no pitch thread, 0, its yaw with no yaw thread) composed with it, the node's translation kept (<c>000647f4</c>, the
	/// rotations' product); that composed with the machine's frame is the shot transform; the template's muzzle triple
	/// plus <see cref="MountPointOffset"/>, as 16-bit sums, through it is the muzzle, and its rotation's angles are the
	/// firing angles. The refire countdown takes the template's refire delay and byte 0 of both flag blocks is set.
	/// Retail uses a null frame for a bone the shape lacks, which no retail chassis has; here the node is the identity,
	/// an engine guard.
	/// </summary>
	public Es1MountShot PrepareShot(Es1Mech machine) {
		var node = machine.PartNodeTransform(Hardpoint.BoneId) ?? Transform3.Identity;
		if (YawThread == null || PitchThread == null) {
			var aim = Transform3.FromEuler(PitchThread == null ? Convergence.Pitch : (short)0, 0,
				YawThread == null ? Convergence.Yaw : (short)0);
			node = Transform3.Concat(aim, node);
		}

		var transform = Transform3.Concat(node, machine.Frame);
		var (x, y, z) = Template.MuzzleOffset;
		var muzzle = BarrelMuzzle(new Es1MountShot(transform, default, default), x, y, z);
		var angles = transform.ToEuler();

		_refireCountdown = Template.RefireDelay;
		_fired = true;
		_firedPending = true;
		return new Es1MountShot(transform, muzzle, angles);
	}

	/// <summary>
	/// Slot <c>+0x38</c>, the pool turn (docs/es1/simulation/weapon-mount-classes.md#charging), given whether a mount served
	/// before it this pass was mid-charge and the budget, answering the budget left: an energy mount's
	/// <see cref="ChargeCapacitor"/>, the ELF's <c>ElfMount_SpinUpAndChargeTick</c> (<c>0000b748</c>), every other class's
	/// <see cref="RefireTick"/>.
	/// </summary>
	public short PoolTurn(bool midChargeSeen, short budget, Es1SimClock clock) => Kind switch {
		Es1MountKind.Energy => ChargeCapacitor(midChargeSeen, budget, clock),
		Es1MountKind.Elf => SpinUpAndChargeTick(midChargeSeen, budget, clock),
		_ => RefireTick(budget, clock),
	};

	/// <summary>
	/// <c>WeaponMount_RefireTick</c> (<c>0000b408</c>): unless destroyed, each byte of the <c>+0x33</c> block ANDed with
	/// its twin in <c>+0x3b</c> (<c>WeaponMount_AndFlagBlocks</c>, <c>0000b4a8</c>) and the twins cleared; the refire
	/// countdown counted down; a playing muzzle flash stepped one cell, stopping when it wraps to 0. Answers the budget.
	/// Retail divides by the model's first sequence's frame count without testing for a model; with none, or a count of 0,
	/// the flash stands, an engine guard.
	/// </summary>
	private short RefireTick(short budget, Es1SimClock clock) {
		if (Destroyed) {
			return budget;
		}

		_fired &= _firedPending;
		_firedPending = false;
		PowerHandOff &= _powerHandOffPending;
		_powerHandOffPending = false;
		clock.CountdownTimerTick(ref _refireCountdown);
		if (MuzzleFlash && FirstSequenceFrames is > 0 and var frames) {
			CellFrames[0] = (short)(((ushort)CellFrames[0] + 1) % frames);
			if (CellFrames[0] == 0) {
				MuzzleFlash = false;
			}
		}

		return budget;
	}

	/// <summary>
	/// <c>WeaponMount_ChargeCapacitor</c> (<c>0000b4d4</c>): unless destroyed, <see cref="RefireTick"/>; the deficit is the
	/// charge target, or 0 once a mount served before it was mid-charge, less the capacitor; a deficit of 0 or less clears
	/// <see cref="MidCharge"/> and gives back up to <see cref="GiveBack"/>; otherwise it draws min(<see cref="ChargeRate"/>,
	/// or <see cref="MidChargeRate"/> while mid-charge, the budget, the deficit). The draw goes into the capacitor and
	/// comes off the budget.
	/// </summary>
	private short ChargeCapacitor(bool midChargeSeen, short budget, Es1SimClock clock) {
		if (Destroyed) {
			return budget;
		}

		RefireTick(budget, clock);
		short deficit = (short)((midChargeSeen ? 0 : ChargeTarget) - (short)Capacitor);
		short draw;
		if (deficit < 1) {
			MidCharge = false;
			draw = Math.Max(deficit, (short)-GiveBack);
		} else {
			draw = Math.Min(Math.Min(MidCharge ? MidChargeRate : ChargeRate, budget), deficit);
		}

		Capacitor = (short)((short)Capacitor + draw);
		return (short)(budget - draw);
	}

	/// <summary>
	/// <c>ElfMount_SpinUpAndChargeTick</c> (<c>0000b748</c>): unless destroyed, while spinning up the cell timer counted
	/// down, and at 0 either, on the model's last cell of its first sequence, the spin-up latched and stopped, or the first
	/// cell frame stepped and the timer zeroed; then <see cref="ChargeCapacitor"/>. Retail reads the model without testing
	/// for one; with none, or no sequence, the spin-up latches at once, as ES2's does, an engine guard.
	/// </summary>
	private short SpinUpAndChargeTick(bool midChargeSeen, short budget, Es1SimClock clock) {
		if (Destroyed) {
			return budget;
		}

		if (SpinningUp && clock.CountdownTimerTick(ref _cellTimer) == 0) {
			int frames = FirstSequenceFrames;
			if (frames <= 0 || frames - 1 == (ushort)CellFrames[0]) {
				SpinUpLatched = true;
				SpinningUp = false;
			} else {
				CellFrames[0] = (short)(((ushort)CellFrames[0] + 1) % frames);
				_cellTimer = 0;
			}
		}

		return ChargeCapacitor(midChargeSeen, budget, clock);
	}

	/// <summary>The model's first sequence's frame count, its <c>+0x20</c> table's first entry; 0 with no model or no sequence.</summary>
	private int FirstSequenceFrames =>
		CellFrames.Length > 0 && (Model as TSShape)?.SequenceList is { Length: > 0 } counts ? (ushort)counts[0] : 0;

	private void SetFirstCellFrame(short frame) {
		if (CellFrames.Length > 0) {
			CellFrames[0] = frame;
		}
	}

	/// <summary>
	/// Slot <c>+0x68</c>, the gauge factory, kept at <c>+0x77</c> (docs/es1/simulation/weapon-mount-classes.md#gauges):
	/// an energy or ELF mount's <c>WeaponMount_CreateEnergyGauge</c> (<c>0000a97c</c>), the row through
	/// <see cref="Es1Cockpit.CreateEnergyWeaponGauge"/> and one set-state with the raw charge target as the power; an
	/// ammunition or grenade mount's <c>WeaponMount_CreateAmmoGauge</c> (<c>0000ab20</c>); the ECM's
	/// <c>EcmMount_CreateGauge</c> (<c>0000abec</c>), through <see cref="Es1Cockpit.CreatePodGauge"/>. The name is
	/// <see cref="DisplayName"/> and the row the hardpoint's signed byte <c>+7</c>.
	/// </summary>
	public void CreateGauge(Es1Cockpit cockpit) {
		short row = (sbyte)Hardpoint.FireChainNumber;
		switch (Kind) {
			case Es1MountKind.Energy or Es1MountKind.Elf:
				var energy = cockpit.CreateEnergyWeaponGauge(this, DisplayName(), row);
				Gauge = energy;
				if (energy != null) {
					energy.SetState(energy.GetState() with { Power = ChargeTarget });
				}

				break;
			case Es1MountKind.Ammunition or Es1MountKind.Grenade:
				Gauge = cockpit.CreateAmmoWeaponGauge(this, DisplayName(), row);
				break;
			case Es1MountKind.Ecm:
				Gauge = cockpit.CreatePodGauge(this, row);
				break;
		}
	}

	/// <summary>
	/// Slot <c>+0x54</c>, the gauge push, once a frame from <see cref="Es1WeaponMounts.PerFrameUpdate"/>
	/// (docs/es1/simulation/weapon-mount-classes.md#gauges): the class's own, below. Nothing with no gauge, which a mount
	/// whose row the cockpit could not place has, an engine guard.
	/// </summary>
	public void PushGaugeState(bool ready, bool inChain, bool armed, Es1SimClock clock) {
		switch (Gauge) {
			case Es1EnergyWeaponGauge energy:
				PushEnergyGaugeState(energy, ready, inChain, armed);
				break;
			case Es1AmmoWeaponGauge ammunition:
				PushAmmoGaugeState(ammunition, ready, inChain, armed, clock);
				// GrenadeMount_PushGaugeState (0000b714) then prints the throw distance on the monochrome debug screen
				// (Mono_Printf), which this engine has no counterpart of.
				break;
			case Es1PodGauge pod:
				// EcmMount_PushGaugeState (0000b588): the switch, the low byte of the block's first word, at +0x7b and +0x7d.
				ChargeTarget = (short)(pod.GetStateBlock().Switch ? 1 : 0);
				Capacitor = ChargeTarget;
				break;
		}
	}

	/// <summary>
	/// <c>WeaponMount_PushEnergyGaugeState</c> (<c>0000b5b8</c>): a copy of the gauge's block; with
	/// <see cref="PowerHandOff"/> clear the block's power comes back as the charge target, power × 1200 ≫ 10, and with it
	/// set the charge target goes out as the power, (target ≪ 10) / 1200; the value (capacitor ≪ 10) / 1200; the
	/// destroyed and mid-charge bytes and the three flags; then the gauge's set-state.
	/// </summary>
	private void PushEnergyGaugeState(Es1EnergyWeaponGauge gauge, bool ready, bool inChain, bool armed) {
		var block = gauge.GetState();
		if (!PowerHandOff) {
			ChargeTarget = (short)(block.Power * FullCharge >> 10);
		} else {
			block.Power = (ChargeTarget << 10) / FullCharge;
		}

		block.Value = ((short)Capacitor << 10) / FullCharge;
		block.Destroyed = Destroyed;
		block.MidCharge = MidCharge;
		block.Armed = armed;
		block.InChain = inChain;
		block.Ready = ready;
		gauge.SetState(block);
	}

	/// <summary>
	/// <c>WeaponMount_PushAmmoGaugeState</c> (<c>0000b67c</c>): a copy of the gauge's block; the rounds in 256ths, while
	/// above the rounds × 256, fall by <c>Math_IntegrateRateOverTick</c>(<see cref="RoundsRollRate"/>), and are raised to
	/// the rounds × 256 when below it; the value the 256ths ≫ 8; the destroyed byte and the three flags; then the gauge's
	/// set-state.
	/// </summary>
	private void PushAmmoGaugeState(Es1AmmoWeaponGauge gauge, bool ready, bool inChain, bool armed, Es1SimClock clock) {
		var block = gauge.GetState();
		int rounds = ChargeTarget * 0x100;
		if (rounds < Capacitor) {
			Capacitor -= clock.IntegrateRateOverTick(RoundsRollRate);
		}

		if (Capacitor < rounds) {
			Capacitor = rounds;
		}

		block.Value = Capacitor >> 8;
		block.Destroyed = Destroyed;
		block.Armed = armed;
		block.InChain = inChain;
		block.Ready = ready;
		gauge.SetState(block);
	}

	/// <summary>
	/// <c>WeaponMount_MuzzleOffset</c> (<c>0000b918</c>), ES2's (docs/es1/simulation/weapon-mount-classes.md#the-slots):
	/// the hardpoint's mount point plus <c>WeaponMountTemplate_SideMuzzleOffset</c> (<c>0000bbd4</c>), the template's
	/// vertical figure (tail <c>0x28</c>) for mounting codes 0 and 1, its lateral one (tail <c>0x24</c>) for 2 and 3, with
	/// the sign the code picks (docs/retail/simulation/weapon-firing.md#where-the-shot-comes-from--weaponmount_prepareshot-0040e788).
	/// </summary>
	public Vec3i MountPointOffset {
		get {
			int lateral = 0;
			int vertical = 0;
			if (Template.Tail is { Length: >= 0x2a } tail) {
				switch (Hardpoint.MountingCode) {
					case 0:
						vertical = BitConverter.ToInt16(tail, 0x28);
						break;
					case 1:
						vertical = -BitConverter.ToInt16(tail, 0x28);
						break;
					case 2:
						lateral = -BitConverter.ToInt16(tail, 0x24);
						break;
					case 3:
						lateral = BitConverter.ToInt16(tail, 0x24);
						break;
				}
			}

			return new Vec3i(Hardpoint.Offset[0] + lateral, Hardpoint.Offset[1], Hardpoint.Offset[2] + vertical);
		}
	}

	/// <summary>
	/// <c>Shape_TranslatePointLists</c> (<c>0000a308</c>,
	/// docs/es1/simulation/weapon-mount-classes.md#moving-the-model-to-its-mount-point--shape_translatepointlists-0000a308):
	/// a part list, shape, cell-animation, detail or BSP part passes the offset to each child; a group or BSP group adds it,
	/// as 16-bit sums, to each point a <c>TSPoly</c>-class poly uses, its centre and its corners; any other part is left.
	/// The core's reader keeps no alias poly, so a point only an alias poly uses is not moved, as in retail. Indices past
	/// the group's lists are skipped, where retail reads past them; no retail model holds one.
	/// </summary>
	public static void TranslatePointLists(TSObject? part, short x, short y, short z) {
		switch (part) {
			case TSGroup group:
				MoveGroupPoints(group, x, y, z);
				break;
			case ANShape:
				break;
			case TSPartList { Parts: { } children }:
				foreach (var child in children) {
					TranslatePointLists(child, x, y, z);
				}

				break;
		}
	}

	/// <summary>A group's half of <see cref="TranslatePointLists"/>: mark the points the polys use, then move the marked ones.</summary>
	private static void MoveGroupPoints(TSGroup group, short x, short y, short z) {
		if (group.Points is not { } points) {
			return;
		}

		var marks = new bool[points.Length];
		var indexes = group.Indexes ?? [];
		foreach (var poly in (group.Polys ?? []).OfType<TSPoly>()) {
			Mark(marks, poly.Center);
			for (int k = 0; k < poly.VertexCount; k++) {
				int at = poly.VertexList + k;
				if (at >= 0 && at < indexes.Length) {
					Mark(marks, indexes[at]);
				}
			}
		}

		for (int i = 0; i < points.Length; i++) {
			if (marks[i]) {
				var point = points[i];
				point.X = (short)(point.X + x);
				point.Y = (short)(point.Y + y);
				point.Z = (short)(point.Z + z);
			}
		}
	}

	private static void Mark(bool[] marks, int index) {
		if (index >= 0 && index < marks.Length) {
			marks[index] = true;
		}
	}
}
