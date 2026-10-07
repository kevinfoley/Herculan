using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Sim;

/// <summary>
/// One fitted hardpoint — DBSIM's weapon-mount object, built by
/// <c>MechLoadout_ConstructWeaponMounts</c> (<c>0040fff8</c>) from three things that have to be
/// joined: the machine type's own hardpoint list (<c>gl\&lt;HERC&gt;.GL</c>), the fit the mission
/// gave it (<c>player.mec</c> or <c>script.dat</c>), and the weapon id's template
/// (<see cref="WeaponCatalog"/>).
///
/// <para><b>The hardpoint list drives the join, not the fit.</b> The factory walks the <c>.GL</c>
/// records in file order and reads each one's byte at <c>+0x17</c> as an index into the fit's two
/// parallel arrays — so a machine's mounts are ordered by its own chassis, and the fit is addressed
/// through it rather than iterated. That is why the same <c>player.mec</c> entry produces a
/// different-looking weapon panel on two different HERCs, and why reading the fit array in order
/// gets the order wrong.</para>
///
/// <para><b>Fields are shared, not per-class.</b> <c>+0x7b</c> and <c>+0x7d</c> mean different
/// things depending on which class holds them: rounds for an ammunition mount, a charge target and a
/// capacitor level for an energy or ELF one. They are modelled here under the names each class gives
/// them, with the raw offsets noted, rather than as one abstract "level".</para>
///
/// <para><b>The constructor does not decide the class.</b> The factory builds an ELF by running the
/// energy constructor and then swapping the object's vtable — see <see cref="WeaponMountKind.Elf"/>.
/// That is why <see cref="Kind"/> is what everything here branches on rather than which fields were
/// initialised.</para>
/// </summary>
public sealed partial class WeaponMount {
	/// <summary>The catalog id of a mount the factory builds nothing for.</summary>
	public const int EmptyWeaponId = 0;

	private readonly Weapons.WeaponMountTemplate? _template;
	private readonly GunLayout.HardpointEntry _hardpoint;

	/// <summary>The weapon's <c>WEAPONS.DAT</c> template, mount <c>+0x1c</c>; null with no catalog.</summary>
	internal Weapons.WeaponMountTemplate? Template => _template;

	/// <summary>
	/// The part id of the hardpoint attachment slot this mount's shape is spliced into — see
	/// <see cref="Render.DtsMeshBuilder.AttachmentPartIds"/>.
	/// </summary>
	public short HardpointBoneId => _hardpoint.BoneId;

	internal WeaponMount(int mountIndex, GunLayout.HardpointEntry hardpoint, int weaponId,
			short secondaryKey, WeaponCatalog catalog, Func<int, int>? modelCellCount = null) {
		_hardpoint = hardpoint;
		MountIndex = mountIndex;
		GaugeSlot = hardpoint.FireChainNumber;
		LoadoutSlot = hardpoint.LoadoutSlot;
		LinkPartnerOffset = (sbyte)hardpoint.LinkPartnerOffset;
		WeaponId = weaponId;
		SecondaryKey = secondaryKey;
		Kind = WeaponCatalog.Kind(weaponId);
		Name = catalog.MountName(weaponId, secondaryKey);
		Projectile = catalog.Projectile(weaponId, secondaryKey);
		_lookedUpProjectile = Projectile is { Type: { } type } own
			? catalog.Lookup(type, own.SubtypeId) ?? own
			: Projectile;
		_correctedProjectile = catalog.CorrectedProjectile(weaponId, secondaryKey);
		_template = catalog.Template(weaponId);

		switch (Kind) {
			case WeaponMountKind.Ammunition:
				// WeaponMount_CtorAmmunition (0040e140): the magazine size comes off the template and the mount powers up
				// holding a full one. The level is kept in 256ths of a round; the gauge prints
				// level >> 8.
				ChargeTarget = MagazineSize;
				Charge = MagazineSize << 8;
				break;

			// The ELF class runs this same constructor before the factory swaps its vtable, so it
			// powers up with the identical capacitor.
			case WeaponMountKind.Energy:
			case WeaponMountKind.Elf:
				ChargeTarget = EnergyCapacitorFull;
				Charge = EnergyCapacitorFull;
				ChargeRate = EnergyChargeRate;
				_powerToGauge = true;
				_powerToGaugeThisTick = true;
				break;

			// TurboPod_Ctor (0040e2bc) is the one pod constructor that arms anything: a full tank at
			// +0x7d and the engaged flag at +0x81 clear. Every other pod leaves both at zero.
			case WeaponMountKind.Pod when weaponId == MechPods.TurboPodWeaponId:
				Charge = TurboChargeFull;
				break;
		}

		// The Targeting Pod is the one pod class with state of its own, and the one mount class that
		// overrides the condition slot below. Everything it holds is in TargetingPodLock.
		if (weaponId == MechPods.TargetingWeaponId) {
			ComponentLock = new TargetingPodLock();
		}

		// WeaponMount_CtorBase (0040df30) sets +0x4c on every mount it builds; the pod base constructor
		// (Pod_CtorBase, 0040e234) immediately clears it again, which is one of the two independent reasons a
		// pod can never be armed.
		Selectable = Kind != WeaponMountKind.Pod;

		// WeaponMount_CtorBase (0040df30)'s own first act: an invisibly-mounted hardpoint loads no shape, and every
		// other one loads the weapon model its template names for the mounting code it sits at.
		ModelShapeIndex = _hardpoint.MountingCode < InvisibleMounting && _template != null
			? _template.ModelShapeIndex(_hardpoint.MountingCode)
			: -1;
		FlashCellCount = ModelShapeIndex >= 0 ? modelCellCount?.Invoke(ModelShapeIndex) ?? 0 : 0;
	}

	/// <summary>
	/// The <c>.GL</c> mounting code (<c>+6</c>) that means the hardpoint carries no visible weapon.
	/// Every test the original spells as <c>.GL +6 &lt; 4</c> is this one.
	/// </summary>
	public const int InvisibleMounting = 4;

	/// <summary>
	/// Which shape of <c>dts\MECHWPNS.DTS</c> this mount is drawn as, or -1 for an invisible
	/// mounting — <c>WeaponMount_ShapeForMountingCode</c> (<c>0040fab0</c>), which the base constructor calls only when the hardpoint's
	/// mounting code is under <see cref="InvisibleMounting"/>. The mount owns a private copy of that
	/// shape in the original (<c>mount+0x10</c>), because it translates the geometry to the muzzle
	/// point and steps its flipbook independently of every other mount carrying the same weapon.
	///
	/// <para>It goes back to -1 when the mount is knocked out — see <see cref="Destroy"/>, which is
	/// the only thing that changes it after construction.</para>
	/// </summary>
	public int ModelShapeIndex { get; private set; }

	/// <summary>
	/// The Targeting Pod's component lock, on the one mount that is a Targeting Pod and null on every
	/// other. See <see cref="TargetingPodLock"/>; <see cref="MechPods.TargetingMount"/> is how the
	/// machine reaches it.
	/// </summary>
	public TargetingPodLock? ComponentLock { get; }

	/// <summary>
	/// This mount's index in the machine's mount array — its position in the <c>.GL</c> file. It is
	/// what the selected-weapon index, the fire-group arrays and <see cref="LinkPartnerOffset"/> are
	/// all relative to. The Heads-Down Display's weapon list prints in <see cref="LoadoutSlot"/> order
	/// instead — see <see cref="Cockpit.DamageHardpoint"/>.
	/// </summary>
	public int MountIndex { get; }

	/// <summary>
	/// Which cockpit weapon row this mount owns — the <c>.GL</c> record's own byte at <c>+7</c>,
	/// which the mount hands to the gauge factory as a <c>.GAU</c> weapon-slot index. Row <c>n</c>
	/// prints the digit <c>n+1</c>, so this is the panel's numbering minus one. It is a different
	/// order from <see cref="MountIndex"/>.
	/// </summary>
	public int GaugeSlot { get; }

	/// <summary>
	/// Which slot of the fit's arrays this hardpoint draws from — the <c>.GL</c> record's byte at
	/// <c>+0x17</c>. The <c>.PDG</c>'s weapon-icon list is indexed by it too.
	/// </summary>
	public int LoadoutSlot { get; }

	/// <summary>
	/// The <c>.GL</c> record's signed byte at <c>+0x16</c>: how far away in the mount array this
	/// hardpoint's link partner sits, or zero for a hardpoint that has none. Retail chassis pair
	/// their left and right mirror hardpoints with +1/-1. It is what pairs two mounts into one trigger
	/// pull — see <see cref="WeaponMounts.PartnerOf"/> and <see cref="WeaponMounts.FireTick"/>.
	/// </summary>
	public int LinkPartnerOffset { get; }

	/// <summary>The fit's catalog weapon id for this hardpoint.</summary>
	public int WeaponId { get; }

	/// <summary>
	/// The template's damage-detail icon — <c>+0x1c</c>'s <c>+0x50</c>, read by
	/// <c>PaperDoll_BuildWeaponIcons</c> (<c>00437c8c</c>). -1 for none. See
	/// <see cref="Cockpit.PaperDollDamage.PlaceWeaponIcon"/>.
	/// </summary>
	public int DamageIcon => _template?.DamageIconIndex ?? -1;

	/// <summary>
	/// The fit's parallel second value for this hardpoint — the ammunition type a launcher is loaded
	/// with. Retail data puts 5 in every slot that is not a launcher.
	/// </summary>
	public short SecondaryKey { get; }

	/// <summary>Which mount class the factory built.</summary>
	public WeaponMountKind Kind { get; }

	/// <summary>
	/// The name this mount's gauge prints. A launcher is named by its loaded ammunition, everything
	/// else by its weapon id — see <see cref="WeaponCatalog.MountName"/>.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// The <c>PROJ.DAT</c> record this mount holds, or null for a pod and for <c>ECM</c>. The fire
	/// dispatch tests its type and the AI scores and leads with it; what a shot applies is
	/// <see cref="ShotProjectile"/>.
	/// </summary>
	public ProjectileData.Projectile? Projectile { get; }

	/// <summary>
	/// The record a shot from this mount applies its damage, splash and impact effects from, and whose
	/// subtype id picks its <c>BULLETS.DAT</c> or <c>BEAM.DAT</c> record. Retail's shot constructors
	/// look the record up again by type and subtype id (<see cref="WeaponCatalog.Lookup"/>) and take
	/// the first match, which for <c>ATC75</c>, <c>ATC100</c>, <c>LAS400</c> and <c>LAS500</c> is an
	/// earlier weapon's record. With <see cref="TweakSettingDefinitions.FixWeaponDamageRecords"/> on in
	/// <paramref name="world"/>'s <see cref="SimWorld.Tweaks"/>, the shot is <see cref="Projectile"/> under its corrected subtype id
	/// (<see cref="WeaponCatalog.CorrectedProjectile"/>). See docs/retail/formats/proj-dat.md#lookup.
	/// </summary>
	public ProjectileData.Projectile? ShotProjectile(SimWorld world) =>
		world.Tweaks.GetSettingValue(TweakSettingDefinitions.FixWeaponDamageRecords)
			? _correctedProjectile
			: _lookedUpProjectile;

	private readonly ProjectileData.Projectile? _lookedUpProjectile;
	private readonly ProjectileData.Projectile? _correctedProjectile;

	/// <summary>
	/// The magazine size — the template's field at <c>+0x3a</c>, which <c>WeaponMount_CtorAmmunition</c> (<c>0040e140</c>) reads as
	/// both the round count a mount starts with and the count it is capped at. Zero for anything that
	/// is not an ammunition mount.
	/// </summary>
	public short MagazineSize =>
		Kind == WeaponMountKind.Ammunition && _template?.Tail is { Length: >= 0x1a } tail
			? BitConverter.ToInt16(tail, 0x18)
			: (short)0;

	/// <summary>
	/// The value <c>WeaponMount_GetAmmoType</c> (<c>0040e644</c>, mount vtable <c>+0x60</c>) reports:
	/// this mount's <c>PROJ.DAT</c> missile subtype, or <see cref="NotAMissile"/> when it fires
	/// anything else. The energy class returns the same sentinel unconditionally
	/// (<c>WeaponMount_GetEnergyAmmoType</c>).
	///
	/// <para>It is what indexes the machine's missile-lock state — see
	/// <see cref="MechObject.MissileLocked"/>.</para>
	/// </summary>
	public short AmmoType => Projectile is { } record && record.Type == ProjectileType.Rocket
		? record.SubtypeId
		: NotAMissile;

	/// <summary>
	/// <c>WeaponMount_GetAmmoType</c>'s "this is not a launcher" return. It is deliberately one past
	/// the last real subtype, so it also serves as the length of every per-subtype array in the lock
	/// system.
	/// </summary>
	public const short NotAMissile = 5;

	/// <summary>
	/// The round count <c>WeaponMount_GetAmmoType</c> hands back through its out parameter —
	/// <c>mount+0x7b</c>, which for an ammunition mount is the rounds it has left. Zero for anything
	/// that is not a launcher, so an empty rack contributes nothing to the lock system's fitment
	/// tally.
	/// </summary>
	public short AmmoRounds => AmmoType == NotAMissile ? (short)0 : ChargeTarget;

	/// <summary>
	/// <c>+0x7b</c>. An ammunition mount keeps its remaining round count here; an energy mount keeps
	/// the charge level it is asking the pool for, which doubles as its priority in the arbitration.
	/// </summary>
	public short ChargeTarget { get; internal set; }

	/// <summary>
	/// <c>+0x7d</c>. An ammunition mount's rounds in 256ths; an energy mount's capacitor level in
	/// pool units.
	/// </summary>
	public int Charge { get; internal set; }

	/// <summary>
	/// <c>+0x7f</c>. How much an energy mount takes per tick when it is served, lowered by
	/// <see cref="ConditionChanged"/> as its component is damaged; zero for the other classes, which
	/// take nothing.
	/// </summary>
	public short ChargeRate { get; internal set; }

	/// <summary>
	/// <c>+0x43</c>. Set while this mount is the one drawing on the pool. Every mount served after it
	/// this tick is told to target zero instead and gives its own charge back.
	/// </summary>
	public bool Charging { get; internal set; }

	/// <summary>
	/// <c>+0x49</c>. A destroyed mount: it charges nothing, fires nothing, and its cockpit row prints
	/// <c>OFFLINE</c> in place of the weapon's name. <see cref="Destroy"/> is what sets it.
	/// </summary>
	public bool Disabled { get; internal set; }

	/// <summary>
	/// <c>+0x4c</c>. Whether this mount can be armed at all. Clear for a pod from construction, and
	/// cleared on an ammunition mount the moment its magazine runs out
	/// (<c>WeaponMount_FireDispatch_Missile</c>) — an empty weapon drops out of the selection cycle
	/// rather than staying armed. <see cref="FireAmmunition"/> is what empties one.
	/// </summary>
	public bool Selectable { get; internal set; }

	/// <summary>
	/// <c>+0x4b</c>. Whether this mount is link-fired with its <see cref="LinkPartnerOffset"/>
	/// partner. Both halves of a pair carry it, and it is always set and cleared as a pair — see
	/// <see cref="WeaponMounts.ToggleLink"/>.
	/// </summary>
	public bool Linked { get; internal set; }

	/// <summary>
	/// <b>The weapon's range, in world units</b> — the template's int32 at <c>0x30</c>, which
	/// <c>WeaponMount_FireDispatch_GunBeam</c> hands straight to <c>Bullet_FireBurst</c> as the ray's
	/// length, and that call is what identifies the field. <c>WeaponMounts_ToggleChainMember</c>
	/// (<c>004110ac</c>) also requires it to be positive before it will put a hardpoint into a fire
	/// chain.
	///
	/// <para>It does not fit the manual's 20 m figure for the ELF — ELF reads 20000 units, which is
	/// 120 m at the simulation's own scale — but the manual is not what identifies a field, and the
	/// fire path is.
	/// Retail values run 75000 (ATC20, 450 m) down to 15000 (ELF2, 90 m), descending with calibre
	/// across each family.</para>
	///
	/// <para>Zero for every pod, which is what still makes the chain gate work: a hardpoint with no
	/// range is not a weapon.</para>
	/// </summary>
	public int Range => _template?.Range ?? 0;

	/// <summary>
	/// What one shot takes out of the capacitor — the same template field at <c>0x38</c> that is the
	/// upper half of <see cref="ChargeThreshold"/>'s pair, read again by the beam dispatch as
	/// <c>min(cost, charge)</c>.
	///
	/// <para>The two shapes of that pair are two kinds of weapon. A laser reads the same number twice
	/// (LAS100 80/80): it fires at a fixed cost the moment it holds that much, so its shots are all
	/// identical. <c>PBEAM</c>, <c>EMP</c> and <c>PLAS</c> read a small low and a 10000 high (300 /
	/// 10000): the threshold is then whatever the mount is charging to, and the cost is the whole
	/// capacitor — a charge-up weapon whose shot is worth as much as the pilot let it accumulate. The
	/// manual's "power level" is that charge target, and the keys below are what move it.</para>
	/// </summary>
	public short ShotCost => _template?.ShotCost ?? 0;

	/// <summary>
	/// Whether this mount carries a capacitor charged off the Master Energy Pool. True for
	/// <see cref="WeaponMountKind.Elf"/> as well as <see cref="WeaponMountKind.Energy"/>: the factory
	/// builds an ELF with the energy constructor and then swaps its vtable, and the swap leaves the
	/// charge, power-level, wake and gauge slots pointing at the energy class's own.
	/// </summary>
	private bool IsEnergyClass => Kind is WeaponMountKind.Energy or WeaponMountKind.Elf;

	/// <summary>
	/// Vtable slot <c>0x5c</c> — whether the mount can no longer fight, which
	/// <see cref="MechObject.ChooseWeapon"/> asks before it looks at a mount at all: destroyed for
	/// the energy classes (<c>WeaponMount_EnergyIsSpent</c>, <c>0040ed34</c>), destroyed or out of
	/// rounds for an ammunition mount (<c>WeaponMount_AmmoIsSpent</c>, <c>0040ed48</c>), and always
	/// for a pod (<c>WeaponMount_IsSpent_Always</c>, <c>0040f8a4</c>). So a machine left with only
	/// pods and empty magazines has run dry. See docs/retail/simulation/weapon-mounts.md.
	/// </summary>
	public bool IsSpent => Kind switch {
		WeaponMountKind.Energy or WeaponMountKind.Elf => Disabled,
		WeaponMountKind.Ammunition => Disabled || ChargeTarget == 0,
		_ => true,
	};

	/// <summary>
	/// Vtable slot <c>0x54</c> — whether <see cref="MechObject.CombatRating"/> adds this mount's
	/// <see cref="AiRatingValue"/>. An ammunition mount counts only while it holds at least an eighth
	/// of its <see cref="MagazineSize"/> (<c>WeaponMount_AmmoCountsInCombatRating</c>,
	/// <c>0040f520</c>); every other class always does (<c>WeaponMount_CountsInCombatRating_Always</c>,
	/// <c>004111e9</c>).
	/// </summary>
	public bool CountsInCombatRating =>
		Kind != WeaponMountKind.Ammunition || ChargeTarget >= MagazineSize >> 3;

	/// <summary>
	/// Where this mount's weapon model stands, in world space: the firing hardpoint's own posed bone,
	/// with the hardpoint's mount point in the translation.
	///
	/// <para>The original gets there the other way round — the base constructor translates the
	/// freshly-loaded shape's own point lists by that offset (<c>Shape_TranslatePointLists</c>, <c>0040dd4c</c>) and then draws
	/// the shape at the bone, which is why every mount owns a private copy of the shape rather than
	/// sharing one. Offsetting the frame instead puts the same geometry in the same place off one
	/// shared model.</para>
	///
	/// <para><b>The offset is <see cref="MountPointOffset"/>, not <see cref="MuzzleOffset"/>.</b> A
	/// weapon hangs at its hardpoint's mount point; the template's own muzzle triple is the length
	/// of the barrel from there, and only the shot travels it.</para>
	/// </summary>
	public Transform3 ModelFrame(MechObject owner) {
		var bone = owner.PartTransform(_hardpoint.BoneId);
		var offset = MountPointOffset;
		var origin = bone.TransformPoint(offset.X, offset.Y, offset.Z);

		bone.X = origin.X;
		bone.Y = origin.Y;
		bone.Z = origin.Z;
		return bone;
	}

	/// <summary>
	/// The template's <c>+0x2c</c> — the minimum range this weapon will engage at, the lower half of
	/// <see cref="RangeAllows"/>'s window. <b>Zero in all 33 retail templates</b>, so it exists in the
	/// format and never bites; see docs/retail/formats/weapons-dat-sim.md.
	/// </summary>
	public int MinimumRange =>
		_template?.Tail is { Length: >= 0x0e } tail ? BitConverter.ToInt32(tail, 0x0a) : 0;

	/// <summary>
	/// The template's <c>+0x34</c> — what firing this weapon costs the AI in
	/// <see cref="MechObject.ChooseWeapon"/>'s score, weighed at ten times the damage credit's gain.
	/// The retail values and what they do to the choice are in docs/retail/simulation/ai-weapons.md
	/// ("Choosing a weapon").
	/// </summary>
	public short AiShotCost =>
		_template?.Tail is { Length: >= 0x14 } tail ? BitConverter.ToInt16(tail, 0x12) : (short)0;

	/// <summary>
	/// <c>WeaponMount_RangeAllows</c> (<c>0040e5f8</c>) — whether a target at <paramref name="range"/> is inside this weapon's
	/// engagement window, <see cref="MinimumRange"/> exclusive to <see cref="Range"/> exclusive. Both
	/// the AI's weapon choice and its ELF latch ask it, as does the cockpit's readiness predicate;
	/// see docs/retail/simulation/weapon-mounts.md ("Readiness").
	/// </summary>
	public bool RangeAllows(int range) => MinimumRange < range && range < Range;

	/// <summary>
	/// The template's <c>+0x4e</c> — what this weapon is worth to the AI's combat rating, scaled by
	/// the mount's condition. Read by <c>Mech_ComputeCombatRating</c> (<c>0041edd8</c>) through the
	/// mount's own <c>+0x1c</c>, which is this template; see
	/// <see cref="MechObject.CombatRating"/> and docs/retail/simulation/ai-targeting.md.
	/// </summary>
	public short AiRatingValue =>
		_template?.Tail is { Length: >= 0x2e } tail ? BitConverter.ToInt16(tail, 0x2c) : (short)0;

	/// <summary>
	/// <c>WeaponMount_MuzzleOffset</c> (<c>0040f540</c>) itself — <b>where the weapon sits</b>, as
	/// against <see cref="MuzzleOffset"/>'s where its shot comes out. It is the hardpoint's own
	/// mount-point offset (<c>.GL +0x10</c>) plus <c>WeaponMountTemplate_SideMuzzleOffset</c>
	/// (<c>0040f904</c>), and nothing else: the template's muzzle triple at <c>+0x40</c> is the
	/// barrel's length down the gun and <c>WeaponMount_PrepareShot</c> is the only thing that adds
	/// it.
	///
	/// <para>The side offset is what makes a mirrored hardpoint pair sit at mirrored points off one
	/// template. The template carries a lateral figure at <c>+0x46</c> and a vertical one at
	/// <c>+0x4a</c>; the hardpoint's mounting code picks one of them and its sign, and only one axis
	/// is ever nonzero.</para>
	///
	/// <para>This is the offset the base mount constructor bakes into its private copy of the weapon
	/// model (<c>Shape_TranslatePointLists</c>, <c>0040dd4c</c>), which is why <see cref="ModelFrame"/> reads it rather than
	/// <see cref="MuzzleOffset"/>: putting the model at the muzzle stands it a barrel's length
	/// clear of the chassis.</para>
	/// </summary>
	internal Vec3i MountPointOffset {
		get {
			int lateral = 0;
			int vertical = 0;

			if (_template?.Tail is { Length: >= 0x2a } tail) {
				switch (_hardpoint.MountingCode) {
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

			return new Vec3i(
				_hardpoint.Offset[0] + lateral,
				_hardpoint.Offset[1],
				_hardpoint.Offset[2] + vertical);
		}
	}
}
