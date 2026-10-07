namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// Row #8 (10 fixed bytes/record + 2 bytes per waypoint) — an ordered list of row #6
/// (<see cref="MapPoint22"/>) positions: a route, <c>script.dat</c> block 3. Each waypoint is 2 bytes
/// on disk, though VSHELL's in-memory slot is 6. See docs/retail/formats/msn-mission-file.md, "Row #8 field
/// decode".
/// </summary>
public class WaypointGroup : MapObject {
	/// <summary>0x02 — condition ref. Retail sets it only on records whose GUID is -1, i.e. variant sources.</summary>
	public short ConditionRef { get; set; }
	public const int ConditionRefWord = 0x02 / 2;

	/// <summary>
	/// 0x04 — variant key: unless <c>-1</c>, the waypoint list is copied from a randomly picked variant
	/// (docs/retail/formats/msn-mission-file.md#variants); such records store no waypoints of their own.
	/// </summary>
	public short VariantKey { get; set; }
	public const int VariantKeyWord = 0x04 / 2;

	/// <summary>0x06 — always -1 in retail; the load does not read it.</summary>
	public short Unk06 { get; set; }

	/// <summary>The waypoints, refs into row #6 in route order. Their count is stored at 0x08.</summary>
	public short[] Waypoints { get; set; } = Array.Empty<short>();
}
