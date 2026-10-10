using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The mount's condition — the vtable <c>0x68</c> notification (<c>WeaponMount_ConditionChangedBase</c>,
/// <c>0040ee0c</c>, and its overrides) that every component write on the machine reaches, and
/// <c>WeaponMount_Destroy</c> (<c>0040f57c</c>), which knocks the mount out.
/// </summary>
public sealed partial class WeaponMount {
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

		world.Effects.SpawnDebrisPiece(DebrisShapeLibraryName, thrownShape,
			world.Effects.DebrisShapeRadius(DebrisShapeLibraryName, thrownShape),
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
}
