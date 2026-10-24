using HercWorks.Core.Data.File.Dbsim;
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

	private static short Band(int intact) => intact >= 90 ? (short)0 : intact >= 74 ? (short)1 : intact >= 51 ? (short)2 : intact >= 1 ? (short)3 : (short)4;
}
