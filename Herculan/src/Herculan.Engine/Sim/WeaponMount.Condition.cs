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
	/// <c>WeaponMount_Destroy</c> (<c>0040f57c</c>) — knocks the mount out. It drops the mount's
	/// model, so the gun is no longer drawn on the chassis, and sets <see cref="Disabled"/>, which
	/// stops the mount charging, firing and being armed and shows its cockpit row as "OFFLINE". A
	/// second call does nothing. Reached from <see cref="ConditionChanged"/> and from the destruction
	/// roll, <c>MechObject.RollWeaponMountDestruction</c>; see
	/// docs/retail/simulation/weapon-mounts.md#losing-a-mount.
	///
	/// <para><b>A visibly-mounted gun is thrown off as wreckage.</b> When the hardpoint's mounting
	/// code is under <see cref="InvisibleMounting"/>, the gun's own shape, out of
	/// <see cref="DebrisShapeLibraryName"/>, leaves the mount point as a <see cref="DebrisObject"/>
	/// heading away from the machine's aim point, at the hardpoint's own pitch and
	/// <see cref="DebrisMass"/>, keeping the mount's attitude. <paramref name="rolled"/> picks the
	/// kind: the certain path throws a piece that bursts, group
	/// <see cref="ComponentDamage.DefaultDebrisGroup"/> with <see cref="DebrisBurstEffect"/>, and the
	/// roll a plain piece that just falls. See
	/// docs/retail/simulation/destruction-effects.md#spawn-sites.</para>
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
	/// What a hit on the machine does to this mount: a mount whose component is fully damaged is
	/// knocked out, and past half damage a launcher may cook off, a gun's refire delay changes and an
	/// energy weapon recharges more slowly.
	///
	/// <para>The mount's vtable slot <c>0x68</c>, the condition notification, which
	/// <c>Mech_ComponentDamageWrite</c> (<c>00417de4</c>) calls on every mount after every component
	/// write with the mount's component reading before and after. The original's four overrides are
	/// one method here, branching on the mount's class: <c>WeaponMount_ConditionChangedBase</c>
	/// (<c>0040ee0c</c>) for the base class and the pods, <c>WeaponMount_ConditionChanged</c>
	/// (<c>0040ee90</c>) for the ammunition class, <c>WeaponMount_ConditionChangedEnergy</c>
	/// (<c>0040ee38</c>) for the energy and ELF classes, and <c>TargetingPod_ConditionChanged</c>
	/// (<c>0040ef6c</c>) for the Targeting Pod. See
	/// docs/retail/simulation/weapon-mounts.md#the-certain-path--the-condition-notification.</para>
	///
	/// <list type="number">
	/// <item><b>Every class:</b> a reading of <see cref="MechObject.FullyDamaged"/> calls
	/// <see cref="Destroy"/>, with no roll.</item>
	/// <item><b>The Targeting Pod</b> caches the reading in
	/// <see cref="TargetingPodLock.ComponentDamage"/>.</item>
	/// <item><b>An energy mount</b> resets <see cref="ChargeRate"/> to <see cref="EnergyChargeRate"/>
	/// and, past <see cref="MountDamageOnset"/>, lowers it by <c>Q10(20, steps * 100)</c>.</item>
	/// <item><b>A loaded ammunition mount</b> (<see cref="ChargeTarget"/> nonzero), past
	/// <see cref="MountDamageOnset"/>: a <c>Rocket</c> mount rolls <see cref="MountCookOffOdds"/> in
	/// 1024 for each <see cref="MountDamageStep"/> the reading crossed and is destroyed on the first
	/// success; a <c>Bullet</c> mount sets <see cref="RefireScale"/>; a <c>Beam</c> mount does
	/// neither.</item>
	/// </list>
	/// </summary>
	/// <param name="random">The simulation's generator, which the cook-off rolls draw from.</param>
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
