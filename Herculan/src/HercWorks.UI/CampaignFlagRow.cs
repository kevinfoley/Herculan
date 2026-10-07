namespace HercWorks.UI;

/// <summary>
/// One slot of the save's campaign flag array — the 1000-short store the <c>.msn</c> condition
/// system tests and a mission's actions write (docs/retail/shell/campaign-loop.md). Slot 0 is the last
/// mission's outcome; slots 0x3c-0x3f are the four chassis-unlock slots (docs/retail/formats/herc-catalogs.md).
/// </summary>
public class CampaignFlagRow {
	public int Index { get; init; }
	public string Hex => $"0x{Index:x3}";
	public short Value { get; set; }
}
