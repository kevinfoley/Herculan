using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Input;

/// <summary>One record of the cockpit's mouse queue, <c>0xe</c> bytes: the driver's record and its time in counts of the clock <c>000981c4</c>.</summary>
public readonly record struct Es1MouseEvent(Es1MouseRecord Record, uint Time);

/// <summary>
/// The cockpit's mouse (docs/es1/simulation/cockpit-mouse.md#the-callback-and-the-queue): the driver's callback,
/// <c>CockpitMouse_OnEvent</c> (<c>ES1DBSIM 00044464</c>), which queues each change of the pointer or its buttons, a
/// move at most once a count of the 60-a-second clock and a button change always; and
/// <c>CockpitMouse_ProcessQueue</c> (<c>000444d0</c>), which the input build runs once a tick to press, release and drag
/// the active root's widgets with them (docs/es1/simulation/cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0).
/// It holds the UI library's globals the two share (<see cref="Widgets"/>) and <c>Cursor_FollowsMouse</c>.
/// </summary>
public sealed class Es1CockpitMouse {
	/// <summary>The driver events <c>CockpitMouse_Init</c> registers for: the move and the left and right buttons' presses and releases.</summary>
	public const byte EventMask = 0x1f;

	/// <summary>The records a queue takes; <c>CockpitMouseQueue_Push</c> refuses one at 99.</summary>
	public const int QueueLimit = 99;

	/// <summary>The two queues' buffers, 100 records each.</summary>
	private const int QueueCapacity = 100;

	private List<Es1MouseEvent> _back = new(QueueCapacity);
	private List<Es1MouseEvent> _front = new(QueueCapacity);
	/// <param name="cursor">The display library's cursor the pointer moves.</param>
	public Es1CockpitMouse(Es1Cursor cursor) {
		Cursor = cursor;
	}

	/// <summary>The UI library's press, release and drag globals.</summary>
	public Es1WidgetGlobals Widgets { get; } = new();

	/// <summary>The display library's cursor, which <c>Cursor_SyncPosition</c> moves.</summary>
	public Es1Cursor Cursor { get; }

	/// <summary><c>Input_GetMouseDevice_Thunk</c>'s answer as <see cref="Init"/> found it: the driver, or none.</summary>
	public Es1HostMouse? Driver { get; private set; }

	/// <summary>
	/// <c>CockpitMouse_CallbackSlot</c> (<c>00094c52</c>), the driver's slot + 1 that <see cref="Init"/>'s registration took.
	/// <c>CockpitMouse_Shutdown</c> (<c>000442fc</c>), which would free it, has no caller <c>es2_xref.py</c> finds and is
	/// not ported.
	/// </summary>
	public int CallbackSlot { get; private set; }

	/// <summary>
	/// The clock <c>000981c4</c> (<see cref="Es1SimClock.Clock"/>), read at each event; 0 until the host wires the
	/// simulation's clock in.
	/// </summary>
	public Func<uint> Now { get; set; } = () => 0;

	/// <summary><c>CockpitMouseLive</c> (<c>000bd512</c>): the callback queues nothing while clear.</summary>
	public bool Live { get; set; }

	/// <summary>
	/// <c>Cursor_FollowsMouse</c> (<c>00094c5b</c>): while set the callback moves the drawn cursor to the pointer; 1 in the
	/// image, cleared for the external view and set on the return (docs/es1/simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904).
	/// </summary>
	public bool FollowsMouse { get; set; } = true;

	/// <summary><c>TapePlayback</c>, whose replay moves the cursor from the queue. Always clear: no plan step ports tapes.</summary>
	public bool TapePlayback => false;

	/// <summary><c>CockpitMouse_DoubleClickTicks</c> (<c>000bd518</c>): the timer's interrupts a second halved, 60, a second of the clock.</summary>
	public uint DoubleClickTicks { get; private set; }

	/// <summary><c>CockpitMouse_DoubleClick</c> (<c>00094c5c</c>): set by a release less than <see cref="DoubleClickTicks"/> after the same button's last.</summary>
	public bool DoubleClick { get; private set; }

	/// <summary><c>CockpitMouse_LastPoint</c> (<c>000b16c0</c>).</summary>
	public (int X, int Y) LastPoint { get; private set; }

	/// <summary><c>CockpitMouse_CanvasPoint</c> (<c>000b16d4</c>): the last point less the root's viewport origin, which the gunsight's pick reads.</summary>
	public (int X, int Y) CanvasPoint { get; private set; }

	/// <summary><c>CockpitMouse_LeftHeld</c>, <c>CockpitMouse_RightHeld</c> (<c>000bd50e</c>, <c>000bd510</c>).</summary>
	public bool LeftHeld { get; private set; }

	/// <inheritdoc cref="LeftHeld"/>
	public bool RightHeld { get; private set; }

	/// <summary><c>CockpitMouse_LastButtons</c> (<c>000bd538</c>).</summary>
	public ushort LastButtons { get; private set; }

	/// <summary><c>CockpitMouse_LastEventTime</c> (<c>00094cc8</c>), 0 in the image.</summary>
	public uint LastEventTime { get; private set; }

	/// <summary><c>CockpitMouse_LastLeftRelease</c>, <c>CockpitMouse_LastRightRelease</c> (<c>00094c60</c>, <c>00094c64</c>), 0 in the image.</summary>
	public uint LastLeftRelease { get; private set; }

	/// <inheritdoc cref="LastLeftRelease"/>
	public uint LastRightRelease { get; private set; }

	/// <summary>The records queued since the last <see cref="ProcessQueue"/>.</summary>
	public int Queued => _back.Count;

	/// <summary>
	/// <c>CockpitMouse_Init</c> (<c>000441cc</c>): <see cref="OnEvent"/> registered with <paramref name="driver"/> for
	/// <see cref="EventMask"/>; the last point, the held bytes and the last buttons cleared; both queues empty; the
	/// double-click window from the timer's divisor; no active root and nothing pressed.
	/// </summary>
	public void Init(Es1HostMouse? driver) {
		Driver = driver;
		if (driver != null) {
			CallbackSlot = driver.AddCallback(OnEvent, EventMask);
		}

		LastPoint = (0, 0);
		LeftHeld = false;
		RightHeld = false;
		LastButtons = 0;
		_back.Clear();
		_front.Clear();
		DoubleClickTicks = (uint)(Es1SimClock.InterruptsPerSecond / 2);
		Widgets.ActiveRoot = null;
		Widgets.PressedIndex = -1;
	}

	/// <summary>
	/// <c>CockpitMouse_OnEvent</c>(record), the driver's callback: nothing while not <see cref="Live"/>. With <i>t</i> the
	/// clock: buttons as they were move the drawn cursor while <see cref="FollowsMouse"/> and queue the record when
	/// <i>t</i> is past <see cref="LastEventTime"/>; changed buttons are stored and the record queued. Either way <i>t</i>
	/// becomes <see cref="LastEventTime"/>.
	/// </summary>
	public void OnEvent(Es1MouseRecord record) {
		uint now = Now();
		if (!Live) {
			return;
		}

		if (LastButtons == record.Buttons) {
			if (FollowsMouse) {
				Cursor.SyncPosition(record.X, record.Y);
			}

			if (LastEventTime < now) {
				Push(now, record);
			}
		} else {
			LastButtons = record.Buttons;
			Push(now, record);
		}

		LastEventTime = now;
	}

	/// <summary><c>CockpitMouseQueue_Push</c> (<c>00044830</c>)(time, record): false when the back queue holds <see cref="QueueLimit"/>; otherwise the record appended.</summary>
	public bool Push(uint time, Es1MouseRecord record) {
		if (_back.Count == QueueLimit) {
			return false;
		}

		_back.Add(new Es1MouseEvent(record, time));
		return true;
	}

	/// <summary>
	/// <c>CockpitMouse_ProcessQueue</c>: the queues swapped and the new back one emptied, and each record of the front
	/// one handled in order on <see cref="Es1WidgetGlobals.ActiveRoot"/>: its buttons to <c>Widget_ClickButtons</c>; a
	/// moved point tracks or drags the pressed widget; a left press presses (a right press presses nothing); a release
	/// sets the double-click, gives <c>Widget_ClickButtons</c> its button and releases or ends the drag. Returns the front
	/// queue, which the tape's recording appends. With no root, which retail does not meet once the cockpit is built,
	/// the widgets are left alone, an engine guard.
	/// </summary>
	public IReadOnlyList<Es1MouseEvent> ProcessQueue() {
		(_back, _front) = (_front, _back);
		_back.Clear();
		var globals = Widgets;
		foreach (var (record, time) in _front) {
			var root = globals.ActiveRoot;
			globals.ClickButtons = record.Buttons;
			var point = (record.X, record.Y);

			// 2. A move.
			if (point != LastPoint) {
				LastPoint = point;
				if (TapePlayback && FollowsMouse) {
					Cursor.SyncPosition(record.X, record.Y);
				}

				if (root != null) {
					CanvasPoint = CanvasOf(root, record);
					if (!globals.DragCapture) {
						if (root.HitTest(record.X, record.Y)) {
							root.TrackPressedWidget(globals, record.X, record.Y);
						}
					} else {
						var widget = root.Clickables[globals.PressedIndex];
						widget.DragTo(record.X - root.Rect.X0, record.Y - root.Rect.Y0);
						Es1WidgetRoot.Repaint(widget);
					}
				}
			}

			// 3. The presses.
			DoubleClick = false;
			bool leftPressed = false, released = false;
			if ((record.Buttons & Es1HostMouse.LeftBit) != 0 && !LeftHeld) {
				LeftHeld = true;
				leftPressed = true;
			}

			if ((record.Buttons & Es1HostMouse.RightBit) != 0 && !RightHeld) {
				RightHeld = true;
			}

			if (leftPressed && root != null) {
				CanvasPoint = CanvasOf(root, record);
				root.OnMouseDown(globals, record.X, record.Y);
			}

			// 4. The releases, the left's and then the right's; the window compared unsigned.
			if ((record.Buttons & Es1HostMouse.LeftBit) == 0 && LeftHeld) {
				LeftHeld = false;
				released = true;
				if (time - LastLeftRelease < DoubleClickTicks) {
					DoubleClick = true;
				}

				LastLeftRelease = time;
				globals.ClickButtons = (ushort)(record.Buttons | Es1HostMouse.LeftBit);
			}

			if ((record.Buttons & Es1HostMouse.RightBit) == 0 && RightHeld) {
				RightHeld = false;
				released = true;
				if (time - LastRightRelease < DoubleClickTicks) {
					DoubleClick = true;
				}

				LastRightRelease = time;
				globals.ClickButtons = (ushort)(record.Buttons | Es1HostMouse.RightBit);
			}

			// 5. After either release.
			if (released && root != null) {
				CanvasPoint = CanvasOf(root, record);
				if (!globals.DragCapture) {
					root.OnMouseUp(globals, record.X, record.Y);
				} else {
					var widget = root.Clickables[globals.PressedIndex];
					widget.State = Es1Widget.Up;
					Es1WidgetRoot.Repaint(widget);
					globals.DragCapture = false;
					widget.OnClick(widget.GetValue(globals));
					globals.PressedIndex = -1;
				}
			}
		}

		return _front;
	}

	/// <summary>The record's point less the root's viewport origin (<c>+0x227</c>, <c>+0x22b</c>).</summary>
	private static (int X, int Y) CanvasOf(Es1WidgetRoot root, Es1MouseRecord record) =>
		(record.X - root.ViewportRect.X0, record.Y - root.ViewportRect.Y0);
}
