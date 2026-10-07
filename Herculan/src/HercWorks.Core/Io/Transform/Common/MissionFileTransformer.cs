using HercWorks.Core.Data.File.Msn;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Transforms byte[] data to and from .MSN mission files (<see cref="MissionFile"/>): the revision
/// word, then the 17 rows in order, row #5 kept raw and row #8's waypoints 2 bytes each. See
/// docs/retail/formats/msn-mission-file.md.
/// </summary>
public class MissionFileTransformer : ByteTransformer<MissionFile> {
	public override MissionFile? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		var data = new MissionFile();

		SetBytes(inputArray);

		data.Revision = IndexShortLE();

		data.Conditions = ReadArray(ParseRow1);
		data.SettingsPatches = ReadArray(ParseRow2);
		data.Variants = ReadArray(ParseRow3);
		data.Texts = ReadArray(ParseRow4);

		int debriefCount = IndexShortLE();
		data.DebriefBytes = IndexSegment(debriefCount * 64);

		data.Points = ReadArray(ParseRow6);
		data.Headings = ReadArray(ParseRow7);
		data.WaypointGroups = ReadArray(ParseRow8);
		data.TriggerAreas = ReadArray(ParseRow9);
		data.Actions = ReadArray(ParseRow10);
		data.ActionTimers = ReadArray(ParseRow11);
		data.Mechs = ReadArray(ParseRow12);
		data.Flyers = ReadArray(ParseRow13);
		data.Bases = ReadArray(ParseRow14);
		data.Orders = ReadArray(ParseRow15);
		data.Groups = ReadArray(ParseRow16);

		ParseRow17(data);

		return data;
	}

	private T[] ReadArray<T>(Func<T> parseOne) {
		var arr = new T[IndexShortLE()];
		for (int i = 0; i < arr.Length; i++) {
			arr[i] = parseOne();
		}
		return arr;
	}

	// ---- Row #1: MissionCondition14 (14 bytes) ----------------------------------------------

	private MissionCondition14 ParseRow1() => new(
		IndexShortLE(), IndexShortLE(), IndexShortLE(), IndexShortLE(),
		IndexShortLE(), IndexShortLE(), IndexShortLE());

	// ---- Row #2: MissionSettingsPatch (82 bytes, applied at load and not kept) ---------------

	private MissionSettingsPatch ParseRow2() => new() { Data = IndexShortLEArray(41) };

	// ---- Row #3: VariantValue8 (8 bytes) ----------------------------------------------------

	private VariantValue8 ParseRow3() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		CompoundConditionPartner = IndexShortLE(),
		Value = IndexShortLE()
	};

	// ---- Row #4: MissionText144 (144 bytes, no GUID) ----------------------------------------

	private MissionText144 ParseRow4() => new() {
		ConditionRef = IndexShortLE(),
		ObjectiveLines = IndexShortLEArray(10),
		BriefingLines = IndexShortLEArray(30),
		IntelligenceLines = IndexShortLEArray(30),
		MovieRef = IndexShortLE()
	};

	// ---- Row #6: MapPoint22 (22 bytes) ------------------------------------------------------

	private MapPoint22 ParseRow6() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Unk06 = IndexShortLE(),
		SumFlag = IndexShortLE(),
		X = IndexIntLE(),
		Y = IndexIntLE(),
		Z = IndexIntLE()
	};

	// ---- Row #7: Heading10 (10 bytes) ----------------------------------------------------------

	private Heading10 ParseRow7() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Unk06 = IndexShortLE(),
		Degrees = IndexShortLE()
	};

	// ---- Row #8: WaypointGroup (10 fixed bytes + nested-count x 2 bytes) -------------------

	private WaypointGroup ParseRow8() {
		var g = new WaypointGroup {
			GUID = IndexShortLE(),
			ConditionRef = IndexShortLE(),
			VariantKey = IndexShortLE(),
			Unk06 = IndexShortLE()
		};

		int nestedCount = IndexShortLE();
		g.Waypoints = IndexShortLEArray(nestedCount);

		return g;
	}

	// ---- Row #9: TriggerArea12 (12 bytes) ---------------------------------------------------

	private TriggerArea12 ParseRow9() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Unk04 = IndexShortLE(),
		Shape = IndexShortLE(),
		PointRef = IndexShortLE(),
		SecondPointOrRadius = IndexShortLE()
	};

	// ---- Row #10: MissionAction82 (82 bytes) ------------------------------------------------

	private MissionAction82 ParseRow10() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Unk04 = IndexShortLE(),
		Type = IndexShortLE(),
		Verb = IndexShortLE(),
		AreaRefs = IndexShortLEArray(8),
		Unk1A = IndexShortLE(),
		CounterPairs = IndexShortLEArray(20),
		TextRefs = IndexShortLEArray(5),
		MessageId = IndexShortLE(),
		TargetRef = IndexShortLE()
	};

	// ---- Row #11: ActionTimer30 (30 bytes) ---------------------------------------------------

	private ActionTimer30 ParseRow11() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Unk04 = IndexShortLE(),
		PrimaryActionRef = IndexShortLE(),
		Delay = IndexShortLE(),
		SequenceRefs = IndexShortLEArray(10)
	};

	// ---- Row #12: MechRosterEntry144 (144 bytes) -----------------------------------------------

	private MechRosterEntry144 ParseRow12() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		CompoundConditionPartner = IndexShortLE(),
		AiRadarActive = IndexShortLE(),
		AiCruiseSpeed = IndexShortLE(),
		DeadZone = IndexShortLEArray(18),
		TypeIndex = IndexShortLE(),
		WeaponRefs = IndexShortLEArray(10),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		PairCount = IndexShortLE(),
		OutOfActionReport = IndexShortLEArray(20),
		WeaponSecondary = IndexShortLEArray(10),
		Constant2 = IndexShortLE(),
		EngagementActionRef = IndexShortLE(),
		DefeatActionRef = IndexShortLE(),
		StartingCondition = IndexShortLE()
	};

	// ---- Row #13: FlyerRosterEntry102 (102 bytes) -------------------------------------------

	private FlyerRosterEntry102 ParseRow13() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Unk06 = IndexShortLE(),
		FlagSpan = IndexShortLEArray(20),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		TypeIndex = IndexShortLE(),
		PairCount = IndexShortLE(),
		OutOfActionReport = IndexShortLEArray(20),
		EngagementActionRef = IndexShortLE(),
		DefeatActionRef = IndexShortLE(),
		UnkVal_100 = IndexShortLE()
	};

	// ---- Row #14: BaseRosterEntry62 (62 bytes) ----------------------------------------------

	private BaseRosterEntry62 ParseRow14() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Unk06 = IndexShortLE(),
		TypeIndex = IndexShortLE(),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		PairCount = IndexShortLE(),
		OutOfActionReport = IndexShortLEArray(20),
		EngagementActionRef = IndexShortLE(),
		DefeatActionRef = IndexShortLE(),
		StartingCondition = IndexShortLE()
	};

	// ---- Row #15: MissionOrder22 (22 bytes) -------------------------------------------------

	private MissionOrder22 ParseRow15() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		CompoundConditionPartner = IndexShortLE(),
		Verb = IndexShortLE(),
		FormationId = IndexShortLE(),
		PointRef = IndexShortLE(),
		RouteRef = IndexShortLE(),
		SubjectKind = IndexShortLE(),
		SubjectRef = IndexShortLE(),
		ActionRef = IndexShortLE()
	};

	// ---- Row #16: MissionGroup164 (164 bytes) ----------------------------------------------

	private MissionGroup164 ParseRow16() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		CompoundConditionPartner = IndexShortLE(),
		PaintsGround = IndexShortLE(),
		NearConstant = IndexShortLE(),
		DeadZone = IndexShortLEArray(18),
		MemberKind = IndexShortLE(),
		FormationId = IndexShortLE(),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		RouteRef = IndexShortLE(),
		MemberRefs = IndexShortLEArray(20),
		OrderRefs = IndexShortLEArray(10),
		Side = IndexShortLE(),
		DeploymentActionRef = IndexShortLE(),
		PairCount = IndexShortLE(),
		OutOfActionReport = IndexShortLEArray(20),
		MapShown = IndexShortLE()
	};

	// ---- Row #17: MissionObjective58 (58 bytes, no GUID) -----------------------------------

	private const int Row17RecordSize = 58;

	/// <summary>
	/// Reads row #17, stopping at the end of the file if it ends inside a record — retail's DEMO2.MSN
	/// does, 42 bytes short (docs/retail/formats/msn-mission-file.md#verification-note). The bytes that are
	/// there go to <see cref="MissionFile.TruncatedRow17Tail"/> so a write reproduces the file.
	/// </summary>
	private void ParseRow17(MissionFile data) {
		int count = IndexShortLE();
		var entries = new MissionObjective58?[count];

		for (int i = 0; i < count; i++) {
			int remaining = GetBytes().Length - Index;
			if (remaining < Row17RecordSize) {
				data.TruncatedRow17Tail = IndexSegment(remaining);
				entries[i] = null;
				break;
			}

			entries[i] = ParseObjective();
		}

		data.Objectives = entries;
	}

	private MissionObjective58 ParseObjective() {
		var r = new MissionObjective58 {
			ConditionRef = IndexShortLE(),
			Required = IndexShortLE(),
			ConditionCode = IndexShortLE(),
			SubjectKind = IndexShortLE(),
			SubjectRef = IndexShortLE(),
			PointRef = IndexShortLE(),
			RouteRef = IndexShortLE(),
			TextRef = IndexShortLE(),
			PairCount = IndexShortLE()
		};

		for (int p = 0; p < r.Pairs.Length; p++) {
			r.Pairs[p] = new CounterPair {
				CounterRef = IndexShortLE(),
				Op = IndexShortLE()
			};
		}

		return r;
	}

	// ==========================================================================================
	// Write path
	// ==========================================================================================

	public override byte[]? Write(MissionFile data) {
		using var outStream = new MemoryStream();

		Emit(outStream, WriteShortLE(data.Revision));

		WriteArray(outStream, data.Conditions!, WriteRow1);
		WriteArray(outStream, data.SettingsPatches!, WriteRow2);
		WriteArray(outStream, data.Variants!, WriteRow3);
		WriteArray(outStream, data.Texts!, WriteRow4);

		Emit(outStream, WriteShortLE((short)(data.DebriefBytes!.Length / 64)));
		Emit(outStream, data.DebriefBytes);

		WriteArray(outStream, data.Points!, WriteRow6);
		WriteArray(outStream, data.Headings!, WriteRow7);
		WriteArray(outStream, data.WaypointGroups!, WriteRow8);
		WriteArray(outStream, data.TriggerAreas!, WriteRow9);
		WriteArray(outStream, data.Actions!, WriteRow10);
		WriteArray(outStream, data.ActionTimers!, WriteRow11);
		WriteArray(outStream, data.Mechs!, WriteRow12);
		WriteArray(outStream, data.Flyers!, WriteRow13);
		WriteArray(outStream, data.Bases!, WriteRow14);
		WriteArray(outStream, data.Orders!, WriteRow15);
		WriteArray(outStream, data.Groups!, WriteRow16);

		WriteRow17(outStream, data);

		return outStream.ToArray();
	}

	private void WriteArray<T>(MemoryStream outStream, T[] items, Action<MemoryStream, T> writeOne) {
		Emit(outStream, WriteShortLE((short)items.Length));
		foreach (var item in items) {
			writeOne(outStream, item);
		}
	}

	private void WriteRow1(MemoryStream o, MissionCondition14 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.Type));
		Emit(o, WriteShortLE(e.FlagIndexOrRangeLower));
		Emit(o, WriteShortLE(e.OperatorOrRangeUpperOrResult));
		Emit(o, WriteShortLE(e.ComparisonOperand));
		Emit(o, WriteShortLE(e.LatestDraw));
	}

	private void WriteRow2(MemoryStream o, MissionSettingsPatch e) {
		Emit(o, WriteShortLESegment(e.Data));
	}

	private void WriteRow3(MemoryStream o, VariantValue8 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.CompoundConditionPartner));
		Emit(o, WriteShortLE(e.Value));
	}

	private void WriteRow4(MemoryStream o, MissionText144 e) {
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLESegment(e.ObjectiveLines));
		Emit(o, WriteShortLESegment(e.BriefingLines));
		Emit(o, WriteShortLESegment(e.IntelligenceLines));
		Emit(o, WriteShortLE(e.MovieRef));
	}

	private void WriteRow6(MemoryStream o, MapPoint22 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.Unk06));
		Emit(o, WriteShortLE(e.SumFlag));
		Emit(o, WriteIntLE(e.X));
		Emit(o, WriteIntLE(e.Y));
		Emit(o, WriteIntLE(e.Z));
	}

	private void WriteRow7(MemoryStream o, Heading10 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.Unk06));
		Emit(o, WriteShortLE(e.Degrees));
	}

	private void WriteRow8(MemoryStream o, WaypointGroup e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.Unk06));
		Emit(o, WriteShortLE((short)e.Waypoints.Length));
		Emit(o, WriteShortLESegment(e.Waypoints));
	}

	private void WriteRow9(MemoryStream o, TriggerArea12 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.Unk04));
		Emit(o, WriteShortLE(e.Shape));
		Emit(o, WriteShortLE(e.PointRef));
		Emit(o, WriteShortLE(e.SecondPointOrRadius));
	}

	private void WriteRow10(MemoryStream o, MissionAction82 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.Unk04));
		Emit(o, WriteShortLE(e.Type));
		Emit(o, WriteShortLE(e.Verb));
		Emit(o, WriteShortLESegment(e.AreaRefs));
		Emit(o, WriteShortLE(e.Unk1A));
		Emit(o, WriteShortLESegment(e.CounterPairs));
		Emit(o, WriteShortLESegment(e.TextRefs));
		Emit(o, WriteShortLE(e.MessageId));
		Emit(o, WriteShortLE(e.TargetRef));
	}

	private void WriteRow11(MemoryStream o, ActionTimer30 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.Unk04));
		Emit(o, WriteShortLE(e.PrimaryActionRef));
		Emit(o, WriteShortLE(e.Delay));
		Emit(o, WriteShortLESegment(e.SequenceRefs));
	}

	private void WriteRow12(MemoryStream o, MechRosterEntry144 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.CompoundConditionPartner));
		Emit(o, WriteShortLE(e.AiRadarActive));
		Emit(o, WriteShortLE(e.AiCruiseSpeed));
		Emit(o, WriteShortLESegment(e.DeadZone));
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, WriteShortLESegment(e.WeaponRefs));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, WriteShortLE(e.PairCount));
		Emit(o, WriteShortLESegment(e.OutOfActionReport));
		Emit(o, WriteShortLESegment(e.WeaponSecondary));
		Emit(o, WriteShortLE(e.Constant2));
		Emit(o, WriteShortLE(e.EngagementActionRef));
		Emit(o, WriteShortLE(e.DefeatActionRef));
		Emit(o, WriteShortLE(e.StartingCondition));
	}

	private void WriteRow13(MemoryStream o, FlyerRosterEntry102 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.Unk06));
		Emit(o, WriteShortLESegment(e.FlagSpan));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, WriteShortLE(e.PairCount));
		Emit(o, WriteShortLESegment(e.OutOfActionReport));
		Emit(o, WriteShortLE(e.EngagementActionRef));
		Emit(o, WriteShortLE(e.DefeatActionRef));
		Emit(o, WriteShortLE(e.UnkVal_100));
	}

	private void WriteRow14(MemoryStream o, BaseRosterEntry62 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.Unk06));
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, WriteShortLE(e.PairCount));
		Emit(o, WriteShortLESegment(e.OutOfActionReport));
		Emit(o, WriteShortLE(e.EngagementActionRef));
		Emit(o, WriteShortLE(e.DefeatActionRef));
		Emit(o, WriteShortLE(e.StartingCondition));
	}

	private void WriteRow15(MemoryStream o, MissionOrder22 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.VariantKey));
		Emit(o, WriteShortLE(e.CompoundConditionPartner));
		Emit(o, WriteShortLE(e.Verb));
		Emit(o, WriteShortLE(e.FormationId));
		Emit(o, WriteShortLE(e.PointRef));
		Emit(o, WriteShortLE(e.RouteRef));
		Emit(o, WriteShortLE(e.SubjectKind));
		Emit(o, WriteShortLE(e.SubjectRef));
		Emit(o, WriteShortLE(e.ActionRef));
	}

	private void WriteRow16(MemoryStream o, MissionGroup164 e) {
		Emit(o, WriteShortLE(e.GUID));
		Emit(o, WriteShortLE(e.ConditionRef));
		Emit(o, WriteShortLE(e.CompoundConditionPartner));
		Emit(o, WriteShortLE(e.PaintsGround));
		Emit(o, WriteShortLE(e.NearConstant));
		Emit(o, WriteShortLESegment(e.DeadZone));
		Emit(o, WriteShortLE(e.MemberKind));
		Emit(o, WriteShortLE(e.FormationId));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, WriteShortLE(e.RouteRef));
		Emit(o, WriteShortLESegment(e.MemberRefs));
		Emit(o, WriteShortLESegment(e.OrderRefs));
		Emit(o, WriteShortLE(e.Side));
		Emit(o, WriteShortLE(e.DeploymentActionRef));
		Emit(o, WriteShortLE(e.PairCount));
		Emit(o, WriteShortLESegment(e.OutOfActionReport));
		Emit(o, WriteShortLE(e.MapShown));
	}

	private void WriteRow17(MemoryStream outStream, MissionFile data) {
		var entries = data.Objectives!;
		Emit(outStream, WriteShortLE((short)entries.Length));

		foreach (var e in entries) {
			if (e == null) {
				// The file ended inside this record: write back the bytes it had.
				if (data.TruncatedRow17Tail != null) {
					Emit(outStream, data.TruncatedRow17Tail);
				}
				continue;
			}

			Emit(outStream, WriteShortLE(e.ConditionRef));
			Emit(outStream, WriteShortLE(e.Required));
			Emit(outStream, WriteShortLE(e.ConditionCode));
			Emit(outStream, WriteShortLE(e.SubjectKind));
			Emit(outStream, WriteShortLE(e.SubjectRef));
			Emit(outStream, WriteShortLE(e.PointRef));
			Emit(outStream, WriteShortLE(e.RouteRef));
			Emit(outStream, WriteShortLE(e.TextRef));
			Emit(outStream, WriteShortLE(e.PairCount));

			foreach (var pair in e.Pairs) {
				Emit(outStream, WriteShortLE(pair.CounterRef));
				Emit(outStream, WriteShortLE(pair.Op));
			}
		}
	}

	private static void Emit(MemoryStream outArr, byte[] data) {
		outArr.Write(data, 0, data.Length);
	}
}
