using HercWorks.Core.Data.File.Dat.Sim;

namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Mech_PlaceLegsOnGround</c> (<c>ES1DBSIM 000144dc</c>): the shadows placed under their parts, and for the player's
/// machine the footfall tests on the latches <c>+0x24a</c> on
/// (docs/es1/simulation/mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc).
/// </summary>
public static class Es1Footfalls {
	/// <summary>The footfall sound and the death arm's.</summary>
	private const short FootfallSound = 0x1d, DeathArmSound = 0x1f;

	/// <summary>The shadow kinds the tests read: a foot and the body.</summary>
	private const int FootKind = 0, BodyKind = 2;

	/// <summary>The death arm's threshold index (file offset 156) and its latch, <c>+0x24e</c>.</summary>
	private const int DeathIndex = 3, DeathLatch = 4;

	/// <summary>
	/// <c>Mech_PlaceLegsOnGround</c>: for each shadow, its position the translation of its part's node through the
	/// machine's frame and its heading the machine's; then, for the player's machine (<c>+0x9d</c>), its entry's footfall
	/// test, each entry taking the next latch. A part the shape lacks takes the identity's translation, where retail reads
	/// address <c>0x14</c>.
	/// </summary>
	public static void PlaceLegsOnGround(Es1Mech mech) {
		var chassis = mech.Type.Chassis;
		var frame = mech.Frame;
		// The locomotion thread's requested sequence, or with none the playing one, read once.
		int gait = mech.LocomotionThread?.TargetSequence ?? -1;
		int latch = 0;
		foreach (var shadow in mech.Shadows) {
			// A slot the damage write emptied is passed over, its latch with it.
			if (shadow == null) {
				continue;
			}

			var node = mech.PartTransform(shadow.PartId);
			shadow.Position = frame.TransformPoint(node.X, node.Y, node.Z);
			shadow.Heading = mech.Rotation.Z;
			if (!mech.IsPlayer) {
				continue;
			}

			if (gait == -1) {
				gait = mech.LocomotionThread?.Sequence ?? -1;
			}

			short sound = -1;
			int index = 0, value = 0;
			bool forward = true;
			if (gait == chassis.AnimId_Death) {
				if (shadow.Root == BodyKind) {
					latch = DeathLatch;
					index = DeathIndex;
					sound = DeathArmSound;
					value = shadow.Position.Z;
				}
			} else if (shadow.Root == FootKind) {
				if (gait == chassis.AnimId_Walk || gait == chassis.AnimId_TurnInPlace
						|| gait == chassis.AnimId_StopMove || gait == chassis.AnimId_StopReverse) {
					forward = mech.PlaybackRate >= 1;
					index = forward ? 0 : 1;
				} else if (gait == chassis.AnimId_Run) {
					index = 2;
				} else {
					latch++;
					continue;
				}

				// The foot's fore-and-aft position in the machine's frame arms its latch past the record's +0xac + 2 × index.
				value = node.Y;
				short arm = Arm(chassis, index);
				if (forward ? arm < value : value < arm) {
					SetLatch(mech, latch, true);
				}

				sound = FootfallSound;
			}

			// Planting: past the record's +0x98 + 2 × index the other way.
			short plant = Plant(chassis, index);
			if (sound != -1 && Latch(mech, latch) && (forward ? value < plant : plant < value)) {
				// PLACEHOLDER: Sound_PlayAt (0004deb8)(the machine's position, sound); no plan step ports sound yet.
				SetLatch(mech, latch, false);
			}

			latch++;
		}
	}

	/// <summary>The arming thresholds, file offsets 170, 172 and 174.</summary>
	private static short Arm(Es1HercSimDat chassis, int index) => index switch {
		0 => chassis.FootfallRearmWalk,
		1 => chassis.FootfallRearmReverse,
		_ => chassis.FootfallRearmRun,
	};

	/// <summary>The planting thresholds, file offsets 150, 152, 154 and 156.</summary>
	private static short Plant(Es1HercSimDat chassis, int index) => index switch {
		0 => chassis.FootfallTriggerWalk,
		1 => chassis.FootfallTriggerReverse,
		2 => chassis.FootfallTriggerRun,
		_ => chassis.FootfallTriggerLand,
	};

	/// <summary>
	/// A latch past <c>+0x24e</c> is past the machine's <c>0x24f</c> bytes, which an entry after the body's in the death
	/// sequence would take; every retail biped's body is its last entry. Here such a latch reads clear and keeps no write.
	/// </summary>
	private static bool Latch(Es1Mech mech, int latch) => latch < mech.FootfallLatches.Length && mech.FootfallLatches[latch];

	/// <inheritdoc cref="Latch"/>
	private static void SetLatch(Es1Mech mech, int latch, bool value) {
		if (latch < mech.FootfallLatches.Length) {
			mech.FootfallLatches[latch] = value;
		}
	}
}
