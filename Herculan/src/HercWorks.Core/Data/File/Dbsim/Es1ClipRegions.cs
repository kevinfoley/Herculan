namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - ES1's clip regions, in SIMVOL0.VOL: <c>edg\*.EDG</c>, and the cockpit views' <c>ed0\</c>–<c>ed3\</c> files, which the
/// same loader reads (docs/es1/formats/hd-ed-clip-regions.md). Rects that span the 320-pixel width, then blocks of one span
/// per row. Read by <see cref="Io.Transform.Dbsim.Es1ClipRegionsTransformer"/>.
/// </summary>
public class Es1ClipRegions {
	/// <summary>The rects, each a first and last row.</summary>
	public Es1ClipRect[] Rects { get; set; } = [];

	/// <summary>The span blocks.</summary>
	public Es1ClipSpanBlock[] Blocks { get; set; } = [];

	/// <summary>
	/// Bytes after the last block, which the loader does not read: two zero bytes in every retail file but <c>ed1\COLOSSUS.ED1</c>,
	/// whose 30 bytes repeat its rect as a block. Kept so a written file gives back its bytes.
	/// </summary>
	public byte[] Unread { get; set; } = [];
}

/// <summary>Rows <see cref="Top"/> to <see cref="Bottom"/>, which the loader stores with columns 0 and 319.</summary>
public class Es1ClipRect {
	public short Top { get; set; }

	public short Bottom { get; set; }
}

/// <summary>One span per row from <see cref="FirstRow"/> down.</summary>
public class Es1ClipSpanBlock {
	public short FirstRow { get; set; }

	public Es1ClipSpan[] Rows { get; set; } = [];
}

/// <summary>A row's first and last column, as the loader stores them.</summary>
public class Es1ClipSpan {
	public short Start { get; set; }

	public short End { get; set; }
}
