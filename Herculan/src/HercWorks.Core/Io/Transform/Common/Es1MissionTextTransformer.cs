using System.Text;
using HercWorks.Core.Data.File.Msn.Es1;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's mission text (<see cref="Es1MissionText"/>; docs/es1/formats/eng-mission-text.md#the-layout). Writing
/// a parsed file gives back its bytes.
///
/// <para>The shell finds a block by searching for its tag's bytes and reads its pieces up to <c>0x10</c>; this reader walks the
/// blocks by their byte counts instead. It throws <see cref="InvalidDataException"/> where a file would not come back whole from
/// the model: a byte count, sum of literal lengths or objective count other than the one its pieces give, a choice whose texts do
/// not follow one another from offset 0, a text without its NUL or with one inside it, a piece byte other than 1 and 2 (the shell
/// passes those over), or bytes after the last block. Every retail file passes.</para>
/// </summary>
public class Es1MissionTextTransformer : ByteTransformer<Es1MissionText> {
	private const byte LiteralPiece = 1;
	private const byte ChoicePiece = 2;
	private const byte EndOfBlock = 0x10;

	/// <exception cref="InvalidDataException">The file does not walk to its last byte by the layout.</exception>
	public override Es1MissionText? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		Need(2);
		var file = new Es1MissionText { Version = IndexShortLE() };

		while (Index < inputArray.Length) {
			file.Blocks.Add(ReadBlock());
		}

		return file;
	}

	public override byte[]? Write(Es1MissionText source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.Version);
		foreach (var block in source.Blocks) {
			byte[] body = BlockBody(block);
			w.Write(Encoding.Latin1.GetBytes(block.Tag));
			w.Write(body.Length);
			w.Write(body);
		}

		w.Flush();
		return o.ToArray();
	}

	private Es1TextBlock ReadBlock() {
		int at = Index;
		Need(8);
		var block = new Es1TextBlock { Tag = IndexString(4) };
		int count = IndexIntLE();
		if (count < 0) {
			throw new InvalidDataException($"The {block.Tag} block at 0x{at:x} counts {count} bytes.");
		}

		Need(count);
		int end = Index + count;
		Need(2);
		short sum = IndexShortLE();
		short? objectives = null;
		if (block.Tag == Es1MissionText.ObjectivesTag) {
			Need(2);
			objectives = IndexShortLE();
		}

		while (true) {
			Need(1);
			byte piece = IndexByte();
			if (piece == EndOfBlock) {
				break;
			}

			block.Pieces.Add(piece switch {
				LiteralPiece => new Es1TextLiteral { Text = ReadText() },
				ChoicePiece => ReadChoice(),
				_ => throw new InvalidDataException($"The piece byte at 0x{Index - 1:x} is {piece}."),
			});
		}

		if (Index != end) {
			throw new InvalidDataException($"The {block.Tag} block at 0x{at:x} counts {count} bytes and ends after {Index - at - 8}.");
		}

		byte[] body = BlockBody(block);
		if (body.Length != count || BitConverter.ToInt16(body, 0) != sum || (objectives is { } n && n != LiteralCount(block))) {
			throw new InvalidDataException($"The {block.Tag} block at 0x{at:x} stores a sum or count its pieces do not give.");
		}

		return block;
	}

	private Es1TextChoice ReadChoice() {
		int at = Index - 1;
		Need(4);
		var choice = new Es1TextChoice { FlagIndex = IndexShortLE() };
		int n = IndexShortLE();
		if (n < 1) {
			throw new InvalidDataException($"The choice at 0x{at:x} has {n} offsets.");
		}

		Need(n * 6L);
		short[] offsets = IndexShortLEArray(n);
		short[] ranges = IndexShortLEArray(2 * n);
		int texts = Index;
		for (int i = 0; i < n - 1; i++) {
			if (offsets[i] != Index - texts) {
				throw new InvalidDataException($"The choice at 0x{at:x} puts text {i} at {offsets[i]}, not {Index - texts}.");
			}

			choice.Options.Add(new Es1TextOption { Low = ranges[2 * i], High = ranges[2 * i + 1], Text = ReadText() });
		}

		if (offsets[n - 1] != Index - texts) {
			throw new InvalidDataException($"The choice at 0x{at:x} ends its texts at {offsets[n - 1]}, not {Index - texts}.");
		}

		choice.UntestedLow = ranges[2 * n - 2];
		choice.UntestedHigh = ranges[2 * n - 1];
		return choice;
	}

	/// <summary>An <c>int16</c> length counting the NUL, and the text with its NUL, which is consumed and not kept.</summary>
	private string ReadText() {
		Need(2);
		int length = IndexShortLE();
		if (length < 1) {
			throw new InvalidDataException($"The text at 0x{Index - 2:x} is {length} bytes long.");
		}

		Need(length);
		int nul = Array.IndexOf(Bytes!, (byte)0, Index, length);
		if (nul != Index + length - 1) {
			throw new InvalidDataException($"The text at 0x{Index:x} has its first NUL at {(nul < 0 ? "none" : (nul - Index).ToString())}, not at {length - 1}.");
		}

		string text = IndexString(length - 1);
		Index++;
		return text;
	}

	/// <summary>The bytes after a block's byte count: the sum of literal lengths, the objective count, the pieces and <c>0x10</c>.</summary>
	private static byte[] BlockBody(Es1TextBlock block) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write((short)block.Pieces.OfType<Es1TextLiteral>().Sum(l => l.Text.Length + 1));
		if (block.Tag == Es1MissionText.ObjectivesTag) {
			w.Write((short)LiteralCount(block));
		}

		foreach (var piece in block.Pieces) {
			switch (piece) {
				case Es1TextLiteral literal:
					w.Write(LiteralPiece);
					WriteText(w, literal.Text);
					break;
				case Es1TextChoice choice:
					WriteChoice(w, choice);
					break;
				default:
					throw new InvalidOperationException($"A {piece.GetType().Name} has no piece byte.");
			}
		}

		w.Write(EndOfBlock);
		w.Flush();
		return o.ToArray();
	}

	private static void WriteChoice(BinaryWriter w, Es1TextChoice choice) {
		w.Write(ChoicePiece);
		w.Write(choice.FlagIndex);
		w.Write((short)(choice.Options.Count + 1));

		int offset = 0;
		foreach (var option in choice.Options) {
			w.Write((short)offset);
			offset += 2 + Encoding.Latin1.GetByteCount(option.Text) + 1;
		}

		w.Write((short)offset);
		foreach (var option in choice.Options) {
			w.Write(option.Low);
			w.Write(option.High);
		}

		w.Write(choice.UntestedLow);
		w.Write(choice.UntestedHigh);
		foreach (var option in choice.Options) {
			WriteText(w, option.Text);
		}
	}

	private static void WriteText(BinaryWriter w, string text) {
		byte[] bytes = Encoding.Latin1.GetBytes(text);
		w.Write((short)(bytes.Length + 1));
		w.Write(bytes);
		w.Write((byte)0);
	}

	private static int LiteralCount(Es1TextBlock block) => block.Pieces.OfType<Es1TextLiteral>().Count();

	private void Need(long bytes) {
		if (Index + bytes > Bytes!.Length) {
			throw new InvalidDataException($"The file ends at 0x{Bytes.Length:x}, {bytes} bytes being needed from 0x{Index:x}.");
		}
	}
}
