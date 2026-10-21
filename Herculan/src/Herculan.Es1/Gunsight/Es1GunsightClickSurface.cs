using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The click surface, <c>Gunsight_ClickSurface_Ctor</c> (<c>ES1DBSIM 000345bc</c>): a <c>0x28</c>-byte child on
/// <c>HUDRovingGunsightVtable</c> (<c>0009379e</c>) whose gadget, kept at <c>+0x24</c>, is registered on the cockpit's
/// root, and whose paint gives the forward view the gunsight's cursor while the pointer is over it
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-click-surface).
/// </summary>
public sealed class Es1GunsightClickSurface : Es1GunsightFieldChild {
	/// <summary>
	/// The constructor: the child, and its <c>0x20</c>-byte gadget built over the same rect (<c>Widget_CtorRect</c>), its
	/// state and drag flag 0, and registered with <c>Widget_RegisterClickable</c> on the cockpit's root.
	/// </summary>
	public Es1GunsightClickSurface(Es1Cockpit cockpit, Es1Rect rect, Es1Gunsight gunsight) : base(cockpit, rect) {
		Gadget = new Es1GunsightGadget(gunsight, cockpit) { Rect = rect };
		cockpit.Root.RegisterClickable(Gadget);
	}

	/// <summary><c>+0x24</c>, the gadget.</summary>
	public Es1GunsightGadget Gadget { get; }

	/// <summary>
	/// <c>Gunsight_ClickSurface_Paint</c> (<c>000346a4</c>): the cursor's position (<c>Cursor_GetPosition</c>) into
	/// <c>+0x1c</c>/<c>+0x20</c>, and <c>Cockpit_SetForwardCursor</c> with <see cref="Es1CockpitCursors.Gunsight"/> while
	/// it lies in the rect, bounds inclusive, and <see cref="Es1CockpitCursors.Arrow"/> otherwise.
	/// </summary>
	public override void Paint() {
		(CenterX, CenterY) = Cockpit.Cursor.GetPosition();
		Cockpit.Cursors.SetForwardCursor(Rect.Contains(CenterX, CenterY) ? Es1CockpitCursors.Gunsight : Es1CockpitCursors.Arrow);
	}
}

/// <summary>
/// The click surface's gadget: a button on <c>CTLButtonControlVtableGunsightCopy</c> (<c>000937b2</c>), the button
/// base's table, under <c>HUDRovingGunsightGadgetVtable</c> (<c>000937da</c>), whose click slot is
/// <c>Gunsight_ClickGadget_OnClick</c> (<c>000337dc</c>) and whose other slots do what the base's do.
/// </summary>
public sealed class Es1GunsightGadget : Es1ButtonWidget {
	private readonly Es1Gunsight _gunsight;
	private readonly Es1Cockpit _cockpit;

	internal Es1GunsightGadget(Es1Gunsight gunsight, Es1Cockpit cockpit) {
		_gunsight = gunsight;
		_cockpit = cockpit;
	}

	/// <summary>
	/// <c>Gunsight_ClickGadget_OnClick</c>(gadget, <paramref name="value"/>): with bit 0, the left button, set,
	/// <see cref="Es1Gunsight.PickArmed"/> set and <c>ObjPick_Arm</c> at <c>CockpitMouse_CanvasPoint</c>
	/// (<c>000b16d4</c>), the click's point as the widget input code stores it (<see cref="Es1ObjectPick.Arm"/>).
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) != 0) {
			_gunsight.PickArmed = true;
			var (x, y) = _cockpit.MouseCanvasPoint;
			_cockpit.Globals.ObjectPick.Arm(x, y);
		}
	}
}
