namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// One 16-byte record of <c>gam\herc_inf.dat</c>, the chassis stat table. See
/// <c>docs/retail/formats/herc-catalogs.md#gamherc_infdat--the-chassis-stat-table</c>.
/// </summary>
public class HercInfEntry {
	public const short OutlawHercId = 0;
	public const short RaptorIIHercId = 1;

	/// <summary><c>+0x00</c>, the chassis type, 0-8.</summary>
	public short HercId { get; set; }

	/// <summary><c>+0x02</c>, mass in tons. Printed by the Herc Construction screen as "%d TONS".</summary>
	public short Weight { get; set; }

	/// <summary><c>+0x04</c>, top speed in kph, printed as "%d KPH".</summary>
	public short Speed { get; set; }

	/// <summary>
	/// <c>+0x06</c>, printed bare by the Herc Construction screen — but <b>not</b> what the game equips. Capacity
	/// comes from a nine-entry table in VSHELL's code, and the two disagree for the Raptor II: this
	/// field says 4 where the delivered machine has 5. Treat it as display text, not as the
	/// hardpoint count. See <c>docs/retail/formats/herc-catalogs.md#rejected-readings</c>.
	/// </summary>
	public short HardpointTotal { get; set; }

	/// <summary><c>+0x08</c>, price in tons; VSHELL multiplies by 1000 to charge the salvage pool, which is in kg.</summary>
	public short SalvageReq { get; set; }

	/// <summary>
	/// <c>+0x0a</c>, meaning open (<c>docs/retail/formats/herc-catalogs.md#open</c>). Retail holds 40, 50, 60, 95, 110, 100, 125, 30
	/// and 70 across the nine chassis.
	/// </summary>
	public short Unknown0A { get; set; }

	/// <summary>
	/// <c>+0x0c</c>, missions a newly bought chassis takes to build. <c>Herc_Order</c> copies it into
	/// the HERC record's missions-left counter (<c>+0x78</c>), which ticks down once per debrief while
	/// the build percentage climbs.
	/// </summary>
	public short BuildMissionCount { get; set; }

	/// <summary>
	/// <c>+0x0e</c>, whether this chassis is available. Campaign state rather than a catalog constant: the
	/// four that ship 0 — Raptor II, Ogre, Maverick, Razor — are exactly the four VSHELL can unlock
	/// from campaign flags, and this array is what the save persists.
	/// </summary>
	public short AvailabilityFlag { get; set; }
}
