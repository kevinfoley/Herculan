using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Transforms byte[] data to and from ES1's .MSN mission files (<see cref="Es1MissionFile"/>): the
/// revision word, then the eleven rows in the order ES1's loader reads them
/// (docs/es1/formats/msn-mission-file.md#the-layout). Writing a parsed file gives back its bytes.
///
/// <para>A file that does not follow that layout is read in the other layout
/// (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout), in which <c>B0_C1_20.MSN</c> and
/// <c>B0_C1_30.MSN</c> stand: ten header words in place of the header-patch row, after the revision, and
/// 88-byte mechs. Writing gives back the layout the file was read in. A file that follows neither throws
/// <see cref="InvalidDataException"/>: a row whose count is negative or whose records cannot fit the
/// bytes left, or bytes left over after the groups. The loader itself tests none of this.</para>
/// </summary>
public class Es1MissionFileTransformer : ByteTransformer<Es1MissionFile> {
	private const int ConditionSize = 14;
	private const int HeaderPatchSize = 22;
	private const int HeaderWordCount = 10;
	private const int PointSize = 22;
	private const int HeadingSize = 10;
	private const int WaypointGroupFixedSize = 10;
	private const int TriggerAreaSize = 12;
	private const int ActionSize = 30;
	private const int MechSize = 106;
	private const int OtherLayoutMechSize = 88;
	private const int FlyerSize = 64;
	private const int BaseSize = 24;
	private const int GroupSize = 88;

	/// <exception cref="InvalidDataException">The file follows neither layout.</exception>
	public override Es1MissionFile? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		try {
			return ParseLayout(inputArray, otherLayout: false);
		} catch (InvalidDataException loaderLayout) {
			try {
				return ParseLayout(inputArray, otherLayout: true);
			} catch (InvalidDataException otherLayout) {
				throw new InvalidDataException($"{loaderLayout.Message} In the other layout: {otherLayout.Message}", loaderLayout);
			}
		}
	}

	private Es1MissionFile ParseLayout(byte[] inputArray, bool otherLayout) {
		SetBytes(inputArray);
		Require(2, "the revision word");

		var data = new Es1MissionFile { Revision = IndexShortLE() };
		if (otherLayout) {
			Require(HeaderWordCount * 2, "the header words");
			data.HeaderWords = IndexShortLEArray(HeaderWordCount);
		}

		data.Conditions = ReadRow("conditions", ConditionSize, ParseCondition);
		if (!otherLayout) {
			data.HeaderPatches = ReadRow("header patches", HeaderPatchSize, ParseHeaderPatch);
		}

		data.Points = ReadRow("points", PointSize, ParsePoint);
		data.Headings = ReadRow("headings", HeadingSize, ParseHeading);
		data.WaypointGroups = ReadRow("waypoint groups", WaypointGroupFixedSize, ParseWaypointGroup);
		data.TriggerAreas = ReadRow("trigger areas", TriggerAreaSize, ParseTriggerArea);
		data.Actions = ReadRow("actions", ActionSize, ParseAction);
		data.Mechs = otherLayout
			? ReadRow("mechs", OtherLayoutMechSize, () => ParseMech(1))
			: ReadRow("mechs", MechSize, () => ParseMech(10));
		data.Flyers = ReadRow("flyers", FlyerSize, ParseFlyer);
		data.Bases = ReadRow("bases", BaseSize, ParseBase);
		data.Groups = ReadRow("groups", GroupSize, ParseGroup);

		int left = GetBytes().Length - Index;
		if (left != 0) {
			throw new InvalidDataException($"{left} bytes follow the groups at 0x{Index:x}; the layout ends there.");
		}

		return data;
	}

	/// <summary>
	/// Reads a row's count and its records. <paramref name="minSize"/> is a record's size, or for
	/// waypoint groups its fixed part, whose waypoints <see cref="ParseWaypointGroup"/> checks.
	/// </summary>
	private T[] ReadRow<T>(string row, int minSize, Func<T> parseOne) {
		Require(2, $"the {row}' count");
		int at = Index;
		int count = IndexShortLE();
		int left = GetBytes().Length - Index;
		if (count < 0 || (long)count * minSize > left) {
			throw new InvalidDataException(
				$"The {row}' count at 0x{at:x}, {count}, of {minSize}-byte records does not fit the {left} bytes left; not the loader's layout.");
		}

		var records = new T[count];
		for (int i = 0; i < count; i++) {
			records[i] = parseOne();
		}

		return records;
	}

	private void Require(int bytes, string what) {
		if (GetBytes().Length - Index < bytes) {
			throw new InvalidDataException($"The file ends at 0x{GetBytes().Length:x}, inside {what}.");
		}
	}

	private Es1Condition ParseCondition() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Type = IndexShortLE(),
		FlagIndexOrRangeLower = IndexShortLE(),
		OperatorOrRangeUpperOrResult = IndexShortLE(),
		ComparisonOperand = IndexShortLE(),
		LatestDraw = IndexShortLE(),
	};

	private Es1HeaderPatch ParseHeaderPatch() => new() {
		ConditionRef = IndexShortLE(),
		HeaderWords = IndexShortLEArray(10),
	};

	private Es1Point ParsePoint() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Word06 = IndexShortLE(),
		SumFlag = IndexShortLE(),
		X = IndexIntLE(),
		Y = IndexIntLE(),
		Z = IndexIntLE(),
	};

	private Es1Heading ParseHeading() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Word06 = IndexShortLE(),
		Value = IndexShortLE(),
	};

	private Es1WaypointGroup ParseWaypointGroup() {
		Require(WaypointGroupFixedSize, "a waypoint group");
		var group = new Es1WaypointGroup {
			GUID = IndexShortLE(),
			ConditionRef = IndexShortLE(),
			VariantKey = IndexShortLE(),
			Word06 = IndexShortLE(),
		};

		int at = Index;
		int count = IndexShortLE();
		if (count < 0 || (long)count * 2 > GetBytes().Length - Index) {
			throw new InvalidDataException($"The waypoint count at 0x{at:x}, {count}, does not fit the bytes left; not the loader's layout.");
		}

		group.Waypoints = IndexShortLEArray(count);
		return group;
	}

	private Es1TriggerArea ParseTriggerArea() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Word04 = IndexShortLE(),
		Shape = IndexShortLE(),
		PointRef = IndexShortLE(),
		Word0A = IndexShortLE(),
	};

	private Es1Action ParseAction() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Word04 = IndexShortLE(),
		Type = IndexShortLE(),
		Verb = IndexShortLE(),
		AreaRefs = IndexShortLEArray(8),
		Word1A = IndexShortLE(),
		Word1C = IndexShortLE(),
	};

	/// <param name="words4E">The words at 0x4E: ten, or one in the other layout.</param>
	private Es1Mech ParseMech(int words4E) => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Word06 = IndexShortLE(),
		Words08 = IndexShortLEArray(20),
		Type = IndexShortLE(),
		Words32 = IndexShortLEArray(10),
		PointRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		Word4A = IndexShortLE(),
		Word4C = IndexShortLE(),
		Words4E = IndexShortLEArray(words4E),
		Word62 = IndexShortLE(),
		ActionRef64 = IndexShortLE(),
		ActionRef66 = IndexShortLE(),
		Word68 = IndexShortLE(),
	};

	private Es1Flyer ParseFlyer() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Word06 = IndexShortLE(),
		Words08 = IndexShortLEArray(20),
		PointRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		Type = IndexShortLE(),
		Word36 = IndexShortLE(),
		Word38 = IndexShortLE(),
		ActionRef3A = IndexShortLE(),
		ActionRef3C = IndexShortLE(),
		Word3E = IndexShortLE(),
	};

	private Es1Base ParseBase() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		VariantKey = IndexShortLE(),
		Word06 = IndexShortLE(),
		Type = IndexShortLE(),
		PointRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		Word0E = IndexShortLE(),
		Word10 = IndexShortLE(),
		ActionRef12 = IndexShortLE(),
		ActionRef14 = IndexShortLE(),
		Word16 = IndexShortLE(),
	};

	private Es1Group ParseGroup() => new() {
		GUID = IndexShortLE(),
		ConditionRef = IndexShortLE(),
		Word04 = IndexShortLE(),
		Words06 = IndexShortLEArray(20),
		MemberKind = IndexShortLE(),
		Word30 = IndexShortLE(),
		Word32 = IndexShortLE(),
		PointRef = IndexShortLE(),
		HeadingRef = IndexShortLE(),
		WaypointGroupRef = IndexShortLE(),
		MemberRefs = IndexShortLEArray(8),
		Word4A = IndexShortLE(),
		Ref4ERow = IndexShortLE(),
		Ref4E = IndexShortLE(),
		ActionRef = IndexShortLE(),
		Word52 = IndexShortLE(),
		Word54 = IndexShortLE(),
		Word56 = IndexShortLE(),
	};

	// ==========================================================================================
	// Write path
	// ==========================================================================================

	public override byte[]? Write(Es1MissionFile data) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);

		w.Write(data.Revision);
		if (data.HeaderWords != null) {
			Words(w, data.HeaderWords);
		}

		WriteRow(w, data.Conditions, (w, e) => Words(w, e.GUID, e.ConditionRef, e.Type, e.FlagIndexOrRangeLower,
			e.OperatorOrRangeUpperOrResult, e.ComparisonOperand, e.LatestDraw));
		if (data.HeaderWords == null) {
			WriteRow(w, data.HeaderPatches, (w, e) => {
				w.Write(e.ConditionRef);
				Words(w, e.HeaderWords);
			});
		}
		WriteRow(w, data.Points, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06, e.SumFlag);
			w.Write(e.X);
			w.Write(e.Y);
			w.Write(e.Z);
		});
		WriteRow(w, data.Headings, (w, e) => Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06, e.Value));
		WriteRow(w, data.WaypointGroups, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06, (short)e.Waypoints.Length);
			Words(w, e.Waypoints);
		});
		WriteRow(w, data.TriggerAreas, (w, e) => Words(w, e.GUID, e.ConditionRef, e.Word04, e.Shape, e.PointRef, e.Word0A));
		WriteRow(w, data.Actions, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.Word04, e.Type, e.Verb);
			Words(w, e.AreaRefs);
			Words(w, e.Word1A, e.Word1C);
		});
		WriteRow(w, data.Mechs, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06);
			Words(w, e.Words08);
			w.Write(e.Type);
			Words(w, e.Words32);
			Words(w, e.PointRef, e.HeadingRef, e.Word4A, e.Word4C);
			Words(w, e.Words4E);
			Words(w, e.Word62, e.ActionRef64, e.ActionRef66, e.Word68);
		});
		WriteRow(w, data.Flyers, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06);
			Words(w, e.Words08);
			Words(w, e.PointRef, e.HeadingRef, e.Type, e.Word36, e.Word38, e.ActionRef3A, e.ActionRef3C, e.Word3E);
		});
		WriteRow(w, data.Bases, (w, e) => Words(w, e.GUID, e.ConditionRef, e.VariantKey, e.Word06, e.Type, e.PointRef,
			e.HeadingRef, e.Word0E, e.Word10, e.ActionRef12, e.ActionRef14, e.Word16));
		WriteRow(w, data.Groups, (w, e) => {
			Words(w, e.GUID, e.ConditionRef, e.Word04);
			Words(w, e.Words06);
			Words(w, e.MemberKind, e.Word30, e.Word32, e.PointRef, e.HeadingRef, e.WaypointGroupRef);
			Words(w, e.MemberRefs);
			Words(w, e.Word4A, e.Ref4ERow, e.Ref4E, e.ActionRef, e.Word52, e.Word54, e.Word56);
		});

		w.Flush();
		return o.ToArray();
	}

	private static void WriteRow<T>(BinaryWriter w, T[] records, Action<BinaryWriter, T> writeOne) {
		w.Write((short)records.Length);
		foreach (var record in records) {
			writeOne(w, record);
		}
	}

	private static void Words(BinaryWriter w, params short[] words) {
		foreach (short word in words) {
			w.Write(word);
		}
	}
}
