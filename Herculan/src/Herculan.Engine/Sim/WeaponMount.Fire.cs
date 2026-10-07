using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// Firing — the readiness test (vtable <c>0x2c</c>), the trigger read and the ELF spin-up (<c>0x30</c>),
/// the fire dispatches (<c>0x28</c>) and their shared prologue <c>WeaponMount_PrepareShot</c>
/// (<c>0040e788</c>), the refire delay, the muzzle flash and the gun convergence.
/// </summary>
public sealed partial class WeaponMount {
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
	/// <c>+0x31</c>, the refire countdown. Zero means the mount is out of its delay. A shot arms it
	/// with <see cref="RefireDelay"/> and the mount's own turn at the pool counts it down by the
	/// timestep, so it is the same clock everything else in the simulation runs on.
	/// </summary>
	public short RefireTimer => _refireTimer;

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
	/// (docs/retail/formats/proj-dat.md#open).</para>
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
		if (ShotProjectile(world) is not { } projectile) {
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
	/// <c>Mech_ConvergeGunsOnRange</c> (<c>0041a74c</c>) — the toe-in that makes this hardpoint's
	/// shots cross the sight line at <paramref name="range"/>, for every mount through
	/// <see cref="WeaponMounts.ConvergeOnRange"/>; a range of zero squares them up again.
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
		// the node transform (Transform_ConcatRotation, 0047f3e8: the rotations, with the node's
		// translation) and only then with the machine's, and composition is associative.
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
}
