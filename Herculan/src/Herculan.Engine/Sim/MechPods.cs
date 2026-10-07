namespace Herculan.Engine.Sim;

/// <summary>
/// The five non-firing "pods" a HERC can carry, resolved out of its hardpoint fit.
///
/// <para>DBSIM does not model these as a separate equipment category: a pod is an ordinary weapon
/// mount, built from an ordinary hardpoint by the same factory every gun goes through, and it
/// occupies a hardpoint like any other. What sets them apart is one pass at the end of
/// <c>Mech_ConfigureLoadout</c> (<c>004175dc</c>): <c>MechLoadout_FileEquipmentPods</c> walks the
/// finished mount list and files five specific weapon ids into a five-pointer array at
/// <c>mech+0x307</c>, one slot per id. <b>It is a pointer array, not a set of flags</b> — a consumer
/// that wants the pod's own condition reads it back off the mount, which is why the slots here are
/// the mounts and the <c>bool</c>s are derived.</para>
///
/// <para>The slot/id table, why the ids are the shell catalog's, why slot order is not id order, and
/// the shared pod damage curve are in docs/retail/simulation/equipment-pods.md. The
/// constants below are the ids that document names; the last mount in hardpoint order wins a slot,
/// which is reproduced here.</para>
/// </summary>
/// <param name="EcmMount">Slot 0, <c>mech+0x307</c> — the ECM pod (catalog id 18).</param>
/// <param name="TargetingMount">
/// Slot 1, <c>mech+0x30b</c> — the Targeting Pod (id 29), which lets [Tab] single out one component
/// of the selected machine; see docs/retail/simulation/target-selection.md.
/// </param>
/// <param name="ShieldPodMount">
/// Slot 2, <c>mech+0x30f</c> — the Shield Pod (id 30). Read by <c>Mech_ComputeShieldCapacity</c>,
/// which adds its bonus to shield capacity, and by nothing else: the pod class overrides no
/// behaviour, holds no state and has no button on its cockpit row, so the mount and its damage are
/// the whole of it. See <see cref="ShieldCharge"/>.
/// </param>
/// <param name="EnergyPodMount">
/// Slot 3, <c>mech+0x313</c> — the Energy Pod (id 32). Read by <c>Mech_ComputeReactorRate</c>, which
/// adds its bonus to reactor output, and like the Shield Pod inert in every other respect; see
/// <see cref="MechObject.ReactorOutputRate"/>.
/// </param>
/// <param name="TurboPodMount">
/// Slot 4, <c>mech+0x317</c> — the Turbo Pod (id 31), read by the locomotion tick. It is the one pod
/// that draws on the Master Energy Pool, through its own <c>+0x34</c> override.
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
	/// <para><b>Live, not cached.</b> The Shield and Energy pods take this route; the Targeting Pod
	/// is the one that caches its reading in <c>+0x7f</c> from its vtable <c>+0x68</c> instead, and
	/// every one of its four thresholds wants that cache rather than this — see
	/// <see cref="TargetingPodLock.ComponentDamage"/>. The two agree in practice, from a zeroed
	/// pod block at spawn and a per-mount notification on every damage write, but they are reached
	/// by different code and only one of them is what the pod's readers test. See
	/// docs/retail/simulation/equipment-pods.md.</para>
	/// </summary>
	public static short DamageOf(WeaponMount? pod, ComponentDamage? damage) =>
		pod == null || damage == null
			? (short)0
			: (short)damage.DamagePercent(pod.LoadoutSlot + WeaponMounts.FirstMountComponent);

	/// <summary>
	/// <c>MechLoadout_FileEquipmentPods</c>'s pass over the finished mount list. It runs against the
	/// mounts, not the raw fit: a weapon id sitting in a fit slot no hardpoint addresses builds no
	/// mount and so fits no pod.
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
