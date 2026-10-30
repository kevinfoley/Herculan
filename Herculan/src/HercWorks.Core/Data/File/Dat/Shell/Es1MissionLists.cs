namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// One of the shell's name lists: a count byte and that many NUL-terminated mission or list names
/// (docs/es1/formats/bst-battle-sets.md#name-lists). The whole of ES1's <c>bst\ANY_*.DAT</c>, and each position of a battle
/// set and each list of <c>msn\SING_MSN.DAT</c>, <c>cfg\TM_FILES.DAT</c> and the <c>snc\*_FILES.DAT</c>.
/// </summary>
public class Es1NameList {
	/// <summary>The longest name a list holds, its NUL included: <c>StrList_Read</c> (<c>ES1GO 000201b7</c>) reads each into 13 bytes.</summary>
	public const int NameBufferLength = 13;

	/// <summary>The names, in file order; the count byte is their number.</summary>
	public string[] Names { get; set; } = [];

	/// <summary>
	/// The byte after a count of 0, which <c>StrList_Read</c> skips; null where the file ends at the count, as the retail
	/// <c>ANY_CNV.DAT</c> and <c>ANY_ESC.DAT</c> do.
	/// </summary>
	public byte? SkippedByte { get; set; }
}

/// <summary>
/// FILE - ES1's <c>bst\BTLSET_0.DAT</c>, <c>BTLSET_1.DAT</c>, <c>BTLSET_2.DAT</c> and <c>TEST_BTL.DAT</c> in
/// <c>ZONES.VOL</c>: a count byte of campaigns, each a count byte of positions and a name list per position
/// (docs/es1/formats/bst-battle-sets.md#battle-sets). Read by <see cref="Io.Transform.Shell.Es1BattleSetTransformer"/>.
/// </summary>
public class Es1BattleSet {
	public Es1Campaign[] Campaigns { get; set; } = [];

	/// <summary>
	/// <c>BattleSet_NextCampaignTable</c> (<c>ES1GO 0007ead0</c>): per campaign 0 to 7, the campaign after its last position
	/// for outcome 0 and outcome 1; 99 a career won and −1 none (docs/es1/shell/between-missions.md#moving-on).
	/// </summary>
	public static readonly short[,] NextCampaignTable = {
		{ 1, 2 }, { 3, 4 }, { 3, -1 }, { 5, 6 }, { 5, -1 }, { 7, -1 }, { 7, -1 }, { 99, -1 },
	};

	/// <summary>
	/// <c>BattleSet_PeekNextCampaign</c> (<c>0001418e</c>)(set, <paramref name="outcome"/>, <paramref name="position"/>,
	/// <paramref name="campaign"/>): 0 when the position after <paramref name="position"/> is in the campaign
	/// (<see cref="NextPosition"/>), otherwise <see cref="NextCampaignTable"/>'s campaign; nothing written. Retail indexes the
	/// campaigns and the table with no bounds test; a campaign outside either reads as having no positions here and an
	/// outcome or campaign outside the table gives −1, engine guards.
	/// </summary>
	public short PeekNextCampaign(int outcome, short position, short campaign) =>
		NextPosition(campaign, ref position) ? (short)0 : NextCampaign(campaign, outcome);

	/// <summary>
	/// <c>BattleSet_Advance</c> (<c>000141d0</c>)(set, <paramref name="outcome"/>, position*, campaign*): the position moved
	/// on (<see cref="NextPosition"/>); past the campaign's last, the campaign set to <see cref="NextCampaignTable"/>'s and
	/// the position to 0, answering true; otherwise false. Bounds as <see cref="PeekNextCampaign"/>'s.
	/// </summary>
	public bool Advance(int outcome, ref short position, ref short campaign) {
		if (NextPosition(campaign, ref position)) {
			return false;
		}

		campaign = NextCampaign(campaign, outcome);
		position = 0;
		return true;
	}

	/// <summary>
	/// <c>Campaign_NextPosition</c> (<c>0001431f</c>)(campaign, position*): 1 added to the position; true when it is at least
	/// 0 and below the campaign's position count.
	/// </summary>
	private bool NextPosition(short campaign, ref short position) {
		position++;
		int count = campaign >= 0 && campaign < Campaigns.Length ? Campaigns[campaign].Positions.Length : 0;
		return position >= 0 && position < count;
	}

	private static short NextCampaign(short campaign, int outcome) =>
		campaign >= 0 && campaign < NextCampaignTable.GetLength(0) && outcome is 0 or 1 ? NextCampaignTable[campaign, outcome] : (short)-1;
}

/// <summary>One campaign of a battle set: its mission positions in play order.</summary>
public class Es1Campaign {
	/// <summary>
	/// Per position, the list a mission is taken from: name 0 after outcome 0 and name 1 after outcome 1, or the only name
	/// when the list holds one (docs/es1/formats/bst-battle-sets.md#a-careers-missions).
	/// </summary>
	public Es1NameList[] Positions { get; set; } = [];
}

/// <summary>
/// FILE - ES1's <c>msn\SING_MSN.DAT</c> in <c>ZONES.VOL</c>: a count byte and that many name lists, the single missions'
/// lists (docs/es1/formats/bst-battle-sets.md#single-missions). Read by
/// <see cref="Io.Transform.Shell.Es1SingleMissionsTransformer"/>.
/// </summary>
public class Es1SingleMissions {
	public Es1NameList[] Lists { get; set; } = [];
}
