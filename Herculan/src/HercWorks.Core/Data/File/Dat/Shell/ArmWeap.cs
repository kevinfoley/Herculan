using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/ARM_WEAP.DAT — the weapons screen's panel layout against
/// <c>dba\arm_weap.dba</c>: one panel per weapon the armory offers (26 in retail), then one per
/// guidance kind (4). See docs/retail/formats/herc-catalogs.md#gamarm_weapdat.
/// </summary>
public class ArmWeap {
	public short TotalWeapons { get; set; }

	/// <summary>Number of guidance-kind panels.</summary>
	public short TotalGuidancePanels { get; set; }

	/// <summary>The weapon panels, each carrying its weapon id. A weapon with no panel cannot be bought.</summary>
	public UiHardpointGraphic[]? Entries { get; set; }

	/// <summary>The guidance-kind panels, each carrying its ammo type: SARH 0, ARH 1, ARM 2, EO 3.</summary>
	public UiHardpointGraphic[]? GuidancePanels { get; set; }

	public ArmWeap() { }

	public ArmWeap(short totalWeapons) {
		TotalWeapons = totalWeapons;
		Entries = new UiHardpointGraphic[totalWeapons];
	}
}
