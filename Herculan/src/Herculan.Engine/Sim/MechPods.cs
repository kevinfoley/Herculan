namespace Herculan.Engine.Sim;

/// <summary>
/// The five non-firing "pods" a HERC can carry, resolved out of its hardpoint fit.
///
/// <para>A pod is an ordinary weapon mount on an ordinary hardpoint. What makes it a pod is
/// <c>MechLoadout_FileEquipmentPods</c> (<c>0040fb2c</c>), the last pass of
/// <c>Mech_ConfigureLoadout</c> (<c>004175dc</c>), which files five weapon ids from the finished
/// mount list into a five-pointer array at <c>mech+0x307</c> — see <see cref="FromLoadout"/> and
/// docs/retail/simulation/equipment-pods.md. The slots here are therefore the mounts, which a
/// consumer reads the pod's own condition back off, and the <c>bool</c>s are derived.</para>
/// </summary>
/// <param name="EcmMount">Slot 0, <c>mech+0x307</c> — the ECM pod, <see cref="EcmWeaponId"/>.</param>
/// <param name="TargetingMount">
/// Slot 1, <c>mech+0x30b</c> — the Targeting Pod, <see cref="TargetingWeaponId"/>, which lets [Tab]
/// single out one component of the selected HERC; see <see cref="TargetingPodLock"/>.
/// </param>
/// <param name="ShieldPodMount">
/// Slot 2, <c>mech+0x30f</c> — the Shield Pod, <see cref="ShieldPodWeaponId"/>, whose one reader
/// found is <c>Mech_ComputeShieldCapacity</c> (<c>00417bec</c>): it adds the pod's bonus to shield
/// capacity. Otherwise the pod is inert. See <see cref="ShieldCharge"/>.
/// </param>
/// <param name="EnergyPodMount">
/// Slot 3, <c>mech+0x313</c> — the Energy Pod, <see cref="EnergyPodWeaponId"/>, whose one reader
/// found is <c>Mech_ComputeReactorRate</c> (<c>00417d08</c>): it adds the pod's bonus to reactor
/// output. Otherwise the pod is inert. See <see cref="MechObject.ReactorOutputRate"/>.
/// </param>
/// <param name="TurboPodMount">
/// Slot 4, <c>mech+0x317</c> — the Turbo Pod, <see cref="TurboPodWeaponId"/>, read by the
/// locomotion tick. It is the one pod that draws on the Master Energy Pool, through its own
/// <c>+0x34</c> override.
/// </param>
public readonly record struct MechPods(
	WeaponMount? EcmMount,
	WeaponMount? TargetingMount,
	WeaponMount? ShieldPodMount,
	WeaponMount? EnergyPodMount,
	WeaponMount? TurboPodMount) {

	/// <summary>Catalog id 18 — <c>ECM</c>, slot 0.</summary>
	public const int EcmWeaponId = 0x12;

	/// <summary>Catalog id 29 — <c>TARG</c>, slot 1.</summary>
	public const int TargetingWeaponId = 0x1d;

	/// <summary>Catalog id 30 — <c>SHLD</c>, slot 2.</summary>
	public const int ShieldPodWeaponId = 0x1e;

	/// <summary>Catalog id 31 — <c>TURB</c>, slot 4.</summary>
	public const int TurboPodWeaponId = 0x1f;

	/// <summary>Catalog id 32 — <c>ENRG</c>, slot 3.</summary>
	public const int EnergyPodWeaponId = 0x20;

	/// <summary>A machine carrying no pods at all.</summary>
	public static MechPods None => default;

	/// <summary>Whether slot 0 is filled — the original's <c>mech+0x307 != 0</c>.</summary>
	public bool Ecm => EcmMount != null;

	/// <inheritdoc cref="Ecm"/>
	public bool Targeting => TargetingMount != null;

	/// <inheritdoc cref="Ecm"/>
	public bool ShieldPod => ShieldPodMount != null;

	/// <inheritdoc cref="Ecm"/>
	public bool EnergyPod => EnergyPodMount != null;

	/// <inheritdoc cref="Ecm"/>
	public bool TurboPod => TurboPodMount != null;

	/// <summary>
	/// One pod's own component damage, Q8 over 0-256 with 0 pristine — the figure both pod bonus
	/// curves degrade on.
	///
	/// <para>The original reads it live, at the point of use:
	/// <c>Component_ReadDamagePercent(mech+0x206, pod.GL[+0x17] + 19)</c>. That index is the mount's
	/// fit slot plus <see cref="WeaponMounts.FirstMountComponent"/> — a pod's component is its
	/// hardpoint's, like any other mount's, so shooting the hardpoint a pod sits on is what degrades
	/// it. Answers 0 for an absent pod or a machine with no component model, which is the reading a
	/// pristine pod gives and so leaves the bonus at full.</para>
	///
	/// <para><b>Live, not cached.</b> The Shield and Energy pods read this; the Targeting Pod's
	/// thresholds test its own cached reading, <see cref="TargetingPodLock.ComponentDamage"/>,
	/// instead.</para>
	/// </summary>
	public static short DamageOf(WeaponMount? pod, ComponentDamage? damage) =>
		pod == null || damage == null
			? (short)0
			: (short)damage.DamagePercent(pod.LoadoutSlot + WeaponMounts.FirstMountComponent);

	/// <summary>
	/// <c>MechLoadout_FileEquipmentPods</c>'s pass over the finished mount list. It runs against the
	/// mounts, not the raw fit: a weapon id sitting in a fit slot no hardpoint addresses builds no
	/// mount and so fits no pod. A second copy of a pod takes the slot from the first, so the last
	/// mount in hardpoint order wins.
	/// </summary>
	public static MechPods FromLoadout(WeaponMounts mounts) {
		WeaponMount? ecm = null, targeting = null, shieldPod = null, energyPod = null, turboPod = null;

		foreach (var mount in mounts.Mounts) {
			switch (mount.WeaponId) {
				case EcmWeaponId: ecm = mount; break;
				case TargetingWeaponId: targeting = mount; break;
				case ShieldPodWeaponId: shieldPod = mount; break;
				case TurboPodWeaponId: turboPod = mount; break;
				case EnergyPodWeaponId: energyPod = mount; break;
			}
		}

		return new MechPods(ecm, targeting, shieldPod, energyPod, turboPod);
	}
}
