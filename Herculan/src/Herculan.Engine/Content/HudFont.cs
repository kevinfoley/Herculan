using System.Buffers.Binary;
using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Engine.Content;

/// <summary>
/// How a label sits horizontally in its rect — DBSIM's own flag values, which
/// <c>Label_SetRect</c> tests as a bit mask (<c>&amp; 2</c> then <c>&amp; 4</c>, otherwise left).
/// There is no vertical counterpart: every label is vertically centred. See
/// <see cref="HudFont.Place"/>.
/// </summary>
public enum LabelAlign {
	/// <summary>Anchored to the rect's left edge. Retail's default, and what the MFD titles, the
	/// status labels and the order rows all pass.</summary>
	Left = 1,

	/// <summary>Centred in the rect — every button caption.</summary>
	Center = 2,

	/// <summary>Anchored to the rect's right edge.</summary>
	Right = 4,
}

/// <summary>
/// A <c>.DFN</c>/<c>.HFN</c> bitmap font — DBSIM's only HUD text mechanism. The header layout, the
/// per-font ink indices and the verification against all 54 retail font files are in
/// docs/retail/formats/dfn-hfn-dci.md, "<c>.DFN</c> / <c>.HFN</c> — bitmap font".
///
/// <para>Two consequences this class is built around. Each glyph is <c>width x cellHeight</c> bytes,
/// one palette index per pixel with 0 transparent and exactly one ink value, so the 18
/// colour-scheme fonts (<c>ColorSchemePanels</c>, <c>0049b0ac</c>) are copies of one typeface and a
/// widget picks its text colour by picking a font, never by passing a colour. And the declared width
/// is the advance, art included, so <see cref="Measure"/> is a plain sum of widths with no
/// tracking.</para>
///
/// <para><c>.HFN</c> is the 640-wide mode's font (cell height 13, 217 glyphs) and <c>.DFN</c> the
/// 320-wide mode's (cell height 10, 223 glyphs); they are separate art, not a 2x scale of each other.
/// This engine renders the 640-wide cockpit, so <see cref="Load"/> reads <c>hfn\</c>.</para>
/// </summary>
public sealed class HudFont {
	/// <summary>Resource folder and extension for the 640-wide font set.</summary>
	public const string ResourceFolder = "hfn";

	private readonly DynamixBitmap[] _glyphs;

	private HudFont(string name, int firstCharCode, int cellHeight, int baseline, int inkHeight,
			DynamixBitmap[] glyphs) {
		Name = name;
		FirstCharCode = firstCharCode;
		CellHeight = cellHeight;
		Baseline = baseline;
		InkHeight = inkHeight;
		_glyphs = glyphs;
	}

	/// <summary>The font's resource name, e.g. <c>WHITE</c> — also its key in <see cref="HudSpriteSheet"/>.</summary>
	public string Name { get; }

	/// <summary>Character code of glyph 0. 32 (space) in every retail file.</summary>
	public int FirstCharCode { get; }

	/// <summary>Every glyph's cell height in pixels — 13 for <c>.HFN</c>.</summary>
	public int CellHeight { get; }

	/// <summary>Header field G: the row the glyph art sits on. Not used for layout, kept for reference.</summary>
	public int Baseline { get; }

	/// <summary>
	/// The height a label centres by — 11 in every <c>.HFN</c> against a 13-row cell, 8 in every
	/// <c>.DFN</c> against a 10-row one. <b>Not</b> <see cref="CellHeight"/>, which is what the glyph
	/// art occupies: it is the ink-tall band that gets centred in a label's rect, with the two cell
	/// rows past it hanging below as descender space. Centring the full cell instead sits every label
	/// a pixel and a half high. See docs/retail/formats/dfn-hfn-dci.md, "<c>inkHeight</c> and label
	/// placement".
	/// </summary>
	public int InkHeight { get; }

	/// <summary>Glyphs in character order, as bitmaps ready to pack into a texture atlas.</summary>
	public IReadOnlyList<DynamixBitmap> Glyphs => _glyphs;

	/// <summary>Glyph index for <paramref name="c"/>, or null when the font has no glyph for it.</summary>
	public int? GlyphIndex(char c) {
		int index = c - FirstCharCode;
		return index >= 0 && index < _glyphs.Length ? index : null;
	}

	/// <summary>Advance width of <paramref name="c"/> in pixels; 0 when the font has no glyph for it.</summary>
	public int Width(char c) => GlyphIndex(c) is { } index ? _glyphs[index].Cols : 0;

	/// <summary>Total advance width of <paramref name="text"/> in pixels.</summary>
	public int Measure(string text) {
		int total = 0;
		foreach (char c in text) {
			total += Width(c);
		}

		return total;
	}

	/// <summary>
	/// Device pixels a run's measured width is trimmed by before it is aligned — the original's
	/// <c>1 &lt;&lt; XCoordShift</c>, which drops the trailing advance past the last glyph so a
	/// centred or right-aligned run sits on its ink rather than on its spacing column.
	/// </summary>
	public const int TrailingAdvanceTrim = 1 << CockpitViewGeometry.CoordShift;

	/// <summary>
	/// Where a run of text lands inside a rect — the top-left device pixel of its first glyph — as
	/// <c>Label_SetRect</c> (<c>00438884</c>) and <c>Label_SetText</c> (<c>00438920</c>) place it
	/// between them. Every HUD label in the game goes through that pair, so this is the one placement
	/// rule the cockpit, the MFD and the Heads-Down Display all share. The formula is in
	/// docs/retail/formats/mfd.md, "Label placement"; there is no vertical alignment flag, so it is
	/// <see cref="InkHeight"/> that gets centred.
	///
	/// <para>All of it is integer arithmetic in the original, including both <c>&gt;&gt; 1</c>s, and
	/// this method keeps it that way: in floating point a label lands up to a pixel off on either
	/// axis, which is visible at this art's scale.</para>
	/// </summary>
	public (int X, int Y) Place(string text, int x0, int y0, int x1, int y1, LabelAlign align,
			int marginX = 0, int marginY = 0) {
		int anchorX = align switch {
			LabelAlign.Center => ((x1 - x0) >> 1) + x0 + marginX,
			LabelAlign.Right => x1 - marginX,
			_ => x0 + marginX,
		};

		int width = Measure(text) - TrailingAdvanceTrim;
		int textX = align switch {
			LabelAlign.Center => anchorX - (width >> 1),
			LabelAlign.Right => anchorX - width,
			_ => anchorX,
		};

		int anchorY = ((y1 - y0) >> 1) + y0 + (InkHeight >> 1) + marginY + 1;
		return (textX, anchorY - InkHeight);
	}

	/// <summary>
	/// Loads and parses <c>&lt;folder&gt;\&lt;name&gt;.&lt;FOLDER&gt;</c>. Returns null when the
	/// resource is missing or does not parse as a panel resource — callers draw no text rather than
	/// substituting another font, since in this format the font *is* the colour.
	/// </summary>
	/// <param name="folder">
	/// Resource folder, which is also the extension. Defaults to <see cref="ResourceFolder"/>, the
	/// simulator's 640-wide set. The shell ships its own fonts only as <c>dfn\</c> and draws them
	/// unscaled on a 640-wide canvas, which is a different arrangement from DBSIM's
	/// <c>dfn</c>/<c>hfn</c> pair but the same file format — see <see cref="Shell.ShellArt"/>.
	/// </param>
	public static HudFont? Load(GameContent content, string name, string? folder = null) {
		folder ??= ResourceFolder;
		if (content.Read(folder, name + "." + folder.ToUpperInvariant()) is not { } bytes
			|| bytes.Length < 34) {
			return null;
		}

		short I16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));

		int glyphCount = I16(0x08);
		int firstCharCode = I16(0x0c);
		int cellHeight = I16(0x0e);
		int baseline = I16(0x14);
		int inkHeight = I16(0x1a);
		int poolLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x1e));

		int poolStart = 0x22;
		int offsetsStart = poolStart + poolLength;
		int widthsStart = offsetsStart + glyphCount * 4;
		if (glyphCount <= 0 || cellHeight <= 0 || poolLength <= 0
			|| widthsStart + glyphCount > bytes.Length) {
			return null;
		}

		var glyphs = new DynamixBitmap[glyphCount];
		for (int i = 0; i < glyphCount; i++) {
			int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offsetsStart + i * 4));
			int end = i + 1 < glyphCount
				? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offsetsStart + (i + 1) * 4))
				: poolLength;
			int width = bytes[widthsStart + i];

			// The width byte and the slice length are two independent statements of the same fact in
			// every retail file. Trusting the slice keeps a malformed file from reading past its pool.
			int length = end - start;
			if (start < 0 || length < 0 || start + length > poolLength || length != width * cellHeight) {
				return null;
			}

			var pixels = new byte[length];
			Array.Copy(bytes, poolStart + start, pixels, 0, length);
			glyphs[i] = new DynamixBitmap {
				Rows = (short)cellHeight,
				Cols = (short)width,
				BitDepth = 8,
				ImageDataLen = length,
				ImageData = pixels,
			};
		}

		return new HudFont(name, firstCharCode, cellHeight, baseline, inkHeight, glyphs);
	}
}
