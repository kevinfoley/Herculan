using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Which of the cockpit's two system buttons, by the child index <c>SystemButtons_OnChildClick</c>
/// (<c>004345a0</c>) switches on — the order <c>SystemButtons_Ctor</c> (<c>00434368</c>) builds them in.
/// </summary>
public enum SystemButton {
	/// <summary>The right-hand button, child 0 at <c>CockpitViewInstance+0x246</c>: the on-line manual. <c>SYSBUTTN</c> frame 0.</summary>
	Manual = 0,

	/// <summary>The left-hand button, child 1 at <c>+0x24a</c>: full screen. <c>SYSBUTTN</c> frame 1.</summary>
	FullScreen = 1,
}

/// <summary>
/// The cockpit's two system buttons — the pair of <c>SystemGadget</c>s <c>SystemButtons_Ctor</c>
/// (<c>00434368</c>) builds at the screen's top-right corner. See docs/retail/formats/cockpit-input.md, "The two
/// system buttons", for the retail evidence behind each member.
///
/// <para><b>Screen space, not art space.</b> The original moves the pair's rects against the root's on
/// every view change, so they stay on the same screen pixels in every view and over whatever the view
/// shows there. This engine's screen is a 640x480 one scaled to the window's height, as the modal panels'
/// is (<see cref="AlertPanelLayout.Placement"/>), but with its right edge on the window's right edge rather
/// than centred (<see cref="Place"/>): retail's 4:3 screen's top-right corner is the window's, so in a wider
/// window the pair goes to the window's own corner, as the side screen-edge strips go to its edges
/// (<see cref="Render.CockpitScreenLayout.SideViewEdgeAt"/>). In a 4:3 window that is the centred screen,
/// which at rest coincides with the forward panel's art. Neither moves with the pan or a glance. Their
/// rects are hit-tested ahead of every other cockpit widget, which is where <c>SystemButtons_Ctor</c>
/// registers them (<see cref="Input.CockpitInput"/>), and through the same placement they are drawn
/// with.</para>
/// </summary>
public static class SystemButtons {
	/// <summary>The bank both frames come from, <c>sysbuttn</c> in <c>hba\</c>.</summary>
	public const string Bank = "SYSBUTTN";

	/// <summary>How many there are — <c>CockpitViewInstance+0x25a</c>, which the constructor sets to 2.</summary>
	public const int Count = 2;

	private const int Shift = CockpitViewGeometry.CoordShift;

	/// <summary>
	/// A button's rect on the 640x480 screen, inclusive on all four edges as <c>Widget_HitTest</c>'s is:
	/// the constructor's hardcoded <c>x1 = 0x13d</c> for the right-hand one and two units further left
	/// of its <c>x0</c> for the other, each <c>0xc</c> wide, rows <c>2</c> to <c>2 + 0xb</c>, all
	/// shifted by <c>VideoMode_X/YCoordShift</c>.
	/// </summary>
	public static AlertPanelLayout.Rect Rect(SystemButton button) {
		int x1 = 0x13d << Shift;
		if (button == SystemButton.FullScreen) {
			x1 -= (0xc << Shift) + (2 << Shift);
		}

		int y0 = 2 << Shift;
		return new AlertPanelLayout.Rect(x1 - (0xc << Shift), y0, x1, (0xb << Shift) + y0);
	}

	/// <summary>The <see cref="Bank"/> frame a button draws: frame 0 for child 0, frame 1 for child 1.</summary>
	public static int Frame(SystemButton button) => (int)button;

	/// <summary>
	/// Where the 640x480 screen the pair sits on lands in the window: scaled to the window's height, its top
	/// edge on the window's and its right edge on the window's right edge. The placement both the paint
	/// and <see cref="At"/> go through.
	/// </summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) {
		float scale = Math.Max(windowHeight, 1) / (float)AlertPanelLayout.ScreenHeight;
		return new AlertPanelLayout.Placement(scale, windowWidth - AlertPanelLayout.ScreenWidth * scale, 0f);
	}

	/// <summary>
	/// Whether a button draws this frame — <c>SystemButtons_PaintForPointer</c> (<c>00434520</c>), which
	/// gives a button state 0, drawn, while the pointer's row lies within the button's own rows, and
	/// state 3, which shows what is underneath, otherwise. Only the row is tested, so the pair comes up
	/// wherever across the screen the pointer is in those rows. A pointer the window does not have
	/// (NaN) shows neither.
	/// </summary>
	/// <param name="windowWidth">Framebuffer width.</param>
	/// <param name="windowHeight">Framebuffer height.</param>
	/// <param name="pointerY">The pointer's row in framebuffer pixels.</param>
	public static bool Showing(SystemButton button, int windowWidth, int windowHeight, float pointerY) {
		var (_, y) = Place(windowWidth, windowHeight).ToPanel(0f, pointerY);
		var rect = Rect(button);
		return y >= rect.Y0 && y <= rect.Y1;
	}

	/// <summary>
	/// The system button under a window pixel, or null. Hidden or showing makes no difference: the
	/// state <c>SystemButtons_PaintForPointer</c> leaves a hidden button in is 3, which
	/// <c>Widget_HitTestChildren</c> tests like any other, and the pointer is on a button's rows whenever
	/// it is over the button.
	/// </summary>
	public static CockpitWidget? At(int windowWidth, int windowHeight, float windowX, float windowY) {
		var place = Place(windowWidth, windowHeight);
		var (x, y) = place.ToPanel(windowX, windowY);

		for (int i = 0; i < Count; i++) {
			var button = (SystemButton)i;
			var rect = Rect(button);
			if (!rect.Contains(x, y)) {
				continue;
			}

			var (x0, y0) = place.ToWindow(rect.X0, rect.Y0);
			var (x1, y1) = place.ToWindow(rect.X1 + 1, rect.Y1 + 1);
			return new CockpitWidget(CockpitWidgetId.System(button), CockpitSurface.Window,
				(int)x0, (int)y0, (int)MathF.Ceiling(x1) - 1, (int)MathF.Ceiling(y1) - 1, Lit: false);
		}

		return null;
	}
}

/// <summary>
/// What the two system buttons reach, both of which act on the window rather than the game:
/// <c>SystemButtons_OnChildClick</c> (<c>004345a0</c>) sends child 0 to <c>OnlineManual_Raise</c> (<c>0045f054</c>)
/// and child 1 to <c>Video_ToggleFullscreen</c> (<c>004666c4</c>). The keys that make the same two calls reach
/// them the same way.
/// </summary>
public interface ISystemButtonActions {
	/// <summary>The on-line manual, out of full screen first.</summary>
	void OpenManual();

	/// <summary>Full screen on or off.</summary>
	void ToggleFullScreen();
}
