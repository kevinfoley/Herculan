using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Editor;
using Herculan.Engine.World;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RosterFit"/> over a small synthetic mission whose two hercs draw the one alternative of a table: an edit asked
/// to write the alternative changes both, one asked to write the herc alone leaves the alternative and the other herc, and a
/// chassis change writes the new type's whole fit; and <see cref="UnitTypeCatalog"/> offers a structure no vehicle type.
/// </summary>
public class RosterFitTests {
	private const short Hercs = 0;
	private const short Squad = 9;
	private const short First = 10;
	private const short Second = 11;

	/// <summary>Catches an edit of a drawn field written as an overlay of the one herc, where the view asks for the alternative.</summary>
	[Fact]
	public void AnEditOfADrawnFieldChangesEveryHercDrawingTheAlternative() {
		var document = Open();
		int records = document.Mission.Mechs!.Length;

		RosterFit.Write(document, First, [(MsnField.MechWeapon(0), 8)], toAlternative: true, _ => false);

		Assert.Equal(8, Weapons(document, First)[0]);
		Assert.Equal(8, Weapons(document, Second)[0]);
		Assert.Equal(records, document.Mission.Mechs!.Length);
	}

	/// <summary>Catches "Only this herc" ignored: the edit must leave the alternative, and so the other herc drawing it, as they were.</summary>
	[Fact]
	public void AnEditOfOneHercAloneLeavesTheAlternativeAndTheOtherHerc() {
		var document = Open();
		var alternative = document.AlternativeOf(MsnField.MechWeapon(0), First)!.Value;

		RosterFit.Write(document, First, [(MsnField.MechWeapon(0), 8)], toAlternative: false, _ => false);

		Assert.Equal(8, Weapons(document, First)[0]);
		Assert.Equal(6, Weapons(document, Second)[0]);
		Assert.Equal(6, ((MechRosterEntry144)document.RecordAt(alternative)!).WeaponRefs[0]);
	}

	/// <summary>
	/// Catches a chassis change that keeps the old chassis's weapons in a slot the new type's fit leaves empty: written to the
	/// alternative, every slot takes the Cybrid fit a new herc of the type gets, its unset slots included.
	/// </summary>
	[Fact]
	public void AChassisChangeTakesTheNewTypesWholeFit() {
		var document = Open();
		var (weapons, ammo) = HercGroups.CybridFit(15)!.Value;

		RosterFit.Write(document, First, RosterFit.Chassis(15, weapons, ammo), toAlternative: true, _ => false);

		foreach (short herc in new[] { First, Second }) {
			Assert.Equal(15, document.Script.Mechs[Exported(document, 12, herc)!.Value].TypeIndex);
			Assert.Equal(weapons, Weapons(document, herc));
		}
	}

	/// <summary>Catches a structure offered a vehicle type, or a ground vehicle a structure type: a convoy turned into a building.</summary>
	[Fact]
	public void AStructureIsOfferedNoVehicleType() {
		var catalog = new UnitTypeCatalog([
			(ToolboxKind.Herc, 0, "APOC"), (ToolboxKind.Vehicle, 45, "Truck"), (ToolboxKind.Structure, 1, "Tower"),
			(ToolboxKind.Structure, 2, "Hangar"), (ToolboxKind.Vehicle, 46, "Tank"),
		]);

		Assert.Equal([1, 2], catalog.ChoicesFor(MissionUnitKind.Base, isVehicle: false));
		Assert.Equal([45, 46], catalog.ChoicesFor(MissionUnitKind.Base, isVehicle: true));
	}

	// Two hercs drawing table 0's one alternative, a type 3 with weapon 6 in its first two slots; group 0 is the player's machine.
	private static MissionDocument Open() {
		var squad = Group(Squad, Hercs, position: -1, condition: -1, 0);
		squad[MissionGroup164.HeadingRefWord] = 0;
		var msn = new SyntheticMsn()
			.Add(1, Condition(0, condition: -1, type: ConditionEditing.VariantTable, lower: 1, upper: 0, operand: 100))
			.Add(1, Condition(1, condition: 0, type: ConditionEditing.Range, lower: 0, upper: 99, operand: 0))
			.Add(7, Heading(0, 0))
			.Add(12, Mech(0, type: 0))
			.Add(12, Mech(-1, type: 3, condition: 1, weapons: [6, 6]))
			.Add(12, Mech(First, type: -1, variant: 1)).Add(12, Mech(Second, type: -1, variant: 1))
			.Add(16, squad)
			.Add(16, Group(20, Hercs, position: -1, condition: -1, First, Second));
		return MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], () => _ => 0));
	}

	private static short[] Weapons(MissionDocument document, short guid) =>
		document.Script.Mechs[Exported(document, 12, guid)!.Value].WeaponRefs;
}
