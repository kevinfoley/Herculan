namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>dat\DESERT.DAT</c>, <c>TEMPER.DAT</c>, <c>ICE.DAT</c> and <c>URBAN.DAT</c>: 24-byte records and no
/// count, record <i>i</i> the dividing line of root shape <i>i</i> of the theater's <c>dgs\</c> library, which the
/// simulator's draw order tests an object near the shape against. Layout and use:
/// docs/es1/formats/zone-terrain.md#dattheater-the-shapes-dividing-lines. Read by
/// <see cref="Io.Transform.Dbsim.Es1TheaterShapeLinesTransformer"/>.
/// </summary>
public class Es1TheaterShapeLines {
	public Es1ShapeLine[] Lines { get; set; } = [];
}

/// <summary>
/// One shape's dividing line: its two ends in the shape's frame. A record whose <see cref="StartZ"/> is negative gives the
/// shape no line; every such retail record is (0, 0, −1, 0, 0, −1).
/// </summary>
public class Es1ShapeLine {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x18;

	public int StartX { get; set; }

	public int StartY { get; set; }

	public int StartZ { get; set; }

	public int EndX { get; set; }

	public int EndY { get; set; }

	public int EndZ { get; set; }
}
