namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>dat\DEBRIS.DAT</c>, its one debris database's groups: an <c>int16</c> group count, then per group a
/// throw count, a piece count and that many 4-byte pieces, where ES2's <c>_DEB.DAT</c> pieces (<see cref="DebrisHerc"/>)
/// are 14 bytes. Layout and use: docs/es1/formats/debris-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1DebrisDatTransformer"/>.
/// </summary>
public class Es1DebrisDat {
	public Es1DebrisGroup[] Groups { get; set; } = [];
}

/// <summary>One group of pieces (docs/es1/formats/debris-dat.md#throwing-a-group).</summary>
public class Es1DebrisGroup {
	/// <summary>The bound of the random number of pieces thrown: 0 to this − 1. 3 to 7 in retail.</summary>
	public short ThrowCount { get; set; }

	public Es1DebrisPiece[] Pieces { get; set; } = [];
}

/// <summary>One piece, 4 bytes.</summary>
public class Es1DebrisPiece {
	/// <summary>The piece's root shape in <c>dts\DEBRIS.DTS</c>.</summary>
	public short ShapeIndex { get; set; }

	/// <summary>The piece's share of its group's weighted draw.</summary>
	public short Weight { get; set; }
}
