using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's wipes, which change the whole picture in interleaved passes of rows a tick apart
/// (docs/es1/shell/repair-and-build.md#the-hangars-selection, "The wipes"): <c>Screen_BlindsCopy</c> and
/// <c>Screen_BlindsClear</c>.
/// </summary>
public sealed class Es1ScreenBlinds {
	/// <summary>
	/// The word <c>0009b00c</c>, the bottom of the rect <c>0009b000</c> <c>Shell_LoadData</c> sets to the display's
	/// attribute 3 less 1: the rows below which a pass starts a copy, 199.
	/// </summary>
	private const int RowCount = Es1VideoMemory.PageHeight - 1;

	private readonly Es1ShellDevices _devices;

	public Es1ScreenBlinds(Es1ShellDevices devices) {
		_devices = devices;
	}

	/// <summary>
	/// <c>Screen_BlindsCopy</c> (<c>0004bc0d</c>)(<paramref name="passes"/>, <paramref name="to"/>, <paramref name="from"/>,
	/// flag): the display's source page set to <paramref name="from"/> and its draw page to <paramref name="to"/>; for each
	/// pass <i>i</i> below <paramref name="passes"/>, the region of rows <i>r</i> to <i>r</i> + 1, columns 0 to <c>0x13f</c>,
	/// copied for <i>r</i> = <i>i</i>, <i>i</i> + <paramref name="passes"/>, … below <see cref="RowCount"/>
	/// (<c>Region_CopyBetweenPages</c>), then a tick waited (<c>Shell_WaitTicks</c>(1)); then both pages put back. The flag
	/// is not read. PLACEHOLDER: retail installs the render context <c>0007fcd8</c> for the copies, which
	/// <c>Shell_DrawScreen</c> leaves in clip mode 2 around the open window; how <c>Region_CopyBetweenPages</c> treats that
	/// mode's regions is unread, so the copies here take the full screen in clip mode 1.
	/// </summary>
	public IEnumerable<Es1ShellWait> Copy(short passes, int to, int from) {
		var display = _devices.Display;
		int keptSource = display.SourcePage, keptDraw = display.DrawPage;
		display.SourcePage = from;
		display.DrawPage = to;
		var canvas = new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
		for (int pass = 0; pass < passes; pass++) {
			for (int row = pass; row < RowCount; row += passes) {
				canvas.CopyRegion(new Es1Rect(0, row, Es1ShellScreens.FullScreen.X1, row + 1), 0, 0);
			}

			foreach (var wait in _devices.WaitTicks(1)) {
				yield return wait;
			}
		}

		display.SourcePage = keptSource;
		display.DrawPage = keptDraw;
	}

	/// <summary>
	/// <c>Screen_BlindsClear</c> (<c>0004c053</c>)(<paramref name="passes"/>, <paramref name="color"/>, flag): the draw page
	/// cleared to the color (<c>VGA8_ClearPage</c>, the raster's <c>0008663c</c>), <see cref="Copy"/>(passes, the visible
	/// page, the draw page, flag), then the region <c>0009aff8</c>, the full screen, copied from page 2 onto the draw page
	/// with the source page put back after.
	/// </summary>
	public IEnumerable<Es1ShellWait> Clear(short passes, byte color) {
		var display = _devices.Display;
		display.ClearPage(display.DrawPage, color);
		foreach (var wait in Copy(passes, display.VisiblePage, display.DrawPage)) {
			yield return wait;
		}

		int keptSource = display.SourcePage;
		display.SourcePage = Es1ShellBackdrop.BackgroundPage;
		new Es1PanelCanvas(display, Es1ShellScreens.FullScreen).CopyRegion(Es1ShellScreens.FullScreen, 0, 0);
		display.SourcePage = keptSource;
	}
}
