namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #6 (22 bytes/record) — a world position, <c>script.dat</c> block 1. Every positional ref in
/// the file names one of these. See docs/retail/formats/msn-mission-file.md, "Row #6 field decode", and
/// <see cref="VariantKey"/>/<see cref="SumFlag"/> for the two ways a point is computed at load.
/// </summary>
public class MapPoint22 : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>
	/// 0x04 — variant key: unless <c>-1</c>, the coordinates are copied from a randomly picked
	/// variant (docs/retail/formats/msn-mission-file.md#variants). Unused in retail.
	/// </summary>
	public short VariantKey { get; set; }
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>0x06 — always -1 in retail; the load does not read it.</summary>
	public short Unk06 { get; set; }

	/// <summary>
	/// 0x08 — when set, the point is the sum of the two earlier points whose GUIDs are the low words
	/// of <see cref="X"/> and <see cref="Y"/>. Unused in retail.
	/// </summary>
	public short SumFlag { get; set; }
	public const int SumFlagWord = 0x08 / 2;

	/// <summary>0x0A — world X.</summary>
	public int X { get; set; }
	public const int XWord = 0x0A / 2;

	/// <summary>0x0E — world Y.</summary>
	public int Y { get; set; }
	public const int YWord = 0x0E / 2;

	/// <summary>0x12 — world Z (altitude).</summary>
	public int Z { get; set; }
	public const int ZWord = 0x12 / 2;
}
