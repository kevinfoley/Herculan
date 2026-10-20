using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Xunit;
using static Herculan.Engine.Tests.SyntheticMsn;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionDocument"/> over small synthetic missions: each edit writes the <c>.MSN</c> record that supplies the
/// value, so the generated <c>script.dat</c> carries it through every later generation, and undo puts the records back.
/// </summary>
public class MissionDocumentTests {
	private const short Cybrid = 1;

	/// <summary>Catches a generation that starts again from the bytes the document was opened with rather than the edited records.</summary>
	[Fact]
	public void EarlierEditSurvivesALaterOne() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(14, Base(1))
			.Add(16, Group(1, kind: 2, position: 1, condition: -1, 1)));

		document.Change(() => {
			document.PlacePoint(-1, 1, 500, 600, 0, out short point);
			document.SetField(MsnField.BasePosition, 1, point);
		});
		document.SetField(MsnField.GroupSide, 1, Cybrid);
		document.Regenerate();

		var script = document.Script;
		Assert.Equal((500, 600), (script.Coordinates[script.Bases[0].PositionRef].X, script.Coordinates[script.Bases[0].PositionRef].Y));
		Assert.Equal(Cybrid, script.Groups[0].Side);
	}

	/// <summary>Catches an undo snapshot that shares records with the live row, so a write in place changes the snapshot too.</summary>
	[Fact]
	public void UndoAndRedoRestoreTheRecordsExactly() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(14, Base(1))
			.Add(16, Group(1, kind: 2, position: 1, condition: -1, 1)));
		byte[] opened = Write(document);
		byte[] generated = document.ScriptBytes;

		var edit = document.Change(() => {
			document.PlacePoint(-1, 1, 500, 600, 0, out short point);
			document.SetField(MsnField.BasePosition, 1, point);
			document.SetField(MsnField.GroupSide, 1, Cybrid);
		});
		byte[] edited = Write(document);

		document.Undo(edit);
		Assert.Equal(opened, Write(document));
		Assert.Equal(generated, document.ScriptBytes);

		document.Redo(edit);
		Assert.Equal(edited, Write(document));
	}

	/// <summary>
	/// Catches a point move written into the record that created the point's slot rather than the later record of its
	/// GUID that replaced it, which would leave everything naming the point where it was.
	/// </summary>
	[Fact]
	public void MovingASharedPointMovesEverythingNamingIt() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(5, 100))
			.Add(6, Point(5, 200))
			.Add(8, Route(1, 5))
			.Add(9, Circle(1, 5)));

		document.SetPoint(5, 900, 0, 0);

		var script = document.Script;
		short waypoint = script.WaypointGroups[0].Waypoints[0];
		Assert.Equal(waypoint, script.TriggerAreas[0].PointRef);
		Assert.Equal(900, script.Coordinates[waypoint].X);
	}

	/// <summary>Catches a count of a point's namers that misses a row, so a detached move rewrites the shared point in place.</summary>
	[Fact]
	public void DetachedMoveLeavesTheOtherNamerWhereItWas() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(5, 100))
			.Add(8, Route(1, 5))
			.Add(9, Circle(1, 5)));

		document.Change(() => {
			document.PlacePoint(5, 1, 900, 0, 0, out short point);
			document.SetWaypoint(1, 0, point);
		});

		var script = document.Script;
		Assert.Equal(900, script.Coordinates[script.WaypointGroups[0].Waypoints[0]].X);
		Assert.Equal(100, script.Coordinates[script.TriggerAreas[0].PointRef].X);
	}

	/// <summary>
	/// Catches a variant-copied field written into the record that drew the variant, where the copy overwrites it at the
	/// next generation, or into the alternative, which every record drawing it shares.
	/// </summary>
	[Fact]
	public void VariantCopiedFieldIsWrittenInAnOverlayRecord() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(6, Point(1, 100))
			.Add(14, Base(-1, position: 1, condition: 11))
			.Add(14, Base(2, variant: 7)));

		document.Change(() => {
			document.PlacePoint(-1, 1, 900, 0, 0, out short point);
			document.SetField(MsnField.BasePosition, 2, point);
		});
		document.Regenerate();

		var script = document.Script;
		var bases = document.Mission.Bases!;
		Assert.Equal(900, script.Coordinates[script.Bases[0].PositionRef].X);
		Assert.Equal(1, bases[0].PositionRef);
		Assert.Equal(-1, bases[1].PositionRef);
		Assert.Equal((short)2, bases[^1].GUID);
	}

	/// <summary>
	/// Catches row 12's overlay record built with its weapon fit or ammunition types at anything but the merge's unset values
	/// (<c>-1</c> and 5), so changing a variant-keyed herc's chassis — as the training missions' player machines are — would
	/// also overwrite the fit and a launcher's guidance it drew.
	/// </summary>
	[Fact]
	public void VariantKeyedHercKeepsTheFitItDrewWhenItsChassisIsOverlaid() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 1))
			.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 5, operand: 0))
			.Add(12, Mech(-1, type: 0, condition: 11, weapons: [15, 8, 8], ammo: [1]))
			.Add(12, Mech(2, type: -1, variant: 7)));

		document.SetField(MsnField.MechType, 2, 4);

		var herc = document.Script.Mechs[0];
		Assert.Equal(4, herc.TypeIndex);
		Assert.Equal(new short[] { 15, 8, 8, -1, -1, -1, -1, -1, -1, -1 }, herc.WeaponRefs);
		Assert.Equal((short)1, herc.WeaponSecondary[0]);
		Assert.Equal(3, document.Mission.Mechs!.Length);
		Assert.Equal(0, document.Mission.Mechs[0].TypeIndex);
	}

	/// <summary>Catches a new GUID taken from the row's record count, which can be one already in use, merging the new record into it.</summary>
	[Fact]
	public void AppendTakesAFreshGuidAndIsExportedLast() {
		var document = Open(new SyntheticMsn()
			.Add(14, Base(2))
			.Add(14, Base(0)));

		document.Append(14, new BaseRosterEntry62 { ConditionRef = -1, VariantKey = -1, PositionRef = -1, HeadingRef = -1 },
			out short guid);

		Assert.DoesNotContain(document.Mission.Bases!.SkipLast(1), record => record.GUID == guid);
		Assert.Equal(document.Script.Bases.Length - 1, document.ExportedIndex(14, guid));
		Assert.Equal(0, document.ExportedIndex(14, 2));
		Assert.Equal(1, document.ExportedIndex(14, 0));
	}

	/// <summary>
	/// Catches a remove that leaves a later record of the GUID behind, which then carries the group alone, or that
	/// renumbers the refs to later groups by hand.
	/// </summary>
	[Fact]
	public void RemovedGroupResolvesToNothingAndLaterGroupsFollow() {
		var document = Open(new SyntheticMsn()
			.Add(15, GroupOrder(1, group: 2))
			.Add(16, Group(1, kind: 2))
			.Add(16, Group(2, kind: 2))
			.Add(16, Group(2, kind: 2))
			.Add(16, Group(3, kind: 2))
			.Add(17, Objective(condition: -1, kind: 0, subject: 3)));

		document.Remove(16, 2);

		var script = document.Script;
		Assert.Equal(2, script.Groups.Length);
		Assert.Equal(-1, script.Orders[0].SubjectRef);
		Assert.Equal(document.ExportedIndex(16, 3), script.Objectives[0].SubjectRef);
		Assert.Equal(3, document.Mission.Objectives![0]!.SubjectRef);
	}

	/// <summary>Catches a free member slot read from the generated group, where a record that fails its condition leaves a slot it fills in other states.</summary>
	[Fact]
	public void FreeMemberSlotIsFreeInEveryState() {
		const short flagFiveIsSet = 20;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: flagFiveIsSet, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(14, Base(7))
			.Add(14, Base(8))
			.Add(16, Group(1, kind: 2, position: -1, condition: -1, 7))
			.Add(16, Group(1, kind: 2, position: -1, condition: flagFiveIsSet, -1, 8)));

		Assert.Equal(-1, document.Script.Groups[0].MemberRefs[1]);
		Assert.Equal(2, document.FreeMemberSlot(1));
	}

	/// <summary>Catches an unset value written into the supplying record alone, where an earlier record's value then shows through.</summary>
	[Fact]
	public void UnsettingAFieldClearsItInEveryRecordOfTheGuid() {
		var document = Open(new SyntheticMsn()
			.Add(14, Base(7))
			.Add(16, Group(1, kind: 2, position: -1, condition: -1, 7))
			.Add(16, Group(1, kind: 2, position: -1, condition: -1, 7)));

		document.SetField(MsnField.GroupMember(0), 1, -1);

		Assert.Equal(-1, document.Script.Groups[0].MemberRefs[0]);
	}

	/// <summary>Catches a failed change that leaves the edits made before the failure in the records.</summary>
	[Fact]
	public void FailedChangePutsEveryRowBack() {
		var document = Open(new SyntheticMsn()
			.Add(6, Point(1, 100))
			.Add(14, Base(1)));
		byte[] opened = Write(document);

		Assert.Throws<InvalidOperationException>(() => document.Change(() => {
			document.PlacePoint(-1, 1, 500, 0, 0, out _);
			document.SetField(MsnField.BasePosition, 99, 0);
		}));

		Assert.Equal(opened, Write(document));
		Assert.Single(document.Script.Coordinates);
	}

	/// <summary>Catches one roll carried from generation to generation, which draws other variants after an edit and reshuffles the mission.</summary>
	[Fact]
	public void EveryGenerationDrawsTheSameVariants() {
		var document = Open(new SyntheticMsn()
				.Add(1, Condition(guid: 10, condition: -1, type: 3, lower: 7, upper: 0, operand: 2))
				.Add(1, Condition(guid: 11, condition: 10, type: 2, lower: 0, upper: 0, operand: 0))
				.Add(1, Condition(guid: 12, condition: 10, type: 2, lower: 1, upper: 1, operand: 0))
				.Add(6, Point(-1, 100, condition: 11))
				.Add(6, Point(-1, 500, condition: 12))
				.Add(6, Point(1, 0, variant: 7))
				.Add(6, Point(2, 0)),
			() => {
				int draws = 0;
				return bound => draws++ % bound;
			});
		int drawn = document.Script.Coordinates[0].X;

		document.SetPoint(2, 50, 0, 0);

		Assert.Equal(drawn, document.Script.Coordinates[0].X);
	}

	/// <summary>
	/// Catches a header edit written into a row-2 record that a later one overrides, so the generated header keeps the
	/// later record's theater and the edit shows nothing.
	/// </summary>
	[Fact]
	public void HeaderWordIsWrittenWhereTheGenerationReadsIt() {
		var document = Open(new SyntheticMsn().Add(2, Patch(theater: 1)).Add(2, Patch(theater: 3)));

		document.SetHeaderWord(0, 2);
		document.SetHeaderWord(9, 1);

		Assert.Equal(2, document.Script.TheaterIndex);
		Assert.Equal(1, document.Script.TheaterVariant);
	}

	/// <summary>
	/// Catches a field's supplier taken from the generation, or from every record of the GUID whatever its condition: the
	/// later record holds at the open and sets the side too, so either writes it and leaves the base unchanged.
	/// </summary>
	[Fact]
	public void BaseFieldEditSkipsALaterRecordOfAnotherCondition() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: DrawGuid, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: HoldsAtOpen, condition: DrawGuid, type: 2, lower: 0, upper: 9, operand: 0))
			.Add(16, Group(1, kind: 2))
			.Add(16, Group(1, kind: 2, condition: HoldsAtOpen)));

		document.SetField(MsnField.GroupSide, 1, Cybrid);

		Assert.Equal(new short[] { Cybrid, 0 }, document.Mission.Groups!.Select(record => record.Side));
	}

	/// <summary>
	/// Catches an edit that still needs the generation to keep the record it writes: a group whose only record fails its
	/// condition at the open takes a deployment action (<see cref="ActionWiring.SetDeployment"/>) instead of being refused.
	/// </summary>
	[Fact]
	public void EditWritesARecordTheGenerationDrops() {
		const short flagFiveIsSet = 20;
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: flagFiveIsSet, condition: -1, type: 0, lower: 5, upper: 0x119, operand: 1))
			.Add(10, Action(3, type: 1))
			.Add(16, Group(1, kind: 2))
			.Add(16, Group(2, kind: 2, condition: flagFiveIsSet)));

		ActionWiring.SetDeployment(document, 2, 3);

		Assert.Null(document.ExportedIndex(16, 2));
		Assert.Equal(3, document.Mission.Groups![1].DeploymentActionRef);
	}

	/// <summary>
	/// Catches row 2's base built from every record whatever its condition, or the header word's supplier taken from the
	/// generation: the later record holds at the open and sets the theater too, so either writes it there.
	/// </summary>
	[Fact]
	public void HeaderWordSkipsALaterRecordOfAnotherCondition() {
		var document = Open(new SyntheticMsn()
			.Add(1, Condition(guid: DrawGuid, condition: -1, type: 1, lower: 11, upper: 0, operand: 0))
			.Add(1, Condition(guid: HoldsAtOpen, condition: DrawGuid, type: 2, lower: 0, upper: 9, operand: 0))
			.Add(2, Patch(theater: 2))
			.Add(2, Patch(theater: 3, condition: HoldsAtOpen)));

		document.SetHeaderWord(0, 4);

		Assert.Equal(new short[] { 4, 3 },
			document.Mission.SettingsPatches!.Select(patch => patch.Data[MissionSettingsPatch.HeaderWordsStart]));
	}

	// A draw below 11, and a range over it from 0 to 9, which every open's draw of 0 holds.
	private const short DrawGuid = 10;
	private const short HoldsAtOpen = 11;

	// A row-2 record setting the theater alone, clearing no flag.
	private static short[] Patch(short theater, short condition = -1) {
		var words = new short[MissionSettingsPatch.Words];
		words[0] = condition;
		words[MissionSettingsPatch.HeaderWordsStart] = theater;
		Array.Fill(words, (short)-1, MissionSettingsPatch.ClearListStart, MissionSettingsPatch.Words - MissionSettingsPatch.ClearListStart);
		return words;
	}

	private static MissionDocument Open(SyntheticMsn msn, Func<Func<short, int>>? newRoll = null) =>
		MissionDocument.Read(msn.Bytes(), null, new MissionGeneration(new short[MissionGenerator.CampaignFlagCount],
			new short?[MissionGeneration.HeaderWords], newRoll ?? (() => _ => 0)));

	private static byte[] Write(MissionDocument document) => new MissionFileTransformer().Write(document.Mission)!;
}
