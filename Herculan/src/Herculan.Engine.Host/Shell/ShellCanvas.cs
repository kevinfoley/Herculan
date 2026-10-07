using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Shell;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The shell's art and the renderer that draws it. The art is decoded through one palette at load rather than
/// re-mapped per frame, so a palette change loads it again and rebuilds the renderer.
/// </summary>
sealed class ShellCanvas : IDisposable {
	private readonly GameContent _content;

	// --shell-palette's entry, pinned on every tab.
	private readonly string? _pinnedPalette;

	private GL? _gl;
	private ShellRenderer? _renderer;

	public ShellCanvas(GameContent content, ShellArt art, string? pinnedPalette) {
		_content = content;
		_pinnedPalette = pinnedPalette;
		Art = art;
	}

	/// <summary>Reassigned when a tab switches palette — see <see cref="LoadPalette"/>.</summary>
	public ShellArt Art { get; private set; }

	public bool HasRenderer => _renderer != null;

	/// <summary>
	/// Which palette a tab is drawn through: Shell_SelectTabPalette (0043b162)'s, unless --shell-palette
	/// pins one entry everywhere. A tab with no screen ported shows only the strip over the scope's black
	/// fill, so its palette colours the strip alone, as retail's does.
	/// </summary>
	public static string PaletteFor(string? pinnedPalette, int tab, ShellMissionView missionView, int campaignStage) {
		if (pinnedPalette != null) {
			return pinnedPalette;
		}

		return ShellPalette.ForTab(tab, missionView, campaignStage) is { } index
			&& ShellPalette.Name(index) is { } name
			? name : ShellArt.DefaultPaletteName;
	}

	/// <inheritdoc cref="PaletteFor(string?, int, ShellMissionView, int)"/>
	public string PaletteFor(int tab, ShellMissionView missionView, int campaignStage) =>
		PaletteFor(_pinnedPalette, tab, missionView, campaignStage);

	/// <summary>The art was loaded before the game, so a start on the mission tab took stage 1's palette.</summary>
	public void Restage(string paletteName) {
		if (!string.Equals(paletteName, Art.PaletteName, StringComparison.OrdinalIgnoreCase)
				&& ShellArt.Load(_content, paletteName) is { } staged) {
			Art = staged;
		}
	}

	/// <summary>Builds the renderer once the window has a GL context.</summary>
	public void Attach(GL gl) {
		_gl = gl;
		_renderer = new ShellRenderer(gl, Art);
	}

	/// <summary>Shell_InstallPalette (004075b2) by index, unless --shell-palette pins one.</summary>
	public void InstallPalette(int index) {
		if (_pinnedPalette == null && ShellPalette.Name(index) is { } name) {
			LoadPalette(name);
		}
	}

	public void LoadPalette(string name) {
		if (_gl == null || string.Equals(name, Art.PaletteName, StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		if (ShellArt.Load(_content, name) is not { } reloaded) {
			Console.Error.WriteLine($"Palette {name} could not be loaded — keeping {Art.PaletteName}.");
			return;
		}

		Art = reloaded;
		_renderer?.Dispose();
		_renderer = new ShellRenderer(_gl, Art);

		// The new renderer has no content texture, and the old one's was resolved through the old
		// palette anyway — so the tab's content is rasterized again through the palette it is now
		// being drawn in. TabNavigation.Activate calls CanvasContent.Repaint after this returns.
	}

	public void Draw(ShellScreenLayout layout, ShellScreen screen) => _renderer?.Draw(layout, screen);

	public void DrawTexture(ShellScreenLayout layout, GpuTexture texture, int x, int y, int width, int height) =>
		_renderer?.DrawTexture(layout, texture, x, y, width, height);

	public void SetBackdropOverride(ShellImage? image) => _renderer?.SetBackdropOverride(image);

	public void SetContent(ShellSurface? surface) => _renderer?.SetContent(surface);

	public void Dispose() {
		_renderer?.Dispose();
		_renderer = null;
	}
}
