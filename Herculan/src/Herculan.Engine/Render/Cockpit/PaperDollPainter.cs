using Herculan.Engine.Cockpit;
using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Content;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The damage paper doll's recolours, shared by the MFD's status screens and the Heads-Down Display's
/// damage detail.
/// </summary>
internal static class PaperDollPainter {
	/// <summary>
	/// The damage detail's weapon icons (<c>HddDamageScreen_BlitWeaponIcons</c>, <c>00451db8</c>) and the
	/// recolour <c>HddDamageScreen_Update</c> gives them, which is what makes them blue beside a green
	/// doll on two categories and green beside a blue one on the third. Every palette comparison is
	/// against the pixel the screen already holds, so the walks below composite the icons over the doll
	/// to answer it.
	///
	/// <list type="bullet">
	/// <item><b>Structural and internal.</b> The icons are blitted over the doll, then every
	/// <see cref="PaperDollDamage.OkColorId"/> pixel inside each icon's rect goes
	/// <see cref="PaperDollDamage.WeaponIconColorId"/> — doll pixels the rect covers included.</item>
	/// <item><b>Weapons.</b> The doll goes blue first: its view rect's id-12 pixels, then each region
	/// authored in another colour, over that region's rect. The icons are blitted over it and each is
	/// tinted from its hardpoint's reading, the green-to-grey ladder a doll region takes, so an intact
	/// weapon stays green. The doll under them is blue by then and takes no part.</item>
	/// </list>
	/// </summary>
	internal static void AddHddWeaponIcons(CockpitArt hud, HudSpriteSheet sprites, HddDamageView view,
			string dollBank, PaperDollGraphic.ViewEntry doll, int dollView, PaperDollGraphic.HardpointEntry[]? entries,
			IReadOnlyList<DamageHardpoint?> hardpoints, IReadOnlyList<short>? readings, float dollLeft,
			float dollTop, Action<string, int, float, float> blit,
			Action<float, float, float, float, Vector3> fillRect) {
		const int S = (int)CockpitArt.GauToPixelScale;
		const string Bank = PaperDollDamage.WeaponIconBank;

		if (hud.Colors is not { } colors
			|| colors.PaletteIndex(PaperDollDamage.OkColorId) is not { } green
			|| hud.LogicalColor(PaperDollDamage.WeaponIconColorId) is not { } blue) {
			return;
		}

		var dollArt = sprites.Indexed(dollBank, dollView);

		if (view == HddDamageView.Weapons && dollArt is { } art) {
			int ArtAt(int x, int y) =>
				x >= 0 && y >= 0 && x < art.Width && y < art.Height ? art.Pixels[y * art.Width + x] : -1;

			RasterPrimitives.AddIndexedRecolor(0, 0, doll.Size.X * S, doll.Size.Y * S, ArtAt, green, blue, dollLeft, dollTop,
				fillRect);
			foreach (var region in doll.Regions ?? Array.Empty<PaperDollGraphic.ViewRegion>()) {
				if (colors.PaletteIndex(region.ColorId) is { } key && key != green) {
					RasterPrimitives.AddIndexedRecolor(region.TopLeft.X * S, region.TopLeft.Y * S, region.BottomRight.X * S + 1,
						region.BottomRight.Y * S + 1, ArtAt, key, blue, dollLeft, dollTop, fillRect);
				}
			}
		}

		// PaperDoll_BuildWeaponIcons: .PDG entry n places the icon of .GL slot n, and an empty slot, or
		// a weapon with no icon, places none.
		var icons = new List<(PaperDollDamage.WeaponIcon Icon, int Slot, HudSpriteSheet.IndexedFrame Art)>();
		if (hud.WeaponIconSizes is { } sizes) {
			for (int slot = 0; slot < (entries?.Length ?? 0); slot++) {
				if (slot < hardpoints.Count && hardpoints[slot] is { } hardpoint
					&& PaperDollDamage.PlaceWeaponIcon(entries![slot], hardpoint.Icon, sizes, S) is { } icon
					&& sprites.Indexed(Bank, icon.Frame) is { } iconArt) {
					blit(Bank, icon.Frame, dollLeft + icon.X, dollTop + icon.Y);
					icons.Add((icon, slot, iconArt));
				}
			}
		}

		// The topmost icon pixel at a doll-relative point, the last blitted winning; -1 where no icon
		// has one.
		int IconAt(int x, int y) {
			for (int i = icons.Count - 1; i >= 0; i--) {
				var (icon, _, iconArt) = icons[i];
				int u = x - icon.X, v = y - icon.Y;
				if (u >= 0 && v >= 0 && u < iconArt.Width && v < iconArt.Height
					&& iconArt.Pixels[v * iconArt.Width + u] is var pixel and not 0) {
					return pixel;
				}
			}

			return -1;
		}

		foreach (var (icon, slot, _) in icons) {
			int x1 = icon.X + icon.Width, y1 = icon.Y + icon.Height;

			if (view != HddDamageView.Weapons) {
				int ScreenAt(int x, int y) => IconAt(x, y) is var pixel and >= 0 ? pixel
					: dollArt is { } art && x >= 0 && y >= 0 && x < art.Width && y < art.Height
						? art.Pixels[y * art.Width + x]
						: -1;
				RasterPrimitives.AddIndexedRecolor(icon.X, icon.Y, x1, y1, ScreenAt, green, blue, dollLeft, dollTop, fillRect);
				continue;
			}

			if (readings == null || PaperDollDamage.WeaponRowReading(slot, readings) is not { } reading) {
				continue;
			}

			int tintId = PaperDollDamage.TintColorId(PaperDollDamage.State(reading));
			if (colors.PaletteIndex(tintId) != green && hud.LogicalColor(tintId) is { } tint) {
				RasterPrimitives.AddIndexedRecolor(icon.X, icon.Y, x1, y1, IconAt, green, tint, dollLeft, dollTop, fillRect);
			}
		}
	}

	/// <summary>
	/// One paper-doll region repainted at its current damage — <c>PaperDoll_RecolorRect</c> in its mode 0 arm,
	/// the only arm the retail <c>.PDG</c> files reach. It walks the region's rect a pixel at a time
	/// and rewrites just the pixels still holding the colour the art drew that body part in, which is
	/// why the outlines and rivets over a limb survive the recolour.
	///
	/// <para>Both colours are palette indices: the region's own is the <c>COLORS.DAT</c> id the file
	/// carries, resolved at load in the original (<c>PaperDoll_Load</c>) and here at draw time, and
	/// the tint is <see cref="PaperDollDamage.TintColorId"/>'s. When the two agree the original skips
	/// the walk outright, and so does this — an undamaged region is already the colour it should
	/// be.</para>
	///
	/// <para>The rect is the file's inclusive corner pair, doubled and its far corner nudged one
	/// further, because the loader shifts every <c>.PDG</c> coordinate by the video mode's
	/// <c>X/YCoordShift</c> and adds that <c>+1</c> in the 640-wide mode — so the region covers the
	/// full 2x2 device footprint of each source pixel. Matching pixels go out as merged horizontal
	/// runs rather than one quad each.</para>
	/// </summary>
	internal static void AddPaperDollTint(CockpitArt hud, HudSpriteSheet sprites, string bank, int frame,
			PaperDollGraphic.ViewRegion region, int? reading, float dollLeft, float dollTop,
			Action<float, float, float, float, Vector3> fillRect) {
		const int S = (int)CockpitArt.GauToPixelScale;

		if (reading is not { } damage
			|| hud.Colors?.PaletteIndex(region.ColorId) is not { } key
			|| sprites.Indexed(bank, frame) is not { } art) {
			return;
		}

		int tintId = PaperDollDamage.TintColorId(PaperDollDamage.State(damage));
		if (hud.Colors.PaletteIndex(tintId) == key || hud.LogicalColor(tintId) is not { } tint) {
			return;
		}

		int x0 = Math.Max(region.TopLeft.X * S, 0);
		int y0 = Math.Max(region.TopLeft.Y * S, 0);
		int x1 = Math.Min(region.BottomRight.X * S + 1, art.Width - 1);
		int y1 = Math.Min(region.BottomRight.Y * S + 1, art.Height - 1);

		for (int y = y0; y <= y1; y++) {
			int row = y * art.Width;
			int run = -1;
			for (int px = x0; px <= x1 + 1; px++) {
				bool match = px <= x1 && art.Pixels[row + px] == key;
				if (match) {
					run = run < 0 ? px : run;
				} else if (run >= 0) {
					fillRect(dollLeft + run, dollTop + y, dollLeft + px, dollTop + y + 1, tint);
					run = -1;
				}
			}
		}
	}
}
