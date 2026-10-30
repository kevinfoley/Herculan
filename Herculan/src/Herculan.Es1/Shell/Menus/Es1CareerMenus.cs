namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The career menu's screen object, class <c>CareerMenu</c> (vtable <c>00081c12</c>) (docs/es1/shell/career.md#the-career-menu):
/// screen 1200, the list of the careers in the log, NEW, PROCEED, DELETE, STATS, SAVE/RESTORE, OPTIONS and MAIN MENU.
/// </summary>
public sealed class Es1CareerMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1200.dlg</c>.</summary>
	public const short Screen = 1200;

	/// <summary>The list of the careers' names.</summary>
	public const short CareerList = 1202;

	/// <summary>The controls of the seven ids of the table at <c>000809ec</c>.</summary>
	public const short New = 1210, Proceed = 1700, Records = 1220, Stats = 1230, Delete = 1201, UpArrow = 101, DownArrow = 102;

	/// <summary>The screens the handler turns NEW and DELETE into: the disk-space warning and the delete warning.</summary>
	private const short DiskWarning = Es1NewCareerAlert.DiskWarningScreen, DeleteWarning = 1270;

	/// <summary>The career's screen that loads no mission, the service bay.</summary>
	public const short ServiceBay = 1710;

	/// <summary>The technology release, <c>TechReleaseAlert</c>, which 1710 becomes while the record's word <c>+0xa8</c> is not −1.</summary>
	public const short TechRelease = 5555;

	/// <summary>The free bytes NEW asks the drive for.</summary>
	private const long NewCareerBytes = 60000;

	/// <summary>The <c>.ANM</c> PROCEED puts into <c>000aacfe</c>, the string at <c>00080984</c>.</summary>
	private const string MissionAnm = "anm\\esiege3.anm";

	/// <summary>The lines of <c>eng\watext.bin</c> <see cref="SetButtons"/> gives PROCEED and NEW.</summary>
	private const short ProceedLine = 0xbe, NewMoreLine = 0xbd, ProceedMoreLine = 0xbf, NewLine = 0xbc;

	public Es1CareerMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// <c>Menu_Open</c>'s case for 1200 (<c>0003996c</c>) (docs/es1/shell/career.md#the-career-menu): with the careers log's
	/// count 0, PROCEED takes line <c>0xbe</c> and NEW line <c>0xbd</c> through their slot <c>+0x3c</c>
	/// (<see cref="Es1DUIControl.SetTextFromTable"/>), and PROCEED, SAVE/RESTORE, STATS and DELETE are made inactive;
	/// otherwise PROCEED takes line <c>0xbf</c> and NEW line <c>0xbc</c>. Each control is found with
	/// <c>DUIWindow_FindControlById</c>; with no log, as after a file that did not read, the count is 0.
	/// </summary>
	public static void SetButtons(Es1DUIWindow window, Es1ShellData data, Action<Es1DUIWindow, short> makeInactive) {
		bool none = (data.CareerLog?.Entries.Length ?? 0) == 0;
		SetText(window, Proceed, none ? ProceedLine : ProceedMoreLine);
		SetText(window, New, none ? NewMoreLine : NewLine);
		if (none) {
			foreach (short id in new[] { Proceed, Records, Stats, Delete }) {
				makeInactive(window, window.FindControlById(id));
			}
		}
	}

	/// <summary>
	/// A control's slot <c>+0x3c</c> with a line of <c>eng\watext.bin</c>. Retail indexes the controls with the −1 of an id
	/// it does not find; here such an id is passed over, an engine guard.
	/// </summary>
	private static void SetText(Es1DUIWindow window, short id, short line) {
		short index = window.FindControlById(id);
		if (index >= 0) {
			window.Controls[index].SetTextFromTable(line);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>CareerMenu_OnMessage</c> (<c>0002aa0e</c>) (docs/es1/shell/career.md#the-buttons): by the message's
	/// target, the case of the table at <c>000809ec</c> (cases at <c>00080a08</c>); then, for every message,
	/// <see cref="Es1ShellData.SelectedEntry"/> set to the list's selected line (its slot <c>+0x2c</c>). OPTIONS and MAIN
	/// MENU only change the screen. The list is found with <c>DUIWindow_FindEnabledControlById</c>
	/// (<see cref="Es1Menu.FindList"/>); with none, the steps that read it are passed over, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var data = Context.Data;
		short current = data.CareerLog?.Current ?? -1;
		switch (message.Target) {
			case New:
				// 0002aa34.
				if (!DiskHasFreeBytes(NewCareerBytes)) {
					message = message with { Target = DiskWarning };
				}

				break;
			case Proceed:
				// 0002aa54.
				ProceedCase(ref message, current);
				break;
			case Records:
			case Stats:
				// 0002acd3.
				if (FindList(CareerList) is { } list && (list.GetValue() != current || !Context.Mission.Roster.Loaded)) {
					LoadSelected(list);
					if (Es1CareerRoster.PlayerCondition(Context.Mission.Roster.CurrentRecord) != 0) {
						// PLACEHOLDER: Career_StartMission (00016e93)(Career_Roster, 0), the career's mission load, P10's.
					}

					MarkLoaded();
				}

				break;
			case Delete:
				// 0002addd.
				message = message with { Target = DeleteWarning };
				break;
			case UpArrow:
				// 0002ade8: the list's slot +0x1c.
				FindList(CareerList)?.SelectPrev();
				break;
			case DownArrow:
				// 0002ae1f: the list's slot +0x20.
				FindList(CareerList)?.SelectNext();
				break;
		}

		// 0002ae5f.
		if (FindList(CareerList) is { } selected) {
			data.SelectedEntry = selected.GetValue();
		}
	}

	/// <summary>
	/// PROCEED (<c>0002aa54</c>) (docs/es1/shell/career.md#proceed): when the list's line differs from
	/// <paramref name="current"/>, the log's current entry as the handler began, or no career is loaded, the line made the
	/// current entry and loaded (<see cref="LoadSelected"/>); then <see cref="Es1CareerRoster.PeekNextCampaign"/>'s low byte
	/// <c>0xff</c> or <c>0x63</c>, the career lost or won, ends the case with the target 1260 and
	/// <see cref="Es1ShellScreens.ReturnScreen"/> 1000; otherwise the target is the record's word <c>+4</c>, the career's
	/// mission is loaded unless that is 1710 or the player's pilot is lost, and <see cref="MarkLoaded"/>. Then, with the
	/// player's pilot lost, the target 1250 and the return screen 1200; otherwise the target the record's <c>+4</c>, 5555
	/// for 1710 when the record's word <c>+0xa8</c> is not −1, and <c>anm\esiege3.anm</c> to play after the next screen.
	/// </summary>
	private void ProceedCase(ref Es1MenuMessage message, short current) {
		var roster = Context.Mission.Roster;
		var screens = Context.Screens;
		if (FindList(CareerList) is { } list && (list.GetValue() != current || !roster.Loaded)) {
			LoadSelected(list);
			if ((roster.PeekNextCampaign(roster.CurrentRecord) & 0xff) is 0xff or 0x63) {
				screens.ReturnScreen = Es1ShellScreens.MainMenuScreen;
				message = message with { Target = Es1KIAWarningAlert.CareerOverScreen };
				return;
			}

			message = message with { Target = roster.CurrentRecord.Word4 };
			if (message.Target != ServiceBay && Es1CareerRoster.PlayerCondition(roster.CurrentRecord) != 0) {
				// PLACEHOLDER: Career_StartMission (00016e93)(Career_Roster, 0), the career's mission load, P10's.
			}

			MarkLoaded();
		}

		var record = roster.CurrentRecord;
		if (Es1CareerRoster.PlayerCondition(record) == 0) {
			screens.ReturnScreen = Screen;
			message = message with { Target = Es1KIAWarningAlert.KilledScreen };
			return;
		}

		message = message with { Target = record.Word4 };
		if (message.Target != ServiceBay) {
			// PLACEHOLDER: Shell_PrepareMission (0002a523) of the career's mission, which Career_StartMission loads; P10's.
		}

		if (message.Target == ServiceBay && record.Equipment.LastKind != -1) {
			message = message with { Target = TechRelease };
		}

		screens.AnmName = MissionAnm;
		screens.AnmMode = 1;
	}

	/// <summary>The careers log's current entry set to the list's line, and <c>Career_LoadCurrent</c> (<see cref="Es1CareerRoster.LoadCurrent"/>).</summary>
	private void LoadSelected(Es1MTStrListBox list) {
		if (Context.Data.CareerLog is { } log) {
			log.Current = list.GetValue();
		}

		Context.Mission.Roster.LoadCurrent();
	}

	/// <summary><c>Career_BackupLine</c> the last backup's line, <c>Roster_CountUsedRecords</c> − 1, and <c>Career_Loaded</c> set.</summary>
	private void MarkLoaded() {
		var roster = Context.Mission.Roster;
		roster.BackupLine = (short)(roster.CountUsedRecords() - 1);
		roster.Loaded = true;
	}

	/// <summary>
	/// <c>Disk_HasFreeBytes</c> (<c>0002a9ad</c>)(<paramref name="bytes"/>): whether the drive the shell's files go to holds
	/// at least that many free bytes, retail's drive of <c>Path_ApplyAltDrive</c> or the current one. Here the drive is the
	/// install's, where the career's files go; a drive that does not answer counts as having room, an engine guard.
	/// </summary>
	private bool DiskHasFreeBytes(long bytes) {
		try {
			string root = Path.GetPathRoot(Path.GetFullPath(Context.Mission.InstallRoot)) ?? "";
			return new DriveInfo(root).AvailableFreeSpace >= bytes;
		} catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) {
			return true;
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The REGISTRATION window's screen object, class <c>NewCareerAlert</c> (vtable <c>00081e6a</c>)
/// (docs/es1/shell/career.md#starting-a-career): screen 1210, the name box, ACCEPT and CANCEL.
/// </summary>
public sealed class Es1NewCareerAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1210.dlg</c>.</summary>
	public const short Screen = 1210;

	/// <summary>The disk-space warning NEW opens in its place, an <c>UnAssignedAlert</c>, <c>dlg\menu6666.dlg</c>.</summary>
	public const short DiskWarningScreen = 6666;

	/// <summary>ACCEPT's id, and the name box's.</summary>
	private const short Accept = 100, NameBox = 103;

	/// <summary>The screen ACCEPT opens, the campaign's talking head, which shows the message box's kind 2.</summary>
	public const short OpeningScreen = 1490;

	/// <summary>The message box's kind for the campaign's opening lines.</summary>
	public const short OpeningKind = 2;

	/// <summary>The <c>.ANM</c> ACCEPT puts into <c>000aacfe</c>, the string at <c>00080984</c>.</summary>
	private const string MissionAnm = "anm\\esiege3.anm";

	public Es1NewCareerAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>NewCareerAlert_OnMessage</c> (<c>0002aeec</c>): for ACCEPT, the text of the name box, control
	/// 103 found with <c>DUIWindow_FindEnabledControlById</c>, through its slot <c>+0x30</c>
	/// (<see cref="Es1MTEditBox.GetText"/>); <c>Career_New</c>(roster, 0, the text) (<see cref="Es1CareerRoster.New"/>);
	/// the career's first mission loaded; the target 1490; <c>anm\esiege3.anm</c> to play after the next screen; and the
	/// message box's kind 2. CANCEL (99) passes to <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>. Retail reads
	/// the slot of whatever the index −1 gives when no box is found; here the name is then empty, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target != Accept) {
			return;
		}

		string name = Window is { } window && window.FindEnabledControlById(NameBox) is var index and >= 0
			&& window.Controls[index] is Es1MTEditBox box
				? box.GetText()
				: "";
		Context.Mission.Roster.New(0, name);
		// PLACEHOLDER: Career_StartMission (00016e93)(Career_Roster, 0) and Shell_PrepareMission (0002a523), the career's
		// mission load, P10's.
		message = message with { Target = OpeningScreen };
		Context.Screens.AnmName = MissionAnm;
		Context.Screens.AnmMode = 1;
		Context.Globals.MessageBox.SetKind(OpeningKind);
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The delete warning's screen object, class <c>DeleteAlert</c> (vtable <c>00081f1a</c>)
/// (docs/es1/shell/career.md#deleting-a-career): screen 1270, CANCEL and DELETE.
/// </summary>
public sealed class Es1DeleteAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1270.dlg</c>.</summary>
	public const short Screen = 1270;

	/// <summary>DELETE's id.</summary>
	private const short DeleteButton = 100;

	public Es1DeleteAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>DeleteAlert_OnMessage</c> (<c>0002ae96</c>): for DELETE, <c>CareerLog_Delete</c> of the career
	/// menu's selected line (<see cref="Es1ShellData.DeleteCareer"/>), <see cref="Es1ShellData.SelectedEntry"/> set to the
	/// log's current entry after its load, <c>Career_Loaded</c> cleared and the target the career menu. CANCEL (99) passes
	/// to <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>. With no log the current entry reads −1, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target != DeleteButton) {
			return;
		}

		var data = Context.Data;
		data.DeleteCareer(Context.Mission.InstallRoot, data.SelectedEntry);
		data.SelectedEntry = data.CareerLog?.Current ?? -1;
		Context.Mission.Roster.Loaded = false;
		message = message with { Target = Es1CareerMenu.Screen };
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The KIA warnings' screen object, class <c>KIAWarningAlert</c> (vtable <c>00081efa</c>)
/// (docs/es1/shell/main-menu.md#resume): screen 1250, the player's pilot lost, and 1260, the career over, each with DONE.
/// </summary>
public sealed class Es1KIAWarningAlert : Es1Menu {
	/// <summary>The warning that the player's pilot was killed, <c>dlg\menu1250.dlg</c>.</summary>
	public const short KilledScreen = 1250;

	/// <summary>The warning that the career is over, <c>dlg\menu1260.dlg</c>.</summary>
	public const short CareerOverScreen = 1260;

	/// <summary>DONE's id, a <i>back</i>.</summary>
	private const short Done = 99;

	public Es1KIAWarningAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>KIAWarningAlert_OnMessage</c> (<c>0002b34c</c>): DONE's 99 turned into
	/// <see cref="Es1ShellScreens.ReturnScreen"/>, so DONE goes to the screen the warning's opener set.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target == Done) {
			message = message with { Target = Context.Screens.ReturnScreen };
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}
