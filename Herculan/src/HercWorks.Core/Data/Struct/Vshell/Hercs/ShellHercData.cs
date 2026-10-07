namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// The <c>gam\*.dat</c> form of the HERC record — <c>hercs.dat</c>, <c>ini_*.dat</c> and
/// <c>trn_herc.dat</c> — read by <c>HercRecord_ReadCatalogForm</c> (<c>00410e79</c>). The save carries
/// the full record as <see cref="Sav.HercBayEntry"/>, whose field names these share; the four fields
/// here are all the catalogs store, and everything else is derived at load. On disk:
/// <c>int16</c> chassis type, build percent, build missions left, mounts occupied, then that many
/// <c>{ int16 hardpoint; </c><see cref="UiWeaponEntry"/><c> }</c>.
///
/// <para>The two build fields are construction state, not damage. See
/// <c>docs/retail/formats/herc-catalogs.md#the-herc-catalog-record</c>.</para>
/// </summary>
public class ShellHercData {
	/// <summary><c>+0x00</c>, the chassis type, 0-8.</summary>
	public short HercId { get; set; }

	/// <summary>Build progress, percent complete. Record <c>+0x4a</c>.</summary>
	public short BuildPercent { get; set; }

	/// <summary>Missions still to run before delivery; 0 once built. Record <c>+0x78</c>.</summary>
	public short BuildMissionsLeft { get; set; }

	/// <summary>The fitted units, keyed by hardpoint; occupied hardpoints only.</summary>
	public Dictionary<short, UiWeaponEntry>? Hardpoints { get; set; }
}
