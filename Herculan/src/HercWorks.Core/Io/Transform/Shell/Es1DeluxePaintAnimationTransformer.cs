using System.Buffers.Binary;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>anm\*.ANM</c> (<see cref="Es1DeluxePaintAnimation"/>) at the offsets <c>Anm_Load</c>
/// (<c>ES1GO 00013aa8</c>) and <c>Anm_FindFrameRecord</c> (<c>00013d9e</c>) read them: the header, the page table at
/// <c>0x500</c>, and each page at <c>0xb00</c> + <c>0x10000</c> × its index, its records split by the page header's sizes.
///
/// <para>The player tests only the two ids; this reader also rejects a page or record that does not fit, which it could not
/// give back whole. Every page but the last must run the full <c>0x10000</c> bytes to the next.</para>
/// </summary>
public class Es1DeluxePaintAnimationTransformer : ByteTransformer<Es1DeluxePaintAnimation> {
	/// <exception cref="InvalidDataException">The pages or records do not fit the file.</exception>
	public override Es1DeluxePaintAnimation? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		byte[] b = inputArray;
		if (b.Length < Es1DeluxePaintAnimation.FirstPageOffset) {
			throw new InvalidDataException($"The file is {b.Length} bytes, shorter than its page table.");
		}

		var anm = new Es1DeluxePaintAnimation {
			Id = U32(b, 0x00),
			Unread04 = U16(b, 0x04),
			Unread08 = U32(b, 0x08),
			Unread0C = U16(b, 0x0c),
			Unread0E = U16(b, 0x0e),
			ContentType = U32(b, 0x10),
			Unread14 = U16(b, 0x14),
			Unread16 = U16(b, 0x16),
			Unread18 = b[0x18..0x1a],
			FrameCountReduction = b[0x1a],
			Unread1B = b[0x1b..0x40],
			FrameCount = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x40)),
			FramesPerSecond = U16(b, 0x44),
			Unread46 = b[0x46..0x80],
			Unread80 = b[0x80..0x100],
			Unread100 = b[0x100..0x500],
		};

		int pageCount = U16(b, 0x06);
		int tableEnd = Es1DeluxePaintAnimation.PageTableOffset + Es1DeluxePaintAnimation.PageTableEntryLength * pageCount;
		if (tableEnd > Es1DeluxePaintAnimation.FirstPageOffset) {
			throw new InvalidDataException($"{pageCount} pages overrun the page table.");
		}

		anm.UnreadPageTableTail = b[tableEnd..Es1DeluxePaintAnimation.FirstPageOffset];
		anm.Pages = new Es1AnimationPage[pageCount];
		for (int i = 0; i < pageCount; i++) {
			int entry = Es1DeluxePaintAnimation.PageTableOffset + Es1DeluxePaintAnimation.PageTableEntryLength * i;
			int start = Es1DeluxePaintAnimation.FirstPageOffset + Es1DeluxePaintAnimation.PageLength * i;
			int end = i == pageCount - 1 ? b.Length : start + Es1DeluxePaintAnimation.PageLength;
			if (end > b.Length || start + 8 > end) {
				throw new InvalidDataException($"Page {i} at {start} does not fit the file's {b.Length} bytes.");
			}

			var page = new Es1AnimationPage {
				TableFirstRecord = U16(b, entry),
				TableRecordCount = U16(b, entry + 2),
				TableUnread04 = U16(b, entry + 4),
				FirstRecord = U16(b, start),
				Unread04 = U16(b, start + 4),
				Unread06 = U16(b, start + 6),
			};
			int records = U16(b, start + 2);
			int at = start + 8 + 2 * records;
			if (at > end) {
				throw new InvalidDataException($"Page {i}'s {records} record sizes overrun it.");
			}

			page.Records = new Es1AnimationRecord[records];
			for (int r = 0; r < records; r++) {
				int size = U16(b, start + 8 + 2 * r);
				if (at + size > end) {
					throw new InvalidDataException($"Page {i}'s record {r} at {at}, {size} bytes, overruns the page.");
				}

				page.Records[r] = ReadRecord(b, at, size);
				at += size;
			}

			page.Padding = b[at..end];
			anm.Pages[i] = page;
		}

		if (pageCount == 0 && b.Length > Es1DeluxePaintAnimation.FirstPageOffset) {
			throw new InvalidDataException($"A file with no pages has {b.Length - Es1DeluxePaintAnimation.FirstPageOffset} bytes past its page table.");
		}

		return anm;
	}

	public override byte[]? Write(Es1DeluxePaintAnimation source) {
		int tableEnd = Es1DeluxePaintAnimation.PageTableOffset + Es1DeluxePaintAnimation.PageTableEntryLength * source.Pages.Length;
		if (source.Unread18.Length != 2 || source.Unread1B.Length != 0x25 || source.Unread46.Length != 0x3a
			|| source.Unread80.Length != 0x80 || source.Unread100.Length != 0x400
			|| tableEnd + source.UnreadPageTableTail.Length != Es1DeluxePaintAnimation.FirstPageOffset) {
			throw new InvalidDataException("The header's spans or the page table's tail have the wrong length.");
		}

		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Id);
		w.Write(source.Unread04);
		w.Write(checked((ushort)source.Pages.Length));
		w.Write(source.Unread08);
		w.Write(source.Unread0C);
		w.Write(source.Unread0E);
		w.Write(source.ContentType);
		w.Write(source.Unread14);
		w.Write(source.Unread16);
		w.Write(source.Unread18);
		w.Write(source.FrameCountReduction);
		w.Write(source.Unread1B);
		w.Write(source.FrameCount);
		w.Write(source.FramesPerSecond);
		w.Write(source.Unread46);
		w.Write(source.Unread80);
		w.Write(source.Unread100);
		foreach (var page in source.Pages) {
			w.Write(page.TableFirstRecord);
			w.Write(page.TableRecordCount);
			w.Write(page.TableUnread04);
		}

		w.Write(source.UnreadPageTableTail);
		for (int i = 0; i < source.Pages.Length; i++) {
			var page = source.Pages[i];
			long start = o.Position;
			w.Write(page.FirstRecord);
			w.Write(checked((ushort)page.Records.Length));
			w.Write(page.Unread04);
			w.Write(page.Unread06);
			foreach (var record in page.Records) {
				w.Write(checked((ushort)(4 + record.Extra.Length + record.Ops.Length)));
			}

			foreach (var record in page.Records) {
				WriteRecord(w, record);
			}

			w.Write(page.Padding);
			w.Flush();
			if (i < source.Pages.Length - 1 && o.Position - start != Es1DeluxePaintAnimation.PageLength) {
				throw new InvalidDataException($"Page {i} is {o.Position - start} bytes; every page but the last is {Es1DeluxePaintAnimation.PageLength}.");
			}
		}

		w.Flush();
		return o.ToArray();
	}

	/// <summary>The record at <paramref name="at"/>, split as <c>Anm_FindFrameRecord</c> finds its operations.</summary>
	private static Es1AnimationRecord ReadRecord(byte[] b, int at, int size) {
		if (size < 4) {
			throw new InvalidDataException($"The record at {at} is {size} bytes, shorter than its header.");
		}

		var record = new Es1AnimationRecord { Unread00 = b[at], HasExtra = b[at + 1], ExtraLength = U16(b, at + 2) };
		int extra = record.HasExtra == 0 ? 0 : record.ExtraLength + (record.ExtraLength & 1);
		if (4 + extra > size) {
			throw new InvalidDataException($"The record at {at} has {extra} extra bytes, more than its {size}.");
		}

		record.Extra = b[(at + 4)..(at + 4 + extra)];
		record.Ops = b[(at + 4 + extra)..(at + size)];
		return record;
	}

	private static void WriteRecord(BinaryWriter w, Es1AnimationRecord record) {
		int extra = record.HasExtra == 0 ? 0 : record.ExtraLength + (record.ExtraLength & 1);
		if (record.Extra.Length != extra) {
			throw new InvalidDataException($"A record's extra bytes are {record.Extra.Length}, where its header gives {extra}.");
		}

		w.Write(record.Unread00);
		w.Write(record.HasExtra);
		w.Write(record.ExtraLength);
		w.Write(record.Extra);
		w.Write(record.Ops);
	}

	private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));

	private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
