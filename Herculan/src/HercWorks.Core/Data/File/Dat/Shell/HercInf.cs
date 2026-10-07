using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/HERC_INF.DAT — the chassis stat table: a <c>UINT16</c> count (9), then one
/// 16-byte record per chassis type: type, mass (tons), top speed (kph), the hardpoint count the
/// construction screen prints, price (tons), an unread field, build time (missions) and the
/// availability flag. See docs/retail/formats/herc-catalogs.md#gamherc_infdat--the-chassis-stat-table.
/// </summary>
public class HercInf {
	public short TotalHercs { get; set; }
	public HercInfEntry[] Data { get; set; }

	public HercInf(int totalHercs) {
		TotalHercs = (short)totalHercs;
		Data = new HercInfEntry[totalHercs];
	}
}
