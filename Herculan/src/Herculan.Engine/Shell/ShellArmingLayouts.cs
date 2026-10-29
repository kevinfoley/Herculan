using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The nine <c>gam\arm_*.dat</c> layouts, one per chassis: the bay picture's parts and, for each weapon, a record per socket
/// it can be drawn in. Which weapon a mount takes is read from them (<see cref="HasSocket"/>), and the bay picture
/// (<see cref="ShellBayPictures"/>) draws from them. See docs/retail/shell/squad-and-crew.md, "The bay picture".
/// </summary>
public sealed class ShellArmingLayouts {
	/// <summary>The layout files, in chassis-type order — the table at <c>0046ff2c</c>.</summary>
	private static readonly string[] LayoutStems = { "OUTL", "RAPT", "TOMA", "SAMS", "COLO", "APOC", "OGRE", "MAVR", "RAZR" };

	/// <summary>
	/// The part slot a mount's weapon is looked up by in its weapon's group, <c>slot + 2</c> — past the
	/// two halves of the body, which every retail layout puts in slots 0 and 1.
	/// </summary>
	private const int FirstWeaponPart = 2;

	private readonly ArmHerc?[] _layouts;

	private ShellArmingLayouts(ArmHerc?[] layouts) => _layouts = layouts;

	/// <summary>Reads the nine layouts; one missing leaves that chassis without one.</summary>
	public static ShellArmingLayouts Load(GameContent content) {
		var layouts = new ArmHerc?[LayoutStems.Length];
		for (int type = 0; type < LayoutStems.Length; type++) {
			layouts[type] = content.Read(ShellRepairCosts.CatalogFolder, $"ARM_{LayoutStems[type]}.DAT") is { } bytes
				? new ArmHercTransformer().Parse(bytes) : null;
		}

		return new ShellArmingLayouts(layouts);
	}

	/// <summary>How many chassis types have a slot in the table — nine.</summary>
	public static int ChassisCount => LayoutStems.Length;

	/// <summary>How many chassis have a layout — nine when the archive is complete.</summary>
	public int Count => _layouts.Count(layout => layout != null);

	/// <summary>The chassis's layout, or null for a type past the nine or one the archive lacks.</summary>
	public ArmHerc? Layout(int chassisType) =>
		chassisType >= 0 && chassisType < _layouts.Length ? _layouts[chassisType] : null;

	/// <summary>
	/// Whether the chassis's layout has a record for <paramref name="weaponId"/> in <paramref name="mount"/>'s
	/// socket, whatever its frame — <c>Arming_RowLive</c> (<c>004149fb</c>)'s test of
	/// <c>RepairLayout_FindWeaponPart</c>. Every retail layout has a <c>None</c> (0) record for every socket.
	/// </summary>
	public bool HasSocket(int chassisType, int weaponId, int mount) =>
		Layout(chassisType) is { } layout && FindRecord(layout, weaponId, mount) != null;

	/// <summary><c>RepairLayout_FindWeaponPart</c> (<c>00413ccc</c>) itself: the record in the weapon's group whose id is <c>mount + 2</c>, whatever its frame.</summary>
	public static UiHardpointGraphic? FindRecord(ArmHerc layout, int weaponId, int mount) =>
		layout.WeaponHardpoints is { } groups && groups.TryGetValue((short)weaponId, out var sockets)
			? sockets.FirstOrDefault(socket => socket.Id == FirstWeaponPart + mount) : null;
}
