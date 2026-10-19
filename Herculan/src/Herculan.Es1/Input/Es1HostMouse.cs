using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>
/// <b>This engine's own:</b> the host's stand-in for the DOS mouse driver's buttons as <c>Mouse_EventHandler</c>
/// (<c>ES1DBSIM 00055c6f</c>) keeps them in <c>Mouse_Buttons</c> (<c>000962f0</c>): bit 0 the left button, bit 1 the
/// right and bit 2 the middle, the driver's <c>BX</c> (docs/es1/simulation/input.md#the-mouse-driver). Each host
/// button event updates the word, and it holds from one event to the next, as the live block's last mouse record does
/// (docs/es1/simulation/input.md#the-live-block--input_polldeviceblock-0004c2d0). The pointer is not kept: the input
/// build reads 0 for it until plan step 6.
/// </summary>
public sealed class Es1HostMouse {
	public const ushort LeftBit = 1, RightBit = 2, MiddleBit = 4;

	private IMouse? _device;

	/// <summary>The button word, the driver's bits 0, 1 and 2.</summary>
	public ushort Buttons { get; private set; }

	/// <summary>Takes the host mouse's button events; a second call moves them to the new device.</summary>
	public void Attach(IMouse mouse) {
		Detach();
		_device = mouse;
		mouse.MouseDown += OnMouseDown;
		mouse.MouseUp += OnMouseUp;
	}

	/// <summary>Stops taking the host mouse's events; the word keeps its last value.</summary>
	public void Detach() {
		if (_device == null) {
			return;
		}

		_device.MouseDown -= OnMouseDown;
		_device.MouseUp -= OnMouseUp;
		_device = null;
	}

	/// <summary>One host button event: sets or clears the button's bit. Other buttons are not the driver's three.</summary>
	internal void OnButton(MouseButton button, bool down) {
		ushort bit = button switch {
			MouseButton.Left => LeftBit,
			MouseButton.Right => RightBit,
			MouseButton.Middle => MiddleBit,
			_ => 0,
		};

		Buttons = (ushort)(down ? Buttons | bit : Buttons & ~bit);
	}

	private void OnMouseDown(IMouse mouse, MouseButton button) => OnButton(button, true);

	private void OnMouseUp(IMouse mouse, MouseButton button) => OnButton(button, false);
}
