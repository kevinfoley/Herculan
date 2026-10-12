namespace HercWorks.Core.Data.File.Msn.Es1;

/// <summary>
/// FILE - ES1's /ZONES.VOL/MSN/*.MSN — a Metaltech: Earthsiege mission, as authored: the revision word
/// and eleven rows, each an <c>int16</c> count and that many records, in the order ES1's loader reads
/// them (docs/es1/formats/msn-mission-file.md#the-layout). Nothing follows the last row.
///
/// <para>The editing model, as ES2's <see cref="MissionFile"/> is: it round-trips a file byte for
/// byte and keeps every record as authored, refs as GUIDs. It does not apply the conditions, variants
/// and repeated GUIDs the load applies (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats).
/// It also holds a file in the other layout (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout):
/// <see cref="HeaderWords"/> set, <see cref="HeaderPatches"/> empty, and one word in each mech's
/// <see cref="Es1Mech.Words4E"/>.</para>
/// </summary>
public class Es1MissionFile {
	/// <summary>The revision word, 5 in every retail file; the load goes on whatever it holds.</summary>
	public short Revision { get; set; }

	/// <summary>
	/// The ten header words that follow the revision in a file in the other layout, which has no
	/// header-patch row; <c>null</c> for a file in the loader's layout.
	/// </summary>
	public short[]? HeaderWords { get; set; }

	/// <summary>Row 1 — the conditions the other rows' condition refs name.</summary>
	public Es1Condition[] Conditions { get; set; } = [];

	/// <summary>Row 2 — the header patches.</summary>
	public Es1HeaderPatch[] HeaderPatches { get; set; } = [];

	/// <summary>Row 3 — the points.</summary>
	public Es1Point[] Points { get; set; } = [];

	/// <summary>Row 4 — the headings.</summary>
	public Es1Heading[] Headings { get; set; } = [];

	/// <summary>Row 5 — the waypoint groups.</summary>
	public Es1WaypointGroup[] WaypointGroups { get; set; } = [];

	/// <summary>Row 6 — the trigger areas.</summary>
	public Es1TriggerArea[] TriggerAreas { get; set; } = [];

	/// <summary>Row 7 — the actions.</summary>
	public Es1Action[] Actions { get; set; } = [];

	/// <summary>Row 8 — the mechs.</summary>
	public Es1Mech[] Mechs { get; set; } = [];

	/// <summary>Row 9 — the flyers.</summary>
	public Es1Flyer[] Flyers { get; set; } = [];

	/// <summary>Row 10 — the bases.</summary>
	public Es1Base[] Bases { get; set; } = [];

	/// <summary>Row 11 — the groups; the first stands for the player's squad.</summary>
	public Es1Group[] Groups { get; set; } = [];
}
