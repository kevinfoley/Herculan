using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the Heads-Down Display's three comm-box gauges, its <c>0x14a</c> bytes from the display's <c>+0xf6</c>
/// (docs/es1/simulation/heads-down-display.md#the-comm-boxes), as far as the display's own code reads and writes it.
/// PLACEHOLDER: the labels, the pilot's bank and offsets, the message id, the static frame, the speech slot, the
/// portrait, the previous state, the deadline and the scream's phases, with the paints and the state machine that use
/// them, are plan step 6c6's part C (docs/herculan/plan-es1-first-mission.md).
/// </summary>
public sealed class Es1HddCommBox {
	/// <summary>
	/// <c>CommBox_ElementCtor</c> (<c>ES1DBSIM 0003cab8</c>)(gauge), from the display's <c>__vec_ctor</c>; the
	/// constructor's own work is the rects' and the offset pairs' constructors.
	/// </summary>
	internal Es1HddCommBox() {
	}

	/// <summary><c>+0x1d</c>, the video rect: the box's button's rect inset by one pixel, which the display's repaint sets.</summary>
	public Es1Rect VideoRect { get; set; }

	/// <summary><c>+0x2d</c>, the marker rect, the <c>.GAU</c>'s, which the box's button paints in highlight mode 1.</summary>
	public Es1Rect MarkerRect { get; set; }

	/// <summary><c>+0x137</c>, the pilot's name, none until <c>HddGauge_LoadPilotFrames</c> sets it.</summary>
	public string? Name { get; set; }

	/// <summary><c>+0x13b</c>, the state, 0 idle to 3.</summary>
	public int State { get; set; }

	/// <summary><c>+0x147</c>, the comms-out latch, set for a destroyed squadmate.</summary>
	public bool CommsOutLatch { get; set; }
}
