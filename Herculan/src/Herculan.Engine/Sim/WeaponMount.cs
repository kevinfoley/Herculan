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
public sealed class WeaponMount {
	/// <summary>
	/// What an energy mount's capacitor holds at spawn, and the charge level it asks for while idle
	/// — <c>WeaponMount_CtorEnergy</c> (<c>0040e074</c>)'s <c>Q10Multiply(820, 1200)</c>, a literal pair that does not vary by
	/// weapon. Both <c>+0x7b</c> (the target) and <c>+0x7d</c> (the level) start here, so an energy
	/// weapon powers up already charged.
	/// </summary>
	public static readonly short EnergyCapacitorFull = (short)SimMath.Q10Multiply(0x334, EnergyChargeScale);

	/// <summary>
	/// The denominator the charge bar is drawn against — <c>WeaponMount_PushEnergyGaugeState</c> (<c>0040f288</c>) pushes
	/// <c>(charge &lt;&lt; 10) / 1200</c> to a widget whose LED bar has a range of 1024. It is not
	/// the capacitor's own capacity, which is why a fully charged weapon reads four-fifths of a bar
	/// rather than a full one: 960 out of 1200.
	///
	/// <para>What fills the last fifth is the <b>power-level keys</b> — see
	/// <see cref="AdjustPower"/>. <c>WeaponMount_DemandFullCharge</c> (<c>0040f4f0</c>) does the same
	/// thing in one step and was the obvious candidate, but its only caller (<c>WeaponMounts_DemandFullChargeOnArmed_Dead</c> (<c>00410d50</c>),
	/// "raise the armed mount to full and clear everyone else's mid-charge flag") has no reference of
	/// any kind anywhere in the image — neither a call nor a stored address — so nothing in the
	/// retail build ever reaches it.</para>
	/// </summary>
	public const short EnergyChargeScale = 0x4b0;

	/// <summary>How much an energy mount draws per tick when it is the one being served — <c>+0x7f</c>, 20 until its component is past half damage (see <see cref="ConditionChanged"/>).</summary>
	public const short EnergyChargeRate = 0x14;

	/// <summary>
	/// The charge level an idle energy mount asks for — <c>WeaponMount_WakeCapacitor</c> (<c>0040f4d8</c>)'s literal <c>0x334</c>,
	/// the same 820 <see cref="EnergyCapacitorFull"/> is derived from. A mount with a shot demanded
	/// of it raises its target to <see cref="EnergyChargeScale"/> instead.
	/// </summary>
	public const short EnergyIdleTarget = 0x334;

	/// <summary>
	/// What a mount whose turn has passed bleeds back into the pool each tick, once some other mount
	/// has declared itself mid-charge — <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>)'s floor of -5 on a negative deficit.
	/// </summary>
	public const short EnergyBleedBack = 5;

	/// <summary>
	/// Catalog id 25, <c>PLAS</c> — the one weapon <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>) singles out by id. Its
	/// capacitor deficit counts double and only half of what it draws is stored, so it costs twice
	/// the pool for the same charge.
	/// </summary>
	public const int HalfEfficiencyWeaponId = 0x19;

	/// <summary>The catalog id of a mount the factory builds nothing for.</summary>
	public const int EmptyWeaponId = 0;

	/// <summary>
	/// The step one press of the power-level keys moves an energy mount's charge target —
	/// <c>WeaponMount_AdjustPowerLevel</c> (<c>0040f48c</c>)'s literal <c>0x50</c>, clamped to 0..<see cref="EnergyChargeScale"/>.
	/// </summary>
	public const short EnergyPowerStep = 0x50;

	/// <summary>
	/// The fixed-point scale on <see cref="RefireDelay"/> at full health — <c>+0x63</c>, which the
	/// base mount constructor (<c>WeaponMount_CtorBase</c>, <c>0040df30</c>) writes into every mount. A Q10 unit, so an
	/// undamaged mount arms the template's own figure exactly.
	/// </summary>
	public const short RefireScaleFull = 0x400;

	/// <summary>
	/// <c>+0x63</c> as it currently stands. Only a gun mount's own damage moves it — see
	/// <see cref="ConditionChanged"/>, which steps it down by
	/// <see cref="RefireScalePerDamageStep"/> for every <see cref="MountDamageStep"/> of damage past
	/// <see cref="MountDamageOnset"/> on the mount's component. A launcher's is never touched: that
	/// class cooks off instead.
	/// </summary>
	public short RefireScale { get; private set; } = RefireScaleFull;

	private readonly Weapons.WeaponMountTemplate? _template;
	private readonly GunLayout.HardpointEntry _hardpoint;
	private short _refireTimer;

	/// <summary><c>mount+0x24</c> and <c>+0x28</c> — see <see cref="ConvergeOnRange"/>.</summary>
	private short _convergePitch;

	private short _convergeYaw;
	private bool _firedSinceShuffle;
	private bool _firedThisTick;
	private bool _flashPlaying;
	private bool _spinUpRunning;
	private bool _spinUpLatched;
	private short _spinUpCellTimer;

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
	/// How many cells the weapon model's flipbook has — the shape's <c>SequenceList[0]</c>,
	/// <c>*shape+0x20</c>. Retail weapon shapes carry two to seven; a shape with no sequence at all
	/// (every pod) reports one and so never flashes, and zero means the install has no such shape.
	/// </summary>
	public int FlashCellCount { get; }

	/// <summary>
	/// Which cell of the weapon model's flipbook is showing — the first entry of the private
	/// per-sequence frame array the base constructor allocates at <c>mount+0x14</c>, and
	/// <b>the muzzle flash</b>. Cell zero is the gun at rest; a shot starts the book and
	/// <see cref="ChargeTick"/> walks it one cell a tick until it wraps back to zero.
	/// </summary>
	public int FlashCell { get; private set; }

	/// <summary>
	/// This mount's index in the machine's mount array — its position in the <c>.GL</c> file. It is
	/// what the selected-weapon index, the fire-group arrays and <see cref="LinkPartnerOffset"/> are
	/// all relative to. The Heads-Down Display's weapon list prints in <see cref="LoadoutSlot"/> order
	/// instead — see <see cref="Content.DamageHardpoint"/>.
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
	/// <see cref="Content.PaperDollDamage.PlaceWeaponIcon"/>.
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
	/// earlier weapon's record. With <see cref="TweakSettingDefinitions.FixWeaponDamageRecords"/> on,
	/// the shot is <see cref="Projectile"/> under its corrected subtype id
	/// (<see cref="WeaponCatalog.CorrectedProjectile"/>). See docs/formats/proj-dat.md#lookup.
	/// </summary>
	public ProjectileData.Projectile? ShotProjectile =>
		TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.FixWeaponDamageRecords)
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
	/// The on/off button on this mount's cockpit row — the pod gauge's own byte at <c>gauge+0xc2</c>,
	/// which <c>TogglePodGauge_OnClick</c> (<c>004419fc</c>) XORs when the row is pressed.
	///
	/// <para><b>It lives on the gauge in the original, not on the mount</b>, and the pod's tick copies
	/// it across each frame. It is kept here because the cockpit's rows are rebuilt from sim state
	/// every frame rather than being objects that persist, so the mount is the only thing on the
	/// press's side of the frame that outlives it. Only the two pods with a
	/// <see cref="HasPodButton"/> gauge class ever have it moved.</para>
	/// </summary>
	public bool PodButton { get; internal set; }

	/// <summary>
	/// Whether this row's press has anything to toggle: <c>CockpitView_CreatePodGauge</c>
	/// (<c>004321d4</c>) builds a <c>TogglePodGauge</c> for the ECM pod and a <c>TurboPodGauge</c>
	/// derived from it for the Turbo pod, and the plain <c>PodGauge</c> — whose click sets the repaint
	/// byte and returns — for the other three. See docs/simulation/equipment-pods.md.
	/// </summary>
	public bool HasPodButton => Kind == WeaponMountKind.Pod
		&& WeaponId is MechPods.EcmWeaponId or MechPods.TurboPodWeaponId;

	/// <summary>
	/// <c>pod+0x81</c>, the Turbo Pod's engaged flag, and the only thing
	/// <see cref="MechObject.TurboSpeedBonus"/> is gated on. Raised by <see cref="EngageTurbo"/> and
	/// dropped either by the row's button going off or by the tank running dry.
	/// </summary>
	public bool TurboEngaged { get; internal set; }

	/// <summary>What <c>TurboPod_Ctor</c> fills the tank to, and the ceiling the pool refills it to.</summary>
	public const int TurboChargeFull = 2000;

	/// <summary>
	/// The charge <c>TurboPod_Engage</c> (<c>0040f09c</c>) demands before it will engage — so a pod
	/// that has just run itself dry cannot be switched straight back on.
	/// </summary>
	public const int TurboEngageCharge = 600;

	/// <summary>What an engaged Turbo Pod spends per tick — <c>TurboPod_ChargeTick</c>'s <c>0x23</c>.</summary>
	public const int TurboSpendRate = 0x23;

	/// <summary>And what it buys back per tick out of the weapons' leftover pool budget.</summary>
	public const short TurboRefillRate = 0x14;

	/// <summary>
	/// The range the Turbo row's LED bar is built with — <c>TurboPodGauge_Ctor</c>'s literal
	/// <c>0x9c4</c>. It is larger than the tank, so a full pod fills four-fifths of its bar, the same
	/// way an energy weapon's does.
	/// </summary>
	public const int TurboMeterRange = 0x9c4;

	/// <summary>
	/// <c>TurboPod_Engage</c> (<c>0040f09c</c>): engage if the pod is idle and holds more than
	/// <see cref="TurboEngageCharge"/>. Both the player's row button and the two AI sites that sprint
	/// — the flee behaviour and a long drive under a standing squad order — come through here, which
	/// is why an AI machine engages one at all despite never ticking its pods.
	/// </summary>
	/// <param name="world">Optional, and only so the engage tone has somewhere to go.</param>
	/// <param name="audible">
	/// Whether to sound it. The original gates the sound on the pod having a cockpit gauge
	/// (<c>+0x79</c>), which no AI machine's pod has.
	/// </param>
	/// <returns>Whether the pod engaged just now.</returns>
	internal bool EngageTurbo(SimWorld? world = null, bool audible = false) {
		if (TurboEngaged || Charge <= TurboEngageCharge) {
			return false;
		}

		TurboEngaged = true;
		if (audible) {
			world?.Sounds?.Play(Audio.SoundId.Throttle);
		}

		return true;
	}

	/// <summary>
	/// <c>WeaponMount_Destroy</c> (<c>0040f57c</c>) — the mount side of losing a hardpoint, reached
	/// from the destruction roll a band change on one of the machine's mount components makes (see
	/// <c>MechObject</c>'s <c>ApplyDirectFireDamage</c>) and from the mount's own condition
	/// notification (<c>WeaponMount_ConditionChangedBase</c>, <c>0040ee0c</c>) when that reports a fully-damaged component.
	///
	/// <para>Two writes, and they are the whole of the state change: the weapon model at
	/// <c>mount+0x10</c> is dropped, so the gun stops being drawn on the chassis, and the destroyed
	/// byte at <c>+0x49</c> is set, which is what stops the mount charging, firing and being armed,
	/// and turns its cockpit row into <c>OFFLINE</c>. It is idempotent in the original too: the whole
	/// body is under a test of that byte.</para>
	///
	/// <para><b>And the gun goes flying.</b> A visibly-mounted hardpoint (<c>.GL +6 &lt;</c>
	/// <see cref="InvisibleMounting"/>) throws its own model — the same shape index, out of
	/// <see cref="DebrisShapeLibraryName"/> rather than the library it was drawn from — off the mount
	/// point as a <see cref="DebrisObject"/>, on a <c>Math_EulerToward</c> bearing away from the
	/// machine's aim point, at the hardpoint's own stated pitch and a flat <see cref="DebrisMass"/>.
	/// It keeps the muzzle frame's attitude, so it tumbles from the angle it was mounted at.</para>
	///
	/// <para><b>The two ways of losing a mount throw different wreckage.</b>
	/// <paramref name="rolled"/> is the original's third argument, and it decides the pair: the
	/// certain path through <see cref="ConditionChanged"/> passes 0 and gets a piece that bursts —
	/// group <see cref="ComponentDamage.DefaultDebrisGroup"/> with <see cref="DebrisBurstEffect"/>
	/// behind it — while the destruction roll passes 1 and gets a plain piece that just falls. So a
	/// gun lost because its bracket was shot away goes up, and one lost to the roll simply
	/// drops.</para>
	/// </summary>
	/// <param name="world">Where the wreckage goes, or null to change the state alone.</param>
	/// <param name="owner">The machine the mount hangs off, which places the throw.</param>
	/// <param name="rolled">
	/// Whether this came from the destruction roll rather than the certain path — see above.
	/// </param>
	/// <param name="debris">The machine's own debris table, for the burst's group.</param>
	internal void Destroy(SimWorld? world = null, MechObject? owner = null, bool rolled = true,
			DebrisDatabase? debris = null) {
		if (Disabled) {
			return;
		}

		bool visible = _hardpoint.MountingCode < InvisibleMounting;
		int thrownShape = ModelShapeIndex;

		ModelShapeIndex = -1;
		Disabled = true;

		if (!visible || thrownShape < 0 || world == null || owner == null) {
			return;
		}

		var bone = owner.PartTransform(_hardpoint.BoneId);
		var offset = MountPointOffset;
		var muzzle = bone.TransformPoint(offset.X, offset.Y, offset.Z);

		world.SpawnDebrisPiece(DebrisShapeLibraryName, thrownShape,
			world.DebrisShapeRadius(DebrisShapeLibraryName, thrownShape),
			muzzle, bone.ToEuler(),
			SimTrig.EulerToward(muzzle, owner.AimPoint).Z, _hardpoint.DebrisPitch, DebrisMass,
			rolled ? (short)-1 : ComponentDamage.DefaultDebrisGroup,
			rolled ? (short)-1 : DebrisBurstEffect,
			debris);
	}

	/// <summary>
	/// The shape file a knocked-off gun is thrown as a piece of — <c>dts\MECHWPN2.DTS</c>, the second
	/// weapon model library, indexed by the same
	/// <see cref="Weapons.WeaponMountTemplate.ModelShapeIndex"/> the mount was drawn by. It shares
	/// <c>WPNTEX</c> with <c>MECHWPNS.DTS</c>; <c>Weapons_LoadResourceTables</c> binds that bank to
	/// every shape in both.
	/// </summary>
	public const string DebrisShapeLibraryName = "MECHWPN2.DTS";

	/// <summary>What the thrown gun's launch speed is divided by — the literal 0x4b0.</summary>
	public const short DebrisMass = 0x4b0;

	/// <summary>
	/// The <c>EXPLOS.DAT</c> effect a bursting thrown gun sets off where it lands — the literal 0x14.
	/// </summary>
	public const short DebrisBurstEffect = 0x14;

	/// <summary>
	/// The mount's vtable slot <c>0x68</c>, the condition notification — <c>WeaponMount_ConditionChangedBase</c> (<c>0040ee0c</c>) for the
	/// base class and the pods, <c>WeaponMount_ConditionChanged</c> (<c>0040ee90</c>) for the ammunition class,
	/// <c>WeaponMount_ConditionChangedEnergy</c> (<c>0040ee38</c>) for the energy and ELF classes, and
	/// <c>TargetingPod_ConditionChanged</c> (<c>0040ef6c</c>) for the Targeting Pod.
	/// <c>Mech_ComponentDamageWrite</c> reads every mount's component before its write and again
	/// after, and hands both readings to every mount on the machine, so this runs on all of them for
	/// any hit anywhere and is a no-op wherever the two agree.
	///
	/// <list type="number">
	/// <item><b>A component that reads <see cref="MechObject.FullyDamaged"/> destroys its mount</b>,
	/// with no roll. That is the base class' whole slot, and the certain half of losing a
	/// hardpoint — the roll in <c>MechObject.RollWeaponMountDestruction</c> is the other, and takes
	/// mounts out before their component is gone.</item>
	/// <item><b>A launcher cooks off.</b> Past <see cref="MountDamageOnset"/> — half damage — a
	/// <c>Rocket</c> mount rolls <see cref="MountCookOffOdds"/> in 1024 once for every
	/// <see cref="MountDamageStep"/> the reading crossed, and the first success destroys it. A hit
	/// that takes the component from pristine to nearly gone therefore rolls five or six times.</item>
	/// <item><b>A gun's refire scale moves instead</b>, by
	/// <see cref="RefireScalePerDamageStep"/> per step over the same range — see
	/// <see cref="RefireScale"/>. A <c>Bullet</c> mount is never rolled for and a beam mount is
	/// neither rolled for nor rescaled.</item>
	/// <item><b>An energy mount charges more slowly instead.</b> Its <see cref="ChargeRate"/> is
	/// reset to <see cref="EnergyChargeRate"/> on every notification and, past the same onset, loses
	/// <c>Q10(20, steps * 100)</c> — 19, 17, 15, 13, 11 over the five steps. Neither the cook-off nor
	/// the refire scale applies to it, whatever its projectile type.</item>
	/// </list>
	///
	/// <para><b>An empty ammunition mount is exempt from both of its effects</b> — the original gates
	/// them on <c>+0x7b</c>, <see cref="ChargeTarget"/>, so a launcher out of missiles cannot cook
	/// off.</para>
	/// </summary>
	/// <param name="world">Where the wreckage the mount throws goes — see <see cref="Destroy"/>.</param>
	/// <param name="owner">The machine the mount hangs off.</param>
	/// <param name="debris">Its own debris table.</param>
	/// <param name="before">The mount's component reading before the write, 0 pristine and 256 gone.</param>
	/// <param name="after">The same reading after it.</param>
	internal void ConditionChanged(SimRandom random, int before, int after, SimWorld? world = null,
			MechObject? owner = null, DebrisDatabase? debris = null) {
		// TargetingPod_ConditionChanged (0040ef6c): the base slot, then the reading cached -- the only
		// writer of the cache.
		if (ComponentLock != null) {
			ComponentLock.ComponentDamage = (short)after;
		}

		if (after == MechObject.FullyDamaged) {
			Destroy(world, owner, rolled: false, debris);
		}

		if (IsEnergyClass) {
			ChargeRate = EnergyChargeRate;
			if (after > MountDamageOnset) {
				ChargeRate -= (short)SimMath.Q10Multiply(EnergyChargeRate,
					(after - MountDamageOnset) / MountDamageStep * 100);
			}

			return;
		}

		if (Kind != WeaponMountKind.Ammunition || ChargeTarget == 0 || Projectile is not { } projectile
				|| after <= MountDamageOnset) {
			return;
		}

		int last = (after - MountDamageOnset) / MountDamageStep;

		if (projectile.Type != ProjectileType.Rocket) {
			if (projectile.Type == ProjectileType.Bullet) {
				RefireScale = (short)(RefireScaleFull - last * RefireScalePerDamageStep);
			}

			return;
		}

		// The original's own loop bounds. C division truncates toward zero, so a reading below the
		// onset gives a step of 0 rather than a negative one until it is a full step below; the
		// clamp is what keeps a shot that crosses the onset from rolling more than once for it.
		int step = (before - MountDamageOnset) / MountDamageStep;
		if (step < 0) {
			step = -1;
		}

		for (; step < last; step++) {
			if (random.NextMasked(0x3ff) < MountCookOffOdds) {
				Destroy(world, owner, rolled: false, debris);
				return;
			}
		}
	}

	/// <summary>
	/// The component reading a mount's own damage starts to tell on it at — half gone. Below it a
	/// mount is as good as new however much the section around it has taken.
	/// </summary>
	public const int MountDamageOnset = 0x80;

	/// <summary>
	/// How much further damage buys one more roll for a launcher, or one more step off a gun's
	/// <see cref="RefireScale"/>. The reading runs to 256, so there are five steps in all.
	/// </summary>
	public const int MountDamageStep = 25;

	/// <summary>
	/// A launcher's odds of cooking off per <see cref="MountDamageStep"/>, out of 1024 — a shade
	/// under 30%, compounding over however many steps one hit crossed.
	/// </summary>
	public const int MountCookOffOdds = 300;

	/// <summary>
	/// What one <see cref="MountDamageStep"/> takes off a gun mount's <see cref="RefireScale"/>.
	///
	/// <para><b>It shortens the refire delay.</b> The scale multiplies the template's figure, so a
	/// gun on a half-wrecked mount arms half the delay and fires roughly twice as fast. That reads
	/// backwards for damage and it is what the original does — <c>WeaponMount_ConditionChanged</c> (<c>0040ee90</c>) subtracts from
	/// <c>0x400</c> and <c>WeaponMount_PrepareShot</c> multiplies by the result.</para>
	/// </summary>
	public const int RefireScalePerDamageStep = 0x66;

	/// <summary>
	/// <c>+0x31</c>, the refire countdown. Zero means the mount is out of its delay. A shot arms it
	/// with <see cref="RefireDelay"/> and the mount's own turn at the pool counts it down by the
	/// timestep, so it is the same clock everything else in the simulation runs on.
	/// </summary>
	public short RefireTimer => _refireTimer;

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
	public int Range =>
		_template?.Tail is { Length: >= 0x12 } tail ? BitConverter.ToInt32(tail, 0x0e) : 0;

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
	public short ShotCost =>
		_template?.Tail is { Length: >= 0x18 } tail ? BitConverter.ToInt16(tail, 0x16) : (short)0;

	/// <summary>
	/// The refire delay a shot arms, in the same timer units <see cref="RefireTimer"/> counts down in
	/// — the template's <c>0x4c</c>, scaled by <see cref="RefireScale"/>.
	///
	/// <para>At the simulation's 81-per-tick countdown, the retail 1200 that most weapons carry is
	/// about 15 ticks, or 0.6 s. <c>ELF</c> and <c>ELF2</c> carry <b>zero</b>, so they never have a
	/// delay at all — a continuous beam, held down and firing every tick the capacitor allows.</para>
	///
	/// <para>The scale is a full <c>0x400</c> until the mount's own component takes damage, at which
	/// point a gun's delay <i>shortens</i>. See <see cref="RefireScalePerDamageStep"/>.</para>
	/// </summary>
	public short RefireDelay =>
		_template?.Tail is { Length: >= 0x2c } tail
			? (short)SimMath.Q10Multiply(RefireScale, BitConverter.ToInt16(tail, 0x2a))
			: (short)0;

	/// <summary>Whether the pool arbitration treats this mount as half-efficient — <c>PLAS</c> alone.</summary>
	public bool HalfEfficiency => WeaponId == HalfEfficiencyWeaponId;

	/// <summary>
	/// Whether this mount carries a capacitor charged off the Master Energy Pool. True for
	/// <see cref="WeaponMountKind.Elf"/> as well as <see cref="WeaponMountKind.Energy"/>: the factory
	/// builds an ELF with the energy constructor and then swaps its vtable, and the swap leaves the
	/// charge, power-level, wake and gauge slots pointing at the energy class's own.
	/// </summary>
	private bool IsEnergyClass => Kind is WeaponMountKind.Energy or WeaponMountKind.Elf;

	/// <summary>
	/// Whether the mount fired during the previous tick — the mount's <c>+0x33</c> flag, and the only
	/// thing that lets an ELF keep firing below a full capacitor.
	///
	/// <para>The original keeps two byte blocks, <c>+0x33</c> and <c>+0x3b</c>. Firing sets both
	/// (<c>WeaponMount_PrepareShot</c>); each tick <c>WeaponMount_RefireTick</c> <b>ands</b>
	/// <c>+0x33</c> with <c>+0x3b</c> and then clears <c>+0x3b</c> (<c>WeaponMount_AndFlagBlocks</c>, <c>0040f881</c>). So the flag
	/// survives exactly as long as the mount fires on every tick and drops on the first tick after one
	/// it sat out — a "still firing", not a "has ever fired".</para>
	/// </summary>
	public bool FiringSustained => _firedSinceShuffle;

	/// <summary>
	/// Vtable slot <c>0x3c</c>, <c>WeaponMount_WakeCapacitor</c> (<c>0040f4d8</c>): put an energy mount's charge target back to its
	/// idle level. Only the energy class implements it — the other two have a no-op in that slot.
	/// </summary>
	internal void WakeCapacitor() {
		if (IsEnergyClass && !Disabled) {
			ChargeTarget = EnergyIdleTarget;
		}
	}

	/// <summary>Rounds remaining, as the ammunition gauge prints them — <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>)'s <c>+0x7d &gt;&gt; 8</c>.</summary>
	public int Rounds => Charge >> 8;

	/// <summary>
	/// The charge bar's value, over the 0-1024 range its LED bar was built with —
	/// <c>WeaponMount_PushEnergyGaugeState</c> (<c>0040f288</c>)'s <c>(charge &lt;&lt; 10) / 1200</c>.
	/// </summary>
	public int ChargeMeterValue => (Charge << 10) / EnergyChargeScale;

	/// <summary>
	/// The priority this mount reports to the arbitration — <c>WeaponMount_GetEnergyPriority</c>
	/// (<c>0040f504</c>) for an energy mount, a flat zero for every other class
	/// (<c>WeaponMount_GetEnergyPriorityZero</c> (<c>004111e2</c>)). A mount already mid-charge reports 10000 and so is always served first,
	/// which is how one weapon finishes charging before another starts.
	/// </summary>
	public short EnergyPriority => Kind switch {
		WeaponMountKind.Energy or WeaponMountKind.Elf => Charging ? (short)10000 : ChargeTarget,
		_ => 0,
	};

	/// <summary>
	/// Whether the mount could fire right now — the per-class test at vtable slot <c>0x2c</c>.
	///
	/// <list type="bullet">
	/// <item><b>Ammunition</b> (<c>WeaponMount_AmmoCanFire</c>, <c>0040ed6c</c>): not destroyed, out of its refire delay, and
	/// holding at least one round.</item>
	/// <item><b>Energy</b> (<c>WeaponMount_EnergyCanFire</c>): not destroyed, out of its refire delay,
	/// and charged to at least the threshold below.</item>
	/// <item><b>ELF</b> (<c>ElfCanFire</c>): not destroyed and charged to a <i>full</i> capacitor —
	/// unless it is already firing, in which case one shot's worth is enough. It does not consult the
	/// refire delay at all, which for these two weapons is zero anyway.</item>
	/// <item><b>Pods</b> have no such method — they never fire and are never in a fire group.</item>
	/// </list>
	/// </summary>
	public bool CanFire => Kind switch {
		WeaponMountKind.Ammunition => !Disabled && RefireTimer == 0 && ChargeTarget != 0,
		WeaponMountKind.Energy => !Disabled && RefireTimer == 0 && ChargeThreshold <= Charge,
		WeaponMountKind.Elf => ElfCanFire,
		_ => false,
	};

	/// <summary>
	/// Vtable slot <c>0x5c</c> — whether the mount can no longer fight, which
	/// <see cref="MechObject.ChooseWeapon"/> asks before it looks at a mount at all: destroyed for
	/// the energy classes (<c>WeaponMount_EnergyIsSpent</c>, <c>0040ed34</c>), destroyed or out of
	/// rounds for an ammunition mount (<c>WeaponMount_AmmoIsSpent</c>, <c>0040ed48</c>), and always
	/// for a pod (<c>WeaponMount_IsSpent_Always</c>, <c>0040f8a4</c>). So a machine left with only
	/// pods and empty magazines has run dry. See docs/simulation/weapon-mounts.md.
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
	/// <c>ElfMount_CanFire</c> (<c>0040eda0</c>), the ELF class's vtable <c>+0x2c</c> — <b>why an ELF cannot be re-triggered
	/// until its capacitor is back to full</b>.
	///
	/// <para>It uses the same two template fields as the energy test but drops the branch between
	/// them: the threshold is <i>always</i> <c>max(template+0x36, chargeTarget)</c>, and for both
	/// ELFs the target (960, or whatever the power-level keys set) is the larger. So a fresh trigger
	/// pull needs a full capacitor. The second clause is what makes it a sustained beam rather than a
	/// single shot: once <see cref="FiringSustained"/> is set the bar drops to one shot's
	/// <see cref="ShotCost"/>, so the weapon empties itself over as many ticks as it has charge for
	/// and cannot be started again until it has climbed all the way back.</para>
	///
	/// <para>Turning the mount's power level down therefore makes an ELF re-fire sooner and stop
	/// sooner, since the target is both the gate and the fuel — see <see cref="AdjustPower"/>.</para>
	/// </summary>
	private bool ElfCanFire {
		get {
			if (Disabled) {
				return false;
			}

			short floor = _template?.Tail is { Length: >= 0x18 } tail
				? BitConverter.ToInt16(tail, 0x14)
				: (short)0;
			short threshold = Math.Max(floor, ChargeTarget);

			return threshold <= Charge || (FiringSustained && ShotCost <= Charge);
		}
	}

	/// <summary>
	/// How much charge an energy mount needs before it will fire — <c>WeaponMount_EnergyCanFire</c>'s
	/// own arithmetic over the template's two fields at <c>+0x36</c> and <c>+0x38</c>. When the first
	/// is below the second the threshold is the larger of it and the mount's current target; otherwise
	/// the second is used outright. Real templates carry both shapes: <c>EMP</c> reads (350, 10000),
	/// <c>ELF</c> reads (400, 70) — though the ELFs do not reach this test, see
	/// <see cref="ElfCanFire"/>.
	/// </summary>
	private short ChargeThreshold {
		get {
			if (_template?.Tail is not { Length: >= 0x18 } tail) {
				return 0;
			}

			short low = BitConverter.ToInt16(tail, 0x14);
			short high = BitConverter.ToInt16(tail, 0x16);
			return low < high ? Math.Max(low, ChargeTarget) : high;
		}
	}

	/// <summary>
	/// This mount's turn at the Master Energy Pool — vtable slot <c>0x34</c>. An ammunition mount's
	/// override (<c>WeaponMount_RefireTick</c>, <c>0040ef94</c>) hands the budget straight back; an energy mount runs
	/// <c>WeaponMount_ChargeCapacitor</c> (<c>0040f00c</c>):
	///
	/// <list type="number">
	/// <item>The deficit is the mount's target (or zero, once another mount has claimed the tick)
	/// minus its current level, doubled for <c>PLAS</c>.</item>
	/// <item>A positive deficit takes <c>min(charge rate, budget, deficit)</c> — so a mount can be
	/// starved by an empty pool as easily as by its own rate.</item>
	/// <item>A deficit of zero or less clears the mid-charge flag and gives back up to
	/// <see cref="EnergyBleedBack"/> a tick, which is what "targeting zero" means: the capacitor
	/// drains into the pool for someone else to use.</item>
	/// <item>Half of what <c>PLAS</c> draws is thrown away rather than stored.</item>
	/// </list>
	/// </summary>
	/// <param name="budget">What is left of the pool this tick.</param>
	/// <param name="yieldToOther">Whether some earlier mount has already declared itself mid-charge.</param>
	/// <returns>The budget with this mount's draw removed — negative draws put charge back.</returns>
	internal short ChargeTick(short budget, bool yieldToOther) {
		if (Kind == WeaponMountKind.Pod) {
			return WeaponId == MechPods.TurboPodWeaponId ? TurboChargeTick(budget) : budget;
		}

		if (Disabled) {
			return budget;
		}

		// ElfMount_SpinUpAndChargeTick's own half, which runs before it falls through into the
		// energy class's slot below. Only the ELF vtable has it.
		if (Kind == WeaponMountKind.Elf) {
			SpinUpTick();
		}

		// WeaponMount_RefireTick — the refire countdown. It is the whole of an ammunition mount's turn
		// at the pool (that function *is* its vtable slot 0x34) and the first thing the energy class's
		// own slot does, so a mount's cooldown runs on the same pass that charges it and a destroyed
		// mount's does not run at all.
		if (IsEnergyClass || Kind == WeaponMountKind.Ammunition) {
			SimMath.CountdownTimerTick(ref _refireTimer);

			// WeaponMount_AndFlagBlocks (0040f881): +0x33 &= +0x3b, then +0x3b is cleared. See FiringSustained — the ELF
			// readiness test is the one thing that reads the result.
			_firedSinceShuffle &= _firedThisTick;
			_firedThisTick = false;

			MuzzleFlashTick();
		}

		if (!IsEnergyClass) {
			AmmoGaugeDecayTick();
			return budget;
		}

		short deficit = (short)((yieldToOther ? 0 : ChargeTarget) - Charge);
		if (HalfEfficiency) {
			deficit *= 2;
		}

		short draw;
		if (deficit < 1) {
			Charging = false;
			draw = Math.Max(deficit, (short)-EnergyBleedBack);
		} else {
			draw = Math.Min(Math.Min(ChargeRate, budget), deficit);
		}

		Charge += draw;
		if (HalfEfficiency) {
			Charge -= draw >> 1;
		}

		return (short)(budget - draw);
	}

	/// <summary>
	/// <c>TurboPod_ChargeTick</c> (<c>0040f0d0</c>) — the Turbo Pod's own <c>+0x34</c> override, and
	/// the only pod turn at the Master Energy Pool that costs anything. Every other pod inherits
	/// <c>WeaponMount_RefireTick</c> there and hands the budget straight back.
	///
	/// <para>Spending and refilling are gated differently: an engaged pod burns
	/// <see cref="TurboSpendRate"/> a tick whether or not the mount is destroyed, and cuts out when
	/// that empties it, but only a live mount buys any back. So shooting the hardpoint a Turbo Pod
	/// sits on leaves the pilot whatever is in the tank and no more.</para>
	/// </summary>
	private short TurboChargeTick(short budget) {
		if (TurboEngaged) {
			Charge -= TurboSpendRate;
			if (Charge < 1) {
				TurboEngaged = false;
				Charge = 0;
			}
		}

		if (Disabled) {
			return budget;
		}

		short deficit = (short)(TurboChargeFull - Charge);
		if (deficit < 1) {
			return budget;
		}

		short draw = Math.Min(Math.Min(TurboRefillRate, budget), deficit);
		Charge += draw;
		return (short)(budget - draw);
	}

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
	/// The muzzle flash, and the whole of it — <c>WeaponMount_RefireTick</c>'s tail. A shot raises
	/// <c>mount+0x44</c> and this walks the weapon model's flipbook one cell a tick from there;
	/// when it wraps back to cell zero the flag is dropped and the gun is at rest again. So the
	/// flash lasts <see cref="FlashCellCount"/> ticks and the data decides how long that is —
	/// two to seven cells depending on the weapon, seven on both ELFs.
	///
	/// <para>Nothing restarts a flash already playing — the flag is already set, so a mount firing
	/// every tick shows a continuously cycling book rather than one stuck on its first cell.</para>
	/// </summary>
	private void MuzzleFlashTick() {
		if (!_flashPlaying || FlashCellCount <= 0) {
			return;
		}

		FlashCell = (FlashCell + 1) % FlashCellCount;
		if (FlashCell == 0) {
			_flashPlaying = false;
		}
	}

	/// <summary>
	/// Raises <c>mount+0x44</c>, which both fire dispatches do whenever the hardpoint is a visible
	/// one (<c>.GL +6 &lt; 4</c>). The ammunition class raises it on its <c>Bullet</c> branch only:
	/// a rocket comes off a rail rather than out of a barrel and the original lights nothing for it.
	/// </summary>
	private void StartMuzzleFlash() {
		if (ModelShapeIndex >= 0) {
			_flashPlaying = true;
		}
	}

	/// <summary>
	/// Vtable slot <c>0x30</c>, the trigger read — <c>WeaponMount_TriggerHeld</c> for every class but
	/// the ELF, which is the device's fire byte handed straight back, and
	/// <c>ElfMount_TriggerHeld</c> (<c>0040e680</c>) for the ELF, which is a <b>spin-up</b>.
	///
	/// <para>The first press of an ELF's trigger fires nothing. It sets <c>+0x47</c> and returns
	/// zero; <see cref="SpinUpTick"/> then walks the muzzle-flash flipbook one cell a tick, and at
	/// the last cell latches <c>+0x48</c> and clears <c>+0x47</c>. From then on this returns the
	/// trigger byte itself and the weapon fires every tick until release, which drops the latch and
	/// rewinds the book to cell zero. The spin-up is therefore exactly
	/// <see cref="FlashCellCount"/> ticks long — seven for both ELFs, about a third of a second —
	/// and it is the weapon model's own flipbook that sets that length.</para>
	///
	/// <para><b><c>ELF2</c> skips it.</b> The function opens by forcing both flags set when the
	/// template's self-index (<c>+0x56</c>, which is the catalog id) is
	/// <see cref="Elf2WeaponId"/>, so the second-generation weapon fires on the press.</para>
	///
	/// <para><b>It is only asked of a mount that is ready</b> — <see cref="WeaponMounts.FireTick"/>
	/// tests <see cref="CanFire"/> first and returns without reaching this. So an ELF whose
	/// capacitor is still filling does not spin up, and one that empties mid-burst keeps its latch
	/// until the trigger is released after it has recharged.</para>
	/// </summary>
	/// <param name="held">The device's fire byte — see <see cref="MechControls.Fire"/>.</param>
	/// <returns>Whether this mount considers the trigger pulled <i>this</i> tick.</returns>
	internal bool TriggerHeld(bool held) {
		if (Kind != WeaponMountKind.Elf) {
			return held;
		}

		if (WeaponId == Elf2WeaponId) {
			_spinUpRunning = true;
			_spinUpLatched = true;
		}

		if (!_spinUpLatched) {
			if (!held) {
				if (_spinUpRunning && ModelShapeIndex >= 0) {
					FlashCell = 0;
					_spinUpRunning = false;
				}
			} else if (!_spinUpRunning) {
				_spinUpRunning = true;
				_spinUpCellTimer = 0;
			}

			return false;
		}

		if (!held) {
			if (ModelShapeIndex >= 0) {
				FlashCell = 0;
			}

			_spinUpLatched = false;
		}

		return held;
	}

	/// <summary>
	/// <c>ElfMount_SpinUpAndChargeTick</c> (<c>0040f3d8</c>) ahead of its fall-through into
	/// <c>WeaponMount_ChargeCapacitor</c>: while the spin-up is running, step the weapon model's
	/// flipbook one cell, and at its last cell latch the trigger through.
	///
	/// <para>The cell timer at <c>mount+0x84</c> is modelled because it is what the original counts,
	/// but it never delays anything: both the press and each advance reset it to zero, and
	/// <see cref="SimMath.CountdownTimerTick"/> clamps there, so it expires on every tick and the
	/// book really does move a cell per tick.</para>
	///
	/// <para>A mount with no model, or one whose model carries no flipbook, latches immediately —
	/// there are no cells to walk, so those two cases are the original's own first two tests.</para>
	/// </summary>
	private void SpinUpTick() {
		if (!_spinUpRunning || SimMath.CountdownTimerTick(ref _spinUpCellTimer) != 0) {
			return;
		}

		if (ModelShapeIndex < 0 || FlashCellCount <= 0 || FlashCell == FlashCellCount - 1) {
			_spinUpLatched = true;
			_spinUpRunning = false;
			return;
		}

		FlashCell = (FlashCell + 1) % FlashCellCount;
		_spinUpCellTimer = 0;
	}

	/// <summary>
	/// The catalog id whose template self-index <c>ElfMount_TriggerHeld</c> compares against to skip
	/// the spin-up — <c>ELF2</c>, the second-generation weapon.
	/// </summary>
	public const int Elf2WeaponId = 22;

	/// <summary>
	/// The first-generation ELF. With <see cref="Elf2WeaponId"/> it is the pair the AI's fire path
	/// latches on, because only a sustained beam wants the same mount on the next tick.
	/// </summary>
	public const int ElfWeaponId = 6;

	/// <summary>
	/// What the ammunition gauge's own printed count does between the shot and the next: the mount
	/// keeps two figures, the true round count at <c>+0x7b</c> which a shot drops instantly, and a
	/// display figure at <c>+0x7d</c> in 256ths which chases it down at
	/// <see cref="AmmoGaugeDecayRate"/> a tick. That is what makes the cockpit's round counter roll
	/// rather than jump, and it is why an ammunition mount's two "charge" fields disagree for a
	/// moment after every shot.
	///
	/// <para><b>Moved, deliberately.</b> The original does this inside
	/// <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>), the gauge-state push, which runs per
	/// frame and only for the machine whose cockpit is on screen. It is per-tick state driven by
	/// <see cref="SimMath.IntegrateRateOverTick"/>, so it belongs on the tick; the visible result for
	/// the piloted machine is the same, and an AI machine's unread display figure now decays too.</para>
	/// </summary>
	private void AmmoGaugeDecayTick() {
		if (Kind != WeaponMountKind.Ammunition) {
			return;
		}

		int floor = ChargeTarget << 8;
		if (floor < Charge) {
			Charge -= (short)SimMath.IntegrateRateOverTick(AmmoGaugeDecayRate);
		}

		if (Charge < floor) {
			Charge = floor;
		}
	}

	/// <summary>How fast the printed round count chases the real one — <c>WeaponMount_PushAmmoGaugeState</c> (<c>0040f330</c>)'s literal 250 per 125 ms.</summary>
	public const short AmmoGaugeDecayRate = 0xfa;

	/// <summary>
	/// Vtable slot <c>0x38</c>, <c>WeaponMount_AdjustPowerLevel</c> (<c>0040f48c</c>) — the power-level control, which the manual does not
	/// mention, on
	/// <c>[-]</c>/<c>[=]</c> and the numeric keypad's <c>[-]</c>/<c>[+]</c>. Moves this mount's charge
	/// target by <see cref="EnergyPowerStep"/>, clamped to zero and <see cref="EnergyChargeScale"/>.
	/// Only the energy class implements it; the other two have a no-op in that slot.
	///
	/// <para>What it changes depends on which shape the weapon's threshold pair has — see
	/// <see cref="ShotCost"/>. A laser is unaffected in everything but its bar: its threshold and its
	/// cost are both fixed. A charge-up weapon's target <i>is</i> its shot strength, and turning it
	/// down is what makes one fire sooner and hit softer.</para>
	///
	/// <para>Retail also passes the target through the charge bar's slider once a frame, which leaves it
	/// one or two units below what the keys set (960 comes back as 959). This engine keeps the exact
	/// target: docs/simulation/weapon-firing.md#the-charge-bar.</para>
	/// </summary>
	/// <param name="raise">True for the two "up" keys.</param>
	internal void AdjustPower(bool raise) {
		if (!IsEnergyClass) {
			return;
		}

		ChargeTarget += raise ? EnergyPowerStep : (short)-EnergyPowerStep;
		ChargeTarget = Math.Clamp(ChargeTarget, (short)0, EnergyChargeScale);
	}

	/// <summary>
	/// The charge bar's slider committed at <paramref name="position"/> (0..<see cref="Content.ChargeBarSlider.Range"/>),
	/// under <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/>. Retail's path, which no
	/// press reaches there: <c>EnergyWeaponGauge_OnChildClick</c> (<c>00440ef0</c>) clamps the position
	/// into the gauge's state block, and the next <c>WeaponMount_PushEnergyGaugeState</c>
	/// (<c>0040f288</c>) with <c>+0x34</c> clear reads it back as <c>position * 1200 &gt;&gt; 10</c>. This
	/// engine sets the target on the commit itself, skipping the frame's wait and the <c>+0x34</c>
	/// hand-off that would let a key press in that frame win; docs/simulation/weapon-firing.md#the-charge-bar.
	/// </summary>
	/// <returns>Whether this mount has a charge bar to take it — an energy or ELF mount still working.</returns>
	internal bool SetPowerFromChargeBar(int position) {
		if (!IsEnergyClass || Disabled) {
			return false;
		}

		ChargeTarget = ChargeTargetForBarPosition(position);
		return true;
	}

	/// <summary>
	/// The charge target a charge-bar position reads back as: clamped to the slider's range as
	/// <c>EnergyWeaponGauge_OnChildClick</c> clamps it, then <c>position * 1200 &gt;&gt; 10</c>.
	/// </summary>
	internal static short ChargeTargetForBarPosition(int position) =>
		(short)(Math.Clamp(position, 0, Content.ChargeBarSlider.Range) * EnergyChargeScale >> 10);

	/// <summary>
	/// Vtable slot <c>0x28</c>, the fire dispatch — <c>WeaponMount_FireDispatch_GunBeam</c>
	/// (<c>0040ea58</c>) for the energy class and <c>WeaponMount_FireDispatch_Missile</c>
	/// (<c>0040e964</c>) for the ammunition one. Both open with the same prologue
	/// (<c>WeaponMount_PrepareShot</c>, <c>0040e788</c>), which works out where the muzzle is and arms the refire delay, and then
	/// branch on the resolved <c>PROJ.DAT</c> record's own type.
	///
	/// <para><b>All three branches are live.</b> A <see cref="ProjectileType.Beam"/> record resolves
	/// its hit synchronously and is over inside this call; a <see cref="ProjectileType.Bullet"/>
	/// record becomes a travelling <see cref="Projectile"/>; a <see cref="ProjectileType.Rocket"/>
	/// record becomes a <see cref="Rocket"/>. <see cref="ProjectileType.Grenade"/> is the fourth value
	/// and no dispatch tests for it — no weapon template names those records, and their class is
	/// built by <c>Grenade_Construct</c> (<c>0040ac3c</c>), to which no reference is found
	/// (docs/formats/proj-dat.md#open).</para>
	///
	/// <para>Both dispatches also set a flag at <c>mount+0x44</c> whenever the hardpoint's mounting
	/// code says it is visible (<c>.GL +6 &lt; 4</c>). It is the muzzle flash, and nothing here draws
	/// one.</para>
	/// </summary>
	/// <param name="freeShot">
	/// The mission's unlimited-ammunition cheat, <see cref="SimWorld.UnlimitedAmmunition"/>, reaching
	/// the dispatch as the third argument every one of them takes. <b>Only the ammunition class reads
	/// it</b> — the other two take it and hand it to the shared prologue, which has two parameters.
	/// </param>
	internal void Fire(MechObject owner, SimWorld world, bool freeShot = false) {
		var (bone, muzzle) = PrepareShot(owner);

		// Both records share a type, so the dispatch's type tests read the same either way.
		if (ShotProjectile is not { } projectile) {
			return;
		}

		switch (Kind) {
			case WeaponMountKind.Energy:
				FireGunOrBeam(owner, world, projectile, bone, muzzle);
				break;

			case WeaponMountKind.Elf:
				FireElf(owner, world, projectile, bone, muzzle);
				break;

			case WeaponMountKind.Ammunition:
				FireAmmunition(owner, world, projectile, bone, muzzle, freeShot);
				break;
		}
	}

	/// <summary>
	/// <c>ElfMount_FireDispatch</c> (<c>0040ec64</c>), the ELF class's vtable <c>+0x28</c>. Where the energy class branches three
	/// ways on the record's type, this has one branch and it is the beam: an ELF is always a beam.
	///
	/// <para>Two things differ from the energy class's beam branch, both deliberate in the original.
	/// The cost is subtracted <b>unconditionally</b> rather than capped at what the capacitor holds,
	/// so an ELF that fires its last partial shot goes slightly negative and
	/// <see cref="ElfCanFire"/>'s second clause then fails, ending the burst. And the shot's power is
	/// a <b>fixed 1200</b> — <see cref="EnergyChargeScale"/>, the literal the dispatch pushes — not
	/// the charge spent, so every shot in a burst hits as hard as the first however far the capacitor
	/// has drained. That is what makes the ELF the damage outlier the manual describes: its
	/// <c>PROJ.DAT</c> figures are small, but nothing ever scales them down.</para>
	/// </summary>
	private void FireElf(MechObject owner, SimWorld world, ProjectileData.Projectile projectile,
			in Transform3 bone, Vec3i muzzle) {
		Charge -= ShotCost;

		var shot = bone;
		shot.X = muzzle.X;
		shot.Y = muzzle.Y;
		shot.Z = muzzle.Z;
		world.FireBeam(new WeaponShot(shot, Range, projectile, EnergyChargeScale, owner));
	}

	/// <summary>
	/// <c>WeaponMount_FireDispatch_GunBeam</c> (<c>0040ea58</c>) past the prologue — the energy
	/// class's three branches, which are three kinds of weapon.
	///
	/// <list type="bullet">
	/// <item><b>A beam</b> spends <c>min(cost, charge)</c> and resolves its hit here and now.</item>
	/// <item><b>A charge-up gun</b> — the branch taken when the capacitor holds less than the cost,
	/// which for every retail energy gun is <i>always</i>, since they all read a 10000 cost against a
	/// capacitor scaled to 1200. It fires travelling shots worth the whole charge, then either arms a
	/// burst follow-up or empties the capacitor.</item>
	/// <item><b>A fixed-cost gun</b> subtracts the cost and fires one unpowered shot. <b>Nothing in
	/// retail reaches it</b>, for the reason above; it is here because it is the branch that exists,
	/// and because it is what a hand-edited template with a real cost would take.</item>
	/// </list>
	///
	/// <para>Two multi-shot rules sit on the charge-up branch, and both are keyed off template fields
	/// that identify exactly one weapon each. <c>+0x3c == 3</c> is the big EMP cannon (catalog id 19,
	/// which the simulator also calls <c>EMP</c>): it fires <b>three</b> shots, from barrels at
	/// <c>-x</c>, <c>0</c> and <c>+x</c> of the template's own muzzle offset. <c>+0x3e == 0x13</c> is
	/// <c>EMP2</c> (id 23) — that field is <see cref="Weapons.WeaponMountTemplate.ProjDatIndex"/>, and
	/// 0x13 is <c>EMP2</c>'s own <c>PROJ.DAT</c> row, so the test is a weapon check spelled as a data
	/// comparison. It arms <see cref="Bursting"/>, which fires the mount a second time a quarter of a
	/// refire delay later and <i>then</i> empties the capacitor: two volleys per trigger pull.</para>
	/// </summary>
	private void FireGunOrBeam(MechObject owner, SimWorld world, ProjectileData.Projectile projectile,
			in Transform3 bone, Vec3i muzzle) {
		// The dispatch raises the flash before it looks at the projectile type at all, so a beam
		// lights the barrel exactly as a gun does.
		StartMuzzleFlash();

		if (projectile.Type == ProjectileType.Beam) {
			// The cost is capped at what the capacitor actually holds, so a mount that somehow fires
			// under-charged fires a weaker shot rather than going negative. For a laser the two are the
			// same number every time; for a charge-up weapon the cost is larger than the capacitor can
			// ever hold, which is what makes the shot worth the whole of it.
			short beamPower = Math.Min(ShotCost, (short)Charge);
			Charge -= beamPower;

			var shot = bone;
			shot.X = muzzle.X;
			shot.Y = muzzle.Y;
			shot.Z = muzzle.Z;
			world.FireBeam(new WeaponShot(shot, Range, projectile, beamPower, owner));
			return;
		}

		var aim = bone.ToEuler();
		short travelSpeed = owner.TravelSpeed;

		if (Charge >= ShotCost) {
			Charge -= ShotCost;
			world.FireBullet(projectile, muzzle, aim, travelSpeed, 0, owner);
			return;
		}

		short power = (short)Charge;
		world.FireBullet(projectile, muzzle, aim, travelSpeed, power, owner);

		if (Barrels == MultiBarrelCode) {
			world.FireBullet(projectile, BarrelMuzzle(bone, 0), aim, travelSpeed, power, owner);
			world.FireBullet(projectile, BarrelMuzzle(bone, -TemplateMuzzleX), aim, travelSpeed, power, owner);
		}

		if (_template?.ProjDatIndex == BurstProjectileIndex && !Bursting) {
			// A quarter of the ordinary delay, and the capacitor is deliberately left holding its
			// charge — the follow-up volley is worth the same as the first.
			_refireTimer = (short)(RefireDelay >> 2);
			Bursting = true;
			return;
		}

		Bursting = false;
		Charge = 0;
	}

	/// <summary>
	/// <c>WeaponMount_FireDispatch_Missile</c> (<c>0040e964</c>) past the prologue — the ammunition
	/// class, which is a magazine and two projectile branches.
	///
	/// <para><b>The round is now spent</b>, which it was not while nothing left the barrel: the
	/// magazine drops by the template's <c>+0x38</c> — the same field that is a shot's energy cost on
	/// the other class, and 5 on every autocannon against magazines of 500 to 2000 — and a magazine
	/// that reaches zero clears <see cref="Selectable"/>, dropping the weapon out of the selection
	/// cycle rather than leaving it armed and dry.</para>
	///
	/// <para><b>A launcher now pays for its round too.</b> The spend used to be skipped on the
	/// <see cref="ProjectileType.Rocket"/> branch, because the branch fired nothing and a faithful
	/// spend would have emptied a rack for free. The original does it before it looks at the type at
	/// all, and it does it here now.</para>
	///
	/// <para>The two branches are a gun and a launcher — <c>Bullet_Fire</c> for anything that is not
	/// a <see cref="ProjectileType.Rocket"/>, <c>Rocket_Fire</c> for one that is. Only the gun branch
	/// raises the muzzle-flash flag at <c>mount+0x44</c>; a rocket comes off a rail rather than out of
	/// a barrel and the original lights nothing for it.</para>
	///
	/// <para>The original also passes the dispatch a "this shot is free" flag off a pair of debug
	/// globals, which is the one thing that can skip the spend. Nothing in the engine sets it.</para>
	/// </summary>
	private void FireAmmunition(MechObject owner, SimWorld world, ProjectileData.Projectile projectile,
			in Transform3 bone, Vec3i muzzle, bool freeShot) {
		// The whole of what the free-shot flag buys: the round count is left alone, so the mount also
		// never empties itself out of the selection chain below.
		if (!freeShot) {
			ChargeTarget -= ShotCost;
			if (ChargeTarget < 1) {
				ChargeTarget = 0;
				Selectable = false;
			}
		}

		var aim = bone.ToEuler();
		if (projectile.Type == ProjectileType.Rocket) {
			world.FireRocket(projectile, muzzle, aim, owner.TravelSpeed, owner);
			return;
		}

		world.FireBullet(projectile, muzzle, aim, owner.TravelSpeed, 0, owner);
		StartMuzzleFlash();
	}

	/// <summary>
	/// <c>+0x4d</c>. Set by the charge-up branch on the one weapon whose template asks for a burst,
	/// and read by <c>WeaponMount_AutoFireDue</c> (<c>0040ede8</c>) — see
	/// <see cref="AutoFireDue"/>.
	/// </summary>
	public bool Bursting { get; private set; }

	/// <summary>
	/// <c>WeaponMount_AutoFireDue</c> (<c>0040ede8</c>): a mount whose refire delay has run out with
	/// <see cref="Bursting"/> still set is due to fire itself again, without the trigger. The
	/// arbitration pass is what asks — see <see cref="WeaponMounts.ChargeTick"/>.
	/// </summary>
	public bool AutoFireDue => _refireTimer == 0 && Bursting;

	/// <summary>The value the template's <c>+0x3c</c> takes on the one multi-barrel weapon.</summary>
	public const short MultiBarrelCode = 3;

	/// <summary>
	/// The <c>PROJ.DAT</c> row the burst test compares the template's <c>ProjDatIndex</c> against —
	/// <c>EMP2</c>'s.
	/// </summary>
	public const short BurstProjectileIndex = 0x13;

	/// <summary>The template's <c>+0x3c</c>, which is <see cref="MultiBarrelCode"/> or 1.</summary>
	public short Barrels =>
		_template?.Tail is { Length: >= 0x1c } tail ? BitConverter.ToInt16(tail, 0x1a) : (short)1;

	/// <summary>
	/// The template's <c>+0x2c</c> — the minimum range this weapon will engage at, the lower half of
	/// <see cref="RangeAllows"/>'s window. <b>Zero in all 33 retail templates</b>, so it exists in the
	/// format and never bites; see docs/formats/weapons-dat-sim.md.
	/// </summary>
	public int MinimumRange =>
		_template?.Tail is { Length: >= 0x0e } tail ? BitConverter.ToInt32(tail, 0x0a) : 0;

	/// <summary>
	/// The template's <c>+0x34</c> — what firing this weapon costs the AI in
	/// <see cref="MechObject.ChooseWeapon"/>'s score, weighed at ten times the damage credit's gain.
	/// The retail values and what they do to the choice are in docs/simulation/ai-weapons.md
	/// ("Choosing a weapon").
	/// </summary>
	public short AiShotCost =>
		_template?.Tail is { Length: >= 0x14 } tail ? BitConverter.ToInt16(tail, 0x12) : (short)0;

	/// <summary>
	/// <c>WeaponMount_RangeAllows</c> (<c>0040e5f8</c>) — whether a target at <paramref name="range"/> is inside this weapon's
	/// engagement window, <see cref="MinimumRange"/> exclusive to <see cref="Range"/> exclusive. Both
	/// the AI's weapon choice and its ELF latch ask it, as does the cockpit's readiness predicate;
	/// see docs/simulation/weapon-mounts.md ("Readiness").
	/// </summary>
	public bool RangeAllows(int range) => MinimumRange < range && range < Range;

	/// <summary>
	/// <c>Mech_ConvergeGunsOnRange</c> (<c>0041a74c</c>) — the toe-in that makes this hardpoint's
	/// shots cross the sight line at <paramref name="range"/>. Run from
	/// <see cref="MechObject.TorsoPitchTick"/> for every machine, so the player's guns and the AI's
	/// converge the same way; a range of zero squares them up again.
	/// </summary>
	internal void ConvergeOnRange(MechObject owner, int range) {
		if (range == 0) {
			_convergePitch = 0;
			_convergeYaw = 0;
			return;
		}

		var muzzle = MuzzleOffset;
		var (pitch, _, yaw) = SimTrig.EulerToward(
			new Vec3i(0, range, 0),
			new Vec3i(muzzle.X, muzzle.Y, muzzle.Z - owner.Type.EyeOffsetZ));

		_convergePitch = pitch;
		_convergeYaw = yaw;
	}

	/// <summary>
	/// The convergence rotation <see cref="PrepareShot"/> composes under the firing bone, or the
	/// identity when the hardpoint's own two gates are shut. <c>WeaponMount_CtorBase</c> resolves each
	/// gate from a <c>.GL</c> node id and writes zero when that id is negative — which every retail
	/// hardpoint's is, so both halves apply throughout the fleet.
	/// </summary>
	private Transform3 ConvergenceRotation => Transform3.FromEuler(
		ConvergencePitchLocked ? (short)0 : _convergePitch,
		0,
		ConvergenceYawLocked ? (short)0 : _convergeYaw);

	/// <summary>
	/// <c>mount+0x5b</c>, from the hardpoint's <c>.GL +0x02</c>: a model part this hardpoint's pitch is
	/// pinned to instead of converging. <c>WeaponMount_CtorBase</c> writes zero for a negative id, and
	/// every retail hardpoint's is <c>-1</c>, so nothing is ever locked.
	/// </summary>
	private bool ConvergencePitchLocked => _hardpoint.ConvergencePitchNode >= 0;

	/// <summary><c>mount+0x5f</c>, the yaw half, from <c>.GL +0x04</c>.</summary>
	private bool ConvergenceYawLocked => _hardpoint.ConvergenceYawNode >= 0;

	/// <summary>
	/// The template's <c>+0x4e</c> — what this weapon is worth to the AI's combat rating, scaled by
	/// the mount's condition. Read by <c>Mech_ComputeCombatRating</c> (<c>0041edd8</c>) through the
	/// mount's own <c>+0x1c</c>, which is this template; see
	/// <see cref="MechObject.CombatRating"/> and docs/simulation/ai-targeting.md.
	/// </summary>
	public short AiRatingValue =>
		_template?.Tail is { Length: >= 0x2e } tail ? BitConverter.ToInt16(tail, 0x2c) : (short)0;

	/// <summary>The lateral half of the template's own muzzle triple, <c>+0x40</c> — the barrel spacing.</summary>
	private short TemplateMuzzleX =>
		_template?.Tail is { Length: >= 0x20 } tail ? BitConverter.ToInt16(tail, 0x1e) : (short)0;

	/// <summary>
	/// Where one barrel of a multi-barrel weapon sits, in world space: the same three-part offset
	/// <see cref="MuzzleOffset"/> builds, with the template's own lateral figure replaced.
	/// </summary>
	private Vec3i BarrelMuzzle(in Transform3 bone, int lateral) {
		var offset = MuzzleOffset;
		return bone.TransformPoint(offset.X - TemplateMuzzleX + lateral, offset.Y, offset.Z);
	}

	/// <summary>
	/// <c>WeaponMount_PrepareShot</c> (<c>0040e788</c>), the shared fire prologue — where the shot comes from, which way it points,
	/// and the refire delay it costs.
	///
	/// <para>The frame is the firing hardpoint's own model bone, posed as it stands this tick and
	/// composed with the machine's world transform, so <b>a beam follows the torso because the gun
	/// bone does</b>: nothing here adds the twist or the pitch angle, and nothing needs to. The
	/// gun convergence goes on under it — see <see cref="ConvergenceRotation"/>.</para>
	///
	/// <para>The muzzle point itself is three offsets summed in the bone's own space: the weapon
	/// template's, the hardpoint's, and a side offset the template holds separately and the hardpoint
	/// picks the sign of — see <see cref="MuzzleOffset"/>.</para>
	/// </summary>
	/// <returns>
	/// The bone's own world frame (<c>DAT_004a98b8</c>) and the muzzle's world position
	/// (<c>DAT_004a98d8</c>), which the original leaves as two separate globals because the two
	/// branches want them differently: a beam overwrites the frame's translation with the muzzle and
	/// rays down it, while a travelling shot takes the muzzle as a start point and the frame only for
	/// its euler triple and for placing any further barrels.
	/// </returns>
	private (Transform3 Bone, Vec3i Muzzle) PrepareShot(MechObject owner) {
		// The convergence goes on innermost, under the bone's own pose: the original composes it with
		// the node transform and only then with the machine's, and composition is associative.
		var bone = Transform3.Concat(ConvergenceRotation, owner.PartTransform(_hardpoint.BoneId));
		var offset = MuzzleOffset;

		_refireTimer = RefireDelay;

		// The prologue's last two writes, mount +0x33 and +0x3b — see FiringSustained.
		_firedSinceShuffle = true;
		_firedThisTick = true;

		return (bone, bone.TransformPoint(offset.X, offset.Y, offset.Z));
	}

	/// <summary>
	/// Where the muzzle sits in its bone's space — the template's own triple at <c>0x40</c>, the
	/// hardpoint's at <c>+0x10</c>, and <c>WeaponMountTemplate_SideMuzzleOffset</c> (<c>0040f904</c>)'s side offset on top.
	///
	/// <para>That last one is what makes a mirrored pair of hardpoints fire from mirrored points off
	/// one template. The template carries a lateral figure at <c>0x46</c> and a vertical one at
	/// <c>0x4a</c>, and the hardpoint's own mounting code — the <c>.GL</c> byte at <c>+6</c>, which
	/// reads on top / underneath / left / right / invisible — selects one of them and its sign. Only
	/// one axis is ever used: a top or bottom mount takes the vertical figure and no lateral one, a
	/// side mount takes the lateral figure and no vertical one, and an invisible mount takes
	/// neither.</para>
	/// </summary>
	private Vec3i MuzzleOffset {
		get {
			var mount = MountPointOffset;
			if (_template?.Tail is not { Length: >= 0x24 } tail) {
				return mount;
			}

			return new Vec3i(
				BitConverter.ToInt16(tail, 0x1e) + mount.X,
				BitConverter.ToInt16(tail, 0x20) + mount.Y,
				BitConverter.ToInt16(tail, 0x22) + mount.Z);
		}
	}

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
	private Vec3i MountPointOffset {
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
