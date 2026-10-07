namespace HercWorks.Core.Data.Struct.Dbsim;

/// <summary>
/// One 22-byte record of LC_WPNS.DAT — one weapon slot of the <c>LC_BASE</c> structure class, the
/// transport (<c>BASES.DAT</c> type <c>0x22</c>). The record is shared by its three weapon stations,
/// each of which reads it in its own frame. See docs/retail/formats/lc-wpns-dat.md.
/// </summary>
public class LcWeaponSlot {
	/// <summary><c>+0x00</c> — half-width of the pitch window the aim error must be inside to fire, binary angle.</summary>
	public short PitchArc { get; set; }

	/// <summary><c>+0x02</c> — half-width of the yaw window, binary angle, measured from the station's own facing.</summary>
	public short YawArc { get; set; }

	/// <summary><c>+0x04</c> — the slot fires only at a target nearer than this, in world units.</summary>
	public int Range { get; set; }

	/// <summary>
	/// <c>+0x08</c> — the muzzle's X offset from the structure's origin, rotated by the station's
	/// heading.
	/// </summary>
	public int OffsetX { get; set; }

	/// <summary><c>+0x0c</c> — the muzzle's Y offset, rotated with <see cref="OffsetX"/>.</summary>
	public int OffsetY { get; set; }

	/// <summary><c>+0x10</c> — the muzzle's height above the structure's origin, added unrotated.</summary>
	public int OffsetZ { get; set; }

	/// <summary><c>+0x14</c> — the refire delay a shot arms, in the simulation's timer units.</summary>
	public short RefireDelay { get; set; }
}
