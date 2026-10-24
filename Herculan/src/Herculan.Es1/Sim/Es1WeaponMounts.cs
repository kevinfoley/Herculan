using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Part;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
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
/// One fitted weapon, a <c>CONFIG_PART</c> whose model is the weapon's shape (docs/es1/simulation/weapon-mount-classes.md),
/// as its class's constructor leaves it, with the slots that build and feed its cockpit row
/// (docs/es1/simulation/weapon-mount-classes.md#gauges) and the two the chain's toggle calls. PLACEHOLDER: the slots that
/// fire and charge, plan step 7.
/// </summary>
public sealed class Es1WeaponMount {
	/// <summary>The charge target and capacitor an energy mount starts with, <c>Math_Q10Multiply</c>(1200, 820).</summary>
	public const short InitialCharge = 960;

	/// <summary>The full charge, which the energy gauge push scales the Q10 fractions by.</summary>
	public const int FullCharge = 1200;

	/// <summary>The charge target <c>WeaponMount_WakeCapacitor</c> sets.</summary>
	public const short WakeCharge = 0x334;

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
	/// point; null for the invisible mounting.
	/// </summary>
	public TSObject? Model { get; }

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

	/// <summary><c>+0x49</c>, destroyed.</summary>
	public bool Destroyed { get; set; }

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
	/// <c>+0x34</c> and <c>+0x3c</c>, the power-level hand-off bytes the energy constructor sets. PLACEHOLDER: the pool
	/// turn that clears them, plan step 7, is not ported, so they stay set and the energy gauge push always sends the
	/// charge target out to the charge bar.
	/// </summary>
	public bool PowerHandOff { get; set; }

	/// <summary><c>+0x43</c>, the mid-charge byte, which <c>WeaponMount_CtorBase</c> clears.</summary>
	public bool MidCharge { get; set; }

	/// <summary><c>+0x77</c>, the mount's cockpit row, which its gauge factory builds; null before <see cref="CreateGauge"/>.</summary>
	public Es1WeaponGauge? Gauge { get; private set; }

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
	public void PushGaugeState(bool ready, bool inChain, bool armed) {
		switch (Gauge) {
			case Es1EnergyWeaponGauge energy:
				PushEnergyGaugeState(energy, ready, inChain, armed);
				break;
			case Es1AmmoWeaponGauge ammunition:
				PushAmmoGaugeState(ammunition, ready, inChain, armed);
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
	/// <c>WeaponMount_PushAmmoGaugeState</c> (<c>0000b67c</c>): a copy of the gauge's block; the rounds in 256ths raised
	/// to the rounds × 256 when below it; the value the 256ths ≫ 8; the destroyed byte and the three flags; then the
	/// gauge's set-state.
	/// </summary>
	private void PushAmmoGaugeState(Es1AmmoWeaponGauge gauge, bool ready, bool inChain, bool armed) {
		var block = gauge.GetState();
		int rounds = ChargeTarget * 0x100;
		// PLACEHOLDER: while the 256ths are above the rounds × 256 they fall by Math_IntegrateRateOverTick(250) a frame,
		// the count running down after a shot; firing is plan step 7, and until then the two stay equal.
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

/// <summary>
/// The machine's mount manager at <c>+0x198</c> (docs/es1/simulation/weapon-mounts.md#the-manager): the mount array with
/// its holes, and for the player's machine the three chains and the armed mount. Built by
/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0000c31c</c>) and, for the local manager, <c>WeaponMounts_CtorLocal</c>
/// (<c>0000c808</c>).
/// </summary>
public sealed class Es1WeaponMounts {
	/// <summary>The weapon id that builds no mount and leaves the slot empty: one past the 17, and what a negative id becomes.</summary>
	public const short NoWeapon = 17;

	/// <summary>The projectile index meaning no record, which <c>ECM</c> carries.</summary>
	public const short NoProjectile = 20;

	/// <summary>The projectile index meaning a rocket record looked up by the hardpoint's ammunition type.</summary>
	public const short RocketByAmmunition = 21;

	/// <summary>The ECM's weapon id, which no chain takes.</summary>
	public const short EcmWeapon = 13;

	/// <summary>The value <c>+0x1d</c> holds with no mount armed.</summary>
	public const int NoneArmed = 0xff;

	private Es1WeaponMounts(Es1WeaponMount?[] mounts, bool local) {
		Mounts = mounts;
		IsLocal = local;
		Chains = local ? [new bool[mounts.Length], new bool[mounts.Length], new bool[mounts.Length]] : [];
	}

	/// <summary><c>+0x00</c> and <c>+0x04</c>, a mount per hardpoint record, null where none was built.</summary>
	public IReadOnlyList<Es1WeaponMount?> Mounts { get; }

	/// <summary>Whether this is the local manager (vptr <c>0009158a</c>), the player's; otherwise the base (<c>000912f2</c>).</summary>
	public bool IsLocal { get; }

	/// <summary><c>+0x1f</c>, <c>+0x25</c>, <c>+0x2b</c>: per chain, whether each mount is in it. Empty for the base manager.</summary>
	public IReadOnlyList<bool[]> Chains { get; }

	/// <summary><c>+0x1c</c>, the current chain.</summary>
	public int CurrentChain { get; private set; }

	/// <summary><c>+0x1d</c>, the armed mount, <see cref="NoneArmed"/> for none.</summary>
	public int Armed { get; private set; } = NoneArmed;

	/// <summary><c>+0x16</c>, the chain-changed byte.</summary>
	public bool ChainChanged { get; private set; }

	/// <summary>
	/// <c>+0x14</c>, TRACK's latch, which the turret's tracking and <c>Player_PerFrameCockpitUpdate</c> read
	/// (docs/es1/simulation/weapon-mounts.md#the-manager), and <see cref="PerFrameUpdate"/> copies from the console's
	/// <c>TRACK</c> each frame.
	/// </summary>
	public bool TrackLatch { get; private set; }

	/// <summary><c>+0x18</c>, single fire, which arming a row sets.</summary>
	public bool SingleFire { get; private set; }

	/// <summary><c>+0x31</c>, the range to the selected target the per-frame pass stores.</summary>
	public int Range { get; private set; }

	/// <summary><see cref="Armed"/> as the signed byte retail reads, −1 for none.</summary>
	private int ArmedIndex => (sbyte)Armed;

	/// <summary>
	/// <c>WeaponMounts_BuildGauges</c> (<c>0000c9b0</c>)(manager), from <c>Sim_StartMission</c>'s step 13: each mount's
	/// gauge factory (<see cref="Es1WeaponMount.CreateGauge"/>) on <paramref name="cockpit"/>, <c>CockpitViewInstance</c>.
	/// </summary>
	public void BuildGauges(Es1Cockpit cockpit) {
		foreach (var mount in Mounts) {
			mount?.CreateGauge(cockpit);
		}
	}

	/// <summary>
	/// <c>WeaponMounts_SelectByGauge</c> (<c>0000ca70</c>)(manager, <paramref name="gauge"/>): the first mount whose gauge
	/// match passes; false when none does or it cannot be armed (<see cref="Es1WeaponMount.Selectable"/> clear);
	/// otherwise it is armed (<see cref="SetSelection"/>), single fire set, and true.
	/// </summary>
	public bool SelectByGauge(Es1WeaponGauge gauge) {
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is { } mount && mount.MatchesGauge(gauge)) {
				if (!mount.Selectable) {
					return false;
				}

				SetSelection(i);
				SingleFire = true;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>WeaponMounts_SetSelection</c> (<c>0000cb00</c>)(manager, <paramref name="index"/>): when the index is not the
	/// armed one, a linked mount whose hardpoint's partner offset is negative hands it to its partner, and it is stored.
	/// Retail also clears <c>000b5bc8</c>, the electro-optical launch's pending step, which only firing sets (plan step 7).
	/// </summary>
	public void SetSelection(int index) {
		if (index == ArmedIndex) {
			return;
		}

		if (Mounts[index] is { Linked: true } mount && (sbyte)mount.Hardpoint.LinkPartnerOffset is < 0 and var offset) {
			index += offset;
		}

		Armed = (byte)index;
	}

	/// <summary>
	/// <c>WeaponMounts_StepSelection</c> (<c>0000cb54</c>)(manager, <paramref name="step"/>): with a mount armed, from it by
	/// the step, wrapping below 0 to the last mount and past the last to 0, to the first mount that can be armed, is
	/// unlinked or the first half of a pair (a positive partner offset), and is in the current chain, or back to the armed
	/// one; then <see cref="SetSelection"/>.
	/// </summary>
	public void StepSelection(int step) {
		int start = ArmedIndex;
		if (start < 0) {
			return;
		}

		int index = start;
		bool found;
		do {
			index += step;
			if (index < 0) {
				index = Mounts.Count - 1;
			} else if (index >= Mounts.Count) {
				index = 0;
			}

			found = Mounts[index] is { Selectable: true } mount
				&& (!mount.Linked || (sbyte)mount.Hardpoint.LinkPartnerOffset > 0)
				&& Chains[CurrentChain][index];
		} while (!found && index != start);

		SetSelection(index);
	}

	/// <summary>
	/// <c>WeaponMounts_SetChain</c> (<c>0000cf10</c>)(manager, <paramref name="chain"/>): when the chain is not the current
	/// one, it is stored, the selection stepped when the armed mount is not in it, and the chain handed to the console
	/// <paramref name="console"/>, <c>CockpitViewInstance</c>'s <c>+0x1dd</c> (<see cref="Es1ConsoleButtons.SetChainNumber"/>).
	/// </summary>
	public void SetChain(short chain, Es1ConsoleButtons? console) {
		if (chain == CurrentChain) {
			return;
		}

		CurrentChain = (sbyte)chain;
		if (ArmedIndex >= 0 && !Chains[CurrentChain][ArmedIndex]) {
			StepSelection(1);
		}

		console?.SetChainNumber(chain);
	}

	/// <summary><c>WeaponMounts_XorChainBit</c> (<c>0000d180</c>)(manager, <paramref name="index"/>): the mount's entry in the current chain flipped.</summary>
	public void XorChainBit(int index) => Chains[CurrentChain][index] = !Chains[CurrentChain][index];

	/// <summary>
	/// <c>WeaponMounts_ToggleChainMember</c> (<c>0000d5e4</c>)(manager, <paramref name="row"/>): the first mount whose
	/// hardpoint's row (signed byte <c>+7</c>) is the row and whose template's <c>int32</c> <c>+0x30</c> is above 0 has its
	/// chain entry flipped and, when its energy priority is 0, its wake slot called; true, or false when none matches.
	/// </summary>
	public bool ToggleChainMember(short row) {
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is { } mount && (sbyte)mount.Hardpoint.FireChainNumber == row && mount.Template.Range > 0) {
				XorChainBit(i);
				if (mount.EnergyPriority == 0) {
					mount.Wake();
				}

				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>WeaponMounts_ToggleLink</c> (<c>0000d41c</c>)(manager): with a mount armed, the link flipped on it and on the
	/// mount its hardpoint's partner offset names, when the offset is not 0, the partner exists and the two are the same
	/// weapon (the templates' <c>+0x56</c>, the weapon id). A partner index outside the array, which retail reads past
	/// and no retail <c>.GL</c> holds, is no partner, an engine guard.
	/// </summary>
	public void ToggleLink() {
		if (ArmedIndex < 0 || Mounts[ArmedIndex] is not { } armed) {
			return;
		}

		int offset = (sbyte)armed.Hardpoint.LinkPartnerOffset;
		int partner = ArmedIndex + offset;
		if (offset != 0 && partner >= 0 && partner < Mounts.Count && Mounts[partner] is { } other
				&& other.WeaponId == armed.WeaponId) {
			armed.Linked = !armed.Linked;
			other.Linked = !other.Linked;
		}
	}

	/// <summary><c>WeaponMounts_SetTrackLatch</c> (<c>0000d408</c>)(manager): <see cref="TrackLatch"/> set.</summary>
	public void SetTrackLatch() => TrackLatch = true;

	/// <summary>
	/// <c>WeaponMounts_PerFrameUpdate</c> (<c>0000cf78</c>)(manager, <paramref name="range"/>), from
	/// <c>Player_PerFrameCockpitUpdate</c> (docs/es1/simulation/weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78):
	/// the range stored; then the console's block (<see cref="Es1ConsoleButtons.GetStateBlock"/>): with the chain-changed
	/// byte set it is cleared and the manager's chain put in the block, otherwise <see cref="SetChain"/> takes the
	/// block's; the block's tracking byte sets <see cref="TrackLatch"/> or clears it; its link byte runs
	/// <see cref="ToggleLink"/>; the link byte cleared and the block written back
	/// (<see cref="Es1ConsoleButtons.SetStateBlock"/>). Then for each mount, armed when it is the armed one, or when it is
	/// linked, neither destroyed nor unselectable, and its partner is the armed one; a linked mount that is destroyed or
	/// unselectable has its and its partner's link flipped and hands the selection to its partner; then its gauge push
	/// (<see cref="Es1WeaponMount.PushGaugeState"/>) with whether it is ready, in the current chain and armed.
	/// </summary>
	/// <param name="console"><c>CockpitViewInstance</c>'s <c>+0x1dd</c>; with none, which retail always has, the block's part is skipped.</param>
	public void PerFrameUpdate(int range, Es1ConsoleButtons? console) {
		// Retail calls it on the player's local manager alone; a base manager has no chains, an engine guard.
		if (!IsLocal) {
			return;
		}

		// PLACEHOLDER: with 000b5bc8 set and 000b5bc6 clear, WeaponMounts_StepSelection(1) and single fire cleared, the
		// electro-optical launch's hold on the chain; firing is plan step 7, and until then neither is set.
		Range = range;
		// PLACEHOLDER: WeaponMounts_AdvanceToReady (0000ce54), which steps the armed mount on to a ready one; readiness is
		// plan step 7's (WeaponMounts_MountIsReady below).

		if (console != null) {
			var block = console.GetStateBlock();
			if (!ChainChanged) {
				SetChain(block.Chain, console);
			} else {
				ChainChanged = false;
				block.Chain = (short)CurrentChain;
			}

			if (block.Tracking) {
				SetTrackLatch();
			} else {
				TrackLatch = false;
			}

			if (block.Link) {
				ToggleLink();
			}

			block.Link = false;
			console.SetStateBlock(block);
		}

		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is not { } mount) {
				continue;
			}

			bool armed = i == ArmedIndex;
			if (mount.Linked) {
				int partner = i + (sbyte)mount.Hardpoint.LinkPartnerOffset;
				if (!mount.Destroyed && mount.Selectable) {
					if (partner == ArmedIndex) {
						armed = true;
					}
				} else if (partner >= 0 && partner < Mounts.Count && Mounts[partner] is { } other) {
					// A partner outside the array or missing, which retail writes through and no retail .GL holds, is
					// skipped, an engine guard.
					mount.Linked = !mount.Linked;
					other.Linked = !other.Linked;
					SetSelection(partner);
				}
			}

			// PLACEHOLDER: WeaponMounts_MountIsReady (0000cdb0)(manager, mount), the mount's ready slot +0x30, the range
			// test and the missile lock, plan step 7; until then no row shows ready.
			const bool ready = false;
			mount.PushGaugeState(ready, Chains[CurrentChain][i], armed);
		}
	}

	/// <summary>
	/// The factory and, when <paramref name="local"/>, <c>WeaponMounts_CtorLocal</c>'s chains: for each hardpoint record in
	/// file order the weapon and ammunition type its <c>+0x17</c> slot indexes in <paramref name="fit"/> and
	/// <paramref name="ammunition"/>, and the class the weapon id picks
	/// (docs/es1/formats/weapons-dat-sim.md#the-projectile-index). <paramref name="modelFor"/> gives the weapon model for a
	/// template and mounting code.
	/// </summary>
	public static Es1WeaponMounts Build(bool local, GunLayout? hardpoints, Func<int, short> fit, Func<int, short> ammunition,
			Es1Weapons weapons, Func<Weapons.WeaponMountTemplate, int, TSObject?> modelFor,
			Func<int, AnimationThread> addThread) {
		var records = hardpoints?.Hardpoints ?? [];
		var mounts = new Es1WeaponMount?[records.Length];
		for (int i = 0; i < records.Length; i++) {
			var hardpoint = records[i];
			short weapon = fit(hardpoint.LoadoutSlot);
			if (weapon < 0) {
				weapon = NoWeapon;
			}

			if (weapon >= NoWeapon || weapon >= weapons.Templates.Length) {
				continue;
			}

			var template = weapons.Templates[weapon];
			var kind = weapon switch {
				3 => Es1MountKind.Elf,
				4 or 6 or 7 or 8 or 12 or 14 or 15 => Es1MountKind.Energy,
				5 => Es1MountKind.Grenade,
				13 => Es1MountKind.Ecm,
				_ => Es1MountKind.Ammunition,
			};

			var projectile = kind == Es1MountKind.Ecm ? null : Projectile(weapons, template.ProjDatIndex, ammunition(hardpoint.LoadoutSlot));
			var model = hardpoint.MountingCode < Es1MechType.InvisibleMounting ? modelFor(template, hardpoint.MountingCode) : null;
			mounts[i] = new Es1WeaponMount(kind, weapon, hardpoint, template, projectile, model, addThread);
		}

		var manager = new Es1WeaponMounts(mounts, local);
		if (local) {
			manager.BuildChains();
		}

		return manager;
	}

	/// <summary>
	/// The projectile record the template's index names: none for 20, <c>Proj_LookupRecord</c>(ammunition type, rocket) for
	/// 21 with 5 read as 0, otherwise <c>Proj_LookupRecordByIndex</c>; null for an index past the 19 records.
	/// </summary>
	private static ProjectileData.Projectile? Projectile(Es1Weapons weapons, short index, short ammunition) {
		if (index == NoProjectile) {
			return null;
		}

		if (index == RocketByAmmunition) {
			short subtype = ammunition == 5 ? (short)0 : ammunition;
			return weapons.Projectiles.FirstOrDefault(p => p.Type?.Val == 0 && p.SubtypeId == subtype);
		}

		return index >= 0 && index < weapons.Projectiles.Length ? weapons.Projectiles[index] : null;
	}

	/// <summary>
	/// <c>WeaponMounts_CtorLocal</c>'s chains: no mount armed, every mount but the ECM in chain I alone and the first of them
	/// armed, then <c>WeaponMounts_SetChain</c>(0) and the chain-changed byte set. The manager is a fresh zeroed allocation,
	/// so its current chain is already 0 and the set changes nothing.
	/// </summary>
	private void BuildChains() {
		Armed = NoneArmed;
		for (int i = 0; i < Mounts.Count; i++) {
			if (Mounts[i] is not { } mount || mount.WeaponId == EcmWeapon) {
				continue;
			}

			Chains[0][i] = true;
			if (Armed == NoneArmed) {
				Armed = i;
			}
		}

		CurrentChain = 0;
		ChainChanged = true;
	}
}
