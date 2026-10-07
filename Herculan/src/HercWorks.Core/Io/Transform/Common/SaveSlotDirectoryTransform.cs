using HercWorks.Core.Data.File.Sav;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// <c>sav\GAMEFILE.STR</c> — the twelve-slot save directory. Reader <c>GameFileStr_Read</c> (<c>0040ddc8</c>), writer
/// <c>GameFileStr_Write</c> (<c>0040df4b</c>); the layout is in <c>docs/retail/formats/save-games.md</c>.
///
/// <para>Two blocks of the same shape, each preceded by its own count: the filenames, whose trailing
/// byte is always zero, then the labels, whose trailing byte is the slot's in-use flag. Both strings
/// store their own NUL inside the counted length, so a stored length of 10 is nine characters.</para>
///
/// <para>The reader stops after the second block and never looks at the leading length, so a file with
/// a stale tail parses cleanly — which the retail file has, eleven bytes of a longer label block.</para>
/// </summary>
public class SaveSlotDirectoryTransform : ByteTransformer<SaveSlotDirectory> {
	public override SaveSlotDirectory? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length < 6) {
			return null;
		}

		SetBytes(inputArray);
		var directory = new SaveSlotDirectory { StoredLength = IndexIntLE() };

		short fileCount = IndexShortLE();
		for (int i = 0; i < fileCount; i++) {
			var entry = new SaveSlotEntry { FileName = IndexCountedString() };
			IndexByte();
			directory.Slots.Add(entry);
		}

		short labelCount = IndexShortLE();
		for (int i = 0; i < labelCount; i++) {
			string label = IndexCountedString();
			byte inUse = IndexByte();

			// The two counts are written from the same literal, so they agree in every real file. A file
			// whose label block is the longer one still gets its labels read rather than dropped.
			if (i >= directory.Slots.Count) {
				directory.Slots.Add(new SaveSlotEntry());
			}

			directory.Slots[i].Label = label;
			directory.Slots[i].InUse = inUse != 0;
		}

		return directory;
	}

	public override byte[]? Write(SaveSlotDirectory data) {
		using var body = new MemoryStream();
		void Emit(byte[] bytes) => body.Write(bytes, 0, bytes.Length);

		Emit(WriteShortLE((short)data.Slots.Count));
		foreach (var slot in data.Slots) {
			Emit(WriteCountedString(slot.FileName));
			body.WriteByte(0);
		}

		Emit(WriteShortLE((short)data.Slots.Count));
		foreach (var slot in data.Slots) {
			Emit(WriteCountedString(slot.Label));
			body.WriteByte(slot.InUse ? (byte)1 : (byte)0);
		}

		// The length field is the physical file less its own four bytes, which for a file written from
		// scratch is exactly the payload. It is not recomputed from StoredLength: a round-trip of a file
		// that carries a stale tail loses the tail, so the honest length is the one just written.
		using var output = new MemoryStream();
		output.Write(WriteIntLE((int)body.Length));
		body.Position = 0;
		body.CopyTo(output);
		return output.ToArray();
	}

	/// <summary>An <c>int16</c> length followed by that many bytes, the last of which is the NUL.</summary>
	private string IndexCountedString() {
		short length = IndexShortLE();
		return length <= 0 ? string.Empty : IndexString(length).TrimEnd('\0');
	}

	private byte[] WriteCountedString(string value) {
		using var stream = new MemoryStream();
		byte[] length = WriteShortLE((short)(value.Length + 1));
		stream.Write(length, 0, length.Length);
		foreach (char c in value) {
			stream.WriteByte((byte)c);
		}

		stream.WriteByte(0);
		return stream.ToArray();
	}
}
