using HercWorks.Core.Data.File;
using Herculan.Engine.Cockpit;
namespace Herculan.Engine.Content;

/// <summary>
/// <c>str\SYSTEM.STR</c> — the cockpit computer's own message set: every line it says, and for each
/// one the recorded clip that says it. See docs/retail/formats/cockpit-messages.md, "The computer's
/// messages".
///
/// <para>The file is an ordinary <c>.STR</c> string table (see docs/retail/formats/str-strings.md) of two
/// groups, 40 entries then 23, each carrying eight attribute bytes. <b>The groups are not a
/// classification</b> — nothing in the simulator addresses a message by group. Call sites pass one
/// flat number, and that number is the entry's position counted straight through both groups, which
/// is also what attribute byte 0 holds. So this type flattens on load and indexes by that
/// number.</para>
///
/// <para>Reached from the simulation through <see cref="Audio.ComputerVoice"/>, which is what turns
/// a posted message into the sound of one.</para>
/// </summary>
public sealed class SystemMessages {
	/// <summary>The resource this is read from.</summary>
	public const string ResourceName = "SYSTEM.STR";

	/// <summary>How many attribute bytes an entry carries.</summary>
	public const int AttributeCount = 8;

	private readonly Entry[] _entries;

	private SystemMessages(Entry[] entries) {
		_entries = entries;
	}

	/// <summary>One message: the text, and the voice clip that reads it aloud.</summary>
	public readonly record struct Entry(int Id, string Text, int VoiceClip, byte[] Attributes);

	/// <summary>Every message, indexed by the flat id the call sites use.</summary>
	public IReadOnlyList<Entry> Entries => _entries;

	/// <summary>How many messages the file held.</summary>
	public int Count => _entries.Length;

	/// <summary>Message <paramref name="id"/>, or null when the id is outside the table.</summary>
	public Entry? this[int id] => id >= 0 && id < _entries.Length ? _entries[id] : null;

	/// <summary>
	/// Reads the message set out of the mounted archives, or null when <c>SYSTEM.STR</c> is absent or
	/// does not parse.
	/// </summary>
	public static SystemMessages? Load(GameContent content) =>
		SimStrings.Load(content, ResourceName) is { } table ? FromTable(table) : null;

	/// <summary>
	/// Flattens an already-parsed <c>.STR</c>. An entry whose attributes are short is kept with no
	/// voice clip rather than dropped, so that ids past it keep their positions.
	/// </summary>
	public static SystemMessages FromTable(StringFile table) {
		var entries = new List<Entry>();

		for (int group = 0; group < table.GroupCount; group++) {
			foreach (var entry in table.Group(group)) {
				var attributes = entry.Attributes;
				int clip = attributes.Length >= AttributeCount ? attributes[VoiceClipAttribute] : 0;
				entries.Add(new Entry(entries.Count, entry.Text, clip, attributes));
			}
		}

		return new SystemMessages(entries.ToArray());
	}

	/// <summary>
	/// Attribute byte 7 — which <c>CVM_nnnn.WAV</c> in the voice archive reads this line, one-based.
	///
	/// <para>It is a field and not an offset from the id: the numbering runs 1 to 66 across the 63
	/// messages with three values skipped, and the archive holds exactly 66 clips. Those three are
	/// recorded lines no message claims.</para>
	/// </summary>
	public const int VoiceClipAttribute = 7;

	/// <summary>
	/// Attribute byte 0 — the flat message id, which is also the entry's own position. Carried
	/// because the file carries it; <see cref="Entry.Id"/> is the position and is what to index on.
	///
	/// <para>It is the id, not the position, that the original keys on:
	/// <c>SystemMessages_Index</c> (<c>00435970</c>) scatters the strings into a 63-slot table at
	/// <c>base + attr[0] * 9</c> and counts how many landed in each slot, so two entries sharing an id
	/// would become variants of one message and the post would roll between them. The retail file
	/// gives all 63 distinct ids, so every count is one and no variant exists.</para>
	/// </summary>
	public const int IdAttribute = 0;

	/// <summary>
	/// Attribute byte 2 — queue priority. <c>MessagePort.Post</c> inserts before the first queued
	/// message of strictly higher value, so low sorts first. Zero on every entry in the retail file,
	/// which makes the queue plain arrival order.
	/// </summary>
	public const int PriorityAttribute = 2;

	/// <summary>
	/// Attribute byte 3 — the shortest time the line stays on screen once it is up, after which it
	/// will step aside for a message waiting behind it. In units of
	/// <see cref="MessagePort.TicksPerTimingUnit"/>, so effectively seconds.
	/// </summary>
	public const int MinDisplayAttribute = 3;

	/// <summary>Attribute byte 4 — the longest it stays up, after which it comes down regardless.</summary>
	public const int MaxDisplayAttribute = 4;

	/// <summary>Attribute byte 5 — how long after posting before it may be shown. Zero throughout.</summary>
	public const int MinDelayAttribute = 5;

	/// <summary>Attribute byte 6 — how long after posting it is dropped unshown. 20 throughout.</summary>
	public const int MaxDelayAttribute = 6;

	/// <summary><c>WAYPOINT REACHED</c>. Posted by <see cref="Sim.MechObject.PlayerThink"/> and <see cref="NavMarker.Tick"/>.</summary>
	public const int WaypointReached = 0x1d;

	/// <summary><c>MISSION FAILED</c>. Posted by <see cref="Sim.MissionObjectives.MessageFor"/>.</summary>
	public const int MissionFailed = 0x16;

	/// <summary><c>MISSION SUCCESSFUL</c>. Posted by <see cref="Sim.MissionObjectives.MessageFor"/>.</summary>
	public const int MissionSuccessful = 0x17;

	/// <summary><c>MISSION TARGET DETECTED</c>. Posted by <see cref="Sim.MechObject.TargetDetectedArm"/>.</summary>
	public const int MissionTargetDetected = 0x19;

	/// <summary><c>APPROACHING MISSION ZONE BOUNDARY</c>. Posted by <see cref="Sim.MissionObjectives.MessageFor"/>.</summary>
	public const int ApproachingZoneBoundary = 0x1e;

	/// <summary><c>RULES OF ENGAGEMENT VIOLATED. MISSION ABORTED.</c> Posted by <see cref="Sim.MissionObjectives.MessageFor"/>.</summary>
	public const int RulesOfEngagementViolated = 0x20;

	/// <summary>
	/// <c>ENGAGING DATA LINK</c>. Posted by <see cref="Sim.MechObject.DataLinkArm"/>. The first of
	/// four consecutive ids (<c>0x34</c> to <c>0x37</c>); the poster adds its step number to this one.
	/// </summary>
	public const int EngagingDataLink = 0x34;

	/// <summary><c>DATA TRANSFER ABORTED</c>. Posted by <see cref="Sim.MechObject.DataLinkArm"/>.</summary>
	public const int DataTransferAborted = 0x38;

	/// <summary>
	/// <c>POWERUP INITIATED. ALL SYSTEMS NOMINAL.</c> Posted by <see cref="Audio.GameAudio.AnnouncePowerUp"/>
	/// when the machine's internals read no damage.
	/// </summary>
	public const int PowerUpNominal = 0x21;

	/// <summary>
	/// <c>POWERUP INITIATED. INTERNAL DAMAGE DETECTED.</c> Posted by
	/// <see cref="Audio.GameAudio.AnnouncePowerUp"/> in place of <see cref="PowerUpNominal"/> when one
	/// of the first ten internals reads any damage.
	/// </summary>
	public const int PowerUpDamaged = 0x22;

	/// <summary><c>AUTO TRACKING ENGAGED</c>. Posted by <see cref="Sim.MechObject.ToggleAutoTrack"/>.</summary>
	public const int AutoTrackingEngaged = 0x26;

	/// <summary><c>AUTO TRACKING DISABLED</c>. Posted by <see cref="Sim.MechObject.ToggleAutoTrack"/>.</summary>
	public const int AutoTrackingDisabled = 0x27;

	/// <summary><c>JAMMING ENGAGED</c>. Posted by <see cref="Sim.MechObject.PodTick"/>.</summary>
	public const int JammingEngaged = 0x2a;

	/// <summary><c>JAMMING DISABLED</c>. Posted by <see cref="Sim.MechObject.PodTick"/>.</summary>
	public const int JammingDisabled = 0x2b;

	/// <summary><c>ACTIVE RADAR MODE</c>. Posted by <see cref="Sim.MechObject.ToggleScanner"/>.</summary>
	public const int ActiveRadarMode = 0x2c;

	/// <summary><c>PASSIVE RADAR MODE</c>. Posted by <see cref="Sim.MechObject.ToggleScanner"/>.</summary>
	public const int PassiveRadarMode = 0x2d;

	/// <summary><c>INTERNAL DAMAGE: SHIELD GENERATOR</c>. Posted by <see cref="Sim.MechObject.ComponentDamageWrite"/>.</summary>
	public const int InternalDamageShieldGenerator = 0x03;

	/// <summary><c>INTERNAL DAMAGE: ENGINE</c>. Posted by <see cref="Sim.MechObject.ComponentDamageWrite"/>.</summary>
	public const int InternalDamageEngine = 0x04;

	/// <summary><c>INTERNAL DAMAGE: LEG SERVOS</c>. Posted by <see cref="Sim.MechObject.GradeLegs"/>.</summary>
	public const int InternalDamageLegServos = 0x08;

	/// <summary><c>SHIELD GENERATOR DESTROYED</c>. Posted by <see cref="Sim.MechObject.ComponentDamageWrite"/>.</summary>
	public const int ShieldGeneratorDestroyed = 0x0c;

	/// <summary><c>WEAPON DESTROYED</c>. Posted by <see cref="Sim.MechObject.ComponentDamageWrite"/>.</summary>
	public const int WeaponDestroyed = 0x10;

	/// <summary>
	/// <c>DAMAGE LEVEL CRITICAL</c>. Posted by <see cref="Sim.MechObject.ApplyDirectFireDamage"/>, under a tweak.
	/// Unreachable in retail; see KNOWN_ISSUES.md.
	/// </summary>
	public const int DamageLevelCritical = 0x12;

	/// <summary><c>STRUCTURAL FAILURE IMMINENT</c>. Posted by <see cref="Sim.MechObject.GradeLegs"/>.</summary>
	public const int StructuralFailureImminent = 0x13;

	/// <summary><c>SHIELDS CRITICAL</c>. Posted by <see cref="Sim.MechObject.DirectFireHitTest"/>.</summary>
	public const int ShieldsCritical = 0x15;

	/// <summary><c>ENEMY TARGET DESTROYED</c>. Posted by <see cref="Sim.SimObject.AnnounceNeutralised"/>.</summary>
	public const int EnemyTargetDestroyed = 0x2e;

	/// <summary><c>ENEMY TARGET DISABLED</c>. Posted by <see cref="Sim.SimObject.AnnounceNeutralised"/>.</summary>
	public const int EnemyTargetDisabled = 0x2f;

	/// <summary>
	/// <c>TRANSFERRING DATA</c>. Posted by <see cref="Sim.MechObject.DataLinkArm"/>. The one
	/// id the port treats specially: see <see cref="MessagePort"/>.
	/// </summary>
	public const int TransferringData = 0x36;
}
