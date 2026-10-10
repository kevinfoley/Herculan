using Herculan.Engine.Content;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What the RAZOR's altitude scale reads off the world each frame: the ground under the machine, the
/// machine's own height, and the zone's height range both are mapped across. See
/// <see cref="AltitudeScale"/>.
/// </summary>
/// <param name="Ground">
/// The terrain height under the machine — <c>Terrain_HeightQuery</c> (<c>0046e07c</c>) at its x, y,
/// which is <see cref="HeightGrid.HeightAtWorld"/>.
/// </param>
/// <param name="Machine">The machine's own height, <c>mech+0x2e</c>.</param>
/// <param name="Floor">The bottom of the scale: the grid's <c>+0x110</c>, <see cref="HeightGrid.HeightBase"/>.</param>
/// <param name="Ceiling">
/// The top: the grid's <c>+0x114</c>, <see cref="HeightGrid.MaxWorldHeight"/>, plus
/// <see cref="AltitudeScale.CeilingMargin"/>.
/// </param>
public readonly record struct AltitudeReading(int Ground, int Machine, int Floor, int Ceiling) {
	/// <summary>
	/// The reading for <paramref name="pilot"/> over <paramref name="grid"/>, or null when the machine
	/// is not a flyer — <c>Gunsight_PaintAltitudeScale</c>'s own gate, type record <c>+0x50</c>.
	/// </summary>
	public static AltitudeReading? For(MechObject pilot, HeightGrid grid) {
		ArgumentNullException.ThrowIfNull(pilot);
		ArgumentNullException.ThrowIfNull(grid);
		if (!pilot.Type.IsFlyer) {
			return null;
		}

		var position = pilot.Position;
		return new AltitudeReading(grid.HeightAtWorld(position.X, position.Y), position.Z,
			grid.HeightBase, grid.MaxWorldHeight + AltitudeScale.CeilingMargin);
	}
}

/// <summary>
/// The RAZOR's altitude scale on the front window — <c>Gunsight_PaintAltitudeScale</c>
/// (<c>0043dd70</c>), the last thing both gunsight paints draw. Its geometry, in device pixels; the
/// paint's order and what each part shows are in docs/retail/simulation/cockpit-gunsight-hud.md, "The RAZOR's
/// altitude scale".
/// </summary>
public readonly struct AltitudeScale {
	/// <summary>The bank all four frames come from.</summary>
	public const string SpriteBank = "HUD";

	/// <summary>Frame 15, the bracket blitted at <see cref="X"/>, <see cref="Y"/>.</summary>
	public const int HeadFrame = 15;

	/// <summary>Frame 16, the bracket blitted at the scale's foot.</summary>
	public const int FootFrame = 16;

	/// <summary>Frame 17, the marker at the machine's own height.</summary>
	public const int MarkerFrame = 17;

	/// <summary>
	/// Frame 18, the tick strip that scrolls with height. Retail blits it and never shows it
	/// (docs/retail/simulation/cockpit-gunsight-hud.md, "The tick tape is never drawn"), so it is drawn only
	/// under the <see cref="Settings.TweakSettingDefinitions.ShowAltitudeTape"/> tweak.
	/// </summary>
	public const int TapeFrame = 18;

	/// <summary>Palette index of the column's two sides above the ground mark — a constructor immediate, so a raw index.</summary>
	public const int SideColorIndex = 0x49;

	/// <summary>Palette index of the column's solid part, from the ground mark down to the foot.</summary>
	public const int FillColorIndex = 0x4b;

	/// <summary>World units added to the grid's highest point to make the top of the scale.</summary>
	public const int CeilingMargin = 5000;

	/// <summary>World units of height per device row the tick strip scrolls — the paint's unshifted <c>/ 0x32</c>.</summary>
	public const int HeightPerTapeRow = 0x32;

	private const int Shift = CockpitViewGeometry.CoordShift;

	/// <summary>The scale's anchor relative to the reticle point, <c>.GAU</c> units: <c>Gau_RovingGunsightWidget</c>'s widget <c>+0x113</c>.</summary>
	private const int AnchorOffsetX = 0x46;

	/// <inheritdoc cref="AnchorOffsetX"/>
	private const int AnchorOffsetY = -0x12;

	/// <summary>From the anchor to the foot bracket's bottom edge, <c>.GAU</c> units.</summary>
	private const int LengthGau = 0x24;

	private AltitudeScale(int x, int y, int headHeight, int headWidth, int footHeight, int tapeHeight,
			int markerWidth, int markerHeight) {
		X = x;
		Y = y;
		Top = y + headHeight;
		Foot = y + (LengthGau << Shift) - footHeight;
		ColumnRight = x + headWidth;
		ColumnLeft = ColumnRight - (1 << Shift);
		TapeLeft = x + (2 << Shift);
		TapeHeight = tapeHeight;
		MarkerWidth = markerWidth;
		MarkerHeight = markerHeight;
	}

	/// <summary>The anchor's x: where the head bracket is blitted.</summary>
	public int X { get; }

	/// <summary>The anchor's y.</summary>
	public int Y { get; }

	/// <summary>The scale's top row, the head bracket's bottom edge. A height at the ceiling maps here.</summary>
	public int Top { get; }

	/// <summary>The scale's bottom row, where the foot bracket is blitted. A height at the floor maps here.</summary>
	public int Foot { get; }

	/// <summary>The column's left side.</summary>
	public int ColumnLeft { get; }

	/// <summary>The column's right side, one column past the head bracket's last. Both sides are inclusive.</summary>
	public int ColumnRight { get; }

	/// <summary>
	/// The tick strip's left edge. Its clip rect starts here and is the strip's own width, so it cuts
	/// only rows.
	/// </summary>
	public int TapeLeft { get; }

	/// <summary>The tick strip's height — the step between its two copies.</summary>
	public int TapeHeight { get; }

	/// <summary>The marker's width.</summary>
	public int MarkerWidth { get; }

	/// <summary>The marker's height.</summary>
	public int MarkerHeight { get; }

	/// <summary>
	/// This herc's scale, or null when its <c>.GAU</c> has no reticle point or the <c>HUD</c> bank lacks
	/// a frame the paint reads a size from.
	/// </summary>
	public static AltitudeScale? From(CockpitArt art) {
		ArgumentNullException.ThrowIfNull(art);
		if (art.Gau.Reticle is not { } reticle
			|| art.Sprites is not { } sprites
			|| sprites.Sprite(SpriteBank, HeadFrame) is not { } head
			|| sprites.Sprite(SpriteBank, FootFrame) is not { } foot
			|| sprites.Sprite(SpriteBank, MarkerFrame) is not { } marker
			|| sprites.Sprite(SpriteBank, TapeFrame) is not { } tape) {
			return null;
		}

		return new AltitudeScale(
			(reticle.Origin.X + AnchorOffsetX) << Shift,
			(reticle.Origin.Y + AnchorOffsetY) << Shift,
			head.Height * head.Scale, head.Width * head.Scale, foot.Height * foot.Scale,
			tape.Height * tape.Scale,
			marker.Width * marker.Scale, marker.Height * marker.Scale);
	}

	/// <summary>
	/// The row a height maps to: linear from <see cref="AltitudeReading.Floor"/> at <see cref="Foot"/>
	/// to <see cref="AltitudeReading.Ceiling"/> at <see cref="Top"/>, in the paint's 64-bit product and
	/// truncating divide, then clamped to the scale.
	/// </summary>
	public int RowFor(int height, in AltitudeReading reading) {
		int row = Foot - (int)((long)(height - reading.Floor) * (Foot - Top) / (reading.Ceiling - reading.Floor));
		return Top > row ? Top : Foot < row ? Foot : row;
	}

	/// <summary>The marker's top-left for a reading: centred on the column at the machine's row, as the paint centres it.</summary>
	public (int X, int Y) MarkerAt(in AltitudeReading reading) =>
		(((ColumnRight - ColumnLeft) >> 1) + ColumnLeft - (MarkerWidth >> 1) + 1,
			RowFor(reading.Machine, reading) - (MarkerHeight >> 1));

	/// <summary>
	/// Where the tick strip's lower copy starts: the machine's height over <see cref="HeightPerTapeRow"/>,
	/// wrapped at the scale's full length, below <see cref="Top"/>. The upper copy is one
	/// <see cref="TapeHeight"/> above it. Both are clipped to rows <see cref="Top"/> to
	/// <see cref="Foot"/> inclusive.
	/// </summary>
	public int TapeRow(in AltitudeReading reading) =>
		reading.Machine / HeightPerTapeRow % (LengthGau << Shift) + Top;
}
