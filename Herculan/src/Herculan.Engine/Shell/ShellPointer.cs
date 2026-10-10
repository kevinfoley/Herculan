namespace Herculan.Engine.Shell;

/// <summary>
/// The two buttons VSHELL's mouse events tell apart — <c>Mouse_OnButtonState</c> (<c>00408b03</c>)'s
/// sub-codes 2 and 1 for the left going down and up, 4 and 3 for the right.
/// </summary>
public enum ShellMouseButton {
	Left,
	Right,
}

/// <summary>
/// Which event handler a widget's class runs. They disagree about which button acts, and on which
/// edge, so a widget's class decides how a click on it behaves
/// (docs/retail/shell/widgets.md#which-widget-a-click-reaches).
/// </summary>
public enum ShellHandler {
	/// <summary>
	/// <c>WinButton_HandleEvent</c> (<c>004097da</c>) — panels, framed and titled panels and grids — and
	/// <c>ESArm_HandleEvent</c> (<c>0040c3b5</c>), the crew rows' copy of it. Either button: a
	/// press lights the widget, a release while it is lit fires, and a leave puts it out.
	/// </summary>
	Control,

	/// <summary>
	/// <c>ESButtonFont_HandleEvent</c> (<c>00409b0f</c>), every content button: <see cref="Control"/>'s rules,
	/// and the press sound on either button going down.
	/// </summary>
	Button,

	/// <summary>
	/// <c>ESButtonBitmap_HandleEvent</c> (<c>00409df2</c>), the tab strip's class. The left button makes the
	/// press sound and fires on the press; the right goes through <c>WinButton_HandleEvent</c>, silently,
	/// and fires on its release. A leave never puts it out.
	/// </summary>
	ButtonIcon,

	/// <summary>
	/// <c>ESButtonBitmap_HandleEvent</c> on a button its builder gave the auto-repeat flag <c>+0x61</c> and a
	/// clear <c>+0x5d</c> — the mission tab's arrows. The left press lights it and makes the press sound
	/// without firing, and the left release zeroes its repeat count <c>+0x65</c> and fires wherever the press
	/// was; the right goes through <c>WinButton_HandleEvent</c>, which leaves the count alone; a leave puts it
	/// out and zeroes the count. While it is lit, each tick of its alarm fires it again
	/// (<see cref="ShellPointer.RepeatTick"/>).
	/// </summary>
	RepeatButtonIcon,

	/// <summary>
	/// <c>ESRadioButton_HandleEvent</c> (<c>0040a139</c>), the handler of the <c>ButtonIcon</c> subclass <c>ESRadioButton_Ctor</c> (<c>0040a100</c>) builds — the
	/// preferences screen's checkboxes. The left press makes the press sound, lights the widget and fires;
	/// the left release does nothing; the right goes through <c>WinButton_HandleEvent</c>, silently, and
	/// fires on its release. A leave never puts it out.
	/// </summary>
	CheckBox,

	/// <summary>
	/// <c>ESBitmap_HandleEvent</c> (<c>0040b6da</c>), the crew portraits. Fires on any left release
	/// that reaches it, wherever the press was, and ignores the right button.
	/// </summary>
	ImagePanel,

	/// <summary>
	/// <c>ESDialog_HandleEvent</c> (<c>0040beaf</c>), the save list's rows and the name fields of the
	/// registration screen and the DEBUG dialog. Fires on the left press
	/// and takes the pointer; the right button does nothing.
	/// </summary>
	EditField,
}

/// <summary>What a widget is, for telling one from another — the kind and which one of that kind.</summary>
public enum ShellWidgetKind {
	StripButton,
	MainMenuButton,
	PracticeRow,
	PracticeButton,
	PreferencesWidget,
	SaveRow,
	SaveButton,
	RepairRow,
	RepairHotspot,
	RepairButton,
	SquadRow,
	CrewRow,
	CrewRowPortrait,
	CrewSquadPortrait,
	CrewClear,
	BuildChassisRow,
	BuildButton,
	WeaponsRow,
	WeaponsButton,
	WeaponsHotspot,
	ArmoryRow,
	ArmoryButton,
	MissionButton,
	MissionArrow,
	ScrapDialogButton,
	LaunchRefusalOkay,
	EndOfGameOkay,
	ReplayButton,
	RegistrationField,
	RegistrationButton,
	MissionNameField,
	MissionNameButton,
}

/// <summary>One widget. <see cref="Sub"/> is a second index where one kind needs two, as the repair lists' <c>(column, row)</c> do.</summary>
public readonly record struct ShellWidget(ShellWidgetKind Kind, int Index, int Sub = 0);

/// <summary>
/// What the pointer is over: the widget a mouse event is delivered to, its class, and which of its
/// <c>Text</c> children the point is on, or <c>-1</c> for none.
///
/// <para><b>The child is part of the hit.</b> The pump hit-tests down to the innermost widget and
/// sends a leave whenever that changes. A <c>Text</c> takes no mouse events, so its leave climbs to its
/// parent, which puts the parent out: moving from one of a row's text columns to the next cancels a
/// press on that row, just as leaving it would.</para>
/// </summary>
public readonly record struct ShellHit(ShellWidget Widget, ShellHandler Handler, int Leaf = -1) {
	/// <summary>
	/// Which of <paramref name="children"/>, each in <paramref name="widget"/>'s own coordinates, a
	/// canvas point is on, or <c>-1</c>. Siblings are hit newest first, so where two share an edge the
	/// one later in the list — built later — answers.
	/// </summary>
	public static int LeafAt(ShellRect widget, ReadOnlySpan<ShellRect> children, float canvasX, float canvasY) {
		for (int i = children.Length - 1; i >= 0; i--) {
			var child = children[i];
			if (new ShellRect(widget.X0 + child.X0, widget.Y0 + child.Y0, widget.X0 + child.X1,
					widget.Y0 + child.Y1).Contains(canvasX, canvasY)) {
				return i;
			}
		}

		return -1;
	}

	/// <summary>
	/// A <c>Button</c> at <paramref name="rect"/>. Its caption is the <c>Text</c> child
	/// <c>ESButtonFont_Ctor</c> builds at <c>{1, 0, w, h}</c>, so the button's own left column is the one part
	/// of it the caption does not cover.
	/// </summary>
	public static ShellHit Button(ShellWidget widget, ShellRect rect, float canvasX, float canvasY) =>
		new(widget, ShellHandler.Button,
			LeafAt(rect, [new ShellRect(1, 0, rect.Width - 1, rect.Height - 1)], canvasX, canvasY));

	/// <summary>
	/// A list row at <paramref name="rect"/> whose four text columns <c>ESQuad_AddColumns</c>
	/// (<c>0040a310</c>) cut at <paramref name="cut1"/>, <paramref name="cut2"/> and
	/// <paramref name="cut3"/>: each column is the row's full height, the first starts at 2 and the last
	/// ends one inside the row's right edge, so only the two columns at the left and the right edge
	/// itself are the row's own.
	/// </summary>
	public static ShellHit ListRow(ShellWidget widget, ShellRect rect, int cut1, int cut2, int cut3,
			float canvasX, float canvasY) {
		int bottom = rect.Height - 1;
		return new(widget, ShellHandler.Control, LeafAt(rect, [
			new ShellRect(2, 0, cut1, bottom),
			new ShellRect(cut1, 0, cut2, bottom),
			new ShellRect(cut2, 0, cut3, bottom),
			new ShellRect(cut3, 0, rect.Width - 2, bottom),
		], canvasX, canvasY));
	}
}

/// <summary>
/// VSHELL's pointer: which widget the last move left under it, and the per-widget state its handlers
/// keep between a press and a release. Every mouse event is delivered to that one widget and its
/// class decides what happens (<see cref="ShellHandler"/>); nothing is handed on to a parent.
///
/// <para>The state is the original's: the pointer's target, its lock (<c>+0x1f</c> of the pointer
/// state at <c>g_EventQueue</c> (<c>005ddbd0</c>)), the lit flag <c>+0x45</c> of whichever content widget a press lit,
/// an auto-repeating button's repeat count <c>+0x65</c>, and an edit field's focus flag <c>+0xa7</c>. The strip's lit flags live on its
/// <see cref="ShellButton"/>s, because they are also the tab latch.</para>
///
/// <para><c>WinButton_HandleEvent</c> and <c>ESButtonBitmap_HandleEvent</c> also ignore mouse events while a
/// movie is playing or the movie queue is running (<c>Avi_Playing</c>, <c>MovieQueue_Running</c>).
/// That test is the host's: it delivers nothing here while <see cref="ShellMovieRun"/> is active.</para>
/// </summary>
public sealed class ShellPointer {
	/// <summary>
	/// The delay and period of the alarm an auto-repeating button installs when it is shown and removes when
	/// it is hidden (<c>ESButtonBitmap_HandleEvent</c>'s events 1 and 2), so its ticks run from the show and
	/// not from the press.
	/// </summary>
	public const int RepeatAlarmMilliseconds = 500;

	private readonly ShellScreen _strip;
	private readonly Action? _pressSound;
	private ShellHit? _underPointer;
	private ShellWidget? _lit;
	private ShellWidget? _focused;
	private readonly Dictionary<ShellWidget, int> _repeats = new();

	/// <param name="strip">The strip whose buttons keep their own lit flags.</param>
	/// <param name="pressSound">
	/// The press sound, <c>ShellSound_PlayPress</c> (<c>0042eecf</c>), for the classes that make it;
	/// null for none.
	/// </param>
	public ShellPointer(ShellScreen strip, Action? pressSound = null) {
		_strip = strip;
		_pressSound = pressSound;
	}

	/// <summary>The widget mouse events go to, or null.</summary>
	public ShellHit? Target { get; private set; }

	/// <summary>
	/// Whether an edit field has taken the pointer. While it has, a move changes nothing and every
	/// button event goes to that field.
	/// </summary>
	public bool Locked { get; private set; }

	/// <summary>The content widget a press has lit and a release would fire, or null.</summary>
	public ShellWidget? Lit => _lit;

	/// <summary>The edit field whose focus flag <c>+0xa7</c> is set, or null.</summary>
	public ShellWidget? Focused => _focused;

	/// <summary>An auto-repeating button's <c>+0x65</c>: the ticks it has fired on since its count was last zeroed.</summary>
	public int RepeatCount(ShellWidget widget) => _repeats.GetValueOrDefault(widget);

	/// <summary>
	/// A tick of <paramref name="widget"/>'s alarm (event <c>0x200</c>), which <c>ESButtonBitmap_HandleEvent</c>
	/// acts on for an auto-repeating button that is lit, enabled and shown: one more on its count, a repaint,
	/// and its handler run again. The caller passes only buttons that are shown. Whichever button lit it, so a
	/// right press repeats as a left one does.
	/// </summary>
	public void RepeatTick(ShellWidget widget, Action<ShellWidget> fire) {
		if (_lit != widget) {
			return;
		}

		_repeats[widget] = RepeatCount(widget) + 1;
		fire(widget);
	}

	/// <summary>
	/// What <c>SaveScreen_BeginRename</c> (<c>004377d2</c>) does to the pointer: the target moved onto
	/// <paramref name="field"/> without a hit test (<c>Pointer_SetTarget</c>, <c>00469cbc</c>), the lock
	/// set, and a left press posted at it, which gives the field the focus and runs its handler.
	/// </summary>
	public void Grab(ShellHit field, Action<ShellWidget> fire) {
		Leave();
		Target = field;
		Locked = true;
		Press(ShellMouseButton.Left, fire);
	}

	/// <summary>
	/// Enter in a focused edit field: the focus cleared and the lock released (<c>Pointer_Unlock</c>,
	/// <c>00469cdc</c>), which hit-tests again where the pointer is.
	/// </summary>
	public void ReleaseFocus() {
		_focused = null;
		Locked = false;
		Move(_underPointer);
	}

	/// <summary>
	/// A move to wherever <paramref name="hit"/> is. When the hit differs from the target, the target is
	/// sent a leave and the new hit becomes the target, as <c>EventQueue_Pump</c> (<c>00469ba4</c>)
	/// does; none of these classes acts on the enter that follows. While the pointer is locked the pump
	/// skips the hit test and nothing changes, but the position is kept for when it is released.
	/// </summary>
	public void Move(ShellHit? hit) {
		_underPointer = hit;
		if (!Locked && hit != Target) {
			Leave();
			Target = hit;
		}
	}

	/// <summary>A button going down, delivered to the target. <paramref name="fire"/> runs the widget's handler.</summary>
	public void Press(ShellMouseButton button, Action<ShellWidget> fire) {
		if (Target is not { } target) {
			return;
		}

		switch (target.Handler) {
			case ShellHandler.Control:
				_lit = target.Widget;
				break;

			case ShellHandler.Button:
				_pressSound?.Invoke();
				_lit = target.Widget;
				break;

			case ShellHandler.RepeatButtonIcon:
				if (button == ShellMouseButton.Left) {
					_pressSound?.Invoke();
				}

				_lit = target.Widget;
				break;

			case ShellHandler.CheckBox:
				_lit = target.Widget;
				if (button == ShellMouseButton.Left) {
					_pressSound?.Invoke();
					fire(target.Widget);
				}

				break;

			case ShellHandler.ButtonIcon when _strip.Button(target.Widget.Index) is { Enabled: true } strip:
				strip.Lit = true;
				if (button == ShellMouseButton.Left) {
					_pressSound?.Invoke();
				}

				strip.Repaint();
				if (button == ShellMouseButton.Left) {
					fire(target.Widget);
				}

				break;

			case ShellHandler.EditField when button == ShellMouseButton.Left:
				PressEditField(target.Widget, fire);
				break;
		}
	}

	/// <summary>A button coming up, delivered to the target.</summary>
	public void Release(ShellMouseButton button, Action<ShellWidget> fire) {
		if (Target is not { } target) {
			return;
		}

		switch (target.Handler) {
			case ShellHandler.Control or ShellHandler.Button when _lit == target.Widget:
				fire(target.Widget);
				_lit = null;
				break;

			case ShellHandler.CheckBox when button == ShellMouseButton.Right && _lit == target.Widget:
				fire(target.Widget);
				_lit = null;
				break;

			// The left release is the class's own: it zeroes the repeat count before firing. The right is
			// WinButton_HandleEvent's, which knows nothing of the count.
			case ShellHandler.RepeatButtonIcon when button == ShellMouseButton.Left || _lit == target.Widget:
				_lit = null;
				if (button == ShellMouseButton.Left) {
					_repeats.Remove(target.Widget);
				}

				fire(target.Widget);
				break;

			// The left release puts the flag out without repainting, so a tab that has just latched
			// stays drawn lit. The right release fires first and then puts it out and repaints — after
			// the handler has latched the tab, so a tab picked with the right button ends up unlit.
			case ShellHandler.ButtonIcon when _strip.Button(target.Widget.Index) is { Enabled: true } strip:
				if (button == ShellMouseButton.Left) {
					strip.Lit = false;
				} else if (strip.Lit) {
					fire(target.Widget);
					strip.Lit = false;
					strip.Repaint();
				}

				break;

			case ShellHandler.ImagePanel when button == ShellMouseButton.Left:
				fire(target.Widget);
				break;
		}
	}

	/// <summary>
	/// The leave the target is sent. <see cref="ShellHandler.Control"/> acts on it, and so does a
	/// <c>ButtonIcon</c> whose <c>+0x5d</c> its builder cleared, which also zeroes its repeat count; the strip
	/// keeps the 1 <c>ESButtonBitmap_Ctor</c> sets and stays lit.
	/// </summary>
	private void Leave() {
		if (Target is { Handler: ShellHandler.Control or ShellHandler.Button or ShellHandler.RepeatButtonIcon } target
				&& _lit == target.Widget) {
			_lit = null;
			if (target.Handler == ShellHandler.RepeatButtonIcon) {
				_repeats.Remove(target.Widget);
			}
		}
	}

	/// <summary>
	/// <c>ESDialog_HandleEvent</c>'s press. An unfocused field takes the focus and the pointer and
	/// fires. A focused one — the pointer is locked onto it, so every press comes here — gives both up,
	/// hit-tests again, and posts the press over, so it lands on whatever is under the pointer now.
	/// </summary>
	private void PressEditField(ShellWidget field, Action<ShellWidget> fire) {
		if (_focused != field) {
			_focused = field;
			Locked = true;
			fire(field);
			return;
		}

		_focused = null;
		Locked = false;
		Move(_underPointer);
		Press(ShellMouseButton.Left, fire);
	}
}
