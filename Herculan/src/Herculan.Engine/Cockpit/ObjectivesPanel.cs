using Herculan.Engine.Content;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The in-mission objectives panel — <c>obj_alrt</c> (<c>ObjectivesPanel_Ctor</c>, <c>0045751c</c>), what [F11] puts up over
/// the cockpit. It lists <c>script.dat</c> block 13, which is the mission's objective text as the
/// player is shown it; see docs/retail/simulation/alert-panels.md.
///
/// <para>This type is the panel's data and its state. Its geometry is in
/// <see cref="ObjectivesPanelLayout"/> and its pixels are drawn by
/// <see cref="Render.Cockpit.AlertPanelPainter.DrawObjectivesPanel"/>, out of the same sprite atlas and
/// <c>.HFN</c> fonts the rest of the cockpit draws from.</para>
///
/// <para><b>It is modal, and the simulation does not tick behind it.</b> The original runs its own
/// event loop (<c>ObjectivesPanel_RunModal</c>, <c>00457ae4</c>) that polls input, repaints the widgets it owns and presents —
/// and never calls the sim tick. Entering also pauses both message ports
/// (<c>MessagePort_Pause</c>) and saves the framebuffer, so the frozen cockpit and whatever the
/// computer was saying are still there when the panel comes down.</para>
/// </summary>
public sealed class ObjectivesPanel {
	/// <summary>The panel's own string table: its title and its one button's caption.</summary>
	public const string StringsFileName = "OBJ_ALRT.STR";

	/// <summary>The panel background's sprite bank — one frame, the whole plate.</summary>
	public const string BackgroundBank = "OBJ_ALRT";

	/// <summary>Group 0 of <see cref="StringsFileName"/> — <c>OBJECTIVES</c>.</summary>
	private const int TitleGroup = 0;

	/// <summary>Group 1 — <c>RETURN</c>.</summary>
	private const int ButtonGroup = 1;

	private ObjectivesPanel(string title, string buttonCaption, IReadOnlyList<string> lines) {
		Title = title;
		ButtonCaption = buttonCaption;
		Lines = lines;
	}

	/// <summary>The panel's title, drawn centred in the plate's title bar.</summary>
	public string Title { get; }

	/// <summary>The one button's caption.</summary>
	public string ButtonCaption { get; }

	/// <summary>
	/// The objective lines, already resolved from block 13's <c>data\mission.str</c> refs and already
	/// trimmed to what the panel can show — see <see cref="Build"/>.
	/// </summary>
	public IReadOnlyList<string> Lines { get; }

	/// <summary>Whether the panel is up. The simulation does not tick while it is.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// Whether the button shows pressed — it paints its second plate and captions itself in <c>PUSHED</c>
	/// rather than <c>ACTIVE</c> while it does (<c>PanelButton_Paint</c>, <c>00454ff8</c>): while the pointer
	/// holds it, and while a key's press flash does.
	/// </summary>
	public bool ButtonPressed => (_armed && !_pressPopped) || _presses.IsLit(0);

	// The pointer's press on the button, which a release over it completes; and whether a flash ending has
	// let the button up under it, which the press survives.
	private bool _armed;
	private bool _pressPopped;
	private readonly AlertPanelPresses _presses = new();

	// One widget, RETURN, so the walk lands back on it — and puts the pointer back on it.
	private readonly AlertPanelFocus _focus = new(_ => ObjectivesPanelLayout.Button, 1);

	/// <inheritdoc cref="AlertPanelFocus.TakePointer"/>
	public bool TakeFocusPointer(out int panelX, out int panelY) => _focus.TakePointer(out panelX, out panelY);

	/// <summary>
	/// Reads the panel's own captions out of the mounted archives and resolves a mission's block-13
	/// refs against its text. Returns null when the string table is missing, since a panel with no
	/// title and no button caption is not worth putting up.
	/// </summary>
	/// <param name="objectiveTextRefs"><see cref="World.Mission.ObjectiveTextRefs"/>.</param>
	/// <param name="textAt">Resolves one of those indices to its line.</param>
	public static ObjectivesPanel? Build(GameContent content, IReadOnlyList<int> objectiveTextRefs,
			Func<int, string> textAt) {
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(objectiveTextRefs);
		ArgumentNullException.ThrowIfNull(textAt);

		if (SimStrings.Load(content, StringsFileName) is not { } strings) {
			return null;
		}

		// The paint skips an empty line rather than leaving a blank row for it, and stops once seven
		// labels have text — the panel builds seven and no more, so an eighth entry is dropped.
		var lines = new List<string>(ObjectivesPanelLayout.LineCount);
		foreach (int reference in objectiveTextRefs) {
			string text = textAt(reference);
			if (text.Length == 0) {
				continue;
			}

			lines.Add(text);
			if (lines.Count == ObjectivesPanelLayout.LineCount) {
				break;
			}
		}

		return new ObjectivesPanel(
			strings.Text(TitleGroup, 0) ?? string.Empty,
			strings.Text(ButtonGroup, 0) ?? string.Empty,
			lines);
	}

	/// <summary>
	/// Puts the panel up. [F11] is scancode <c>0x57</c>, which
	/// <c>CockpitWidgets_HandleCommand</c> answers by constructing the panel and running its modal
	/// loop; nothing in that loop answers <c>0x57</c> again, so pressing [F11] a second time does not
	/// take the panel back down.
	/// </summary>
	public void Open() {
		IsOpen = true;
		_armed = false;
		_pressPopped = false;
		_presses.Reset();

		// ObjectivesPanel_RunModal (00457ae4) focuses RETURN before its first pass, which puts the pointer on it.
		_focus.Set(0);
	}

	/// <summary>Takes the panel down, however it was dismissed.</summary>
	public void Close() {
		IsOpen = false;
		_armed = false;
		_pressPopped = false;
		_presses.Reset();
		_focus.Clear();
	}

	/// <summary>
	/// A key the panel's own handler (<c>AlertPanel_HandleEvent</c>, <c>00454e10</c>) answers. [Return] presses the focused
	/// widget and [Esc] presses the panel's cancel widget; the panel sets both to its one button, so
	/// either closes it, and the press flashes it — so the panel stays up one more frame, see
	/// <see cref="Present"/>. The focus walk, with one widget, lands back on it.
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on a clock that runs while the panel is up.</param>
	/// <returns>True when the key was the panel's to answer.</returns>
	public bool HandleKey(AlertPanelKey key, long nowTicks) =>
		IsOpen && _focus.AnswerKey(key, 0, _ => PressButton(nowTicks));

	/// <summary>The same handler's stick half — see <see cref="AlertPanelFocus.AnswerStick"/>.</summary>
	/// <returns>True when either button was the panel's to answer.</returns>
	public bool HandleStick(bool trigger, bool button2, long nowTicks) =>
		IsOpen && _focus.AnswerStick(trigger, button2, _ => PressButton(nowTicks));

	// AlertPanel_PressWidget: the button's click, then its flash.
	private void PressButton(long nowTicks) {
		Click();
		_presses.Flash(0, nowTicks);
	}

	/// <summary>
	/// The tail of one pass of the panel's loop — <c>AlertPanel_Present</c> (<c>00454ab0</c>), as far as
	/// the button goes: services the press flash, then closes the panel if its close flag survives the hold
	/// a still-queued flash puts on it (<see cref="AlertPanelPresses.HoldClose"/>).
	/// </summary>
	public void Present(long nowTicks) {
		if (!IsOpen) {
			return;
		}

		if (_presses.Service(nowTicks).Contains(0)) {
			_pressPopped = _armed;
		}

		if (_presses.HoldClose()) {
			Close();
		}
	}

	// ObjectivesPanel_OnChildClick (00457c30): child 0, the one button, sets the close flag.
	private void Click() => _presses.RequestClose();

	/// <summary>
	/// A mouse press inside the panel, in the panel's own device pixels. Arms the button when the
	/// press lands on it, the way <c>Widget_OnMouseDown</c> does.
	/// </summary>
	public void PointerDown(float panelX, float panelY) {
		if (IsOpen) {
			_armed = ObjectivesPanelLayout.Button.Contains(panelX, panelY);
			_pressPopped = false;
		}
	}

	/// <summary>
	/// A mouse release, in the same space. Takes the panel down at the frame's <see cref="Present"/> when
	/// press and release both landed on the button — <c>Widget_OnMouseUp</c> re-hit-tests before it calls the click, so a press dragged off
	/// its widget never fires.
	/// </summary>
	public void PointerUp(float panelX, float panelY) {
		if (!IsOpen) {
			return;
		}

		bool onButton = _armed && ObjectivesPanelLayout.Button.Contains(panelX, panelY);
		_armed = false;
		_pressPopped = false;
		if (onButton) {
			Click();
		}
	}
}
