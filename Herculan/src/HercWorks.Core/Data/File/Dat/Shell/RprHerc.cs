using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/RPR_[herc].DAT — the repair-bay layout for one chassis: a counted list of
/// layout records for the chassis' own components (drawn from <c>dba\rpr_[herc].dba</c>), one record
/// for the internals diagram (from <c>dba\[herc]_int.dba</c>), then per-weapon groups of hardpoint
/// layout records keyed by weapon id. Each record is part id, x, y, frame and blit flags. See
/// docs/retail/formats/herc-catalogs.md#gamrpr_dat--repair-bay-layout.
/// </summary>
public class RprHerc {
	public short BodyImgTotal { get; set; }
	public Dictionary<short, UiImageDBA>? BodyImages { get; set; }

	public UiHardpointGraphic? InternalImage { get; set; }

	/// <summary>Number of per-weapon groups that follow.</summary>
	public short TotalHardpoints { get; set; }
	public Dictionary<short, UiHardpointGraphic[]>? WeaponHardpoints { get; set; }
}
