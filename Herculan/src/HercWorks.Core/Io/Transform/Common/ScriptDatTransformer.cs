using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Transforms byte[] data to and from <c>data\script.dat</c> (<see cref="ScriptDat"/>): the 20-byte
/// header and the 13 count-prefixed blocks, stopping at block 13's end — bytes past it are a
/// longer earlier mission's leftovers, and a write does not pad to any fixed length. See
/// docs/retail/formats/script-dat.md.
/// </summary>
public class ScriptDatTransformer : ByteTransformer<ScriptDat> {
	public override ScriptDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		var data = new ScriptDat();

		SetBytes(inputArray);

		data.HeaderBytes = IndexSegment(20);

		data.Coordinates = ReadArray(ParseCoordinate);
		data.Headings = ReadArray(ParseHeading);
		data.WaypointGroups = ReadArray(ParseWaypointGroup);
		data.TriggerAreas = ReadArray(ParseTriggerArea);
		data.Actions = ReadArray(ParseAction);
		data.ActionTimers = ReadArray(ParseActionTimer);
		data.Mechs = ReadArray(ParseMech);
		data.Flyers = ReadArray(ParseFlyer);
		data.Bases = ReadArray(ParseBase);
		data.Orders = ReadArray(ParseOrder);
		data.Groups = ReadArray(ParseGroup);
		data.Objectives = ReadArray(ParseObjective);

		int objectiveCount = IndexShortLE();
		data.ObjectiveTextRefs = IndexShortLEArray(objectiveCount);

		return data;
	}

	private T[] ReadArray<T>(Func<T> parseOne) {
		var arr = new T[IndexShortLE()];
		for (int i = 0; i < arr.Length; i++) {
			arr[i] = parseOne();
		}
		return arr;
	}

	// ---- Block 1: ScriptCoordinate (12 bytes) -----------------------------------------------

	private ScriptCoordinate ParseCoordinate() => new() {
		X = IndexIntLE(),
		Y = IndexIntLE(),
		Z = IndexIntLE()
	};

	// ---- Block 2: ScriptHeading (2 bytes) ----------------------------------------------------

	private ScriptHeading ParseHeading() => new() { Degrees = IndexShortLE() };

	// ---- Block 3: ScriptWaypointGroup (variable) ---------------------------------------------

	private ScriptWaypointGroup ParseWaypointGroup() {
		int count = IndexShortLE();
		return new ScriptWaypointGroup { Waypoints = IndexShortLEArray(count) };
	}

	// ---- Block 4: ScriptTriggerArea (6 bytes) -----------------------------------------------

	private ScriptTriggerArea ParseTriggerArea() => new() {
		Shape = IndexShortLE(),
		PointRef = IndexShortLE(),
		SecondPointOrRadius = IndexShortLE()
	};

	// ---- Block 5: ScriptAction (74 bytes) ------------------------------------------------------

	private ScriptAction ParseAction() => new() {
		Type = IndexShortLE(),
		Verb = IndexShortLE(),
		AreaRefs = IndexShortLEArray(8),
		CounterRefs = IndexShortLEArray(10),
		CounterOps = IndexShortLEArray(10),
		TextRefs = IndexShortLEArray(5),
		MessageId = IndexShortLE(),
		TargetRef = IndexShortLE()
	};

	// ---- Block 6: ScriptActionTimer (24 bytes) --------------------------------------------------

	private ScriptActionTimer ParseActionTimer() => new() {
		PrimaryActionRef = IndexShortLE(),
		Delay = IndexShortLE(),
		SequenceRefs = IndexShortLEArray(10)
	};

	// ---- Block 7: ScriptMechRecord (134 bytes) ------------------------------------------

	private ScriptMechRecord ParseMech() => new() {
		HeadBytes = IndexSegment(40),
		TypeIndex = IndexShortLE(),
		WeaponRefs = IndexShortLEArray(10),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		TailBytes = IndexSegment(68)
	};

	// ---- Block 8: ScriptFlyerRecord (92 bytes) ---------------------------------------------

	private ScriptFlyerRecord ParseFlyer() => new() {
		HeadBytes = IndexSegment(40),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		TypeIndex = IndexShortLE(),
		TailBytes = IndexSegment(46)
	};

	// ---- Block 9: ScriptBaseRecord (52 bytes) --------------------------------------------

	private ScriptBaseRecord ParseBase() => new() {
		TypeIndex = IndexShortLE(),
		PositionRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		TailBytes = IndexSegment(46)
	};

	// ---- Block 10: ScriptOrder (14 bytes) ------------------------------------------

	private ScriptOrder ParseOrder() => new() {
		Verb = IndexShortLE(),
		FormationId = IndexShortLE(),
		PointRef = IndexShortLE(),
		RouteRef = IndexShortLE(),
		SubjectKind = IndexShortLE(),
		SubjectRef = IndexShortLE(),
		ActionRef = IndexShortLE()
	};

	// ---- Block 11: ScriptGroup (156 bytes) -------------------------------------------

	private ScriptGroup ParseGroup() => new() {
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
		CounterRefs = IndexShortLEArray(10),
		CounterOps = IndexShortLEArray(10),
		MapShown = IndexShortLE()
	};

	// ---- Block 12: ScriptObjective (54 bytes) ------------------------------------------

	private ScriptObjective ParseObjective() => new() {
		Required = IndexShortLE(),
		ConditionCode = IndexShortLE(),
		SubjectKind = IndexShortLE(),
		SubjectRef = IndexShortLE(),
		PointRef = IndexShortLE(),
		RouteRef = IndexShortLE(),
		TextRef = IndexShortLE(),
		CounterRefs = IndexShortLEArray(10),
		CounterOps = IndexShortLEArray(10)
	};

	// ==============================================================================================
	// Write path
	// ==============================================================================================

	public override byte[]? Write(ScriptDat data) {
		using var outStream = new MemoryStream();

		Emit(outStream, data.HeaderBytes);

		WriteArray(outStream, data.Coordinates, WriteCoordinate);
		WriteArray(outStream, data.Headings, WriteHeading);
		WriteArray(outStream, data.WaypointGroups, WriteWaypointGroup);
		WriteArray(outStream, data.TriggerAreas, WriteTriggerArea);
		WriteArray(outStream, data.Actions, WriteAction);
		WriteArray(outStream, data.ActionTimers, WriteActionTimer);
		WriteArray(outStream, data.Mechs, WriteMech);
		WriteArray(outStream, data.Flyers, WriteFlyer);
		WriteArray(outStream, data.Bases, WriteBase);
		WriteArray(outStream, data.Orders, WriteOrder);
		WriteArray(outStream, data.Groups, WriteGroup);
		WriteArray(outStream, data.Objectives, WriteObjective);

		Emit(outStream, WriteShortLE((short)data.ObjectiveTextRefs.Length));
		Emit(outStream, WriteShortLESegment(data.ObjectiveTextRefs));

		return outStream.ToArray();
	}

	private void WriteArray<T>(MemoryStream outStream, T[] items, Action<MemoryStream, T> writeOne) {
		Emit(outStream, WriteShortLE((short)items.Length));
		foreach (var item in items) {
			writeOne(outStream, item);
		}
	}

	private void WriteCoordinate(MemoryStream o, ScriptCoordinate e) {
		Emit(o, WriteIntLE(e.X));
		Emit(o, WriteIntLE(e.Y));
		Emit(o, WriteIntLE(e.Z));
	}

	private void WriteHeading(MemoryStream o, ScriptHeading e) {
		Emit(o, WriteShortLE(e.Degrees));
	}

	private void WriteWaypointGroup(MemoryStream o, ScriptWaypointGroup e) {
		Emit(o, WriteShortLE((short)e.Waypoints.Length));
		Emit(o, WriteShortLESegment(e.Waypoints));
	}

	private void WriteTriggerArea(MemoryStream o, ScriptTriggerArea e) {
		Emit(o, WriteShortLE(e.Shape));
		Emit(o, WriteShortLE(e.PointRef));
		Emit(o, WriteShortLE(e.SecondPointOrRadius));
	}

	private void WriteAction(MemoryStream o, ScriptAction e) {
		Emit(o, WriteShortLE(e.Type));
		Emit(o, WriteShortLE(e.Verb));
		Emit(o, WriteShortLESegment(e.AreaRefs));
		Emit(o, WriteShortLESegment(e.CounterRefs));
		Emit(o, WriteShortLESegment(e.CounterOps));
		Emit(o, WriteShortLESegment(e.TextRefs));
		Emit(o, WriteShortLE(e.MessageId));
		Emit(o, WriteShortLE(e.TargetRef));
	}

	private void WriteActionTimer(MemoryStream o, ScriptActionTimer e) {
		Emit(o, WriteShortLE(e.PrimaryActionRef));
		Emit(o, WriteShortLE(e.Delay));
		Emit(o, WriteShortLESegment(e.SequenceRefs));
	}

	private void WriteMech(MemoryStream o, ScriptMechRecord e) {
		Emit(o, e.HeadBytes);
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, WriteShortLESegment(e.WeaponRefs));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, e.TailBytes);
	}

	private void WriteFlyer(MemoryStream o, ScriptFlyerRecord e) {
		Emit(o, e.HeadBytes);
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, e.TailBytes);
	}

	private void WriteBase(MemoryStream o, ScriptBaseRecord e) {
		Emit(o, WriteShortLE(e.TypeIndex));
		Emit(o, WriteShortLE(e.PositionRef));
		Emit(o, WriteShortLE(e.HeadingRef));
		Emit(o, e.TailBytes);
	}

	private void WriteOrder(MemoryStream o, ScriptOrder e) {
		Emit(o, WriteShortLE(e.Verb));
		Emit(o, WriteShortLE(e.FormationId));
		Emit(o, WriteShortLE(e.PointRef));
		Emit(o, WriteShortLE(e.RouteRef));
		Emit(o, WriteShortLE(e.SubjectKind));
		Emit(o, WriteShortLE(e.SubjectRef));
		Emit(o, WriteShortLE(e.ActionRef));
	}

	private void WriteGroup(MemoryStream o, ScriptGroup e) {
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
		Emit(o, WriteShortLESegment(e.CounterRefs));
		Emit(o, WriteShortLESegment(e.CounterOps));
		Emit(o, WriteShortLE(e.MapShown));
	}

	private void WriteObjective(MemoryStream o, ScriptObjective e) {
		Emit(o, WriteShortLE(e.Required));
		Emit(o, WriteShortLE(e.ConditionCode));
		Emit(o, WriteShortLE(e.SubjectKind));
		Emit(o, WriteShortLE(e.SubjectRef));
		Emit(o, WriteShortLE(e.PointRef));
		Emit(o, WriteShortLE(e.RouteRef));
		Emit(o, WriteShortLE(e.TextRef));
		Emit(o, WriteShortLESegment(e.CounterRefs));
		Emit(o, WriteShortLESegment(e.CounterOps));
	}

	private static void Emit(MemoryStream outArr, byte[] data) {
		outArr.Write(data, 0, data.Length);
	}
}
