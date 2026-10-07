using HercWorks.Core.Data.Struct.Vshell.Hercs;

namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - /SHELL/GAM/WEAPONS.DAT — the shell's weapon catalog: a <c>UINT16</c> count (33), then per
/// weapon its id, a length-prefixed NUL-terminated catalog code, price, unlock flag and rank; then a
/// <c>UINT16</c> count and the armory's starting stock as weapon units (id, condition, ammo type).
/// See docs/retail/formats/weapons-dat.md#file-level-format; the 29-byte in-memory record this loads into
/// is not the same shape as the on-disk one.
/// </summary>
public class WeaponsDat {
	public short TotalCount { get; set; }
	public Entry[] Data { get; set; }
	public short StartWeaponTotal { get; set; }
	public UiWeaponEntry[]? StartingWeapons { get; set; }

	public WeaponsDat(int total) {
		TotalCount = (short)total;
		Data = new Entry[total];
	}

	public Entry AddEntry(int idx) {
		var item = new Entry();
		Data[idx] = item;
		return item;
	}

	public class Entry {
		public short Id { get; set; }
		public short NameLen { get; set; }
		public byte[]? Name { get; set; }

		/// <summary>Price in tons; VSHELL scales it x1000 at load to give the cost in kg.</summary>
		public short SalvageCost { get; set; }

		/// <summary>The weapon's starting unlock flag, campaign state from then on.</summary>
		public byte StartUnlock { get; set; }

		/// <summary>
		/// The rank: the order <c>Armory_AutoFillQueue</c> walks the catalog in, low first, queueing a
		/// weapon that is unlocked and owned fewer than twice. Ranks 1-28 run advanced-to-basic (PLAS 1,
		/// MSL6 28); NONE and the three Bull weapons hold 99, MFAC 0. Stored in a parallel array rather
		/// than inside the 29-byte record. See docs/retail/shell/armory.md and
		/// docs/retail/formats/weapons-dat.md#the-rank-byte-and-what-retail-actually-fits.
		/// </summary>
		public short AutobuildPriority { get; set; }
	}
}
