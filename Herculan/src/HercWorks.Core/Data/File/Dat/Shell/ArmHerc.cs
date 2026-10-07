using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/ARM_[HERC].dat — the armory layout for one chassis: two leading layout records
/// (the top and bottom halves of the squad panel's bay picture), then per-weapon groups of hardpoint
/// layout records keyed by weapon id. See docs/retail/formats/herc-catalogs.md#gamarm_dat--armory-layout.
/// </summary>
public class ArmHerc {
	/// <summary>Part id of the top half within its picture — 0 in every retail file.</summary>
	public short TopImgPartId { get; set; }
	public UiImageDBA? HercTopImg { get; set; }

	/// <summary>Part id of the bottom half within its picture — 1 in every retail file.</summary>
	public short BottomImgPartId { get; set; }
	public UiImageDBA? HercBotImg { get; set; }

	/// <summary>Number of per-weapon groups that follow.</summary>
	public short TotalWeapons { get; set; }

	/// <summary>Per-weapon groups: weapon id to that weapon's layout record for each hardpoint.</summary>
	public Dictionary<short, UiHardpointGraphic[]>? WeaponHardpoints { get; set; }
}
