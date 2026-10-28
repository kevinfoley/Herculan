using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Render;
using Herculan.Es1.View;

namespace Herculan.Es1.Render;

/// <summary>
/// The 3D pass's colours kept to the live palette. Retail's screen is 8-bit and shows every index it draws through the
/// live palette, so whatever is installed into it (docs/es1/rendering/palette.md#installing-colors), the death flash's
/// palettes and fades among them (docs/es1/simulation/main-loop.md#the-death-flash--sim_deathflash-00058414), shows on the
/// world as on the cockpit. The renderer resolves the world's indices to colours through tables built at load; when
/// <see cref="Es1Monitor.PaletteVersion"/> has moved since the tables were last shown, <see cref="Follow"/> shows them in
/// the monitor's live palette and sends them again, so a still palette costs nothing.
///
/// <para>What follows: the shaded faces' ramp table (<see cref="SurfaceRampTable"/>), the palette-by-row table every
/// textured and solid face reads (<see cref="PaletteRampTable"/>), whose indices, the distance bands' ranges and the
/// texture colour tables (docs/es1/rendering/polygon-shading.md#the-texture-colour-tables), stay as they were built, and
/// the backdrop's ground (<see cref="Es1Backdrop.ShowIn"/>). What does not, each a PLACEHOLDER where it is built: the
/// effects' billboards, the sky's bands, the skyline and the clouds.</para>
/// </summary>
public sealed class Es1WorldPalette {
	private readonly SceneRenderer _renderer;
	private readonly SurfaceRampTable? _shadeRamps;
	private readonly PaletteRampTable? _paletteRamp;
	private readonly IReadOnlyList<Es1Backdrop> _backdrops;
	private int _shownVersion;
	private int[] _shownColors;

	/// <summary>
	/// Keeps <paramref name="renderer"/>'s tables, <paramref name="shadeRamps"/> and <paramref name="paletteRamp"/> as it
	/// was given them, and <paramref name="backdrops"/>' ground to <paramref name="monitor"/>'s live palette, which they
	/// were built in. A shade table not built from palette indices (<see cref="SurfaceRampTable.CanRecolor"/>) is left as
	/// it is.
	/// </summary>
	public Es1WorldPalette(SceneRenderer renderer, SurfaceRampTable? shadeRamps, PaletteRampTable? paletteRamp,
			IReadOnlyList<Es1Backdrop> backdrops, Es1Monitor monitor) {
		_renderer = renderer;
		_shadeRamps = shadeRamps is { CanRecolor: true } ? shadeRamps : null;
		_paletteRamp = paletteRamp;
		_backdrops = backdrops;
		_shownVersion = monitor.PaletteVersion;
		_shownColors = Colors(monitor.Palette);
	}

	/// <summary>
	/// The tables and the ground shown in <paramref name="monitor"/>'s live palette when an entry changed since they last
	/// were. The shade table, the large one, is rebuilt whole only when an entry a shaded cell names changed
	/// (<see cref="SurfaceRampTable.ShadedCellsName"/>), and otherwise only its palette row is, so the shield rings'
	/// entries, which no shaded cell names, cost one row.
	/// </summary>
	public void Follow(Es1Monitor monitor) {
		if (monitor.PaletteVersion == _shownVersion || monitor.Palette is not { } live) {
			return;
		}

		_shownVersion = monitor.PaletteVersion;
		var colors = Colors(live);
		bool changed = false, shadedChanged = false;
		for (int index = 0; index < colors.Length; index++) {
			if (colors[index] != _shownColors[index]) {
				changed = true;
				shadedChanged |= _shadeRamps?.ShadedCellsName(index) == true;
			}
		}

		_shownColors = colors;
		if (!changed) {
			return;
		}

		if (shadedChanged) {
			_shadeRamps!.Recolor(live);
			_renderer.ReuploadShadeRamps(_shadeRamps);
		} else if (_shadeRamps != null) {
			_shadeRamps.RecolorPaletteRow(live);
			_renderer.ReuploadShadeRampPaletteRow(_shadeRamps);
		}

		if (_paletteRamp != null) {
			_paletteRamp.Recolor(live);
			_renderer.ReuploadPaletteRamp(_paletteRamp);
		}

		foreach (var backdrop in _backdrops) {
			backdrop.ShowIn(live);
		}
	}

	/// <summary>Each entry's color as one value, red in the low byte, or −1 where <paramref name="palette"/> lacks it.</summary>
	private static int[] Colors(DynamixPalette? palette) {
		var colors = new int[256];
		for (int index = 0; index < colors.Length; index++) {
			colors[index] = -1;
			if (palette != null && palette.Colors.TryGetValue(index, out var entry)) {
				var color = entry.GetColor();
				colors[index] = color.R | color.G << 8 | color.B << 16;
			}
		}

		return colors;
	}
}
