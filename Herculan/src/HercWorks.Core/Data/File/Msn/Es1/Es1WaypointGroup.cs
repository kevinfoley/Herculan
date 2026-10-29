namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// Row 5 (10 bytes and 2 per waypoint) — a waypoint group, ES2's row 8 field for field
/// (docs/retail/formats/msn-mission-file.md#row-8-field-decode--the-waypoint-group-dat_00470656-10-fixed-bytes--nested-count2-bytesrecord).
/// </summary>
public class Es1WaypointGroup : MapObject {
	/// <summary>0x02 — condition ref.</summary>
	public short ConditionRef { get; set; }

	/// <summary>0x04 — random table key; a template supplies the waypoint list.</summary>
	public short RandomTableKey { get; set; }

	/// <summary>0x06 — unnamed, as in ES2's record.</summary>
	public short Word06 { get; set; }

	/// <summary>The waypoints, point refs (row 3) in order. Their count is the word at 0x08.</summary>
	public short[] Waypoints { get; set; } = [];
}
