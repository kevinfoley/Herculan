namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// FILE - /ZONES.VOL/MSN/*.MSN — a mission, as authored: a revision word (always 5) followed by 17
/// rows in a fixed order, most a <c>[uint16 count]</c> then that many fixed-size records. See
/// docs/retail/formats/msn-mission-file.md.
///
/// <para>This is the editing model: it round-trips a file byte for byte and keeps every record as
/// authored, refs as GUIDs, resolved on demand by the lookups below. It does not apply the
/// conditions, variants, merges and renumbering the game's own load does — that is
/// <see cref="Io.Transform.Common.MissionGenerator"/>, which produces <c>script.dat</c>.</para>
/// </summary>
public class MissionFile {
	/// <summary>The revision word; always 5, which VSHELL asserts.</summary>
	public short Revision { get; set; }

	/// <summary>Row #1 — the conditions every other row's condition ref names.</summary>
	public MissionCondition14[]? Conditions { get; set; }

	/// <summary>Row #2 — the mission's settings: its zone, theater and the rest of the <c>script.dat</c> header, and campaign flags to reset. See <see cref="MissionSettingsPatch"/>.</summary>
	public MissionSettingsPatch[]? SettingsPatches { get; set; }

	/// <summary>Row #3 — condition-gated values: the briefing and debrief movie ids.</summary>
	public VariantValue8[]? Variants { get; set; }

	/// <summary>Row #4 — the mission's objective, briefing and intelligence text. Retail missions have at most one.</summary>
	public MissionText144[]? Texts { get; set; }

	/// <summary>
	/// Row #5 — the debrief text, 64 bytes per record, which the mission load skips and the campaign
	/// debrief reads (docs/retail/formats/msn-mission-file.md#row-5--the-debrief). Kept raw.
	/// </summary>
	public byte[]? DebriefBytes { get; set; }

	/// <summary>Row #6 — world positions.</summary>
	public MapPoint22[]? Points { get; set; }

	/// <summary>Row #7 — headings.</summary>
	public Heading10[]? Headings { get; set; }

	/// <summary>Row #8 — routes.</summary>
	public WaypointGroup[]? WaypointGroups { get; set; }

	/// <summary>Row #9 — trigger areas.</summary>
	public TriggerArea12[]? TriggerAreas { get; set; }

	/// <summary>Row #10 — mission actions.</summary>
	public MissionAction82[]? Actions { get; set; }

	/// <summary>Row #11 — mission timers.</summary>
	public ActionTimer30[]? ActionTimers { get; set; }

	/// <summary>Row #12 — the mech roster.</summary>
	public MechRosterEntry144[]? Mechs { get; set; }

	/// <summary>Row #13 — the flyer roster.</summary>
	public FlyerRosterEntry102[]? Flyers { get; set; }

	/// <summary>Row #14 — the base roster.</summary>
	public BaseRosterEntry62[]? Bases { get; set; }

	/// <summary>Row #15 — mission-group orders.</summary>
	public MissionOrder22[]? Orders { get; set; }

	/// <summary>Row #16 — mission groups.</summary>
	public MissionGroup164[]? Groups { get; set; }

	/// <summary>
	/// Row #17 — mission objectives. An entry is null only where the file ends inside it — retail's
	/// one case is DEMO2.MSN; see <see cref="TruncatedRow17Tail"/>.
	/// </summary>
	public MissionObjective58?[]? Objectives { get; set; }

	/// <summary>
	/// The bytes of a row #17 record the file ends inside of (DEMO2.MSN: 16 where 58 were due), null
	/// otherwise. Kept so a write reproduces the file, truncation included.
	/// </summary>
	public byte[]? TruncatedRow17Tail { get; set; }

	public VariantValue8? GetVariant(short guid) => FindByGuid(Variants, guid);
	public MapPoint22? GetPoint(short guid) => FindByGuid(Points, guid);
	public Heading10? GetHeading(short guid) => FindByGuid(Headings, guid);
	public WaypointGroup? GetWaypointGroup(short guid) => FindByGuid(WaypointGroups, guid);
	public TriggerArea12? GetTriggerArea(short guid) => FindByGuid(TriggerAreas, guid);
	public MissionAction82? GetAction(short guid) => FindByGuid(Actions, guid);
	public ActionTimer30? GetActionTimer(short guid) => FindByGuid(ActionTimers, guid);
	public MechRosterEntry144? GetMech(short guid) => FindByGuid(Mechs, guid);
	public FlyerRosterEntry102? GetFlyer(short guid) => FindByGuid(Flyers, guid);
	public BaseRosterEntry62? GetBase(short guid) => FindByGuid(Bases, guid);
	public MissionOrder22? GetOrder(short guid) => FindByGuid(Orders, guid);
	public MissionGroup164? GetGroup(short guid) => FindByGuid(Groups, guid);

	/// <summary>
	/// The first record of a row with the GUID, or null for -1. Each row's refs name records of one
	/// specific other row, so the search is per row.
	/// </summary>
	private static T? FindByGuid<T>(T[]? rows, short guid) where T : MapObject =>
		guid == -1 ? null : rows?.FirstOrDefault(r => r.GUID == guid);
}
