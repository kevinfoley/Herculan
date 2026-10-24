using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The block the MFD's screens share, the display's <c>+0x7a</c>–<c>+0x89</c>
/// (docs/es1/simulation/mfd-display.md#the-display-object), which each screen's <c>+4</c> points at. All 0 from the
/// display's <c>memset</c>, but for the range. Its second dword, <c>+0x7e</c>, has no reader found
/// (docs/es1/simulation/mfd-display.md#open) and is not kept.
/// </summary>
public sealed class Es1MfdSharedBlock {
	/// <summary><c>+0x7a</c>, FLASH COMM's row.</summary>
	public int FlashCommRow { get; set; }

	/// <summary><c>+0x82</c>, the status screens' subject, read afresh at each paint (docs/es1/simulation/mfd-display.md#the-subject).</summary>
	public IEs1SimObject? Subject { get; set; }

	/// <summary><c>+0x86</c>, the scanner's range in world units.</summary>
	public int ScannerRange { get; set; }
}

/// <summary>
/// One of the MFD's six screens, on the base <c>MFDisplay_Ctor</c> (<c>ES1DBSIM 00039c50</c>)
/// (docs/es1/simulation/mfd-status-screen.md#the-screen-object): the rect <c>+0</c>, the display's screen rect, and the
/// shared block <c>+4</c>; its table at <c>+8</c> holds the paint and the update.
/// </summary>
public abstract class Es1MfdScreen {
	/// <summary><c>MFDisplay_Ctor</c>(screen, <paramref name="rect"/>), with <c>+4</c> pointed at <paramref name="shared"/> as the display does after.</summary>
	protected Es1MfdScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared) {
		Cockpit = cockpit;
		Rect = rect;
		Shared = shared;
	}

	/// <summary>The cockpit the screen draws through.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>, the rect, the display's <c>+0xb4</c>.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+4</c>, the display's shared block.</summary>
	public Es1MfdSharedBlock Shared { get; }

	/// <summary>Slot <c>+4</c>, the paint, which the display's repaint calls.</summary>
	public abstract void Paint();

	/// <summary>Slot <c>+8</c>, the update, which the display's per-frame update calls on the screen's schedule.</summary>
	public abstract void Update();
}

/// <summary>
/// PLACEHOLDER: a screen not yet ported, whose paint and update do nothing: FLASH COMM
/// (<c>MfdFlashCommScreen_Ctor</c>, <c>00035c04</c>) and the missile camera (<c>MfdMissileViewScreen_Ctor</c>,
/// <c>000360c0</c>), plan step 6c5 part C, which replaces it in <see cref="Es1MfdDisplay"/>'s constructor.
/// </summary>
public sealed class Es1MfdPlaceholderScreen : Es1MfdScreen {
	internal Es1MfdPlaceholderScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared)
		: base(cockpit, rect, shared) {
	}

	/// <inheritdoc/>
	public override void Paint() {
	}

	/// <inheritdoc/>
	public override void Update() {
	}
}
