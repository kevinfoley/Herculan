using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// A HERC's combat rating and how two compare (docs/es1/simulation/mech-ai.md#the-combat-reassess--mech_aicombatreassess-00016e04,
/// which links ES2's derivation, docs/retail/simulation/ai-targeting.md#relative-combat-rating).
/// </summary>
public static class Es1CombatRating {
	/// <summary>The jitter mask each rating takes before a comparison, <c>AND AX,0x3e8</c> on the generator's word.</summary>
	public const int JitterMask = 1000;

	/// <summary>The difference within which two jittered ratings count as even.</summary>
	public const int EvenRange = 400;

	/// <summary>The system readings counted, and the reading above which each costs its penalty.</summary>
	public const int PenalisedSystems = 10, SystemPenaltyReading = 0xb4;

	/// <summary>The components whose condition counts, slots 0–18.</summary>
	public const int RatedComponents = 19;

	/// <summary>The readout entries of the components, the systems and the mounts (<see cref="Es1Mech.ReadDamageReadouts"/>).</summary>
	private const int FirstComponentReading = 1, FirstSystemReading = 20, FirstMountReading = 32, MountReadings = 10;

	/// <summary>
	/// <c>Mech_ComputeCombatRating</c> (<c>ES1DBSIM 000189f0</c>)(<paramref name="mech"/>): from
	/// <see cref="Es1Mech.ReadDamageReadouts"/>, the type record's <c>+0x44</c>; plus, for each mount of the manager whose
	/// reading is not negative and whose slot <c>+0x58</c> counts it (<see cref="Es1WeaponMount.CountsInCombatRating"/>),
	/// Q8(<c>0x100</c> − its reading, its template's <c>+0x4e</c>); plus Q8(<c>0x100</c> − the reading, the piece's armor)
	/// for each of the first <see cref="RatedComponents"/> components whose reading is not negative; less the type record's
	/// <c>+0x7e</c>[<i>i</i>] for each of the first <see cref="PenalisedSystems"/> systems reading over
	/// <see cref="SystemPenaltyReading"/>. Shifted right 4 into <see cref="Es1Mech.CombatRating"/>, with
	/// <see cref="Es1Mech.SkipCombatRating"/> set; the Q8s are ES1's rounding multiply.
	/// </summary>
	public static short Compute(Es1Mech mech) {
		var readings = mech.ReadDamageReadouts();
		var chassis = mech.Type.Chassis;
		int rating = chassis.AiRatingBase;
		if (mech.Mounts is { } mounts) {
			for (int i = 0; i < mounts.Mounts.Count; i++) {
				// Retail reads a mount past the buffer's ten readings off its own stack; it is passed over here.
				if (i >= MountReadings || mounts.Mounts[i] is not { } mount) {
					continue;
				}

				short reading = readings[FirstMountReading + i];
				if (reading >= 0 && mount.CountsInCombatRating) {
					rating += SimMath.Q8MultiplyRounded(0x100 - reading, mount.AiRatingValue);
				}
			}
		}

		for (int i = 0; i < RatedComponents; i++) {
			short reading = readings[FirstComponentReading + i];
			if (reading >= 0) {
				// Retail reads the table through the header unchecked; with no header the armor is 0 here.
				short armor = mech.Damage?.Piece(i)?.Armor ?? (short)0;
				rating += SimMath.Q8MultiplyRounded(0x100 - reading, armor);
			}
		}

		var penalties = chassis.AiRatingSystemPenalty;
		for (int i = 0; i < PenalisedSystems; i++) {
			if (readings[FirstSystemReading + i] > SystemPenaltyReading) {
				rating -= penalties[i];
			}
		}

		mech.CombatRating = (short)(rating >> 4);
		mech.SkipCombatRating = true;
		return mech.CombatRating;
	}

	/// <summary>
	/// <c>Mech_CompareCombatRating</c> (<c>00016968</c>), the HERC's slot <c>+0x50</c>, (<paramref name="mech"/>,
	/// <paramref name="candidate"/>): for a candidate of class 0, each rating plus <c>Math_RandomNext</c> &amp;
	/// <see cref="JitterMask"/>, the machine's drawn first; 1 when the two differ by under <see cref="EvenRange"/>, 2 when
	/// the candidate's is the higher, otherwise 0; 0 for any other class.
	/// </summary>
	public static int Compare(Es1Mech mech, IEs1SimObject candidate, Es1SimGlobals globals) {
		if (candidate.TargetClass != 0) {
			return 0;
		}

		short mine = (short)((globals.Random.Next() & JitterMask) + mech.CombatRating);
		// Every class-0 object is a HERC; one that is not, which retail reads as one, rates 0 here.
		short theirs = (short)((globals.Random.Next() & JitterMask) + (candidate is Es1Mech other ? other.CombatRating : 0));
		short difference = (short)(mine - theirs);
		if (Es1CombatStates.Magnitude(difference) < EvenRange) {
			return 1;
		}

		return mine < theirs ? 2 : 0;
	}

	/// <summary>
	/// <c>Ai_SumAttackerRatings</c> (<c>00016a14</c>)(<paramref name="mech"/>): the sum of the ratings of every machine of
	/// <c>g_MechPool</c>, walked oldest first with <c>Pool_Prev</c>, whose target is <paramref name="mech"/>.
	/// </summary>
	public static int SumAttackerRatings(Es1Mech mech, Es1SimGlobals globals) {
		int sum = 0;
		foreach (var other in globals.MechPool) {
			if (ReferenceEquals(other.Target, mech)) {
				sum += other.CombatRating;
			}
		}

		return sum;
	}
}
