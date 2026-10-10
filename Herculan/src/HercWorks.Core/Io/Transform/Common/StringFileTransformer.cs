using System.Text;
using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes .STR string tables (<see cref="StringFile"/>), every group with its attribute
/// bytes. See docs/retail/formats/str-strings.md#layout.
/// </summary>
public class StringFileTransformer : ByteTransformer<StringFile> {
	/// <summary>
	/// Walks the layout. Returns null on any inconsistency — a length running past the declared
	/// content, or a group that does not complete — rather than a partial table: group indices are
	/// positional, so a truncated walk would silently shift every later group.
	/// </summary>
	public override StringFile? Parse(byte[]? bytes) {
		if (bytes == null || bytes.Length < 4) {
			return null;
		}

		int end = 4 + BitConverter.ToInt32(bytes, 0);
		if (end < 4 || end > bytes.Length) {
			return null;
		}

		var groups = new List<StringFile.Entry[]>();
		int at = 4;
		while (at + 2 <= end) {
			int count = BitConverter.ToInt16(bytes, at);
			at += 2;
			if (count < 0) {
				return null;
			}

			var entries = new StringFile.Entry[count];
			for (int i = 0; i < count; i++) {
				if (at + 2 > end) {
					return null;
				}

				int length = BitConverter.ToInt16(bytes, at);
				at += 2;
				if (length < 0 || at + length >= end) {
					return null;
				}

				// The stored length counts the NUL terminator; the text is everything before it, read as
				// Latin-1 so a byte past 0x7f keeps its value and writes back (docs/retail/formats/str-strings.md#layout).
				string text = Encoding.Latin1.GetString(bytes, at, Math.Max(length - 1, 0));
				at += length;

				int attributeCount = bytes[at++];
				if (at + attributeCount > end) {
					return null;
				}

				entries[i] = new StringFile.Entry(text, bytes[at..(at + attributeCount)]);
				at += attributeCount;
			}

			groups.Add(entries);
		}

		return at == end ? new StringFile { Groups = groups.ToArray() } : null;
	}

	/// <summary>Writes the layout back: the content length, then each group as read.</summary>
	public override byte[]? Write(StringFile source) {
		using var content = new MemoryStream();
		using (var writer = new BinaryWriter(content, Encoding.Latin1, leaveOpen: true)) {
			foreach (var group in source.Groups) {
				writer.Write((short)group.Length);
				foreach (var entry in group) {
					byte[] text = Encoding.Latin1.GetBytes(entry.Text);
					writer.Write((short)(text.Length + 1));
					writer.Write(text);
					writer.Write((byte)0);
					writer.Write((byte)entry.Attributes.Length);
					writer.Write(entry.Attributes);
				}
			}
		}

		var output = new byte[4 + content.Length];
		BitConverter.GetBytes((int)content.Length).CopyTo(output, 0);
		content.ToArray().CopyTo(output, 4);
		return output;
	}
}
