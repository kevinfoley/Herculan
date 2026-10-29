using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// One of a machine's weapon icons, a <c>0x22</c>-byte entry of the list <c>PaperDoll_BuildWeaponIcons</c>
/// (<c>ES1DBSIM 0003058c</c>) builds per <c>.PDG</c> hardpoint (docs/es1/simulation/paper-doll.md#the-weapon-icons), which
/// the Heads-Down Display's damage screen draws over the doll. Positions are relative to the doll's origin.
/// </summary>
public sealed class Es1WeaponIcon {
	/// <summary><c>+0</c>, the icon's frame of <c>dba\weapons</c>; none for a hardpoint with no mount or no icon.</summary>
	public DynamixBitmap? Bitmap { get; init; }

	/// <summary><c>+4</c>, <c>+8</c>, the point the bitmap is blitted at.</summary>
	public (int X, int Y) Position { get; init; }

	/// <summary>
	/// <c>+0xc</c>–<c>+0x18</c>, the icon's rect, from its aligned point to that plus its <c>WEAPONS.PDG</c> size. Retail
	/// leaves it as the element constructor left it, unset, for a hardpoint with no icon; it is (0, 0, 0, 0) here.
	/// </summary>
	public Es1Rect Rect { get; init; }

	/// <summary><c>+0x1c</c>, the hardpoint's blit flags.</summary>
	public int BlitFlags { get; init; }

	/// <summary><c>+0x20</c>, an <c>int16</c>: the position in the gun layout, and so in the mount array, of the hardpoint's mount.</summary>
	public short MountIndex { get; init; }
}
