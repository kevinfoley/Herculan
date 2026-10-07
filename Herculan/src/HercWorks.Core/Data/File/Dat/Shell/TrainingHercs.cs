using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - SHELL/GAM/TRN_HERC.DAT — nine HERC catalog records (<see cref="ShellHercData"/>), one per
/// chassis type, with no count prefix: a second stock-fit set beside the <c>INI_*.DAT</c> files.
/// Whether anything loads it is open; see
/// docs/retail/formats/herc-catalogs.md#gamtrn_hercdat--a-second-stock-fit-set.
/// </summary>
public sealed class TrainingHercs {
	public List<ShellHercData>? Data { get; set; }
}
