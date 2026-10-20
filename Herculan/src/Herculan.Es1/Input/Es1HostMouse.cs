using System.Numerics;
using Herculan.Engine.Render;
using Silk.NET.Input;

namespace Herculan.Es1.Input;

/// <summary>A record the mouse driver hands its callbacks and the live block copies: the pointer and the button word, <c>{x, y, buttons}</c>.</summary>
/// <param name="X">The pointer's column of the 320 × 200 screen.</param>
/// <param name="Y">Its row.</param>
/// <param name="Buttons">Bit 0 the left button, bit 1 the right, bit 2 the middle.</param>
public readonly record struct Es1MouseRecord(int X, int Y, ushort Buttons);

/// <summary>
/// The host's stand-in for the DOS mouse driver as ES1 drives it (docs/es1/simulation/input.md#the-mouse-driver): the
/// pointer and the buttons <c>Mouse_EventHandler</c> (<c>ES1DBSIM 00055c6f</c>) keeps in <c>Mouse_X</c>, <c>Mouse_Y</c>
/// and <c>Mouse_Buttons</c> (<c>000962e8</c>–<c>000962f0</c>), held from one event to the next, and the callbacks
/// <c>Mouse_AddCallback</c> registers, each run for the events its mask names.
///
/// <para><b>This engine's own:</b> the host's pointer is mapped into the 320 × 200 screen through the monitor's 4:3
/// placement (<see cref="Pointer"/>, <see cref="ScreenPointer"/>, docs/herculan/plan-es1-first-mission.md decision 7)
/// and held to x 0–319 and y 0–199, the bounds <c>Input_BuildSourceTable</c> gives the driver. A host move is one move
/// event and a host button change one press or release event, each with the pointer where the host has it.</para>
/// </summary>
public sealed class Es1HostMouse {
	/// <summary>The button word's bits.</summary>
	public const ushort LeftBit = 1, RightBit = 2, MiddleBit = 4;

	/// <summary>
	/// The driver's event mask: bit 0 the pointer moved, 1 and 2 the left button pressed and released, 3 and 4 the right's
	/// (docs/es1/simulation/cockpit-mouse.md#the-callback-and-the-queue), 5 and 6 the middle's, <c>INT 33h</c>'s.
	/// </summary>
	public const byte Moved = 1, LeftPressed = 2, LeftReleased = 4, RightPressed = 8, RightReleased = 0x10,
		MiddlePressed = 0x20, MiddleReleased = 0x40;

	/// <summary>The callback slots <c>Mouse_CallbackMask</c> (<c>000962f4</c>) has.</summary>
	public const int CallbackSlots = 32;

	private readonly (Action<Es1MouseRecord> Callback, byte Mask)?[] _callbacks = new (Action<Es1MouseRecord>, byte)?[CallbackSlots];
	private IMouse? _device;
	private bool _positionSet;

	/// <summary>The pointer's column, <c>Mouse_X</c>, 0–319.</summary>
	public int X { get; private set; }

	/// <summary>The pointer's row, <c>Mouse_Y</c>, 0–199.</summary>
	public int Y { get; private set; }

	/// <summary>The button word, <c>Mouse_Buttons</c>, the driver's bits 0, 1 and 2.</summary>
	public ushort Buttons { get; private set; }

	/// <summary>The window's mapping between the host's pointer and the screen, as it stands; with none the pointer is not read.</summary>
	public Func<ScreenPointer>? Pointer { get; set; }

	/// <summary>
	/// Takes the host mouse's events; a second call moves them to the new device. A position <see cref="SetPosition"/>
	/// gave before there was a device is put on the host's pointer now, as the driver's pointer would already be there.
	/// </summary>
	public void Attach(IMouse mouse) {
		Detach();
		_device = mouse;
		mouse.MouseMove += OnMouseMove;
		mouse.MouseDown += OnMouseDown;
		mouse.MouseUp += OnMouseUp;
		if (_positionSet) {
			WarpHost();
		}
	}

	/// <summary>Stops taking the host mouse's events; the pointer and the word keep their last values.</summary>
	public void Detach() {
		if (_device == null) {
			return;
		}

		_device.MouseMove -= OnMouseMove;
		_device.MouseDown -= OnMouseDown;
		_device.MouseUp -= OnMouseUp;
		_device = null;
	}

	/// <summary>
	/// <c>Mouse_AddCallback</c> (<c>00055bec</c>)(callback, mask): the lowest free slot takes the callback and the mask's
	/// low byte; returns the slot + 1, or 0 when all are taken, where retail still writes a mask byte past its table.
	/// </summary>
	public int AddCallback(Action<Es1MouseRecord> callback, byte mask) {
		for (int slot = 0; slot < CallbackSlots; slot++) {
			if (_callbacks[slot] == null) {
				_callbacks[slot] = (callback, mask);
				return slot + 1;
			}
		}

		return 0;
	}

	/// <summary><c>Mouse_RemoveCallback</c> (<c>00055c35</c>)(slot): frees slot − 1.</summary>
	public void RemoveCallback(int slot) {
		if (slot >= 1 && slot <= CallbackSlots) {
			_callbacks[slot - 1] = null;
		}
	}

	/// <summary>
	/// <c>Mouse_SetPosition</c> (<c>00055ab8</c>)(<i>x</i>, <i>y</i>): the point becomes the pointer's last position and
	/// the host's pointer is moved onto that pixel of the screen.
	/// </summary>
	public void SetPosition(int x, int y) {
		X = x;
		Y = y;
		_positionSet = true;
		WarpHost();
	}

	/// <summary>
	/// <c>Mouse_PostEvent</c> (<c>00055bc2</c>)(event, mask): <c>Mouse_EventHandler</c> run as the driver would run it,
	/// with the event's point and buttons and <paramref name="mask"/>.
	/// </summary>
	public void PostEvent(Es1MouseRecord record, byte mask) => HandleEvent(mask, record.X, record.Y, record.Buttons);

	/// <summary>One host move to the client position (<paramref name="clientX"/>, <paramref name="clientY"/>): a move event at the screen pixel under it.</summary>
	internal void OnMove(float clientX, float clientY) {
		if (Pointer == null) {
			return;
		}

		var (x, y) = Pointer().ToScreen(clientX, clientY);
		HandleEvent(Moved, x, y, Buttons);
	}

	/// <summary>One host button event: the button's bit set or cleared, and its press or release event at the pointer. Other buttons are not the driver's three.</summary>
	internal void OnButton(MouseButton button, bool down) {
		var (bit, pressed, released) = button switch {
			MouseButton.Left => (LeftBit, LeftPressed, LeftReleased),
			MouseButton.Right => (RightBit, RightPressed, RightReleased),
			MouseButton.Middle => (MiddleBit, MiddlePressed, MiddleReleased),
			_ => ((ushort)0, (byte)0, (byte)0),
		};
		if (bit == 0) {
			return;
		}

		ushort buttons = (ushort)(down ? Buttons | bit : Buttons & ~bit);
		HandleEvent(down ? pressed : released, X, Y, buttons);
	}

	/// <summary>
	/// <c>Mouse_EventHandler</c>: the buttons, x and y stored, and each callback whose mask shares a bit with
	/// <paramref name="mask"/> called with the record.
	/// </summary>
	private void HandleEvent(byte mask, int x, int y, ushort buttons) {
		Buttons = buttons;
		X = x;
		Y = y;
		var record = new Es1MouseRecord(x, y, buttons);
		foreach (var entry in _callbacks) {
			if (entry is { } callback && (callback.Mask & mask) != 0) {
				callback.Callback(record);
			}
		}
	}

	private void WarpHost() {
		if (_device == null || Pointer == null) {
			return;
		}

		var (clientX, clientY) = Pointer().ToClient(X, Y);
		_device.Position = new Vector2(clientX, clientY);
	}

	private void OnMouseMove(IMouse mouse, Vector2 position) => OnMove(position.X, position.Y);

	private void OnMouseDown(IMouse mouse, MouseButton button) {
		SyncFromHost(mouse);
		OnButton(button, true);
	}

	private void OnMouseUp(IMouse mouse, MouseButton button) {
		SyncFromHost(mouse);
		OnButton(button, false);
	}

	/// <summary>The pointer read from the host before a button event, which the driver reports with the pointer where it is.</summary>
	private void SyncFromHost(IMouse mouse) {
		if (Pointer != null) {
			(X, Y) = Pointer().ToScreen(mouse.Position.X, mouse.Position.Y);
		}
	}
}
