using System.Text;

namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// FILE - ES1's /ZONES.VOL/MSN/*.ENG — a Metaltech: Earthsiege mission's briefing, objectives and debriefings: the version
/// word and the tagged blocks in file order (docs/es1/formats/eng-mission-text.md#the-layout). The lengths, sums and counts
/// the file stores are derived from the pieces, so the model holds only the text and the choices.
/// </summary>
public class Es1MissionText {
	/// <summary>The briefing's tag.</summary>
	public const string BriefingTag = "BRF:";

	/// <summary>The objectives' tag.</summary>
	public const string ObjectivesTag = "OBJ:";

	/// <summary>A debriefing's tag.</summary>
	public const string DebriefingTag = "DEB:";

	/// <summary>The version word, 1 in every retail file; the shell builds no text from any other.</summary>
	public short Version { get; set; } = 1;

	/// <summary>The blocks, in file order.</summary>
	public List<Es1TextBlock> Blocks { get; set; } = [];

	/// <summary>
	/// The block of <paramref name="tag"/> the shell reads for <paramref name="ordinal"/>: the ordinal's block of that tag,
	/// or the tag's last block when the ordinal is past it (docs/es1/formats/eng-mission-text.md#reading-a-block). Null when the
	/// file has no block of the tag.
	/// </summary>
	public Es1TextBlock? Find(string tag, int ordinal) {
		var blocks = Blocks.Where(b => b.Tag == tag).ToList();
		return blocks.Count == 0 ? null : blocks[Math.Clamp(ordinal, 0, blocks.Count - 1)];
	}
}

/// <summary>One tagged block of an ES1 <c>.ENG</c> (docs/es1/formats/eng-mission-text.md#the-layout).</summary>
public class Es1TextBlock {
	/// <summary>The four-byte tag, colon included: <see cref="Es1MissionText.BriefingTag"/>, <see cref="Es1MissionText.ObjectivesTag"/> or <see cref="Es1MissionText.DebriefingTag"/>.</summary>
	public string Tag { get; set; } = "";

	/// <summary>The pieces, in order. In an objectives block each literal is one objective.</summary>
	public List<Es1TextPiece> Pieces { get; set; } = [];

	/// <summary>
	/// The text the shell builds from the block: the pieces run together, each choice replaced by the text its flag's value
	/// picks, or by nothing (docs/es1/formats/eng-mission-text.md#reading-a-block).
	/// </summary>
	/// <param name="flag">A campaign flag's value by its index, as <c>data\mission.var</c> holds it.</param>
	public string Compose(Func<short, short> flag) {
		var text = new StringBuilder();
		foreach (var piece in Pieces) {
			text.Append(piece switch {
				Es1TextLiteral literal => literal.Text,
				Es1TextChoice choice => choice.Pick(flag(choice.FlagIndex))?.Text ?? "",
				_ => "",
			});
		}

		return text.ToString();
	}
}

/// <summary>A piece of an ES1 <c>.ENG</c> block.</summary>
public abstract class Es1TextPiece {
}

/// <summary>Piece byte 1: text shown as it stands. The file stores it with an <c>int16</c> length and a NUL, which the model leaves out.</summary>
public class Es1TextLiteral : Es1TextPiece {
	public string Text { get; set; } = "";
}

/// <summary>Piece byte 2: one of several texts, picked by a campaign flag's value (docs/es1/formats/eng-mission-text.md#the-layout).</summary>
public class Es1TextChoice : Es1TextPiece {
	/// <summary>The index of the flag whose value picks the text.</summary>
	public short FlagIndex { get; set; }

	/// <summary>The texts, each with the range of the flag's values that picks it.</summary>
	public List<Es1TextOption> Options { get; set; } = [];

	/// <summary>The last of the file's range pairs, which the shell does not test; (0, 0) in every retail choice.</summary>
	public short UntestedLow { get; set; }

	/// <inheritdoc cref="UntestedLow"/>
	public short UntestedHigh { get; set; }

	/// <summary>The last option whose range holds <paramref name="value"/>, both ends inclusive, or null.</summary>
	public Es1TextOption? Pick(short value) => Options.LastOrDefault(o => o.Low <= value && value <= o.High);
}

/// <summary>One text of a <see cref="Es1TextChoice"/> and the flag values that pick it.</summary>
public class Es1TextOption {
	/// <summary>The lowest value of the flag that picks the text.</summary>
	public short Low { get; set; }

	/// <summary>The highest value of the flag that picks the text.</summary>
	public short High { get; set; }

	/// <summary>The text, without the NUL the file stores.</summary>
	public string Text { get; set; } = "";
}
