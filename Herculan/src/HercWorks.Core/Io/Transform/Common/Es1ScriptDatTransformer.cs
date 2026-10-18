using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's <c>data\script.dat</c> (<see cref="Es1ScriptDat"/>): the header and the nine blocks at the strides
/// docs/es1/formats/script-dat.md#writing gives, then whatever follows, kept as <see cref="Es1ScriptDat.Tail"/>. Writing a
/// parsed file gives back its bytes.
///
/// <para>A file that ends inside the header or a block throws <see cref="InvalidDataException"/>, as does a negative count;
/// the simulator's reader tests neither.</para>
/// </summary>
public class Es1ScriptDatTransformer : ByteTransformer<Es1ScriptDat> {
	/// <exception cref="InvalidDataException">The file ends inside the header or a block, or a count is negative.</exception>
	public override Es1ScriptDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		Need(Es1ScriptDat.HeaderLength * 2, "the header");
		var file = new Es1ScriptDat { Header = IndexShortLEArray(Es1ScriptDat.HeaderLength) };
		file.Points = Block("points", 12, () => new Es1ScriptPoint { X = IndexIntLE(), Y = IndexIntLE(), Z = IndexIntLE() });
		file.Headings = Block("headings", 2, IndexShortLE);
		file.WaypointGroups = Block("waypoint groups", 2, () => {
			int count = IndexShortLE();
			if (count < 0) {
				throw new InvalidDataException($"A waypoint count at 0x{Index - 2:x} is {count}.");
			}

			Need(count * 2, "a waypoint group");
			return IndexShortLEArray(count);
		});
		file.TriggerAreas = Block("trigger areas", 6, () => new Es1ScriptTriggerArea {
			Shape = IndexShortLE(), PointRef = IndexShortLE(), Word0A = IndexShortLE(),
		});
		file.Actions = Block("actions", 24, () => new Es1ScriptAction {
			Type = IndexShortLE(), Verb = IndexShortLE(), AreaRefs = IndexShortLEArray(8), Word1A = IndexShortLE(),
			Word1C = IndexShortLE(),
		});
		file.Mechs = Block("mechs", 98, () => new Es1ScriptMech {
			Words08 = IndexShortLEArray(20), Type = IndexShortLE(), Words32 = IndexShortLEArray(10), PointRef = IndexShortLE(),
			HeadingRef = IndexShortLE(), Word4A = IndexShortLE(), Word4C = IndexShortLE(), Words4E = IndexShortLEArray(10),
			Word62 = IndexShortLE(), ActionRef64 = IndexShortLE(), ActionRef66 = IndexShortLE(), Word68 = IndexShortLE(),
		});
		file.Flyers = Block("flyers", 56, () => new Es1ScriptFlyer {
			Words08 = IndexShortLEArray(20), PointRef = IndexShortLE(), HeadingRef = IndexShortLE(), Type = IndexShortLE(),
			Word36 = IndexShortLE(), Word38 = IndexShortLE(), ActionRef3A = IndexShortLE(), ActionRef3C = IndexShortLE(),
			Word3E = IndexShortLE(),
		});
		file.Bases = Block("bases", 16, () => new Es1ScriptBase {
			Type = IndexShortLE(), PointRef = IndexShortLE(), HeadingRef = IndexShortLE(), Word0E = IndexShortLE(),
			Word10 = IndexShortLE(), ActionRef12 = IndexShortLE(), ActionRef14 = IndexShortLE(), Word16 = IndexShortLE(),
		});
		file.Groups = Block("groups", 82, () => new Es1ScriptGroup {
			Words06 = IndexShortLEArray(20), MemberKind = IndexShortLE(), Word30 = IndexShortLE(), Word32 = IndexShortLE(),
			PointRef = IndexShortLE(), HeadingRef = IndexShortLE(), WaypointGroupRef = IndexShortLE(),
			MemberRefs = IndexShortLEArray(8), Word4A = IndexShortLE(), Ref4ERow = IndexShortLE(), Ref4E = IndexShortLE(),
			ActionRef = IndexShortLE(), Word52 = IndexShortLE(), Word54 = IndexShortLE(), Word56 = IndexShortLE(),
		});
		file.Tail = inputArray[Index..];
		return file;
	}

	/// <summary>A block's count and records; <paramref name="size"/> is a record's size, or a waypoint group's fixed part.</summary>
	private T[] Block<T>(string block, int size, Func<T> readOne) {
		Need(2, $"the {block}' count");
		int count = IndexShortLE();
		if (count < 0) {
			throw new InvalidDataException($"The {block}' count at 0x{Index - 2:x} is {count}.");
		}

		var records = new T[count];
		for (int i = 0; i < count; i++) {
			Need(size, $"the {block}");
			records[i] = readOne();
		}

		return records;
	}

	private void Need(int bytes, string what) {
		if (GetBytes().Length - Index < bytes) {
			throw new InvalidDataException($"The file ends at 0x{GetBytes().Length:x}, inside {what}.");
		}
	}

	public override byte[]? Write(Es1ScriptDat data) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		Words(w, data.Header);
		Block(w, data.Points, (w, e) => {
			w.Write(e.X);
			w.Write(e.Y);
			w.Write(e.Z);
		});
		Block(w, data.Headings, (w, e) => w.Write(e));
		Block(w, data.WaypointGroups, (w, e) => {
			w.Write(checked((short)e.Length));
			Words(w, e);
		});
		Block(w, data.TriggerAreas, (w, e) => Words(w, e.Shape, e.PointRef, e.Word0A));
		Block(w, data.Actions, (w, e) => {
			Words(w, e.Type, e.Verb);
			Words(w, e.AreaRefs);
			Words(w, e.Word1A, e.Word1C);
		});
		Block(w, data.Mechs, (w, e) => {
			Words(w, e.Words08);
			w.Write(e.Type);
			Words(w, e.Words32);
			Words(w, e.PointRef, e.HeadingRef, e.Word4A, e.Word4C);
			Words(w, e.Words4E);
			Words(w, e.Word62, e.ActionRef64, e.ActionRef66, e.Word68);
		});
		Block(w, data.Flyers, (w, e) => {
			Words(w, e.Words08);
			Words(w, e.PointRef, e.HeadingRef, e.Type, e.Word36, e.Word38, e.ActionRef3A, e.ActionRef3C, e.Word3E);
		});
		Block(w, data.Bases, (w, e) => Words(w, e.Type, e.PointRef, e.HeadingRef, e.Word0E, e.Word10, e.ActionRef12,
			e.ActionRef14, e.Word16));
		Block(w, data.Groups, (w, e) => {
			Words(w, e.Words06);
			Words(w, e.MemberKind, e.Word30, e.Word32, e.PointRef, e.HeadingRef, e.WaypointGroupRef);
			Words(w, e.MemberRefs);
			Words(w, e.Word4A, e.Ref4ERow, e.Ref4E, e.ActionRef, e.Word52, e.Word54, e.Word56);
		});
		w.Write(data.Tail);
		w.Flush();
		return o.ToArray();
	}

	private static void Block<T>(BinaryWriter w, T[] records, Action<BinaryWriter, T> writeOne) {
		w.Write(checked((short)records.Length));
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
