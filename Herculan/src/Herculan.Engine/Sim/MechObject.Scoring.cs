using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// What a machine's fighting is worth to the mission's results: the kill credit
/// (<c>Mech_CreditNeutralisedTarget</c>, <c>00415710</c>) and its per-class tally, and the salvage a
/// wreck and its lost guns yield (<c>Mech_SalvageValue</c>, <c>00418e60</c>).
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// <c>Mech_CreditNeutralisedTarget</c> (<c>00415710</c>), the mech's vtable <c>+0x60</c> — told to the machine that just put
	/// <paramref name="victim"/> out of the fight, from both of
	/// <see cref="ComponentDamageWrite"/>'s branches, from <see cref="BaseObject.ApplyDamage"/> and from
	/// <see cref="FlyerObject.ApplyComponentDamage"/>. The base class' slot
	/// (<c>SimObject_CreditNeutralisedTargetNoOp</c> (<c>00411b2c</c>)) is an empty stub, so only a HERC credits anything.
	///
	/// <para><paramref name="wasImmobilised"/> is the victim's reading from <i>before</i> this
	/// change, and it is what stops a machine being counted twice: a HERC whose legs went first was
	/// already credited then, so finishing it off scores nothing more. Only a first, cross-team
	/// neutralisation adds to the tally — <see cref="KillsOf"/>, one counter per target class.</para>
	///
	/// <para>Two callouts go out from here, and only the first is under
	/// <paramref name="wasImmobilised"/>: a squadmate that scored says so, and a squadmate that has
	/// just been stopped cries out whether or not this was the blow that counted. The second is the
	/// one place in the original that forces a post past a destroyed machine's own silence — see
	/// <see cref="PostSquadMessage"/>.</para>
	///
	/// <para>A squadmate the player itself stopped also adds one to mission counter 10
	/// (<c>DAT_004a9f08</c>), again whether or not this was the blow that counted.</para>
	///
	/// <para>A victim of the player's group is always a HERC: <c>DBSim_BuildGroupRecord</c>
	/// (<c>00423b34</c>) builds every member of a group from the one class its discriminator names, so
	/// the squadmate half never meets a structure or a flyer.</para>
	/// </summary>
	internal void CreditNeutralised(SimWorld world, SimObject victim, bool wasImmobilised) {
		MechObject? player = world.PlayerMech;

		if (!wasImmobilised && Group != null && victim.Group != null
				&& Group.Side != victim.Group.Side) {
			if (player != null && !ReferenceEquals(player, this)
					&& ReferenceEquals(player.Group, Group)) {
				PostSquadMessage(world, SquadMessageScoredAKill);
			}

			if ((uint)victim.TargetClass < (uint)_killsByClass.Length) {
				_killsByClass[(int)victim.TargetClass]++;
			}

			ScoredAKill = true;
		}

		if (player != null && victim is MechObject squadmate && !ReferenceEquals(player, squadmate)
				&& ReferenceEquals(player.Group, squadmate.Group)) {
			squadmate.PostSquadMessage(
				world,
				squadmate.Destroyed ? SquadMessageDestroyed : SquadMessageWentDown,
				force: true);

			if (ReferenceEquals(player, this)) {
				world.BumpMissionCounter(World.MissionLoader.SquadmatesDownedCounter, 1);
			}
		}
	}

	/// <summary>
	/// <c>mech+0x2a4</c> — how many of <paramref name="targetClass"/> this machine has put out of the
	/// fight this mission, counted once per victim: <c>INC word [mech + class*2 + 0x2a4]</c> at
	/// <c>0041576d</c>, the class being the victim's <c>+0x1a8</c>. The first three are the Herc, Base and
	/// Flyer kills the mission's results carry to the pilot (<see cref="MissionResults"/>).
	///
	/// <para>The original indexes the array unchecked. The four classes an object can hold are all it is
	/// kept for here; how far the original's array runs past the three the results read is not
	/// established.</para>
	/// </summary>
	public short KillsOf(TargetClass targetClass) =>
		(uint)targetClass < (uint)_killsByClass.Length ? _killsByClass[(int)targetClass] : (short)0;

	private readonly short[] _killsByClass = new short[(int)TargetClass.GroundVehicle + 1];

	/// <summary>
	/// <c>mech+0xb3</c> — the mission placed this machine already broken, so its wreck is worth nothing.
	/// <see cref="ApplyStartingCondition"/> raises it on its two worst grades, so a mission cannot be farmed
	/// by authoring derelicts into it.
	/// </summary>
	public bool WorthNoSalvage { get; private set; }

	/// <summary>
	/// <c>Mech_SalvageValue</c> (<c>00418e60</c>) — what this machine's wreck is worth to the player's side, in
	/// kilograms before the results' own scale (<see cref="MissionResults"/>). Zero when
	/// <see cref="WorthNoSalvage"/> is set. Otherwise every mount, in hardpoint order, whose component reads
	/// under half damaged goes onto the salvage list with its condition; and the chassis is worth
	/// <see cref="MechTypeRecord.SalvageScale"/> of its <see cref="ComponentDamage.WeightedArmorRemaining"/>, the
	/// scale halved when component 0 is at full damage. See
	/// docs/retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60.
	/// </summary>
	internal int SalvageValue(SimWorld world) {
		if (WorthNoSalvage || _damage == null) {
			return 0;
		}

		foreach (var mount in Weapons.Slots) {
			if (mount == null) {
				continue;
			}

			int reading = _damage.DamagePercent(WeaponMounts.FirstMountComponent + mount.LoadoutSlot);
			if ((short)reading < SalvageableMountReading) {
				world.QueueSalvage((short)mount.WeaponId, SalvageCondition(reading));
			}
		}

		int scale = Type.SalvageScale;
		if ((short)_damage.DamagePercent(0) == FullyDamaged) {
			scale >>= 1;
		}

		return SimMath.Q10Multiply(_damage.WeightedArmorRemaining(), scale);
	}

	/// <summary>The reading a mount's component must be under for the mount to be salvaged — the literal <c>0x80</c>.</summary>
	private const short SalvageableMountReading = 0x80;

	/// <summary><c>mech+0xa6</c> — raised by the first kill this machine scores. Latched.</summary>
	public bool ScoredAKill { get; private set; }

	/// <summary>
	/// The Q10 share of a mount's reading its salvage condition is taken from — the literal 500 at
	/// <c>00418a9b</c>, just under a half.
	/// </summary>
	private const int MountSalvageScale = 500;

	/// <summary>
	/// A Q8 damage reading as the percentage condition the salvage list and the results' status blocks
	/// carry: <c>(0x100 - reading) * 100</c> shifted right 8, arithmetically (<c>SAR</c> at <c>00418ace</c>
	/// and <c>00423dbc</c>, where the decompiler shows an unsigned shift).
	/// </summary>
	internal static short SalvageCondition(int reading) => (short)((short)(0x100 - reading) * 100 >> 8);
}
