using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// One hangar bay's HERC record in <see cref="File.Sav.PlayerSave"/> — the save form, written by
/// <c>Herc_Write</c> (<c>0041123e</c>), in the order the properties appear. The <c>gam\*.dat</c>
/// catalogs carry a shorter form of the same record, <see cref="ShellHercData"/>. See
/// <c>docs/retail/formats/save-games.md#herc-record--122-bytes-0x7a-in-memory</c>.
/// </summary>
public class HercBayEntry {
	/// <summary><c>+0x00</c>, the chassis type, 0-8.</summary>
	public HercLUT? ChassisType { get; set; }

	/// <summary>
	/// <c>+0x02</c>, the chassis type again through <c>Herc_TypeToIndex</c>'s identity map — the one
	/// the chassis stat table and the <c>estext.bin</c> name are indexed by.
	/// </summary>
	public short ChassisIndex { get; set; }

	/// <summary>The status block's 13 external facet conditions, 0-100.</summary>
	public Dictionary<HercExternals, ShellHercPart>? ExternalConditions { get; set; }

	/// <summary>
	/// The status block's ten internal entries: the nine internal components, then (as
	/// <see cref="HercInternals.Pilot"/>) the machine's overall condition.
	/// </summary>
	public Dictionary<HercInternals, ShellHercPart>? InternalConditions { get; set; }

	/// <summary>The status block's ten per-hardpoint conditions, one per mount slot.</summary>
	public ShellHercPart[] HardpointConditions { get; set; } = new ShellHercPart[10];

	/// <summary><c>+0x4a</c>, build progress, percent complete.</summary>
	public short BuildPercent { get; set; }

	/// <summary><c>+0x78</c>, missions of construction left before delivery.</summary>
	public short BuildMissionsLeft { get; set; }

	/// <summary><c>+0x4c</c>, the mount capacity.</summary>
	public short MountCapacity { get; set; }

	/// <summary>
	/// <c>+0x4e</c>, mounts occupied — the reader's loop count for <see cref="Mounts"/>, which
	/// serializes occupied slots only.
	/// </summary>
	public short MountsOccupied { get; set; }

	/// <summary>The fitted weapon units, keyed by mount slot.</summary>
	public Dictionary<short, ShellWeaponEntry> Mounts { get; set; } = new();
}
