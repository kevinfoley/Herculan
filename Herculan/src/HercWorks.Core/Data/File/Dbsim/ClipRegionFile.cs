namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/EDG/*.EDG, /SIMVOL0/HD0-HD3/*.HD0-.HD3, /SIMVOL0/ED0-ED3/*.ED0-.ED3 — the screen
/// region a display may draw into, as rects and per-scanline span blocks: a cockpit view's 3D-viewport
/// cutout, or the heads-down display's clip shape. All three read through
/// <c>CockpitClipRegions_Load</c> (<c>0042dcf0</c>); layout and retail files are in
/// docs/retail/formats/hd-ed-clip-regions.md. Coordinates are as stored, before the loader's shift for
/// the 320-wide sets.
/// </summary>
public class ClipRegionFile {
	public Rect[] Rects { get; set; } = [];

	public SpanBlock[] Blocks { get; set; } = [];

	/// <summary>Bytes after the last block, which the loader does not read: one INT16 0 in every retail file.</summary>
	public byte[] TrailingBytes { get; set; } = [];

	/// <summary>One rect, inclusive on all four edges.</summary>
	public class Rect {
		public short Y0 { get; set; }
		public short Y1 { get; set; }
		public short X0 { get; set; }
		public short X1 { get; set; }
	}

	/// <summary>Consecutive scanlines from <see cref="FirstRow"/>, one <see cref="Span"/> per row.</summary>
	public class SpanBlock {
		public short FirstRow { get; set; }
		public Span[] Rows { get; set; } = [];
	}

	/// <summary>One scanline's visible columns, inclusive at both ends.</summary>
	public class Span {
		public short XStart { get; set; }
		public short XEnd { get; set; }
	}
}
