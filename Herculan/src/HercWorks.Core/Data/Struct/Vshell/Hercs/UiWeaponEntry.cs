using HercWorks.Core.Util;

namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// The six-byte <c>gam\*.dat</c> form of a weapon unit (<c>WeaponUnit_ReadCatalogForm</c>,
/// <c>00411a36</c>): record <c>+0x00</c>, <c>+0x06</c> and <c>+0x08</c>. The hardpoint it sits on
/// is the parent's key. <see cref="Sav.ShellWeaponEntry"/> is the full save form. See
/// <c>docs/retail/formats/herc-catalogs.md#the-weapon-unit-record</c>.
/// </summary>
public class UiWeaponEntry {
	/// <summary>The weapon catalog id.</summary>
	public short WeaponId { get; set; }

	/// <summary>The unit's condition, 0-100.</summary>
	public short Condition { get; set; }

	/// <summary>The ammo type — the guidance kind 0-3 (SARH, ARH, ARM, EO), or 5 for none.</summary>
	public MissileType? Guidance { get; set; }

	public UiWeaponEntry() { }

	public UiWeaponEntry(short weaponId, short condition, MissileType guidance) {
		WeaponId = weaponId;
		Condition = condition;
		Guidance = guidance;
	}

	public byte[] ToByte() {
		var data = new byte[6];

		ByteOps.ShortLEToByteArr(data, 0, WeaponId);
		ByteOps.ShortLEToByteArr(data, 2, Condition);
		ByteOps.ShortLEToByteArr(data, 4, (short)(Guidance?.Id ?? 0));

		return data;
	}
}
