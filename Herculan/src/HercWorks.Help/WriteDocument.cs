using System.Buffers.Binary;
using System.Text;
using HercWorks.Help.Internal;

namespace HercWorks.Help;

/// <summary>
/// The text of a Windows Write document, the format of each language folder's <c>README.WRI</c>, which
/// the help file's <c>Readme</c> action names (docs/formats/winhelp.md#macros).
///
/// <para>A Write file is a 128-byte header, the text, then the formatting: character and paragraph
/// runs, pictures and OLE objects. Only the text is read. The header's word 0 is the magic
/// <c>0xBE31</c> and its dword at offset 14, <c>fcMac</c>, is the offset just past the text, which
/// starts at 128. The text is Windows-1252 with CRLF line ends; nothing after <c>fcMac</c> is
/// looked at.</para>
///
/// <para>As with <see cref="HelpFile"/>, the input is treated as hostile: this is handed bytes, opens
/// nothing, and returns null and a reason for anything malformed rather than throwing. The text comes
/// back with CRLF as <c>\n</c> and every control character other than tab and <c>\n</c> removed; it is
/// still untrusted, and a caller putting it in a page encodes it.</para>
/// </summary>
public static class WriteDocument {
	private const ushort Magic = 0xBE31;
	private const int HeaderBytes = 128;
	private const int TextEndOffset = 14;

	/// <summary>
	/// The document's text, or null with <paramref name="error"/> saying why when the bytes are not a
	/// Write document this reads or are over <see cref="HelpLimits.MaxReadmeBytes"/>.
	/// </summary>
	public static string? ReadText(byte[] bytes, out string? error, HelpLimits? limits = null) {
		ArgumentNullException.ThrowIfNull(bytes);
		limits ??= HelpLimits.Default;
		if (bytes.Length > limits.MaxReadmeBytes) {
			error = $"{bytes.Length} bytes is over the {limits.MaxReadmeBytes}-byte limit";
			return null;
		}

		if (bytes.Length < HeaderBytes) {
			error = $"{bytes.Length} bytes is shorter than the {HeaderBytes}-byte header";
			return null;
		}

		ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
		if (magic != Magic) {
			error = $"magic 0x{magic:X4} is not a Write document's 0x{Magic:X4}";
			return null;
		}

		uint textEnd = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(TextEndOffset));
		if (textEnd < HeaderBytes || textEnd > (uint)bytes.Length) {
			error = $"the text ends at {textEnd}, outside {HeaderBytes}..{bytes.Length}";
			return null;
		}

		string raw = Windows1252.Decode(bytes.AsSpan(HeaderBytes, (int)textEnd - HeaderBytes));
		var text = new StringBuilder(raw.Length);
		for (int i = 0; i < raw.Length; i++) {
			char c = raw[i];
			if (c == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n') {
				continue;
			}

			if (c is '\t' or '\n' || !char.IsControl(c)) {
				text.Append(c);
			}
		}

		error = null;
		return text.ToString();
	}
}
