using System.Text;
using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The training port's box — what <c>PilotMessagePort_Paint</c> (<c>0043660c</c>) draws in place of
/// the ordinary port's single speaker-coloured line. The instruction's sentences are joined and
/// word-wrapped (<see cref="Wrap"/>), and the box is sized to the result: as wide as the widest
/// line plus <see cref="HorizontalPadding"/> each side and centred on the screen, as tall as the
/// lines plus one. White text on the computer's black, framed in its red. Derivation:
/// docs/retail/simulation/cockpit-messages.md, "The training port".
/// </summary>
/// <param name="Top">
/// Device-pixel top edge: the <c>.GAU</c> rect's own top raised by
/// <see cref="HercWorks.Core.Data.File.Gau.HPilotMessagePort.TrainingLift"/>.
/// </param>
public readonly record struct TrainingMessageLayout(int Top) {
	/// <summary>
	/// The font — <c>ColorSchemePanels[10]</c>, <c>WHITE</c>, which the port's constructor
	/// (<c>TrainingMessagePort_Ctor</c>, <c>00436244</c>) stores at <c>+0x4df</c> from <c>0049b0d4</c>.
	/// </summary>
	public const string Font = "WHITE";

	/// <inheritdoc cref="PilotMessageBoxLayout.HorizontalPadding"/>
	public const int HorizontalPadding = PilotMessageBoxLayout.HorizontalPadding;

	/// <summary>One line of text — <c>8 &lt;&lt; YCoordShift</c>.</summary>
	public const int LineHeight = 8 * MessageTickerLayout.CoordScale;

	/// <summary>
	/// Characters a line may reach before the wrap breaks it — 80 in the 640-wide mode this engine
	/// draws, 60 in the 320-wide one.
	/// </summary>
	public const int LineLimit = 80;

	/// <summary>The fill, <c>COLORS.DAT</c> id 19 — the computer's black.</summary>
	public const int FillColorId = MessageTickerLayout.BackgroundColorId;

	/// <summary>The frame, id 9 — the computer's red.</summary>
	public const int BorderColorId = MessageTickerLayout.BorderColorId;

	/// <summary>This herc's box, or null when its <c>.GAU</c> carries no rect for one.</summary>
	public static TrainingMessageLayout? From(CockpitArt? hud) {
		if (hud?.Gau.PilotMessagePort is not { } rect) {
			return null;
		}

		return new TrainingMessageLayout((rect.Origin.Y - rect.TrainingLift) * MessageTickerLayout.CoordScale);
	}

	/// <summary>Box height for <paramref name="lineCount"/> lines — one line's worth more than the text.</summary>
	public static int Height(int lineCount) => (lineCount + 1) * LineHeight;

	/// <inheritdoc cref="PilotMessageBoxLayout.Left"/>
	public static int Left(int screenWidth, int textWidth) => PilotMessageBoxLayout.Left(screenWidth, textWidth);

	/// <inheritdoc cref="PilotMessageBoxLayout.Right"/>
	public static int Right(int screenWidth, int textWidth) => PilotMessageBoxLayout.Right(screenWidth, textWidth);

	/// <summary>
	/// Device-pixel x every line starts at. The lines are left-aligned inside the box, not centred
	/// one by one.
	/// </summary>
	public static int TextLeft(int screenWidth, int textWidth) => Left(screenWidth, textWidth) + HorizontalPadding;

	/// <summary>
	/// Device-pixel top of line <paramref name="line"/>'s glyphs. The paint anchors the first line
	/// at <c>top + lineHeight / 2 + lineHeight</c> and steps one line height; the glyph blitter
	/// subtracts <see cref="HudFont.InkHeight"/> from the anchor, as it does for the ticker.
	/// </summary>
	public int LineTop(int line, HudFont font) =>
		Top + (LineHeight >> 1) + LineHeight + line * LineHeight - font.InkHeight;

	/// <summary>
	/// <c>PilotMessagePort_WrapText</c> (<c>00436318</c>): joins an instruction's sentences into
	/// lines of at most <see cref="LineLimit"/> characters, and says which line is the widest.
	///
	/// <para>A sentence that fits on the current line is appended to it after a space. One that does
	/// not is split at its last space that still fits: the head goes on the current line, again after
	/// a space, and the rest starts the next line <b>unwrapped</b>, however long it is. Two quirks
	/// follow from the code and are kept: the space before a split head is added even to an empty
	/// line, so an instruction whose first sentence is too long starts with a blank, and the widest
	/// line is chosen by character count, not by measured width.</para>
	/// </summary>
	/// <returns>The lines, and the index of the widest, or -1 when there are none.</returns>
	public static (IReadOnlyList<string> Lines, int Widest) Wrap(IReadOnlyList<string> sentences, int limit = LineLimit) {
		var buffer = new List<StringBuilder> { new() };
		int line = 0, column = 0, count = 0, widest = 0, widestLine = -1;

		foreach (string sentence in sentences) {
			int length = sentence.Length;

			if (length + column < limit) {
				if (buffer[line].Length != 0) {
					buffer[line].Append(' ');
				} else {
					count++;
				}

				buffer[line].Append(sentence);
				column += length + 1;
			} else {
				int split = -1;

				for (int k = 0; k < length; k++) {
					bool last = k == length - 1;
					if (sentence[k] != ' ' && !last) {
						continue;
					}

					if (k + column < limit && !last) {
						split = k;
						continue;
					}

					string rest = sentence;
					int headLength = 0;

					if (split >= 0) {
						buffer[line].Append(' ').Append(sentence, 0, split);
						if (column == 0) {
							count++;
						}

						headLength = split;
						column += split;
						if (column > widest) {
							widest = column - 1;
							widestLine = line;
						}

						rest = sentence[(split + 1)..];
					}

					line++;
					if (buffer.Count <= line) {
						buffer.Add(new StringBuilder());
					}

					buffer[line].Clear().Append(rest);
					column = length - headLength;
					count++;
					break;
				}
			}

			if (column > widest) {
				widest = column - 1;
				widestLine = line;
			}
		}

		var lines = new string[Math.Min(count, buffer.Count)];
		for (int i = 0; i < lines.Length; i++) {
			lines[i] = buffer[i].ToString();
		}

		return (lines, widestLine < lines.Length ? widestLine : -1);
	}
}
