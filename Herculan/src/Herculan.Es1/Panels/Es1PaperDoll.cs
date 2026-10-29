using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The paper doll's tinting (docs/es1/simulation/paper-doll.md#tinting): a region of a drawn damage diagram repainted
/// in a color for how damaged its part is, and the damage bands both the tint and a condition line read.
/// </summary>
public static class Es1PaperDoll {
	/// <summary>The palette index <see cref="RecolorMode.AddTint"/> leaves alone.</summary>
	public const byte SparedIndex = 63;

	/// <summary>A region's <c>recolorMode</c>, the four <see cref="RecolorRect"/> takes.</summary>
	public enum RecolorMode {
		/// <summary>0: a pixel of key to key + 2 shifted by tint − key; nothing when tint = key.</summary>
		Shift = 0,

		/// <summary>1: a pixel of key set to tint; nothing when tint = key.</summary>
		Replace = 1,

		/// <summary>2: tint added to every pixel but <see cref="SparedIndex"/>.</summary>
		AddTint = 2,

		/// <summary>3: a pixel of key to key + 2 set to tint.</summary>
		Fill = 3,
	}

	/// <summary>
	/// <c>Damage_ToConditionState</c> (<c>ES1DBSIM 00030eb8</c>)(<paramref name="damage"/>): with intact = (256 −
	/// damage) × 100 &gt;&gt; 8, 0 at 90 or more, 1 at 74 or more, 2 at 51 or more, 3 at 1 or more, else 4.
	/// </summary>
	public static short ToConditionState(int damage) => Band((0x100 - damage) * 100 >> 8);

	/// <summary>
	/// <c>Damage_PickRegionTint</c> (<c>00030dc4</c>)(out, <paramref name="region"/>, <paramref name="damage"/>): the
	/// state of <see cref="ToConditionState"/>'s bands and the palette index the region is tinted with: for modes 0, 2
	/// and 3, 54, 57, 92, 51 and 62 by state; for mode 1 the region's <c>colorId</c> for state 0, <c>colorId</c> less the
	/// state for states 1 and 2 (60 plus the state when <c>colorId</c> is 60), and 48 and 62 for states 3 and 4. A mode
	/// above 3, where retail writes what two registers held, gives state 0 and the region's <c>colorId</c>, an engine guard.
	/// </summary>
	public static (short State, int Tint) PickRegionTint(PaperDollGraphic.ViewRegion region, int damage) {
		short state = ToConditionState(damage);
		switch (region.RecolorMode) {
			case 0 or 2 or 3:
				return (state, state switch { 0 => 0x36, 1 => 0x39, 2 => 0x5c, 3 => 0x33, _ => 0x3e });
			case 1:
				int color = region.ColorId;
				return (state, state switch {
					0 => color,
					1 or 2 => color == 0x3c ? 0x3c + state : color - state,
					3 => 0x30,
					_ => 0x3e,
				});
			default:
				return (state, region.ColorId);
		}
	}

	/// <summary>
	/// <c>PaperDoll_RecolorRect</c> (<c>00030818</c>)(<paramref name="mode"/>, <paramref name="tint"/>,
	/// <paramref name="key"/>, <paramref name="rect"/>): the display's draw page made its source page, and the rect moved
	/// by <paramref name="canvas"/>'s translation walked a row at a time, left to right, each pixel of the draw page read
	/// and rewritten as <see cref="RecolorMode"/> says, with no clip. A pixel the world was drawn into, which retail
	/// reads as the world's own index, is left, an engine guard.
	/// </summary>
	public static void RecolorRect(Es1PanelCanvas canvas, int mode, int tint, int key, Es1Rect rect) {
		var display = canvas.Display;
		display.SourcePage = display.DrawPage;
		if ((mode is (int)RecolorMode.Shift or (int)RecolorMode.Replace && tint == key) || mode is < 0 or > 3) {
			return;
		}

		var (dx, dy) = canvas.Translation;
		for (int y = rect.Y0 + dy; y <= rect.Y1 + dy; y++) {
			for (int x = rect.X0 + dx; x <= rect.X1 + dx; x++) {
				ushort cell = display.Cell(display.DrawPage, x, y);
				if (cell == Es1VideoMemory.WorldCell) {
					continue;
				}

				int pixel = cell;
				int? written = (RecolorMode)mode switch {
					RecolorMode.Shift => pixel >= key && pixel <= key + 2 ? pixel + (tint - key) : null,
					RecolorMode.Replace => pixel == key ? tint : null,
					RecolorMode.AddTint => pixel != SparedIndex ? pixel + tint : null,
					_ => pixel >= key && pixel <= key + 2 ? tint : null,
				};
				if (written is { } value) {
					display.Plot(x, y, (byte)value);
				}
			}
		}
	}

	/// <summary>
	/// <c>PaperDoll_RecolorRectFromArt</c> (<c>00030ab4</c>)(<paramref name="mode"/>, <paramref name="tint"/>,
	/// <paramref name="key"/>, <paramref name="artOrigin"/>, <paramref name="art"/>, <paramref name="rect"/>)
	/// (docs/es1/simulation/paper-doll.md#tinting): <see cref="RecolorRect"/>, except that mode 0 tests the art's pixel
	/// rather than the screen's and walks even when tint = key. The art's pixel for a screen pixel (<i>x</i>,
	/// <i>y</i>) of the rect is the byte at (<i>y</i> − the origin's <i>y</i>) × the art's width + (<i>x</i> − the
	/// origin's <i>x</i>), with no bounds, so an offset past the width reads on into the next row; a pixel of
	/// <paramref name="key"/> to <paramref name="key"/> + 2 writes itself + (tint − key) to the draw page. An offset
	/// outside the art's bytes, which retail reads unchecked, writes nothing, an engine guard.
	/// </summary>
	public static void RecolorRectFromArt(Es1PanelCanvas canvas, int mode, int tint, int key, (int X, int Y) artOrigin,
			DynamixBitmap? art, Es1Rect rect) {
		if (mode != (int)RecolorMode.Shift) {
			RecolorRect(canvas, mode, tint, key, rect);
			return;
		}

		var display = canvas.Display;
		display.SourcePage = display.DrawPage;
		if (art?.ImageData is not { } pixels) {
			return;
		}

		var (dx, dy) = canvas.Translation;
		for (int y = rect.Y0; y <= rect.Y1; y++) {
			for (int x = rect.X0; x <= rect.X1; x++) {
				int index = (y - artOrigin.Y) * art.Cols + (x - artOrigin.X);
				if (index < 0 || index >= pixels.Length) {
					continue;
				}

				int pixel = pixels[index];
				if (pixel >= key && pixel <= key + 2) {
					display.Plot(x + dx, y + dy, (byte)(pixel + (tint - key)));
				}
			}
		}
	}

	/// <summary>
	/// <c>PaperDoll_BuildWeaponIcons</c> (<c>0003058c</c>)(<paramref name="machine"/>)
	/// (docs/es1/simulation/paper-doll.md#the-weapon-icons): an icon for each hardpoint <i>n</i> of the chassis' paper
	/// doll. Its <see cref="Es1WeaponIcon.MountIndex"/> is the first position in the gun layout whose slot byte is
	/// <i>n</i>, or the layout's count when none is, and its mount the mount array's entry there. With no mount, or a
	/// template whose damage icon (<c>+0x50</c>) is −1, the icon has no bitmap, and its point, rect and flags, which retail
	/// leaves as the allocation held and the damage screen's recolor passes then walk, are 0 here, an engine guard. Otherwise its frame is the icon number × 2
	/// plus the hardpoint's <c>frameOffset</c>, as an <c>int16</c>, its bitmap that frame of <paramref name="bank"/> and its
	/// size that entry of <paramref name="sizes"/>; its point the hardpoint's, moved left by the width (alignment bits 0–2
	/// = 2) or half of it (4) and up by the height (the other bits = <c>0x10</c>) or half of it (<c>0x20</c>); its rect
	/// from that point to it plus the size; its blit flags the hardpoint's, and for flags 2 the point moved left again by
	/// the bitmap's width less the size's. A frame past the bank or the sizes, and a position past the mount array, which
	/// retail reads unchecked, give no bitmap, a size of 0 or no mount, engine guards.
	/// </summary>
	public static IReadOnlyList<Es1WeaponIcon> BuildWeaponIcons(Es1Mech machine, IReadOnlyList<DynamixBitmap>? bank,
			WeaponPaperDiagram? sizes) {
		var hardpoints = machine.Type.PaperDoll?.Hardpoints ?? [];
		var layout = machine.Type.GunLayout?.Hardpoints ?? [];
		var mounts = machine.Mounts?.Mounts ?? [];
		var icons = new Es1WeaponIcon[hardpoints.Length];
		for (int n = 0; n < hardpoints.Length; n++) {
			int position = 0;
			while (position < layout.Length && (sbyte)layout[position].LoadoutSlot != n) {
				position++;
			}

			var mount = position < mounts.Count ? mounts[position] : null;
			short number = mount?.Template.DamageIconIndex ?? -1;
			if (number == -1) {
				icons[n] = new Es1WeaponIcon { MountIndex = (short)position };
				continue;
			}

			var hardpoint = hardpoints[n];
			short frame = (short)(number * 2 + (short)hardpoint.FrameOffset);
			var bitmap = bank != null && frame >= 0 && frame < bank.Count ? bank[frame] : null;
			var size = sizes?.Entries is { } entries && frame >= 0 && frame < entries.Length ? entries[frame] : null;
			int width = size?.Width ?? 0, height = size?.Height ?? 0;
			int x = hardpoint.Origin.X, y = hardpoint.Origin.Y;
			switch (hardpoint.Alignment & 7) {
				case 2:
					x -= width;
					break;
				case 4:
					x -= width >> 1;
					break;
			}

			switch (hardpoint.Alignment & ~7) {
				case 0x10:
					y -= height;
					break;
				case 0x20:
					y -= height >> 1;
					break;
			}

			var rect = new Es1Rect(x, y, x + width, y + height);
			if (hardpoint.BlitFlags == 2) {
				x -= (bitmap?.Cols ?? 0) - width;
			}

			icons[n] = new Es1WeaponIcon {
				Bitmap = bitmap, Position = (x, y), Rect = rect, BlitFlags = hardpoint.BlitFlags, MountIndex = (short)position,
			};
		}

		return icons;
	}

	/// <summary>
	/// <c>Sim_StartMission</c>'s step 13 for the weapon icons (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc):
	/// <see cref="Es1Mech.WeaponIcons"/> = <see cref="BuildWeaponIcons"/> for every machine of <paramref name="pool"/>. The
	/// bank, <c>PaperDoll_WeaponIconBank</c> (<c>000b0f14</c>), <c>dba\weapons</c>, and the sizes,
	/// <c>WeaponIconSizes</c> (<c>000b0f18</c>), <c>pdg\weapons</c>, which retail's <c>PaperDoll_InitTables</c>
	/// (<c>00030288</c>) loads with the chassis types, are read here through <paramref name="resources"/>, as nothing else
	/// reads them; a missing file gives none.
	/// </summary>
	public static void BuildWeaponIconsForPool(IEnumerable<Es1Mech> pool, Func<string, string, byte[]?>? resources) {
		var bank = resources?.Invoke("dba", "WEAPONS.DBA") is { } bankBytes
			? new DynamixBitmapArrayTransformer().Parse(bankBytes)?.Images
			: null;
		var sizes = resources?.Invoke("pdg", "WEAPONS.PDG") is { } sizeBytes ? new WeaponPDGTransformer().Parse(sizeBytes) : null;
		foreach (var machine in pool) {
			machine.WeaponIcons = BuildWeaponIcons(machine, bank, sizes);
		}
	}

	private static short Band(int intact) => intact >= 90 ? (short)0 : intact >= 74 ? (short)1 : intact >= 51 ? (short)2 : intact >= 1 ? (short)3 : (short)4;
}
