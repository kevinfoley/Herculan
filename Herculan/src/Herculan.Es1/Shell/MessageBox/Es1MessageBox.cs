using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Ttm;

namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// The message box's file (docs/es1/shell/message-box.md#the-message-box): on the record <see cref="Es1MessageBoxRecord"/>,
/// <c>DigitalDialog_ShowMessage</c>, <c>MessageBox_Run</c>, <c>MessageBox_Answer</c> and <c>MessageBox_Close</c>, over the
/// dialog record <see cref="Es1ShellDialog"/> and the talking head <see cref="Es1LipSyncPlayer"/>. Kinds 0 and 3, a single
/// mission's opening line and its debriefing line, are ported; the career's kinds are S3's.
/// </summary>
public sealed class Es1MessageBox {
	/// <summary>
	/// <c>MessageBox_NextScreen</c> (<c>00081690</c>), the screen each kind posts when it is answered
	/// (docs/es1/shell/message-box.md#the-kinds).
	/// </summary>
	private readonly short[] _nextScreen = [1501, 1500, 1700, 1510, 1520, 1490, 9999, 1520, 1520, 1520, 1520, 1520];

	/// <summary><c>Mission_OutcomeGrade</c>'s tables: <c>000810f4</c> for the mission kinds 0, 1, 2 and 6, <c>00081108</c> for the rest.</summary>
	private static readonly short[] GradesA = [0, 0, 1, 1, 1, 2, 2, 3, 3, 0], GradesB = [0, 0, 1, 1, 2, 2, 3, 3];

	private readonly Es1MenuGlobals _globals;
	private readonly Es1ShellDialog _dialog;
	private readonly Es1ShellMission _mission;
	private readonly Es1ShellData _data;
	private readonly Func<Es1TtmManager?> _scripts;
	private readonly Func<Func<Es1PanelCanvas>> _getFullScreen;

	/// <param name="globals">The window globals, which hold the record and the pending message.</param>
	/// <param name="mission">The mission's globals: <c>MessageBox_Line</c>, the header word kind 0 reads and the words kind 3 grades.</param>
	/// <param name="data">The shared data: <c>Shell_RandomState</c>.</param>
	/// <param name="getFullScreen"><c>RenderContext_GetFullScreen</c> (<see cref="Es1ShellScreens.GetFullScreen"/>).</param>
	public Es1MessageBox(Es1MenuGlobals globals, Es1ShellDialog dialog, Es1ShellMission mission, Es1ShellData data,
			Func<Es1TtmManager?> scripts, Func<Func<Es1PanelCanvas>> getFullScreen) {
		_globals = globals;
		_dialog = dialog;
		_mission = mission;
		_data = data;
		_scripts = scripts;
		_getFullScreen = getFullScreen;
	}

	/// <summary><c>Shell_MessageBox</c> (<c>0009b010</c>).</summary>
	public Es1MessageBoxRecord Record => _globals.MessageBox;

	/// <summary>
	/// <c>DigitalDialog_ShowMessage</c> (<c>00037edd</c>)(box) (docs/es1/shell/message-box.md#showing-a-message): nothing while
	/// <i>to show</i> is clear; otherwise the full-screen render context <c>0007fcd8</c> made the scripts' default, and by
	/// the kind the text file, <c>MessageBox_Line</c> and the lip-sync list's words <i>a</i> and <i>b</i>, then
	/// <see cref="Es1ShellDialog.LoadFromFile"/>. A kind outside 0 to 11 returns before the load. It never clears <i>to
	/// show</i>.
	/// </summary>
	public IEnumerable<Es1ShellWait> ShowMessage() {
		var record = Record;
		if (!record.ToShow) {
			yield break;
		}

		var fullScreen = _getFullScreen();
		_scripts()?.SetDefaultContext(fullScreen);
		short a;
		byte b;
		string path;
		switch (record.Kind) {
			case 0:
				b = 1;
				a = 0;
				_mission.MessageBoxLine = _mission.HeaderWord4;
				path = "eng\\C0_text.bin";
				break;
			case 3: {
				b = 1;
				a = 1;
				short grade = OutcomeGrade(_mission.Outcome, _mission.RestoredMissionKind);
				// Math_RandomBelow(Shell_RandomState, 2), drawn for every grade. The generator exists from the start-up's step 11
				// on; a draw of 0 without it is an engine guard.
				short draw = (short)(_data.Random?.Below(2) ?? 0);
				_mission.MessageBoxLine = (short)(grade * 2 + (grade == 1 ? 0 : draw));
				path = "eng\\D0_text.bin";
				break;
			}
			case 1:
			case 2:
			case 4:
			case >= 5 and <= 11:
				// PLACEHOLDER: the career's kinds, S3's (message-box.md's kinds 1, 2 and 4 to 11): eng\C<k>_text.bin,
				// eng\G0_text.bin with Shell_MusicTrack, eng\D<k>_text.bin, and eng\T1_text.bin with MessageBox_PickT1Line
				// (00037bf0) and the two Ttm_SetMode calls on the handles at 0007e718 + 0x12 and + 9. No ported screen puts
				// them up; the box loads nothing.
				yield break;
			default:
				yield break;
		}

		foreach (var wait in _dialog.LoadFromFile(a, b, path)) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Mission_OutcomeGrade</c> (<c>00036837</c>)(<paramref name="ordinal"/>, <paramref name="kind"/>): the ordinal's
	/// entry of the table at <c>000810f4</c> for the kinds 0, 1, 2 and 6 and of <c>00081108</c> for any other. Retail reads
	/// with no bounds test; an ordinal past the table is grade 0 here, an engine guard.
	/// </summary>
	private static short OutcomeGrade(short ordinal, short kind) {
		short[] grades = kind is 0 or 1 or 2 or 6 ? GradesA : GradesB;
		return ordinal >= 0 && ordinal < grades.Length ? grades[ordinal] : (short)0;
	}

	/// <summary>
	/// <c>MessageBox_Run</c> (<c>000382f6</c>)(box, <paramref name="input"/>), the frame loop's step 5
	/// (docs/es1/shell/message-box.md#the-loop): nothing while <i>to show</i> is clear; otherwise the cursor taken off, and
	/// while the input's key word and first button are 0 and <i>up</i> is set: a frame shown, the talking head ticked, the
	/// scripts run (<c>Ttm_RunAll</c>), the box drawn and the input read again; then the answer. A pass with no input block
	/// yet reads as no key and no button here, an engine guard.
	/// </summary>
	/// <param name="input"><c>Shell_InputBlock</c>, which the frame loop's step 2 read.</param>
	/// <param name="presentFrame"><c>Shell_PresentFrame</c>.</param>
	/// <param name="pollDeviceBlock"><c>Input_PollDeviceBlock</c>.</param>
	public IEnumerable<Es1ShellWait> Run(Es1LiveBlock? input, Func<IEnumerable<Es1ShellWait>> presentFrame,
			Func<Es1LiveBlock> pollDeviceBlock) {
		var record = Record;
		if (!record.ToShow) {
			yield break;
		}

		// The raster slot 0008662c, VGA_8's slot 31, takes the cursor off; then NoOp_Void.
		_dialog.Display.Cursor.Hide();
		while ((input == null || (input.CommandWord == 0 && input.MouseButtons[0] == 0)) && record.Up) {
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			foreach (var wait in _dialog.Player.Tick()) {
				yield return wait;
			}

			if (_scripts() is { } scripts) {
				foreach (var wait in scripts.RunAllAfterTick()) {
					yield return wait;
				}
			}

			_dialog.Draw();
			input = pollDeviceBlock();
		}

		// NoOp_Void.
		foreach (var wait in Answer(input)) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>MessageBox_Answer</c> (<c>0003837f</c>)(box, <paramref name="input"/>) (docs/es1/shell/message-box.md#the-answer),
	/// for the kinds 0, 1, 3, 4 and any kind not named below: the pending message's code set to 0 and its target to the
	/// kind's next screen, the index and value left as <see cref="Es1ShellScreens.HandleMessage"/> left them; the box closed
	/// (<see cref="Close"/>); and the cursor drawn.
	/// </summary>
	private IEnumerable<Es1ShellWait> Answer(Es1LiveBlock? input) {
		short kind = Record.Kind;
		switch (kind) {
			case 2:
			case 7:
				// PLACEHOLDER: kind 2's next G0 line or the camni script and screen 1700, and kind 7's next T1 line, S3's
				// (message-box.md's answer), which read the input; no ported screen puts them up, so they post their next screen as
				// kind 0 does.
				break;
			case 5:
			case 6:
			case >= 8 and <= 11:
				// PLACEHOLDER: Ttm_SetMode(0, handle) for the handle bytes at the dword 0007e718 + 0x12 and + 9, S3's.
				break;
		}

		_globals.PendingMessage = _globals.PendingMessage with { Code = 0, Target = NextScreen(kind) };
		foreach (var wait in Close()) {
			yield return wait;
		}

		// The raster slot 00086628, VGA_8's slot 30, draws the cursor.
		_dialog.Display.Cursor.Show();
	}

	/// <summary>The kind's entry of <see cref="_nextScreen"/>; retail reads with no bounds test, and a kind past it posts 0 here, an engine guard.</summary>
	private short NextScreen(short kind) => kind >= 0 && kind < _nextScreen.Length ? _nextScreen[kind] : (short)0;

	/// <summary>
	/// <c>MessageBox_Close</c> (<c>000382d5</c>)(box): the kind set to −1 (<see cref="Es1MessageBoxRecord.SetKind"/>) and the
	/// text box deleted (<see cref="Es1ShellDialog.DeleteBox"/>), whose destructor stops the face.
	/// </summary>
	public IEnumerable<Es1ShellWait> Close() {
		Record.SetKind(Es1MessageBoxRecord.NoKind);
		foreach (var wait in _dialog.DeleteBox()) {
			yield return wait;
		}
	}
}
