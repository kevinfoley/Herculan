namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/PDG/WEAPONS.PDG — the frame sizes of the weapon-icon bank
/// (<c>hba\WEAPONS.HBA</c>/<c>dba\WEAPONS.DBA</c>) the Heads-Down Display's damage detail draws
/// around a paper doll. Unlike every other <c>.PDG</c> it holds no views.
///   0 - INT32 - frame count
///   then count x { INT32 width, INT32 height }, in the 320-wide space.
/// Read by DBSIM's <c>PaperDoll_InitTables</c> (<c>004378d8</c>), which shifts each pair by
/// <c>VideoMode_X/YCoordShift</c>. See docs/retail/formats/cockpit-hud-widgets.md#weapon-icons.
/// </summary>
public class WeaponPaperDiagram {
	public Entry[]? Entries { get; set; }

	public Entry NewEntry() => new();

	public class Entry {
		public int Width { get; set; }
		public int Height { get; set; }
	}
}
