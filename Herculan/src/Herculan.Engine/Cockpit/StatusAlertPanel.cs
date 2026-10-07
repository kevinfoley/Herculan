using HercWorks.Core.Data.File;
using Herculan.Engine.Content;
﻿using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Which of the two panels built from <c>GNL_ALRT.STR</c> a status belongs to. The original has a
/// constructor for each, and their vtables carry the same six entries — so they are one behaviour in
/// two sizes, and which one a status gets is settled by the call site rather than by the status.
/// Every reachable call site agrees with the split below.
/// </summary>
public enum AlertPanelVariant {
	/// <summary>The 444x218 mission-status alert, <c>gnl_alrt</c> — statuses 2 and up.</summary>
	Status,

	/// <summary>The 178x68 pause panel, <c>PausePanel_Ctor</c> — statuses 0 and 1.</summary>
	Pause,
}

/// <summary>
/// The mission-status alert — <c>gnl_alrt</c> (<c>StatusAlertPanel_Ctor</c>, <c>00455934</c>) — and
/// its smaller sibling the pause panel (<c>PausePanel_Ctor</c>, <c>004561c0</c>), which is the same
/// behaviour at a different size and is handled here rather than in a class of its own.
///
/// <para>One panel, two ways in. <b>[Q]</b> asks the mission how it stands and shows the answer with
/// a way out of it (<c>Sim_DispatchCommand</c>'s scancode <c>0x10</c>), and <b>the poll</b> raises
/// the same panel by itself once the mission has been decided
/// (<see cref="MissionObjectives.Poll"/>). Both then compare the button the player pressed against
/// <see cref="EndsMission"/>, and that is what ends the mission.</para>
///
/// <para>Its text is a row of <c>str\GNL_ALRT.STR</c> chosen by the status — a title, up to two
/// button captions and up to four body lines. <see cref="MissionStatus.InProgress"/> is the
/// exception and the interesting one: its body is replaced with the <b>mission's own</b> failure
/// text, the three <c>data\mission.str</c> lines carried by the first mandatory objective that is
/// not satisfied. That is why [Q] reads differently from mission to mission.</para>
///
/// <para><b>The pause panel is statuses 0 and 1</b> — <c>PAUSE</c>, raised by [P], and
/// <c>EXIT EARTHSIEGE?</c>, raised by [Ctrl+Q]. Neither row carries any body text, which is the only
/// reason the shared paint is safe for it: that constructor builds no body labels, and the paint
/// stops before it would reach them. Its geometry is <see cref="PausePanelLayout"/>.</para>
///
/// <para>Modal, like <see cref="ObjectivesPanel"/>: the simulation does not tick behind it.</para>
/// </summary>
public sealed class StatusAlertPanel {
	/// <summary>The panel's string table, shared with the small pause panel of the same family.</summary>
	public const string StringsFileName = "GNL_ALRT.STR";

	/// <summary>Statuses the table has a row for. Rows 0 and 1 belong to the pause panel.</summary>
	public const int StatusCount = 20;

	/// <summary>Group 0 — one title per status.</summary>
	private const int TitleGroup = 0;

	/// <summary>Group 1 — two button captions per status.</summary>
	private const int ButtonGroup = 1;

	/// <summary>Group 2 — four body lines per status.</summary>
	private const int BodyGroup = 2;

	/// <summary>Button captions a status can carry.</summary>
	private const int ButtonsPerStatus = 2;

	/// <summary>Body lines a status can carry.</summary>
	private const int LinesPerStatus = 4;

	/// <summary>
	/// Statuses the status alert's constructor gives one button. Everything in
	/// <see cref="TwoButtonStatuses"/> gets two, and a status in neither set gets none from it.
	/// </summary>
	private static readonly int[] OneButtonStatuses = { 2, 3, 7, 8, 17, 18 };

	/// <inheritdoc cref="OneButtonStatuses"/>
	private static readonly int[] TwoButtonStatuses = { 4, 5, 6, 9, 10, 11, 12, 13, 14, 15, 16, 19 };

	/// <summary>The pause panel's own two: status 0 gets one button and status 1 gets two.</summary>
	public const int PauseStatus = 0;

	/// <inheritdoc cref="PauseStatus"/>
	public const int ExitGameStatus = 1;

	/// <summary>
	/// <c>AlertPanel_EndsMissionButton</c> (<c>0049f5d8</c>) — for each status, the index of the button whose press ends the mission.
	/// Both call sites compare the modal's answer against this entry and nothing else decides it, so
	/// a status whose only button is 0 and whose entry is 1 — status 7, the boundary warning — can
	/// only ever be acknowledged.
	/// </summary>
	private static readonly int[] EndsMissionButton = {
		1, 1, 0, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1,
	};

	private readonly StringFile _strings;
	private string[] _buttons = Array.Empty<string>();
	private string[] _body = Array.Empty<string>();

	private StatusAlertPanel(StringFile strings) {
		_strings = strings;
		_focus = new AlertPanelFocus(index => ButtonRect(Variant, index, _buttons.Length), 0);
	}

	/// <summary>Whether the panel is up. The simulation does not tick while it is.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>The status the open panel is showing.</summary>
	public int Status { get; private set; } = -1;

	/// <summary>Which of the two panels the open one is.</summary>
	public AlertPanelVariant Variant { get; private set; } = AlertPanelVariant.Status;

	/// <summary>The title row for <see cref="Status"/>.</summary>
	public string Title { get; private set; } = string.Empty;

	/// <summary>The open panel's button captions, left to right — one or two of them.</summary>
	public IReadOnlyList<string> Buttons => _buttons;

	/// <summary>The open panel's body lines, already trimmed at the first empty one.</summary>
	public IReadOnlyList<string> Body => _body;

	/// <summary>The button the pointer has pressed and a release over it would answer, or -1.</summary>
	public int PressedButton { get; private set; } = -1;

	/// <summary>
	/// Whether button <paramref name="index"/> shows pressed — its second plate, captioned in <c>PUSHED</c>:
	/// while the pointer holds it, and while a key's press flash does.
	/// </summary>
	public bool ShowsPressed(int index) =>
		(index == PressedButton && !_pressPopped) || _presses.IsLit(index);

	// Whether a flash ending has let the pointer's button up under it; the press survives it.
	private bool _pressPopped;
	private readonly AlertPanelPresses _presses = new();
	private readonly AlertPanelFocus _focus;

	/// <summary>The button [Return] and the trigger press, or -1 — see <see cref="AlertPanelFocus"/>.</summary>
	public int Focus => _focus.Index;

	/// <inheritdoc cref="AlertPanelFocus.TakePointer"/>
	public bool TakeFocusPointer(out int panelX, out int panelY) => _focus.TakePointer(out panelX, out panelY);

	/// <summary>The button that closed the panel, or -1 while it is still up.</summary>
	public int ChosenButton { get; private set; } = -1;

	/// <summary>
	/// Whether the answer the player gave ends the mission — the <see cref="EndsMissionButton"/>
	/// comparison both call sites make. False while the panel is still up.
	/// </summary>
	public bool EndsMission { get; private set; }

	/// <summary>
	/// Reads the panel's string table out of the mounted archives, or null when it is absent.
	/// </summary>
	public static StatusAlertPanel? Build(GameContent content) {
		ArgumentNullException.ThrowIfNull(content);
		return SimStrings.Load(content, StringsFileName) is { } strings
			? new StatusAlertPanel(strings)
			: null;
	}

	/// <summary>Which of the two panels a status is shown on.</summary>
	public static AlertPanelVariant VariantFor(int status) =>
		status is PauseStatus or ExitGameStatus ? AlertPanelVariant.Pause : AlertPanelVariant.Status;

	/// <summary>How many buttons a status's panel carries. Zero means neither panel shows it.</summary>
	public static int ButtonCountFor(int status) =>
		status == PauseStatus ? 1
		: status == ExitGameStatus ? 2
		: Array.IndexOf(OneButtonStatuses, status) >= 0 ? 1
		: Array.IndexOf(TwoButtonStatuses, status) >= 0 ? 2
		: 0;

	/// <summary>Whether either panel can show <paramref name="status"/> at all.</summary>
	public static bool CanShow(int status) =>
		status >= 0 && status < StatusCount && ButtonCountFor(status) > 0;

	/// <summary>The rect of one button of an open panel, in its own panel-local pixels.</summary>
	public static AlertPanelLayout.Rect ButtonRect(AlertPanelVariant variant, int index, int count) =>
		variant == AlertPanelVariant.Pause
			? PausePanelLayout.Button(index, count)
			: StatusAlertPanelLayout.Button(index, count);

	/// <summary>
	/// Where the open panel lands in a window — its <b>own</b> variant's placement. The two sizes
	/// centre to different origins, so a caller that picked the wrong one would transform a click
	/// into the wrong panel's coordinates and hit nothing; asking the panel is what stops that.
	/// Both the draw and the pointer path take it from here.
	/// </summary>
	public AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		Variant == AlertPanelVariant.Pause
			? PausePanelLayout.Place(windowWidth, windowHeight)
			: StatusAlertPanelLayout.Place(windowWidth, windowHeight);

	/// <summary>
	/// Raises the panel for a status.
	/// </summary>
	/// <param name="failureLines">
	/// The outstanding objective's own failure text, used in place of the table's body for
	/// <see cref="MissionStatus.InProgress"/> — the constructor's <c>DAT_004d1f1c</c> substitution.
	/// Ignored for every other status, and when null or empty.
	/// </param>
	/// <returns>False when the status has no panel, leaving this one closed.</returns>
	public bool Open(int status, IReadOnlyList<string>? failureLines = null) {
		int buttonCount = ButtonCountFor(status);
		if (buttonCount == 0) {
			return false;
		}

		Status = status;
		Variant = VariantFor(status);
		Title = _strings.Text(TitleGroup, status) ?? string.Empty;
		_buttons = new string[buttonCount];
		for (int i = 0; i < buttonCount; i++) {
			_buttons[i] = _strings.Text(ButtonGroup, status * ButtonsPerStatus + i) ?? string.Empty;
		}

		var lines = status == (int)MissionStatus.InProgress && failureLines is { Count: > 0 }
			? failureLines
			: Enumerable.Range(0, LinesPerStatus)
				.Select(i => _strings.Text(BodyGroup, status * LinesPerStatus + i) ?? string.Empty)
				.ToArray();

		// The paint stops at the first empty line rather than skipping it, so the body is however many
		// lines run before the first blank. A line holding a single space is not blank and does not
		// stop it, which is how the mission text's own third line reaches the count.
		var body = new List<string>(LinesPerStatus);
		foreach (string line in lines) {
			if (line.Length == 0 || body.Count == LinesPerStatus) {
				break;
			}

			body.Add(line);
		}

		_body = body.ToArray();
		IsOpen = true;
		PressedButton = -1;
		_pressPopped = false;
		_presses.Reset();
		ChosenButton = -1;
		EndsMission = false;

		// StatusAlertPanel_RunModal (00455fe4) focuses button 0 before its first pass, which puts the pointer on it.
		_focus.Count = buttonCount;
		_focus.Set(0);
		return true;
	}

	/// <summary>
	/// Consumes the answer a closed panel is holding, if it has one — the caller gets it exactly
	/// once, so acting on it cannot repeat.
	/// </summary>
	/// <returns>False when the panel is still up or its answer has already been taken.</returns>
	public bool TryTakeAnswer(out int button, out bool endsMission) {
		button = ChosenButton;
		endsMission = EndsMission;
		if (IsOpen || ChosenButton < 0) {
			return false;
		}

		ChosenButton = -1;
		return true;
	}

	/// <summary>Takes the panel down without an answer.</summary>
	public void Close() {
		IsOpen = false;
		PressedButton = -1;
		_pressPopped = false;
		_presses.Reset();
		_focus.Clear();
	}

	/// <summary>
	/// Answers as though <paramref name="button"/> had been pressed, recording whether that answer ends the
	/// mission, and sets the close flag — <c>StatusAlertPanel_OnChildClick</c> (<c>00456160</c>). The panel
	/// comes down at the frame's <see cref="Present"/>.
	/// </summary>
	public void Choose(int button) {
		if (!IsOpen || button < 0 || button >= _buttons.Length) {
			return;
		}

		ChosenButton = button;
		EndsMission = Status >= 0 && Status < EndsMissionButton.Length
			&& button == EndsMissionButton[Status];
		_presses.RequestClose();
	}

	/// <summary>
	/// A key the panel's handler (<c>AlertPanel_HandleEvent</c>, <c>00454e10</c>) answers. [Return] presses
	/// the <see cref="Focus"/> button, which is button 0 until the focus walk moves it, and [Esc] presses the
	/// cancel widget, which the constructor also sets to button 0 — for every two-button status the one that
	/// carries on. The press flashes the button, so the panel stays up one more frame; see <see cref="Present"/>.
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on a clock that runs while the panel is up.</param>
	/// <returns>True when the key was the panel's to answer.</returns>
	public bool HandleKey(AlertPanelKey key, long nowTicks) =>
		IsOpen && _focus.AnswerKey(key, 0, button => PressWidget(button, nowTicks));

	/// <summary>The same handler's stick half — see <see cref="AlertPanelFocus.AnswerStick"/>.</summary>
	/// <returns>True when either button was the panel's to answer.</returns>
	public bool HandleStick(bool trigger, bool button2, long nowTicks) =>
		IsOpen && _focus.AnswerStick(trigger, button2, button => PressWidget(button, nowTicks));

	// AlertPanel_PressWidget: the button's click, then its flash.
	private void PressWidget(int button, long nowTicks) {
		Choose(button);
		_presses.Flash(button, nowTicks);
	}

	/// <summary>
	/// The tail of one pass of the panel's loop — <c>AlertPanel_Present</c> (<c>00454ab0</c>), as far as the
	/// buttons go: services the press flashes, then closes the panel if its close flag survives the hold a
	/// still-queued flash puts on it (<see cref="AlertPanelPresses.HoldClose"/>).
	/// </summary>
	public void Present(long nowTicks) {
		if (!IsOpen) {
			return;
		}

		var ended = _presses.Service(nowTicks);
		if (PressedButton >= 0 && ended.Contains(PressedButton)) {
			_pressPopped = true;
		}

		if (_presses.HoldClose()) {
			Close();
		}
	}

	/// <summary>A mouse press, in panel-local device pixels. Arms whichever button it lands on.</summary>
	public void PointerDown(float panelX, float panelY) {
		PressedButton = IsOpen ? ButtonAt(panelX, panelY) : -1;
		_pressPopped = false;
	}

	/// <summary>
	/// A mouse release, in the same space. Answers when press and release both landed on the same
	/// button — <c>Widget_OnMouseUp</c> re-hit-tests before it calls the click.
	/// </summary>
	public void PointerUp(float panelX, float panelY) {
		if (!IsOpen) {
			return;
		}

		int released = ButtonAt(panelX, panelY);
		int pressed = PressedButton;
		PressedButton = -1;
		_pressPopped = false;
		if (pressed >= 0 && pressed == released) {
			Choose(pressed);
		}
	}

	private int ButtonAt(float panelX, float panelY) {
		for (int i = 0; i < _buttons.Length; i++) {
			if (ButtonRect(Variant, i, _buttons.Length).Contains(panelX, panelY)) {
				return i;
			}
		}

		return -1;
	}
}
