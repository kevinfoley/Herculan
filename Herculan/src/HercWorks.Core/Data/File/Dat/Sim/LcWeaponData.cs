using HercWorks.Core.Data.Struct.Dbsim;

namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /SIMVOL0/DAT/LC_WPNS.DAT — a <c>UINT16</c> count, then 22-byte <see cref="LcWeaponSlot"/>
/// records, one per weapon slot of a transport (<c>BASES.DAT</c> type <c>0x22</c>). See docs/retail/formats/lc-wpns-dat.md.
/// </summary>
public class LcWeaponData {
	public short Total { get; set; }
	public LcWeaponSlot[]? Entries { get; set; }
}
