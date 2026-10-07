namespace Herculan.Engine.Input;

/// <summary>
/// The pointer as the simulator's modal panels use it: read straight off the device, and moved — every focus
/// change puts it on the widget focused, and a panel coming down puts it back.
/// </summary>
public interface IPointerDevice {
	/// <summary>Where the pointer is and what it holds, in framebuffer pixels; NaN with no pointer.</summary>
	(float X, float Y, CockpitMouseButtons Buttons) Pointer();

	/// <summary>Puts the pointer on a framebuffer pixel — <c>Mouse_WarpCursorToPoint</c> (<c>004807d0</c>).</summary>
	void WarpPointer(float x, float y);
}
