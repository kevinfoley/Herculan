using HercWorks.Core.Data.File.Msn;
using System.Diagnostics;
using System.Text;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>Reads and writes a mission's <c>.ENG</c> text; see <see cref="MissionStringFile"/>.</summary>
public class MissionStringFileTransformer : ByteTransformer<MissionStringFile> {
	public override MissionStringFile? Parse(byte[]? inputArray) {
		SetBytes(inputArray!);

		var str = new MissionStringFile();

		var entries = new MissionStringFile.StringEntry[IndexShortLE()];

		for (int i = 0; i < entries.Length; i++) {
			short guid = IndexShortLE();
			short conditionRef = IndexShortLE();
			short parentRef = IndexShortLE();
			short len = IndexShortLE();

			var ent = str.CreateEntry(guid, conditionRef, parentRef, len, IndexString(len));

			Debug.WriteLine($"Created string entry {ent}");
			entries[i] = ent;
		}
		str.Strings = entries;

		return str;
	}

	/// <summary>
	/// Writes the count, then each entry's four shorts and its text, taking the length field from
	/// <see cref="MissionStringFile.StringEntry.Val"/> rather than <see cref="MissionStringFile.StringEntry.Len"/>.
	/// </summary>
	public override byte[]? Write(MissionStringFile? source) {
		if (source?.Strings is not { } entries) {
			return null;
		}

		using var outStream = new MemoryStream();
		void Emit(byte[] bytes) => outStream.Write(bytes, 0, bytes.Length);

		Emit(WriteShortLE((short)entries.Length));
		foreach (var entry in entries) {
			byte[] text = Encoding.Latin1.GetBytes(entry.Val ?? string.Empty);
			Emit(WriteShortLE(entry.Guid));
			Emit(WriteShortLE(entry.ConditionRef));
			Emit(WriteShortLE(entry.ParentRef));
			Emit(WriteShortLE((short)text.Length));
			Emit(text);
		}

		return outStream.ToArray();
	}
}
