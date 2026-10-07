using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Settings;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Cockpit;

/// <summary>Which art the status screen puts in its wireframe viewport.</summary>
public enum MfdSilhouetteKind {
	/// <summary>Nothing — no subject, or one whose class the screen does not recognise.</summary>
	None,

	/// <summary>
	/// A machine's own paper doll, placed by its <c>.PDG</c> view origin plus
	/// <see cref="MfdLayout.WireframeArtOffset"/> rather than centred.
	/// </summary>
	PaperDoll,

	/// <summary>A flat silhouette from the <c>BASES</c>, <c>VEHICLES</c> or <c>FLYERS</c> bank, centred in the viewport.</summary>
	Silhouette,
}

/// <summary>
/// What the MFD's status screen is looking at. One record serves both F1 and F5 because in the
/// original one screen class serves both: <c>MfdDisplay_Ctor</c> builds two
/// <c>MfdStatusScreen_Ctor</c> instances and the only difference between them is what
/// <c>MfdDisplay_Update</c> (<c>00446328</c>) parks in the shared subject field — the squad roster's
/// current entry for mode 0 (<see cref="MfdStatusRoster"/>), <c>CockpitView+0x210</c> (the selection)
/// for mode 4.
///
/// <para>Everything here is read by the screen's paint (<c>MfdStatusScreen_Paint</c>, <c>0043a5a0</c>). The choices that look
/// like mode differences are really subject differences: <c>ID:</c> versus <c>TARGET:</c> is "is this
/// the machine I am flying (or one of my squad)", and the integrity readout versus the range readout
/// is "is this one of ours", both decided from the subject alone.</para>
///
/// <para>A flyer's name comes from <c>FLYERS.DAT</c> <c>NameBytes</c>, the same <c>+0x12</c> the paint
/// reads, and a HERC's from its type name.</para>
/// </summary>
/// <param name="Present">Whether there is a subject at all. False draws the no-target screen.</param>
/// <param name="Identified">
/// False for a subject whose class the screen's switch does not recognise, which prints
/// <c>TARGET:</c> over <see cref="MfdLayout.UnknownNameGroup"/> and leaves the status labels
/// untouched.
/// </param>
/// <param name="Own">
/// Whether this is the player's own machine or a squadmate — the subjects that head the screen with
/// <c>ID:</c> and are named by pilot rather than by type.
/// </param>
/// <param name="Hostile">
/// The subject's side, from its mission group. It picks the name label's font (green for a friendly,
/// red for a hostile) and decides the fifth label: a friendly gets the structural-integrity readout
/// and a hostile gets its range.
/// </param>
/// <param name="Name">The subject's name as the screen prints it, already upper-cased.</param>
/// <param name="Condition">
/// Index into <see cref="MfdLayout.ConditionGroup"/> — 0 OK, 1 SHIELDS DN, 2 INT DAMAGE, 3 CRITICAL,
/// 4 DESTROYED.
/// </param>
/// <param name="Damage">The subject's overall damage as a Q8 fraction, which the integrity readout inverts.</param>
/// <param name="Distance">Eye to subject in world units, printed after <c>DIST:</c> for a hostile.</param>
/// <param name="SilhouetteKind">Which of the two ways the viewport is filled, or neither.</param>
/// <param name="SilhouetteBank">The sprite bank the viewport art comes from.</param>
/// <param name="SilhouetteFrame">Its frame in that bank.</param>
/// <param name="PaperDollName">
/// The machine whose <c>.PDG</c> places a <see cref="MfdSilhouetteKind.PaperDoll"/>, looked up
/// through <see cref="CockpitArt.PaperDollFor"/>.
/// </param>
/// <param name="Readings">
/// The subject's damage-readout buffer (<see cref="Sim.ComponentDamage.ReadDamageReadouts"/>), which
/// is what tints the paper doll region by region and what fills the Heads-Down Display's rows. Null
/// for a subject with no per-component model.
/// </param>
/// <param name="FlyerVariant">
/// Whether the subject's chassis is the flyer kind (<see cref="Sim.MechTypeRecord.IsFlyer"/>). It
/// selects the doll's region-to-component mapping and, on the damage detail, the flyer name tables.
/// </param>
/// <param name="HighlightComponent">
/// The component a Targeting Pod has singled out of this subject, or -1 for none — the original's
/// <c>CockpitView+0x27e</c>, gated on the present flag beside it. It is filled by the caller rather
/// than by <see cref="For"/>, because the pod belongs to the machine doing the looking and not to the
/// subject. See <see cref="MfdLayout.ComponentHighlightColorId"/>.
/// </param>
/// <param name="Scrambled">
/// The screen's scramble flag (<c>+0x34</c>) as it stood when this was painted: a present subject's name,
/// condition and integrity or range print as <see cref="MfdLayout.ScrambledName"/> and its fellows, and a
/// HERC's paper doll and pod highlight are not drawn. Set by <see cref="MfdStatusRefresh"/>.
/// </param>
public readonly record struct MfdStatusSubject(
	bool Present,
	bool Identified,
	bool Own,
	bool Hostile,
	string Name,
	int Condition,
	int Damage,
	int Distance,
	MfdSilhouetteKind SilhouetteKind,
	string? SilhouetteBank,
	int SilhouetteFrame,
	string? PaperDollName,
	IReadOnlyList<short>? Readings = null,
	bool FlyerVariant = false,
	int HighlightComponent = -1,
	bool Scrambled = false) {

	/// <summary>Nothing selected — the state F5 sits in until the player picks something.</summary>
	public static MfdStatusSubject None { get; } = new(
		Present: false, Identified: false, Own: false, Hostile: false,
		Name: "", Condition: 0, Damage: 0, Distance: 0,
		SilhouetteKind: MfdSilhouetteKind.None, SilhouetteBank: null, SilhouetteFrame: 0,
		PaperDollName: null);

	/// <summary>
	/// Reads one subject the way the status screen's paint (<c>MfdStatusScreen_Paint</c>, <c>0043a5a0</c>) reads it. The whole
	/// switch is on the object's target class — <c>obj+0x1a8</c>, the field each constructor writes —
	/// so a HERC gets its paper doll and a component scan, a flyer and a structure get a flat
	/// silhouette and a condition worked out from overall damage alone, and anything else is
	/// <see cref="Identified"/> false.
	/// </summary>
	/// <param name="subject">
	/// What the screen is looking at: the squad roster's current entry for F1, the current selection
	/// for F5. Null gives <see cref="None"/>.
	/// </param>
	/// <param name="viewer">The machine the range is measured from — the original's <c>CockpitView+0x203</c>.</param>
	/// <param name="strings">For the structure and vehicle type-name groups.</param>
	/// <param name="squad">
	/// The comm boxes, which say whether a HERC is a squadmate (<c>Squad_IndexOf</c>, <c>00433134</c>)
	/// and give its pilot's name (<c>HddGauge_Name</c>). Null treats no machine as a squadmate.
	/// </param>
	public static MfdStatusSubject For(Sim.SimObject? subject, Sim.SimObject? viewer,
			StringFile? strings, SquadCommChannel? squad = null) {
		if (subject == null) {
			return None;
		}

		bool own = subject == viewer;
		bool hostile = subject.Side != World.MissionSide.Human;
		int distance;
		if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowTargetDistanceInMeters)) {
			// Use the same distance formula as the MFD F4 SCANNER screen, so the numbers agree.
			if (viewer == null) {
				distance = 0;
			} else {
				int dx = subject.Position.X - viewer.Position.X;
				int dy = subject.Position.Y - viewer.Position.Y;
				distance = Math.Max(SimMath.FastMagnitude2D(dx, dy), 1);
			}
		} else {
			// Tweak not enabled, use the vanilla formula. This does not agree with SCANNER,
			// but it isn't apparent because vanilla displays distance in engine units
			// on the F5 TARGET screen.
			distance = viewer != null ? viewer.Position.ApproxDistanceTo(subject.Position) : 0;
		}

		switch (subject) {
			case Sim.MechObject mech: {
				// A HERC is the only class with per-component readings, so it is the only one whose
				// condition is scanned rather than derived: DESTROYED outright, else CRITICAL when every
				// one of the twelve internals is more than half gone, else INT DAMAGE when any of them is
				// touched at all, else the shields-down latch, else OK.
				int damage = mech.Damage?.OverallDamage ?? 0;
				int condition = 0;
				if (mech.Destroyed) {
					condition = 4;
				} else if (mech.Damage is { } readings) {
					int worst = 0x100;
					int best = 0;
					for (int slot = 0; slot < MfdLayout.ScannedDependents; slot++) {
						int reading = readings.DependentPercent(slot);
						worst = Math.Min(worst, reading);
						best = Math.Max(best, reading);
					}

					condition = worst >= MfdLayout.CriticalDependentDamage ? 3 : best > 0 ? 2 : 0;
				}

				if (condition == 0 && mech.ShieldsDownAlert) {
					condition = 1;
				}

				// The name is the type record's own, which for every retail machine is the herc name -
				// the same name its .HBA paper-doll bank and .PDG diagram are filed under. Only this
				// branch asks whether the subject is one of ours: the machine being flown prints YOU, a
				// squadmate its pilot's name, and both head the screen ID:.
				string name = mech.Name.ToUpperInvariant();
				int squadSlot = squad?.SlotOf(mech) ?? -1;
				string printed = own ? strings?.Text(MfdLayout.SelfNameGroup, 0) ?? name
					: squadSlot >= 0 ? squad!.Name(squadSlot)
					: name;
				return new MfdStatusSubject(
					Present: true, Identified: true, Own: own || squadSlot >= 0, Hostile: hostile,
					Name: printed,
					Condition: condition, Damage: damage, Distance: distance,
					SilhouetteKind: MfdSilhouetteKind.PaperDoll,
					SilhouetteBank: name, SilhouetteFrame: MfdLayout.WireframeViewIndex,
					PaperDollName: name,
					Readings: mech.Damage?.ReadDamageReadouts(),
					FlyerVariant: mech.Type.IsFlyer);
			}

			case Sim.FlyerObject flyer: {
				int damage = flyer.Damage?.OverallDamage ?? 0;
				return new MfdStatusSubject(
					Present: true, Identified: true, Own: own, Hostile: hostile,
					Name: FlyerName(flyer),
					Condition: ConditionFromDamage(damage), Damage: damage, Distance: distance,
					SilhouetteKind: MfdSilhouetteKind.Silhouette,
					SilhouetteBank: MfdLayout.FlyerBank, SilhouetteFrame: MfdLayout.FlyerFrame,
					PaperDollName: null);
			}

			case Sim.BaseObject structure: {
				var type = structure.Type;
				int damage = structure.DamageFraction;
				int nameGroup = type.IsVehicle ? MfdLayout.VehicleNameGroup : MfdLayout.StructureNameGroup;
				return new MfdStatusSubject(
					Present: true, Identified: true, Own: own, Hostile: hostile,
					Name: strings?.Text(nameGroup, type.SilhouetteIndex) ?? "",
					Condition: ConditionFromDamage(damage), Damage: damage, Distance: distance,
					SilhouetteKind: MfdSilhouetteKind.Silhouette,
					SilhouetteBank: type.IsVehicle ? MfdLayout.VehicleBank : MfdLayout.StructureBank,
					SilhouetteFrame: type.SilhouetteIndex,
					PaperDollName: null);
			}

			default:
				return None with { Present = true };
		}
	}

	/// <summary>
	/// A flyer's printed name — its type record's own string, which sits at the same <c>+0x12</c> the
	/// paint reads. Falls back to the model name when the <c>.DAT</c> is missing.
	/// </summary>
	private static string FlyerName(Sim.FlyerObject flyer) {
		if (flyer.SimData?.NameBytes is { Length: > 0 } bytes) {
			string name = System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
			if (name.Length > 0) {
				return name.ToUpperInvariant();
			}
		}

		return flyer.Name.ToUpperInvariant();
	}

	/// <summary>
	/// <c>Damage_ToConditionState</c> — the condition a subject with no per-component readings is in, worked out
	/// from its overall damage alone. The flyer and structure branches of the paint both take this
	/// route; only a HERC gets the component scan. Bands are the original's: 90% and up OK, 74% up
	/// SHIELDS DN, 51% up INT DAMAGE, anything still standing CRITICAL, nothing left DESTROYED.
	/// </summary>
	public static int ConditionFromDamage(int damage) {
		int intact = MfdLayout.IntegrityPercent(damage);
		return intact switch {
			>= 0x5a => 0,
			>= 0x4a => 1,
			>= 0x33 => 2,
			>= 1 => 3,
			_ => 4,
		};
	}
}
