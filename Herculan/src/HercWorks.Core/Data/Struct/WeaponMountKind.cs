namespace HercWorks.Core.Data.Struct;

/// <summary>
/// Which of <c>MechLoadout_ConstructWeaponMounts</c>'s mount classes a weapon id builds — see
/// <see cref="WeaponLUT.MountKind"/> and docs/retail/simulation/weapon-mounts.md#mount-classes.
/// </summary>
public enum WeaponMountKind {
	/// <summary>No mount object at all — an empty slot, or a catalog id the factory has no case for.</summary>
	None,

	/// <summary>A magazine of rounds. Draws nothing from the pool and prints a round count.</summary>
	Ammunition,

	/// <summary>A capacitor charged off the Master Energy Pool. Prints a charge bar.</summary>
	Energy,

	/// <summary>
	/// <c>ELF</c> and <c>ELF2</c> — an energy mount with the vtable at <c>004992c0</c> swapped in
	/// over the energy class's. It carries and charges the same capacitor, and prints the same bar,
	/// but it will not <b>start</b> firing below a full one and its shot is worth a fixed 1200
	/// however much is left (docs/retail/simulation/weapon-mounts.md#elf-and-elf2).
	/// </summary>
	Elf,

	/// <summary>An equipment pod. Fires nothing, prints nothing, and is in no fire group.</summary>
	Pod,
}
