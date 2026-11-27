using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Render;
using Herculan.Es1.View;

namespace Herculan.Es1.Render;

/// <summary>
/// The 3D pass's colours kept to the live palette. Retail's screen is 8-bit and shows every index it draws through the
/// live palette, so whatever is installed into it (docs/es1/rendering/palette.md#installing-colors), the death flash's
/// palettes and fades among them (docs/es1/simulation/main-loop.md#the-death-flash--sim_deathflash-00058414), shows on the
/// world as on the cockpit. When <see cref="Es1ScreenPresenter.PaletteVersion"/> has moved and an entry changed since the
/// world was last shown, <see cref="Follow"/> shows it in the presenter's live palette, so a still palette costs nothing.
///
/// <para>What follows: the renderer's live palette (<see cref="SceneRenderer.SetLivePalette"/>), which the shaded faces'
/// ramp table (<see cref="SurfaceRampTable"/>) and the palette-by-row table every textured and solid face reads
/// (<see cref="PaletteRampTable"/>) are drawn through, so neither table is rebuilt; the backdrop
/// (<see cref="Es1Backdrop.ShowIn"/>); and the palette the effects' billboards' indices are drawn through
/// (<see cref="SpriteBatch.PaletteHandle"/>).</para>
/// </summary>
public sealed class Es1WorldPalette {
	private readonly SceneRenderer _renderer;
	private readonly IReadOnlyList<Es1Backdrop> _backdrops;
	private readonly PaletteTexture? _billboards;
	private int _shownVersion;
	private int[] _shownColors;

	/// <summary>
	/// Keeps the renderer's live palette, the backdrops' ground and
	/// billboards, the effects' billboards' palette, to the presenter's live palette,
	/// which they were set up in.
	/// </summary>
	/// <param name="renderer">The world's renderer, its live palette already <paramref name="presenter"/>'s.</param>
	/// <param name="backdrops">The backdrops the world's passes draw.</param>
	/// <param name="billboards">The effects' and gun rounds' billboards' palette, or null.</param>
	/// <param name="presenter">The screen whose live palette the world follows.</param>
	public Es1WorldPalette(SceneRenderer renderer, IReadOnlyList<Es1Backdrop> backdrops, PaletteTexture? billboards,
			Es1ScreenPresenter presenter) {
		_renderer = renderer;
		_backdrops = backdrops;
		_billboards = billboards;
		_shownVersion = presenter.PaletteVersion;
		_shownColors = Colors(presenter.Palette);
	}

	/// <summary>The world shown in the presenter's live palette when an entry changed since it last was.</summary>
	/// <param name="presenter">The screen whose live palette the world follows.</param>
	public void Follow(Es1ScreenPresenter presenter) {
		if (presenter.PaletteVersion == _shownVersion || presenter.Palette is not { } live) {
			return;
		}

		_shownVersion = presenter.PaletteVersion;
		var colors = Colors(live);
		if (colors.AsSpan().SequenceEqual(_shownColors)) {
			return;
		}

		_shownColors = colors;
		_renderer.SetLivePalette(live);
		foreach (var backdrop in _backdrops) {
			backdrop.ShowIn(live);
		}

		_billboards?.Show(live);
	}

	/// <summary>Each entry's color as one value, red in the low byte, or −1 where the palette lacks it.</summary>
	/// <param name="palette">The palette, or null.</param>
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
