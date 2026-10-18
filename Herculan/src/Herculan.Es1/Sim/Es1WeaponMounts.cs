using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using Herculan.Engine.Anim;

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
/// as its class's constructor leaves it. Its slots, which fire, charge and drive the cockpit row, run nothing in this
/// step: PLACEHOLDER: step 7 (firing) and step 6 (the gauges) port them.
/// </summary>
public sealed class Es1WeaponMount {
	/// <summary>The charge target and capacitor an energy mount starts with, <c>Math_Q10Multiply</c>(1200, 820).</summary>
	public const short InitialCharge = 960;

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

		// PLACEHOLDER: the model's points are not moved to WeaponMount_MuzzleOffset, which WeaponMount_CtorBase hands with
		// the model to 0000a308, unread (docs/es1/simulation/weapon-mount-classes.md#open); so the model is not drawn (see
		// Es1MechModels) until that function is read.
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
				Capacitor = (short)(template.MagazineSize << 8);
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

	/// <summary><c>+0x10</c>, the weapon's model, a root of <c>dts\mechwpns</c>; null for the invisible mounting.</summary>
	public TSObject? Model { get; }

	/// <summary>The transform id <c>Mech_ConfigureLoadout</c> stamps on the model's parts: the hardpoint bone's part's in the machine's shape.</summary>
	public int ModelTransformId { get; set; } = -1;

	/// <summary><c>+0x5b</c> and <c>+0x5f</c>, the convergence threads.</summary>
	public AnimationThread? PitchThread { get; }

	/// <inheritdoc cref="PitchThread"/>
	public AnimationThread? YawThread { get; }

	/// <summary><c>+0x49</c>, destroyed.</summary>
	public bool Destroyed { get; set; }

	/// <summary><c>+0x4b</c>, linked with its partner.</summary>
	public bool Linked { get; set; }

	/// <summary><c>+0x4c</c>, the byte set while the mount can be armed; the ECM's constructor clears it.</summary>
	public bool Selectable { get; set; }

	/// <summary><c>+0x7b</c>: an energy mount's charge target, an ammunition mount's rounds.</summary>
	public short ChargeTarget { get; set; }

	/// <summary><c>+0x7d</c>: an energy mount's capacitor, an ammunition mount's rounds in 256ths, the ECM's switch.</summary>
	public short Capacitor { get; set; }

	/// <summary><c>+0x34</c> and <c>+0x3c</c>, the power-level hand-off bytes the energy constructor sets.</summary>
	public bool PowerHandOff { get; set; }
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
