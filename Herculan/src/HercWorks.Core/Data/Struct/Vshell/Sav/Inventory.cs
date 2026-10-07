namespace HercWorks.Core.Data.Struct.Vshell.Sav;

/// <summary>
/// Block 1 of <see cref="File.Sav.PlayerSave"/>, the armory stock: one <see cref="InventoryItem"/>
/// per <c>weapons.dat</c> catalog id, all 33 written. See
/// <c>docs/retail/formats/save-games.md#armory-stock-record</c>.
/// </summary>
public class Inventory {
	public InventoryItem[]? Items { get; set; }

	public InventoryItem NewEntry() => new();

	/// <summary>
	/// One weapon's stock record: <c>byte</c> unlock flag, <c>int16</c> owned count, then that many
	/// save-form weapon units.
	/// </summary>
	public class InventoryItem {
		/// <summary>The catalog id; not stored, it is the record's position.</summary>
		public WeaponLUT? Id { get; set; }

		/// <summary>The weapon's unlock flag, <c>weapons.dat</c> record <c>+0x16</c>; a byte on disk.</summary>
		public short UnlockFlag { get; set; }

		/// <summary>How many units the player owns, <c>weapons.dat</c> record <c>+0x17</c>.</summary>
		public short Quantity { get; set; }

		/// <summary>
		/// The owned units. The file's last unit is the head of VSHELL's runtime list, so every save
		/// and load reverses their order.
		/// </summary>
		public ShellWeaponEntry[]? Units { get; set; }

		public InventoryItem() { }

		public InventoryItem(int total) {
			Quantity = (short)total;
			Units = new ShellWeaponEntry[total];
		}
	}
}
