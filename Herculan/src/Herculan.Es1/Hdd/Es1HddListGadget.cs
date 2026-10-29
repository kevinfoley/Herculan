using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the command screen's two click regions, <c>HDDListGadget</c> (docs/es1/simulation/hdd-command-screen.md#the-click-regions-class):
/// the order column's or the map's, built as the MFD's click surface is. It draws nothing and queues a left click for
/// the screen's update to decide. Its slots are <c>HDDListGadgetVtable</c> (<c>00094a0a</c>)'s, the file's copies of
/// the MFD click surface's no-op paint, invalidate and flush and of the button base's value, set-value and drag.
/// </summary>
public sealed class Es1HddListGadget : Es1ButtonWidget {
	/// <summary>
	/// <c>HDDListGadget_Ctor</c> (<c>ES1DBSIM 00041fcc</c>)(0, <paramref name="screen"/>, <paramref name="rect"/>): the
	/// widget on the rect, the drag flag 0, the state 0 and the screen <c>+0x28</c>.
	/// </summary>
	internal Es1HddListGadget(Es1HddCommandScreen screen, Es1Rect rect) {
		Screen = screen;
		Rect = rect;
	}

	/// <summary><c>+0x28</c>, the command screen.</summary>
	public Es1HddCommandScreen Screen { get; }

	/// <summary>
	/// Slot <c>+0xc</c>, <c>HDDListGadget_OnClick</c> (<c>0004208c</c>)(gadget, <paramref name="value"/>): with bit 0 set,
	/// <see cref="Es1HddCommandScreen.QueueListClick"/>(the pointer's canvas point, the gadget), with no sound.
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) == 0) {
			return;
		}

		Screen.QueueListClick(Screen.Display.Cockpit.MouseCanvasPoint, this);
	}
}
