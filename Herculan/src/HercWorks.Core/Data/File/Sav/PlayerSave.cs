using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace HercWorks.Core.Data.File.Sav;

/// <summary>
/// FILE - [root]/SAV/GAME_?.SAV — the campaign save. Slots are named by index, not by pilot:
/// <c>GAME_0</c>-<c>GAME_9</c> are the player-named slots, <c>GAME_R</c> the campaign autosave and
/// <c>GAME_T</c> the training autosave. <c>sav\GAMEFILE.STR</c> is the directory that maps a slot to
/// its filename and display label.
///
/// <para>There is no header, no magic and no length field — the file is a bare concatenation of the
/// blocks in the property order below, so it must be parsed by structure. Its writer opens without
/// <c>O_TRUNC</c>, so a shorter save over a longer one leaves a stale tail that is not a parse
/// failure. See <c>docs/retail/formats/save-games.md#savgame_sav--block-order</c>.</para>
/// </summary>
public class PlayerSave {
	/// <summary>Block 1, the armory stock: one record per weapon catalog id.</summary>
	public Inventory? Inventory { get; set; }

	/// <summary>Block 2's leading short — the armory build queue's free-slot count (<c>0046f8d4</c>).</summary>
	public short BuildQueueFreeSlots { get; set; }

	/// <summary>
	/// Block 2's five queue slots, the weapon id of each (<c>0046f8d6</c>). On disk each is preceded by
	/// its slot index, which is written as the array position rather than stored here.
	/// </summary>
	public WeaponLUT[] BuildQueue { get; set; } = new WeaponLUT[5];

	/// <summary>
	/// The career block, 76 shorts / 152 bytes: campaign stage, mission within the stage, then three
	/// counted arrays of line indices into <c>data\mission.str</c> holding the briefing and debrief
	/// prose, then the briefing movie id. The mission number here is cosmetic — VSHELL takes the
	/// actual mission from the slot's own <c>script.dat</c>. The named views below read it.
	///
	/// <para>Size is load-bearing: 152 bytes is the only length that leaves the 36 pilot records
	/// following it aligned. See <c>docs/retail/formats/save-games.md#career-block--152-bytes</c>.</para>
	/// </summary>
	public short[] CareerBlock { get; set; } = new short[76];

	/// <summary>Block 4's 36 pilot records, three squads of twelve.</summary>
	public PilotEntry[]? Squadmates { get; set; }

	/// <summary>
	/// The six shorts closing the squad block plus the two opening the player block — eight in all,
	/// sitting between the last squadmate record and the player's own pilot record. Read through
	/// <see cref="SquadMemberIndex"/>, <see cref="SquadPositionsInPlay"/> and
	/// <see cref="MachinesOnStrength"/>; shorts 3-5 are each squad's next member (<c>00483b4e</c>).
	/// </summary>
	public short[] SquadTailAndPlayerHead { get; set; } = new short[8];

	/// <summary>Block 5's pilot record — the same shape as a squadmate's.</summary>
	public PilotEntry? PlayerPilot { get; set; }

	/// <summary>Block 6, the hangar: occupied bays only, keyed by bay index.</summary>
	public Dictionary<short, HercBayEntry> HercBay { get; set; } = new();

	/// <summary>
	/// Block 7, the nine chassis availability flags — <c>herc_inf.dat</c> record <c>+0x0e</c> for each
	/// chassis type. See <c>docs/retail/formats/herc-catalogs.md#gamherc_infdat--the-chassis-stat-table</c>.
	/// </summary>
	public Dictionary<HercLUT, short> ChassisAvailability { get; set; } = new();

	/// <summary>Block 8, the salvage pool in kilograms (<c>00482af4</c>).</summary>
	public int SalvageTotal { get; set; }

	/// <summary>
	/// Everything past the salvage pool, carried verbatim: the 2000-byte campaign flag array, the
	/// 2-byte game state, a 20-byte block (<c>004832c8</c>) whose content is not established, and any
	/// stale tail the non-truncating writer left behind.
	/// </summary>
	public byte[]? CampaignStateTail { get; set; }

	// ---- Named views over the raw arrays above -------------------------------------------------
	// The arrays stay the storage, so the round trip is unchanged; these name the fields
	// docs/retail/formats/save-games.md decodes.

	/// <summary>
	/// Career block short 0 — the campaign stage, <c>0046fb18</c> as the shell runs it: 0 for training and
	/// 1-5 for the campaign's chapters, the rows of <c>gam\career.dat</c>.
	/// </summary>
	public short CampaignStage { get => CareerBlock[0]; set => CareerBlock[0] = value; }

	/// <summary>Career block short 1 — the mission within the stage.</summary>
	public short MissionInStage { get => CareerBlock[1]; set => CareerBlock[1] = value; }

	/// <summary>
	/// The career block's three counted arrays of <c>data\mission.str</c> line indices (10, 30 and 30
	/// slots, <c>-1</c> empty) that <c>Career_BuildBriefingText</c> assembles into the mission tab's
	/// objectives, briefing and intelligence report. Returned as <c>(count, lines)</c>; read-only views,
	/// since the lines only mean anything against the slot's own <c>missn%d.str</c>.
	/// </summary>
	public (short Count, short[] Lines) CareerObjectives => CareerArray(2, 10);
	public (short Count, short[] Lines) CareerBriefing => CareerArray(13, 30);
	public (short Count, short[] Lines) CareerIntelligence => CareerArray(44, 30);

	/// <summary>
	/// Career block short 75 — the briefing movie's id (<c>004840b8</c>), an index into VSHELL's
	/// movie table that the mission tab's briefing plays.
	/// </summary>
	public short BriefingMovie { get => CareerBlock[75]; set => CareerBlock[75] = value; }

	private (short, short[]) CareerArray(int countIndex, int length) =>
		(CareerBlock[countIndex], CareerBlock.Skip(countIndex + 1).Take(length).ToArray());

	/// <summary>
	/// The three shorts at <c>00483b48</c>: for each squad, the index of its current member among
	/// that squad's twelve pilot records.
	/// </summary>
	public short SquadMemberIndex(int squad) => SquadTailAndPlayerHead[squad];

	/// <summary>Squad positions in play, the player's as position 0 (<c>00482a78</c>).</summary>
	public short SquadPositionsInPlay { get => SquadTailAndPlayerHead[6]; set => SquadTailAndPlayerHead[6] = value; }

	/// <summary>Machines on strength (<c>00482a7a</c>) — the <c>player.mec</c> export's entry count.</summary>
	public short MachinesOnStrength { get => SquadTailAndPlayerHead[7]; set => SquadTailAndPlayerHead[7] = value; }

	/// <summary>The campaign flag array (<c>00482af8</c>): 1000 shorts, the <c>.msn</c> condition store.</summary>
	public const int CampaignFlagCount = 1000;

	/// <summary>
	/// Whether the tail is long enough to hold the flag array and the game state — true for every
	/// parsed retail save.
	/// </summary>
	public bool HasCampaignState => CampaignStateTail is { Length: >= CampaignFlagCount * 2 + 2 };

	public short GetCampaignFlag(int index) => BitConverter.ToInt16(RequireTail(), index * 2);

	public void SetCampaignFlag(int index, short value) =>
		BitConverter.GetBytes(value).CopyTo(RequireTail(), index * 2);

	/// <summary>The two bytes after the flag array — the game state (<c>0048260e</c>).</summary>
	public short GameState {
		get => BitConverter.ToInt16(RequireTail(), CampaignFlagCount * 2);
		set => BitConverter.GetBytes(value).CopyTo(RequireTail(), CampaignFlagCount * 2);
	}

	private byte[] RequireTail() => HasCampaignState
		? CampaignStateTail!
		: throw new InvalidOperationException("This save's tail is too short to hold the campaign flags.");
}
