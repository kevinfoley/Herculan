using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/CAREER.DAT — the campaign stage table: a <c>UINT16</c> stage count (6), then
/// per stage its campaign index, a mission count and that many <c>missions.bin</c> name indices.
/// See docs/retail/shell/campaign-loop.md#the-campaign-table--gamcareerdat.
/// </summary>
public class CareerMissions {
	/// <summary>The stages keyed by campaign index, each with its <c>missions.bin</c> name indices.</summary>
	public Dictionary<MissionSector, int[]>? Sectors { get; set; }

	/// <summary>
	/// The stages in file order, each its campaign index and its <c>missions.bin</c> name indices.
	/// The stage number is the list position — what VSHELL's career position <c>(0046fb18, 0046fb1a)</c>
	/// indexes — which <see cref="Sectors"/> does not keep. See docs/retail/shell/campaign-loop.md.
	/// </summary>
	public List<(short CampaignIndex, int[] Missions)> Stages { get; set; } = new();
}
