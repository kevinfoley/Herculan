using Herculan.Es1.Input;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's mouse queue (docs/es1/shell/map.md#picking-a-gadget), which presses the war map's gadgets: the shell's
/// copies of the simulator's <c>CockpitMouse_*</c> functions, their statements in other code but for the globals and the
/// guards below, run on <see cref="Es1CockpitMouse"/>. The shell's queue has its own callback flag, follow byte and
/// clock, presses the menus' widget globals (<see cref="Menus.Es1MenuGlobals.Widgets"/>), whose active root the map's
/// gadget window becomes, and replays no tape. The menus' windows take the mouse through their own callbacks
/// (<see cref="Menus.Es1ShellMouse"/>).
/// </summary>
public sealed class Es1ShellMouseQueue {
	private readonly Es1CockpitMouse _queue;
	private readonly Es1HostMouse _driver;

	/// <param name="driver">The shell's mouse, <c>Input_GetMouseDevice_Thunk</c>'s answer.</param>
	/// <param name="cursor">The display's cursor, which the callback moves while <see cref="FollowsMouse"/>.</param>
	/// <param name="widgets">The menus' widget globals.</param>
	public Es1ShellMouseQueue(Es1HostMouse driver, Es1Cursor cursor, Es1WidgetGlobals widgets) {
		_driver = driver;
		_queue = new Es1CockpitMouse(cursor, widgets);
	}

	/// <summary>The queue the shell's functions share with the simulator's.</summary>
	public Es1CockpitMouse Queue => _queue;

	/// <summary>
	/// The byte <c>000aa778</c>, 0 in the image, which the war map sets and its destructor clears: the callback
	/// <c>00010900</c> queues nothing while it is clear (docs/es1/shell/war-map.md#open).
	/// </summary>
	public bool Live {
		get => _queue.Live;
		set => _queue.Live = value;
	}

	/// <summary>
	/// The byte <c>0007e5df</c>, 1 in the image, which <see cref="Es1ShellMission.Prepare"/> clears: while it is set the
	/// callback moves the drawn cursor to the pointer.
	/// </summary>
	public bool FollowsMouse {
		get => _queue.FollowsMouse;
		set => _queue.FollowsMouse = value;
	}

	/// <summary>The clock the callback stamps records with, <c>0007e6fc</c> (<see cref="Es1ShellTime.AnmClock"/>); 0 until the host wires it in.</summary>
	public Func<uint> Now {
		get => _queue.Now;
		set => _queue.Now = value;
	}

	/// <summary>
	/// <c>MouseEventQueue_Start</c> (<c>00010559</c>), <c>CockpitMouse_Init</c>'s statements (<see cref="Es1CockpitMouse.Init"/>):
	/// the callback <c>00010900</c> registered with the shell's mouse, its slot kept at <c>0007e5d4</c>; the queues of
	/// <c>CTL_MGR.BND</c>'s offset 4, 100 records, one less taken; no active root and nothing pressed.
	/// </summary>
	public void Start() => _queue.Init(_driver);

	/// <summary>
	/// <c>MouseEventQueue_Stop</c> (<c>000106af</c>): the callback taken off the driver, the slot <c>0007e5d4</c>
	/// cleared and both queues freed (<see cref="Es1CockpitMouse.RemoveCallback"/>).
	/// </summary>
	public void Stop() => _queue.RemoveCallback();

	/// <summary>
	/// The shell's <c>CockpitMouse_ProcessQueue</c> (<c>0001098e</c>), which <c>Shell_PollInput</c> runs first: nothing
	/// while no callback is registered or no root is active; otherwise <see cref="Es1CockpitMouse.ProcessQueue"/>.
	/// </summary>
	public void Process() {
		if (_queue.CallbackSlot == 0 || _queue.Widgets.ActiveRoot == null) {
			return;
		}

		_queue.ProcessQueue();
	}
}
