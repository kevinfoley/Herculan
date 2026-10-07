using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads <c>dgs\BASES.DGS</c> (and the structurally-identical <c>BHULKS.DGS</c>) — the static
/// structure shape library <c>dat\BASES.DAT</c>'s <c>ShapeIndex</c> field selects into for the 57
/// of 65 structure types that aren't in <c>dts\BASES_AN.DTS</c>.
///
/// <para><b>RE summary</b> (DBSIM.EXE, see docs/retail/formats/dgs-hd0-notes.md for the full derivation).
/// <c>BaseType_LoadShape</c> (<c>00405ebc</c>) resolves a shape by index through
/// <c>BaseType_ResolveShape</c> (<c>00474cd8</c>), which reopens <c>dgs\bases</c> and calls the generic polymorphic resource
/// loader <c>ClassItem_LoadResource</c> (<c>0047a038</c>) once per record until it reaches the
/// requested index — i.e. the file is a flat, sequential list of tagged records, not a
/// random-access table. Each record's 8-byte header is <c>[classId:int32][payloadSize:int32]</c>
/// (little-endian); the type code DBSIM passes for this library, <c>0x02BC0001</c>, is exactly
/// the record's own leading 4 bytes on disk. That it resembles <c>recordSize&lt;&lt;16|version</c>
/// is a coincidence, not the container scheme.</para>
///
/// <para><b>Record layout</b>, traced through the class's chain of base-class reads
/// (<c>GridShape_ReadFromStream</c> (<c>0042762c</c>) → <c>TSShape_ReadFromStream</c> (<c>00490d5c</c>) → <c>TSPartList_ReadFromStream</c> (<c>0048fd94</c>) → <c>TSPartBase_ReadFromStream</c> (<c>0048f894</c>)) and
/// verified byte-exact against the retail file (see below):</para>
/// <list type="bullet">
/// <item>3 <c>int16</c> id/name fields, then 6 raw bytes (base class header, unmodelled beyond
/// <see cref="GridShape.BoundingRadius"/>, the third field).</item>
/// <item>an <c>int16</c> child count, then that many recursively-loaded <c>ClassItem</c> objects.
/// <b>Every retail record's one child is an ordinary TSObjectHeader-family chunk</b> (observed tag
/// <c>0x0014000c</c> = <c>TSDetailPart</c>) — byte-identical to a plain <c>.DTS</c> file's own
/// chunk format, so it is read with <see cref="DTSModelTransformer.ReadOneObject"/> rather than a
/// new reader. This is the key finding that makes the format usable: no new mesh format, just a
/// new envelope around the existing one.</item>
/// <item>the rest of the <c>TSShape</c>: a node-transform count, a sequence count, the
/// per-sequence frame counts and the 32-byte node transforms, read by
/// <see cref="DTSModelTransformer.ReadShapeTail"/> into <see cref="GridShape.SequenceList"/> and
/// <see cref="GridShape.NodeTransforms"/>.</item>
/// <item>the shape's collision volume — 5 <c>int16</c> scalars, a fixed 1024-byte height table,
/// then one row of height codes per grid row. <b>This is the part that invites a plausible
/// misreading</b>: walking the tail as "a sub-record size, a sub-record count, three undecoded
/// scalars, an opaque block, then count × size raw bytes" consumes exactly the same bytes, so it
/// parses every retail record correctly while naming all of it wrongly. It is the grid
/// <c>GridShape_ReadFromStream</c> (<c>0042762c</c>) reads and the ray-versus-structure query
/// walks — see <see cref="GridShapeCollision"/>.</item>
/// </list>
///
/// <para><b>Padding.</b> Every record's total on-disk footprint (8-byte header + payload) is
/// padded to an even byte count — confirmed by an independent whole-file scan for the
/// <c>0x02BC0001</c> tag pattern against the retail file: 44 of 45 records have an even
/// <c>payloadSize</c> and need no pad; the one record with an odd <c>payloadSize</c> (its
/// sub-records happen to be an odd byte size) has exactly one pad byte before the next record's
/// header, landing exactly on the next real tag.</para>
///
/// <para><b>Verified against the retail file</b> (<c>simvol0/dgs/BASES.DGS</c>, 565882 content
/// bytes): all 45 records parse with this record shape (an independent tag-pattern scan finds the
/// same 45 offsets the sequential reader does), every embedded child parses through
/// <see cref="DTSModelTransformer"/> with zero exceptions, and the resulting geometry is
/// substantial (1536 groups, 8978 polys total across all 45 shapes) — not degenerate placeholder
/// data.</para>
/// </summary>
public class BasesDgsTransformer : ByteTransformer<GridShapeLibrary> {
	/// <summary>The record header's classId, and the record's own leading 4 on-disk bytes.</summary>
	private const int ShapeTag = 0x02BC0001;

	/// <summary><c>[classId:int32][payloadSize:int32]</c>.</summary>
	private const int RecordHeaderLength = 8;

	/// <summary>
	/// Entries in a collision volume's height table — 256 <c>int32</c>s, one per byte code a grid
	/// cell can hold, which is the 1024-byte block the original reads in one call.
	/// </summary>
	private const int HeightTableEntries = 256;

	public override GridShapeLibrary? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		var dtsReader = new DTSModelTransformer();
		var shapes = new List<GridShape>();

		while (Index + RecordHeaderLength <= inputArray.Length) {
			int recordStart = Index;
			int tag = IndexIntLE();
			int payloadSize = IndexIntLE();

			if (payloadSize < 0 || (long)Index + payloadSize > inputArray.Length) {
				throw new InvalidDataException(
					$"BASES.DGS record at offset {recordStart} declares a {payloadSize}-byte " +
					$"payload, which does not fit the {inputArray.Length}-byte file.");
			}

			int payloadEnd = Index + payloadSize;

			shapes.Add(tag == ShapeTag ? ReadShape(dtsReader, payloadEnd) : SkipUnrecognized(payloadEnd));

			if (Index != payloadEnd) {
				throw new InvalidDataException(
					$"BASES.DGS record at offset {recordStart} (tag 0x{tag:x8}) declares " +
					$"{payloadSize} payload bytes but the reader consumed {Index - (recordStart + RecordHeaderLength)}" +
					$" — the record shape does not match this file.");
			}

			// Every record's on-disk footprint (header + payload) is padded to an even total.
			if ((payloadSize & 1) != 0) {
				Index += 1;
			}
		}

		return new GridShapeLibrary {
			Shapes = shapes.ToArray()
		};
	}

	/// <summary>Not a real record — reserved for a tag this library doesn't recognize, skipped by its own declared length rather than guessed at.</summary>
	private GridShape SkipUnrecognized(int payloadEnd) {
		Index = payloadEnd;
		return default;
	}

	private GridShape ReadShape(DTSModelTransformer dtsReader, int payloadEnd) {
		IndexShortLE(); // +4 -- unmodelled
		IndexShortLE(); // +6 -- unmodelled
		short boundingRadius = IndexShortLE(); // +8 -- see GridShape.BoundingRadius
		Index += 6; // unmodelled base-class raw fields

		short childCount = IndexShortLE();
		TSObject? geometry = null;
		for (int i = 0; i < childCount; i++) {
			byte[] bytes = GetBytes();
			int index = Index;
			var child = dtsReader.ReadOneObject(bytes, ref index);
			Index = index;
			geometry ??= child; // retail data has exactly one child; first one wins if that ever changes.
		}

		int tailIndex = Index;
		var (sequences, transforms) = dtsReader.ReadShapeTail(GetBytes(), ref tailIndex);
		Index = tailIndex;

		var collision = ReadCollision();

		if (Index > payloadEnd) {
			throw new InvalidDataException(
				$"BASES.DGS shape record overran its own payload by {Index - payloadEnd} bytes " +
				"-- the record shape does not match this file.");
		}

		return new GridShape(boundingRadius, geometry, sequences, transforms, collision);
	}

	/// <summary>
	/// The record's collision volume, exactly as <c>GridShape_ReadFromStream</c> reads it: five
	/// scalars, the 256-entry height table (always present, whatever the grid's size), and then one
	/// row of codes per grid row — the row loop is the one thing the original guards, on the row
	/// count alone.
	/// </summary>
	private GridShapeCollision ReadCollision() {
		short columns = IndexShortLE();     // +0x2a
		short rows = IndexShortLE();        // +0x2c
		short originColumn = IndexShortLE(); // +0x2e
		short originRow = IndexShortLE();   // +0x30
		short cellShift = IndexShortLE();   // +0x32

		int[] heights = IndexIntLEArray(HeightTableEntries);

		var cells = new byte[rows < 0 ? 0 : rows][];
		for (int row = 0; row < cells.Length; row++) {
			cells[row] = IndexSegment(columns);
		}

		return new GridShapeCollision(
			columns, rows, originColumn, originRow, cellShift, heights, cells);
	}

	public override byte[]? Write(GridShapeLibrary source) =>
		throw new NotSupportedException("BasesDgsTransformer is read-only -- the engine only draws structures, it never writes .DGS.");
}
