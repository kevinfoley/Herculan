using System.Globalization;
using HercWorks.Core.Data.File.Dat.Shell;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Menus;
using Herculan.Es1.Ttm;

namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// The message box's file (docs/es1/shell/message-box.md#the-message-box): on the record <see cref="Es1MessageBoxRecord"/>,
/// <c>DigitalDialog_ShowMessage</c>, <c>MessageBox_Run</c>, <c>MessageBox_Answer</c> and <c>MessageBox_Close</c>, over the
/// dialog record <see cref="Es1ShellDialog"/> and the talking head <see cref="Es1LipSyncPlayer"/>: kinds 0 and 3, a single
/// mission's opening line and its debriefing line, kinds 1 and 4, a career mission's opening line and its debriefing line,
/// kind 2, a new career's campaign opening lines, and the lines of <c>eng\T1_text.bin</c> kinds 5 to 11 show, kind 7's the
/// lines after a career's mission.
/// </summary>
public sealed class Es1MessageBox {
	/// <summary>
	/// <c>MessageBox_NextScreen</c> (<c>00081690</c>), the screen each kind posts when it is answered
	/// (docs/es1/shell/message-box.md#the-kinds).
	/// </summary>
	private readonly short[] _nextScreen = [1501, 1500, 1700, 1510, 1520, 1490, 9999, 1520, 1520, 1520, 1520, 1520];

	/// <summary><c>Mission_OutcomeGrade</c>'s tables: <c>000810f4</c> for the mission kinds 0, 1, 2 and 6, <c>00081108</c> for the rest.</summary>
	private static readonly short[] GradesA = [0, 0, 1, 1, 1, 2, 2, 3, 3, 0], GradesB = [0, 0, 1, 1, 2, 2, 3, 3];

	/// <summary>The kind of the campaign's opening lines, kind 2.</summary>
	private const short OpeningKind = 2;

	/// <summary>The words each campaign's row of <see cref="OpeningLines"/> holds, <c>0x16</c> bytes.</summary>
	private const int OpeningRow = 11;

	/// <summary>
	/// The table at <c>000814ec</c>: by campaign, a row of 11 words, the lines of <c>eng\G0_text.bin</c> kind 2 shows in
	/// turn, ended by −1.
	/// </summary>
	private static readonly short[] OpeningLines = [
		0, 1, 2, 3, 5, 54, -1, 0, 0, 0, 0,
		6, 8, 9, 54, -1, 0, 0, 0, 0, 0, 0,
		10, 11, 12, 13, 14, 15, 16, 17, 18, 54, -1,
		19, 20, 21, 22, 23, 24, 25, 26, 54, -1, 0,
		27, 28, 29, 30, 31, 32, 54, -1, 0, 0, 0,
		33, 34, 35, 36, 37, 38, 39, 40, 41, 54, -1,
		42, 43, 44, 45, 46, 47, 54, -1, 0, 0, 0,
		48, 49, 50, 51, 52, 53, 54, -1, 0, 0, 0,
	];

	/// <summary>The table at <c>000814d8</c>: by campaign, the music track kind 2 starts, less <c>0xf</c>.</summary>
	private static readonly short[] OpeningTracks = [0, 1, 1, 2, 2, 3, 3, 3];

	/// <summary>The first music track of <see cref="OpeningTracks"/>.</summary>
	private const short OpeningTrackBase = 0xf;

	/// <summary>
	/// The script <see cref="Answer"/> plays after a campaign's last opening line, by the campaign: <c>a_camni.ttm</c> for 0,
	/// <c>d_camni.ttm</c> for 1 and 2, <c>b_camni.ttm</c> for 3 and 4 and <c>g_camni.ttm</c> for 5 to 7.
	/// </summary>
	private static readonly string[] OpeningScripts = ["a_camni.ttm", "d_camni.ttm", "d_camni.ttm", "b_camni.ttm", "b_camni.ttm",
		"g_camni.ttm", "g_camni.ttm", "g_camni.ttm"];

	/// <summary>
	/// The words <c>0008159c</c>, <c>0008159e</c> and <c>000815a0</c>, −1 in the image: the lines of <c>eng\T1_text.bin</c>
	/// kinds 5 to 11 show at each <see cref="LineStep"/>, the first <see cref="PickT1Line"/>'s.
	/// </summary>
	private readonly short[] _t1Lines = [-1, -1, -1];

	/// <summary>
	/// The 41 words at <c>000815a8</c>, by line of <c>eng\T1_text.bin</c>: the lip-sync list's <i>a</i>, 0 for
	/// <c>T0_FILES.DAT</c> and 1 for <c>T1_FILES.DAT</c> (docs/es1/shell/message-box.md#loading-a-line).
	/// </summary>
	private static readonly short[] T1Lists = [
		1, 1, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 1, 1, 0, 0, 0, 0, 1,
	];

	/// <summary><see cref="PickT1Line"/>'s line for kind 5, a new campaign's, and the first of kind 6's, before a mission.</summary>
	private const short CampaignLine = 2, FirstLaunchLine = 3;

	/// <summary>The kind of the lines after a career's mission, which <see cref="Answer"/> steps through.</summary>
	public const short AfterMissionKind = 7;

	/// <summary>
	/// Kind 7's first lines by the band of the squad's HERC's condition (<see cref="PickT1Line"/>): 96 and above, 71 to 95,
	/// 51 to 70 and 1 to 50, each the first of a run of 4, 4, 3 and 4 lines.
	/// </summary>
	private const short UnscratchedLine = 10, ScratchedLine = 15, DamagedLine = 20, WreckedLine = 24;

	/// <summary>
	/// Kind 7's second lines: new technology brought back (<c>0x20</c>, the first of a run of 1), much salvage (<c>0x22</c>)
	/// and HERCs lost (<c>0x1e</c>).
	/// </summary>
	private const short NewTechnologyLine = 0x20, SalvageLine = 0x22, LossesLine = 0x1e;

	/// <summary>The text <c>NONE</c> at <c>000815a2</c>, which kind 7 compares the new technology's text with.</summary>
	private const string NoTechnology = "NONE";

	/// <summary>The salvage above which kind 7's best band takes <see cref="SalvageLine"/>, compared unsigned.</summary>
	private const uint SalvageThreshold = 20000;

	private readonly Es1MenuGlobals _globals;
	private readonly Es1ShellDialog _dialog;
	private readonly Es1ShellMission _mission;
	private readonly Es1ShellData _data;
	private readonly Es1ShellBackdrop _backdrop;
	private readonly Func<Es1TtmManager?> _scripts;
	private readonly Func<Func<Es1PanelCanvas>> _getFullScreen;

	/// <param name="globals">The window globals, which hold the record and the pending message.</param>
	/// <param name="mission">The mission's globals: <c>MessageBox_Line</c>, the header word kind 0 reads, the words kind 3 grades and the roster kind 2 reads.</param>
	/// <param name="data">The shared data: <c>Shell_RandomState</c>.</param>
	/// <param name="backdrop">The backdrop, whose music track kind 2 sets and whose palette <c>0007fcd0</c> its script clears.</param>
	/// <param name="getFullScreen"><c>RenderContext_GetFullScreen</c> (<see cref="Es1ShellScreens.GetFullScreen"/>).</param>
	public Es1MessageBox(Es1MenuGlobals globals, Es1ShellDialog dialog, Es1ShellMission mission, Es1ShellData data,
			Es1ShellBackdrop backdrop, Func<Es1TtmManager?> scripts, Func<Func<Es1PanelCanvas>> getFullScreen) {
		_globals = globals;
		_dialog = dialog;
		_mission = mission;
		_data = data;
		_backdrop = backdrop;
		_scripts = scripts;
		_getFullScreen = getFullScreen;
	}

	/// <summary>
	/// The word <c>000814e8</c>, 0 in the image: the step along a campaign's row of <see cref="OpeningLines"/> that kind 2
	/// shows, which its answer moves on.
	/// </summary>
	public short LineStep { get; private set; }

	/// <summary>The career record's campaign, the word <c>+6</c> of the roster's record its word <c>+7</c> names.</summary>
	private short Campaign => _mission.Roster.CurrentRecord.Campaign;

	/// <summary>
	/// The word of <see cref="OpeningLines"/> at <see cref="Campaign"/> × 11 + <see cref="LineStep"/>. Retail reads past the
	/// table for a campaign outside 0 to 7; here that reads −1, an engine guard.
	/// </summary>
	private short OpeningLine() {
		int index = Campaign * OpeningRow + LineStep;
		return Campaign is >= 0 and < 8 && index >= 0 && index < OpeningLines.Length ? OpeningLines[index] : (short)-1;
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
			case 3:
				b = 1;
				a = 1;
				_mission.MessageBoxLine = DebriefingLine();
				path = "eng\\D0_text.bin";
				break;
			case 4: {
				// A career's debriefing line: the line as kind 3's, of eng\D<k>_text.bin, k as kind 1's, formatted signed, and a
				// 2k + 1.
				b = 1;
				short k = CampaignFileWord();
				a = (short)(k * 2 + 1);
				_mission.MessageBoxLine = DebriefingLine();
				path = string.Create(CultureInfo.InvariantCulture, $"eng\\D{k}_text.bin");
				break;
			}
			case OpeningKind: {
				// The campaign's opening line; and when the campaign's track, 000814d8's word + 0xf, is not Shell_MusicTrack,
				// that track made it and 0007fcd5 set, so the next frame starts it. Retail reads past 000814d8 for a campaign
				// outside 0 to 7; here the track is then left, an engine guard.
				b = 3;
				a = 0;
				_mission.MessageBoxLine = OpeningLine();
				path = "eng\\G0_text.bin";
				short campaign = Campaign;
				if (campaign >= 0 && campaign < OpeningTracks.Length && OpeningTracks[campaign] + OpeningTrackBase != _backdrop.MusicTrack) {
					_backdrop.MusicDue = true;
					_backdrop.MusicTrack = (short)(OpeningTracks[campaign] + OpeningTrackBase);
				}

				break;
			}
			case 1: {
				// A career's mission line: the line as kind 0's, of eng\C<k>_text.bin, k the +0 word of the career's campaign
				// in the battle set, formatted signed.
				b = 1;
				short k = CampaignFileWord();
				a = (short)(k * 2);
				_mission.MessageBoxLine = _mission.HeaderWord4;
				path = string.Create(CultureInfo.InvariantCulture, $"eng\\C{k}_text.bin");
				break;
			}
			case >= 5 and <= 11:
				// A line of eng\T1_text.bin: kind 7 skips the pick and, at step 0, keeps the line set before it.
				if (record.Kind != 7 && LineStep == 0) {
					_mission.MessageBoxLine = PickT1Line(record.Kind);
				}

				if (LineStep != 0) {
					_mission.MessageBoxLine = T1Line(LineStep);
				}

				b = 2;
				a = T1List(_mission.MessageBoxLine);
				path = "eng\\T1_text.bin";
				SetBackdropScriptsMode(Es1TtmPlayer.Hidden);
				break;
			default:
				yield break;
		}

		foreach (var wait in _dialog.LoadFromFile(a, b, path)) {
			yield return wait;
		}
	}

	/// <summary>
	/// Kinds 3 and 4's line, 2<i>g</i> + <i>r</i> (docs/es1/shell/message-box.md#the-kinds): <i>g</i>
	/// <see cref="OutcomeGrade"/> of <see cref="Es1ShellMission.Outcome"/> and <see cref="Es1ShellMission.RestoredMissionKind"/>,
	/// <i>r</i> <c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, 2), drawn for every grade and taken as 0 for grade 1. The
	/// generator exists from the start-up's step 11 on; a draw of 0 without it is an engine guard.
	/// </summary>
	private short DebriefingLine() {
		short grade = OutcomeGrade(_mission.Outcome, _mission.RestoredMissionKind);
		short draw = (short)(_data.Random?.Below(2) ?? 0);
		return (short)(grade * 2 + (grade == 1 ? 0 : draw));
	}

	/// <summary>
	/// <c>Mission_OutcomeGrade</c> (<c>00036837</c>)(<paramref name="ordinal"/>, <paramref name="kind"/>): the ordinal's
	/// entry of the table at <c>000810f4</c> for the kinds 0, 1, 2 and 6 and of <c>00081108</c> for any other. Retail reads
	/// with no bounds test; an ordinal past the table is grade 0 here, an engine guard.
	/// </summary>
	internal static short OutcomeGrade(short ordinal, short kind) {
		short[] grades = kind is 0 or 1 or 2 or 6 ? GradesA : GradesB;
		return ordinal >= 0 && ordinal < grades.Length ? grades[ordinal] : (short)0;
	}

	/// <summary>
	/// Kinds 1 and 4's <i>k</i> (docs/es1/shell/message-box.md#the-kinds): the <c>+0</c> word of the career record's campaign in the
	/// loaded battle set, <c>g_BattleSet</c> (<c>000a02cc</c>), which <c>BattleSet_ReadFile</c> sets to the campaign's word of
	/// <c>0007ea94</c> (<see cref="Es1ShellBackdrop.CampaignWords"/>); −1 with no campaigns or a campaign outside them.
	/// Retail reads past the table for a battle set of more than its ten campaigns; such a campaign is −1 here, an engine
	/// guard.
	/// </summary>
	private short CampaignFileWord() {
		short campaign = Campaign;
		return _data.BattleSet is { } set && campaign >= 0 && campaign < set.Campaigns.Length && campaign < Es1ShellBackdrop.CampaignWords.Length
			? Es1ShellBackdrop.CampaignWords[campaign]
			: (short)-1;
	}

	/// <summary>
	/// <c>MessageBox_PickT1Line</c> (<c>00037bf0</c>)(<paramref name="kind"/>) (docs/es1/shell/message-box.md#the-kinds): sets
	/// the line word <c>0008159c</c>, <see cref="_t1Lines"/>' first, and returns it. Kind 5: line 2, the new campaign's.
	/// Kind 6: line 3 plus <c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, 4), or below 6 when the current career
	/// record's missions played <c>+0xa</c> is not 0. Kind 7, after a career's mission: the second line <c>0008159e</c>
	/// <see cref="NewTechnologyLine"/> + a draw below 1 when the new technology's text <c>0007d808</c>
	/// (<see cref="Es1DebriefingMenu.NewTechnology"/>) differs from <see cref="NoTechnology"/>; then by the condition of the
	/// squad's HERC (<see cref="SquadHercCondition"/>), the line the band's first plus a draw: 96 and above
	/// <see cref="UnscratchedLine"/> + a draw below 4, and the second line <see cref="SalvageLine"/> while it is −1 and the
	/// salvage <c>0007fcf4</c> is above 20000; 71 to 95 <see cref="ScratchedLine"/> + below 4; 51 to 70
	/// <see cref="DamagedLine"/> + below 3; 1 to 50 <see cref="WreckedLine"/> + below 4, and the second line
	/// <see cref="LossesLine"/> while it is −1 and the squad's losses <c>0007ebca</c> are above 1; 0 or less leaves the line
	/// as it was. Any other kind returns the line as it was. A draw without the generator is 0, and no record reads as none
	/// played, engine guards.
	/// </summary>
	public short PickT1Line(short kind) {
		switch (kind) {
			case 6: {
				short played = _mission.CurrentRecord?.MissionsPlayed ?? 0;
				_t1Lines[0] = (short)(FirstLaunchLine + (_data.Random?.Below(played == 0 ? (short)4 : (short)6) ?? 0));
				break;
			}
			case 5:
				_t1Lines[0] = CampaignLine;
				break;
			case AfterMissionKind:
				PickAfterMissionLines();
				break;
		}

		return _t1Lines[0];
	}

	/// <summary><see cref="PickT1Line"/>'s kind 7: the second line, then the first by the band (<c>00037c64</c>–<c>00037dc1</c>).</summary>
	private void PickAfterMissionLines() {
		if (!string.Equals(Es1DebriefingMenu.NewTechnology, NoTechnology, StringComparison.Ordinal)) {
			_t1Lines[1] = (short)(NewTechnologyLine + Draw(1));
		}

		short condition = SquadHercCondition();
		var results = _mission.Results;
		if (condition >= 96) {
			_t1Lines[0] = (short)(UnscratchedLine + Draw(4));
			if (_t1Lines[1] == -1 && (uint)results.Salvage > SalvageThreshold) {
				_t1Lines[1] = SalvageLine;
			}
		} else if (condition >= 71) {
			_t1Lines[0] = (short)(ScratchedLine + Draw(4));
		} else if (condition >= 51) {
			_t1Lines[0] = (short)(DamagedLine + Draw(3));
		} else if (condition > 0) {
			_t1Lines[0] = (short)(WreckedLine + Draw(4));
			if (_t1Lines[1] == -1 && results.LostPilots > 1) {
				_t1Lines[1] = LossesLine;
			}
		}
	}

	/// <summary><c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, <paramref name="bound"/>); 0 without the generator, an engine guard.</summary>
	private short Draw(short bound) => (short)(_data.Random?.Below(bound) ?? 0);

	/// <summary>
	/// The condition kind 7 picks its band by: <c>Herc_OverallCondition</c> (<c>00024b6a</c>,
	/// <see cref="Es1HercRecord.OverallCondition"/>) of the hangar slot the word <c>+0x1c</c> of
	/// <c>CareerRecord_Current</c>'s record names, pilot entry 0's slot <c>+8</c> (its index a local held at 0), not the
	/// player's entry <c>+0xe</c>; 0 for slot −1 or an empty slot of the array <c>000808a0</c>. Retail reads the array with
	/// no bounds test; a slot past it, no record or no hangar reads 0 here, engine guards.
	/// </summary>
	private short SquadHercCondition() {
		if (_mission.CurrentRecord is not { } record || _mission.Hangar is not { } hangar) {
			return 0;
		}

		return hangar.HercIn(record.Pilots[0].Slot) is { } herc ? herc.OverallCondition() : (short)0;
	}

	/// <summary>
	/// The word of <see cref="_t1Lines"/> at <paramref name="step"/>, the line kind 7 shows at that step. Retail reads past
	/// the three words; a step past them is −1 here, an engine guard.
	/// </summary>
	private short T1Line(short step) => step >= 0 && step < _t1Lines.Length ? _t1Lines[step] : (short)-1;

	/// <summary>
	/// The word of <see cref="T1Lists"/> at <paramref name="line"/>, the lip-sync list's <i>a</i>: 0 <c>T0_FILES.DAT</c>, 1
	/// <c>T1_FILES.DAT</c>. Retail reads past the 41 words; a line past them is 0 here, an engine guard.
	/// </summary>
	private static short T1List(short line) => line >= 0 && line < T1Lists.Length ? T1Lists[line] : (short)0;

	/// <summary>
	/// <c>Ttm_SetMode</c> (<c>00046948</c>)(<paramref name="mode"/>, handle) for the backdrop's scripts 6 and 3, the handle bytes
	/// at the dword <c>0007e718</c> + <c>0x12</c> and + 9 (<see cref="Es1ShellBackdrop.GroupTtms"/>), in that order:
	/// mode 2 as kinds 5 to 11 load their line, 0 as their answer closes it. Retail reads the bytes through a null dword
	/// when the backdrop opened none, and past the entries; here no call is made then, an engine guard.
	/// </summary>
	private void SetBackdropScriptsMode(int mode) {
		if (_backdrop.GroupTtms is not { } entries || _scripts() is not { } scripts) {
			return;
		}

		foreach (int entry in new[] { 6, 3 }) {
			if (entry < entries.Length) {
				scripts.SetMode(mode, entries[entry].Handle);
			}
		}
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
		foreach (var wait in Answer(input, presentFrame, pollDeviceBlock)) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>MessageBox_Answer</c> (<c>0003837f</c>)(box, <paramref name="input"/>) (docs/es1/shell/message-box.md#the-answer):
	/// kind 2 by <see cref="AnswerOpening"/>, kind 7 by <see cref="AnswerAfterMission"/>; for 5, 6 and 8 to 11 the backdrop's
	/// two scripts first set back to mode 0
	/// (<see cref="SetBackdropScriptsMode"/>), then as for the kinds 0, 1, 3, 4 and any kind not named below: the pending message's
	/// code set to 0 and its target to the kind's next screen, the index and value left as
	/// <see cref="Es1ShellScreens.HandleMessage"/> left them; the box closed (<see cref="Close"/>); and the cursor drawn.
	/// </summary>
	private IEnumerable<Es1ShellWait> Answer(Es1LiveBlock? input, Func<IEnumerable<Es1ShellWait>> presentFrame,
			Func<Es1LiveBlock> pollDeviceBlock) {
		short kind = Record.Kind;
		switch (kind) {
			case OpeningKind:
				foreach (var wait in AnswerOpening(input, presentFrame, pollDeviceBlock)) {
					yield return wait;
				}

				yield break;
			case AfterMissionKind:
				foreach (var wait in AnswerAfterMission()) {
					yield return wait;
				}

				yield break;
			case 5:
			case 6:
			case >= 8 and <= 11:
				SetBackdropScriptsMode(Es1TtmPlayer.Playing);
				break;
		}

		_globals.PendingMessage = _globals.PendingMessage with { Code = 0, Target = NextScreen(kind) };
		foreach (var wait in Close()) {
			yield return wait;
		}

		// The raster slot 00086628, VGA_8's slot 30, draws the cursor.
		_dialog.Display.Cursor.Show();
	}

	/// <summary>
	/// <c>MessageBox_Answer</c>'s kind 2 (docs/es1/shell/message-box.md#the-answer): <see cref="LineStep"/> up 1, the text
	/// box marked dirty and the box closed. With no key or first button in <paramref name="input"/> and the campaign's next
	/// line not −1, a frame shown and that line put up as kind 2 again (<see cref="ShowMessage"/>). Otherwise
	/// <see cref="LineStep"/> 0, a frame shown, the draw page, the visible page and page 2 cleared to 0 through the raster's
	/// slot <c>0008663c</c>, the campaign's script played (<see cref="PlayTtmInPlace"/>), and the pending message's code
	/// set to 0 and its target to entry 2 of <c>MessageBox_NextScreen</c>, 1700, read as a constant. The cursor is not
	/// drawn. Retail plays a name it never wrote for a campaign outside 0 to 7; here no script plays, an engine guard.
	/// </summary>
	private IEnumerable<Es1ShellWait> AnswerOpening(Es1LiveBlock? input, Func<IEnumerable<Es1ShellWait>> presentFrame,
			Func<Es1LiveBlock> pollDeviceBlock) {
		LineStep++;
		_dialog.MarkDirty();
		foreach (var wait in Close()) {
			yield return wait;
		}

		if ((input == null || (input.CommandWord == 0 && input.MouseButtons[0] == 0)) && OpeningLine() != -1) {
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			Record.SetKind(OpeningKind);
			foreach (var wait in ShowMessage()) {
				yield return wait;
			}

			yield break;
		}

		LineStep = 0;
		foreach (var wait in presentFrame()) {
			yield return wait;
		}

		var display = _dialog.Display;
		display.ClearPage(display.DrawPage, 0);
		display.ClearPage(display.VisiblePage, 0);
		display.ClearPage(Es1ShellBackdrop.BackgroundPage, 0);
		short campaign = Campaign;
		if (campaign >= 0 && campaign < OpeningScripts.Length) {
			foreach (var wait in PlayTtmInPlace(OpeningScripts[campaign], presentFrame, pollDeviceBlock)) {
				yield return wait;
			}
		}

		_globals.PendingMessage = _globals.PendingMessage with { Code = 0, Target = _nextScreen[OpeningKind] };
	}

	/// <summary>
	/// <c>MessageBox_Answer</c>'s kind 7 (docs/es1/shell/message-box.md#the-answer): <see cref="LineStep"/> up 1, the text
	/// box marked dirty and the box closed. While the line at the new step (<see cref="T1Line"/>) is not −1, that line put
	/// up as kind 7 again (<see cref="ShowMessage"/>), whatever the input, with no frame between. At −1 the lines before
	/// the step set to −1, <see cref="LineStep"/> 0, the pending message's code set to 0 and its target to entry 7 of
	/// <c>MessageBox_NextScreen</c> (<c>0008169e</c>, read as a constant), 1520, and the backdrop's two scripts set back
	/// to mode 0. The cursor is not drawn. Retail's store of 1510 in <c>Shell_Mode</c> 0, through the kind word
	/// <see cref="Close"/> has just set to −1, lands on <c>0008168e</c>, the high word of the last entry of
	/// <c>DigitalDialog_ShowMessage</c>'s jump table at <c>00081660</c>, kind 11's; no ported state holds that table, and
	/// no ported path puts kind 7 up in mode 0, so the store is not made here.
	/// </summary>
	private IEnumerable<Es1ShellWait> AnswerAfterMission() {
		LineStep++;
		_dialog.MarkDirty();
		foreach (var wait in Close()) {
			yield return wait;
		}

		if (T1Line(LineStep) != -1) {
			Record.SetKind(AfterMissionKind);
			foreach (var wait in ShowMessage()) {
				yield return wait;
			}

			yield break;
		}

		for (int step = 0; step < LineStep && step < _t1Lines.Length; step++) {
			_t1Lines[step] = -1;
		}

		LineStep = 0;
		_globals.PendingMessage = _globals.PendingMessage with { Code = 0, Target = _nextScreen[AfterMissionKind] };
		SetBackdropScriptsMode(Es1TtmPlayer.Playing);
	}

	/// <summary>
	/// <c>Shell_PlayTtmInPlace</c> (<c>00037dce</c>)(<paramref name="name"/>) (docs/es1/shell/message-box.md#the-answer):
	/// the script opened playing on the scripts' default render context (<c>Ttm_Open</c>(0, 0, 0, name)); then a frame
	/// shown, the script run and the input read, until the script's mode is not playing, the input's key word is not 0 or
	/// its first button is down. A script still playing, so cut short, has the draw page and page 2 cleared, a frame shown
	/// and the new draw page cleared. Then the script closed and the palette <c>0007fcd0</c> cleared. No page is cleared
	/// before, and the default render context is left as it is.
	/// </summary>
	private IEnumerable<Es1ShellWait> PlayTtmInPlace(string name, Func<IEnumerable<Es1ShellWait>> presentFrame,
			Func<Es1LiveBlock> pollDeviceBlock) {
		if (_scripts() is not { } scripts) {
			yield break;
		}

		var display = _dialog.Display;
		byte handle = scripts.Open(Es1TtmPlayer.Playing, 0, null, name);
		while (true) {
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			foreach (var wait in scripts.RunScript(handle)) {
				yield return wait;
			}

			var block = pollDeviceBlock();
			if (scripts.GetMode(handle) != Es1TtmPlayer.Playing || block.CommandWord != 0 || block.MouseButtons[0] != 0) {
				break;
			}
		}

		if (scripts.GetMode(handle) == Es1TtmPlayer.Playing) {
			display.ClearPage(display.DrawPage, 0);
			display.ClearPage(Es1ShellBackdrop.BackgroundPage, 0);
			foreach (var wait in presentFrame()) {
				yield return wait;
			}

			display.ClearPage(display.DrawPage, 0);
		}

		scripts.Close(handle);
		_backdrop.Palette = null;
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
