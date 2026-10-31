using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Hangar;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// ARMAMENT's screen object, class <c>ArmingMenu</c> (vtable <c>00081fe6</c>, <c>0x28</c> bytes)
/// (docs/es1/shell/arming.md): screens 1800 and 1820, a window down the right of the hangar's picture of the selected
/// HERC, where a mount is chosen and a weapon from the stock fitted to it, or a missile rack's guidance kind chosen. Its
/// two hotspots draw through <see cref="WeaponInfoHandler"/> and <see cref="MissileInfoHandler"/>; its statics are
/// <see cref="Es1MenuContext.Arming"/>.
/// </summary>
public sealed class Es1ArmingMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1800.dlg</c>, and the one opened while memory is low, <c>dlg\menu1820.dlg</c>, which lacks the animation.</summary>
	public const short Screen = 1800, LowMemoryScreen = 1820;

	/// <summary>The animation and the weapon panel's hotspot, which share the id, and the missile panel's hotspot.</summary>
	public const short WeaponPanel = 1803, MissilePanel = 1811;

	/// <summary>The weapon list and the id <c>Menu_ListLines</c> gives the missile types under.</summary>
	public const short WeaponList = 1808, MissileTypeList = 1809;

	/// <summary>The HERC and PILOT readouts.</summary>
	public const short ChassisList = 1903, PilotList = 1904;

	/// <summary>The controls of the eleven ids of <c>ArmingMenu_CaseTable</c> (<c>00080db8</c>) besides the lists and the panel.</summary>
	private const short ListUp = 1801, ListDown = 1802, NextHerc = 2004, PrevHerc = 2003, NextMount = 1807, PrevMount = 1806,
		MissileType = 1111, Done = 100;

	/// <summary><c>+0x27</c>'s values: the list shows the weapons, or the missile types.</summary>
	private const byte WeaponsMode = 0, MissileTypesMode = 1;

	/// <summary>MISSILE TYPE's texts, <c>eng\watext.bin</c> lines 0 MISSILE TYPE and 1 WEAPON TYPE.</summary>
	private const short MissileTypeText = 0, WeaponTypeText = 1;

	/// <summary>The missile type of a mount that carries no missile rack, 5, and the first script of the four missile types, 14.</summary>
	private const short NoMissileType = 5, FirstMissileScript = 14;

	/// <summary>The missile racks, weapons 9 to 11.</summary>
	private const short FirstRack = 9, LastRack = 11;

	/// <summary>The screens DONE goes to: the war map in a single mission, the bay before a mission in a career.</summary>
	private const short WarMap = Es1WarMapMenu.Screen, ServiceBay = Es1ServiceBayMenu.Screen;

	/// <summary>
	/// The case of <c>Shell_HandleMessage</c> for 1800 and 1820 (<c>0003f196</c>), its first steps
	/// (docs/es1/shell/arming.md#entering-it): the menu's base, <c>+0x26</c> <c>0xff</c> and <c>+0x27</c> 0; and
	/// <see cref="Es1ArmingGlobals.LowMemPictures"/> none, or while <see cref="Es1ArmingGlobals.LowMemory"/> is set the bank
	/// <see cref="Es1MenuBanks.ForControl"/>(1803) gives. <see cref="Es1ShellScreens"/> runs the rest.
	/// </summary>
	public Es1ArmingMenu(Es1MenuContext context) : base(context) {
		Byte26 = 0xff;
		ListMode = WeaponsMode;
		context.Arming.LowMemPictures = Es1ArmingGlobals.LowMemory ? context.Banks.ForControl(WeaponPanel) : null;
	}

	/// <summary><c>+0x26</c>, which the case sets to <c>0xff</c>; no reader is known.</summary>
	public byte Byte26 { get; }

	/// <summary><c>+0x27</c>, the list's mode: 0 the weapons, 1 the missile types.</summary>
	public byte ListMode { get; private set; }

	private Es1ArmingGlobals Arming => Context.Arming;

	private Es1HangarBay? Bay => Context.Screens.Bay;

	private Es1HercRecord? SelectedHerc => Bay?.SelectedHerc;

	/// <summary><c>Herc_MountHighlightShown</c> (<c>0007ff98</c>): a mount is selected.</summary>
	private bool MountSelected => Context.Data.HercDiagrams.MountHighlightShown;

	/// <summary>
	/// <c>Menu_Open</c>'s changes for 1800 and 1820 (disassembly <c>0003969e</c>, after 1610's): MISSILE TYPE, found in any
	/// state, made inactive; and for 1800 the animation, found among the enabled controls, given script 0 through its slot
	/// <c>+0x24</c> (<see cref="Es1AnimationControl.Select"/>). A control missing is passed over, an engine guard.
	/// </summary>
	public static void SetOpeningControls(Es1DUIWindow window, short number, Action<Es1DUIWindow, short> makeInactive) {
		makeInactive(window, window.FindControlById(MissileType));
		if (number == Screen) {
			Animation(window)?.Select(0);
		}
	}

	/// <summary>The animation <c>DUIWindow_FindEnabledControlById</c> finds for 1803; retail calls the slot of whatever control the index −1 reads, which 1820 reaches, and none is called here, an engine guard.</summary>
	private static Es1AnimationControl? Animation(Es1DUIWindow? window) =>
		window != null && window.FindEnabledControlById(WeaponPanel) is var index and >= 0 && window.Controls[index] is Es1AnimationControl animation
			? animation
			: null;

	/// <summary>The animation's slot <c>+0x24</c> with <paramref name="index"/>, unless <see cref="Es1ArmingGlobals.LowMemory"/> is set.</summary>
	private void Animate(short index) {
		if (!Es1ArmingGlobals.LowMemory) {
			Animation(Window)?.Select(index);
		}
	}

	/// <summary>
	/// The redraw byte <c>+0</c> of the hotspot <c>DUIWindow_FindHotspotById</c> finds for <paramref name="id"/> set, so the
	/// next update draws it. Retail writes the byte before the hotspots for an id not found; here nothing is written, an
	/// engine guard.
	/// </summary>
	private void MarkHotspot(short id) {
		if (Window is { } window && window.FindHotspotById(id) is var index and >= 0) {
			window.Hotspots[index].Pressed = true;
		}
	}

	/// <summary>MISSILE TYPE, found with <c>DUIWindow_FindControlById</c>, given state <paramref name="state"/> and <i>changed</i>; a missing button is passed over, an engine guard.</summary>
	private void SetMissileTypeState(short state) {
		if (Window is { } window && window.FindControlById(MissileType) is var index and >= 0) {
			window.Controls[index].State = state;
			window.Controls[index].Changed = true;
		}
	}

	/// <summary>MISSILE TYPE's slot <c>+0x3c</c>, <c>MTButton_SetTextFromTable</c>, with <paramref name="line"/>.</summary>
	private void SetMissileTypeText(short line) {
		if (Window is { } window && window.FindControlById(MissileType) is var index and >= 0) {
			window.Controls[index].SetTextFromTable(line);
		}
	}

	/// <summary>The state MISSILE TYPE takes for <paramref name="weapon"/>: up for a missile rack, inactive for any other.</summary>
	private static short MissileTypeState(short weapon) => weapon is >= FirstRack and <= LastRack ? Es1DUIControl.Up : Es1DUIControl.Inactive;

	/// <summary>
	/// The selected HERC's word <c>+0x18</c> + 2 × <c>+0x70</c>, the current mount's missile type, 0 with no HERC. Retail
	/// reads it with no test of <c>+0x70</c>, so for −1 it reads <c>+0x16</c>, mount 9's weapon (arming.md's Open); a
	/// HERC with fewer than ten mounts, or a mount past its words, reads 0 here, an engine guard.
	/// </summary>
	private short SelectedMountAmmo() {
		if (Bay is not { } bay || SelectedHerc is not { } herc) {
			return 0;
		}

		short mount = bay.Diagrams.CurrentMount(herc);
		if (mount >= 0 && mount < herc.MountWords.Length) {
			return herc.MountWords[mount];
		}

		return mount == -1 && herc.Weapons.Length > 9 ? herc.Weapons[9] : (short)0;
	}

	/// <summary>The weapon list's lines for the selected HERC: <c>WaText_LinesFromList</c> of <see cref="Es1ArmingGlobals.BuildWeaponList"/>(its <c>+0x70</c>, its chassis), −1 for each with none.</summary>
	private string?[] BuildLines() {
		short mount = -1, chassis = -1;
		if (Bay is { } bay && SelectedHerc is { } herc) {
			mount = bay.Diagrams.CurrentMount(herc);
			chassis = herc.Chassis;
		}

		return Es1MenuBuild.LinesFromList(Context.Data.Text, Arming.BuildWeaponList(Context, mount, chassis));
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>ArmingMenu_OnMessage</c> (<c>0002e54c</c>) (docs/es1/shell/arming.md#what-a-press-does): the
	/// target looked up among the eleven ids of <c>ArmingMenu_CaseTable</c> (<c>00080db8</c>, cases at <c>00080de4</c>);
	/// nothing for any other id. The value is the message's word <c>+4</c>. Of the targets only DONE's, which its case
	/// changes, is a screen.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		switch (message.Target) {
			case WeaponPanel:
				// 0002e572.
				MarkHotspot(WeaponPanel);
				break;
			case WeaponList:
				// 0002e5b2.
				SelectListLine(message.Value);
				break;
			case ListUp:
				// 0002e5c5: the list's slot +0x1c, then its selected line (slot +0x2c).
				if (FindList(WeaponList) is { } upList) {
					upList.SelectPrev();
					SelectListLine(upList.GetValue());
				}

				break;
			case ListDown:
				// 0002e653: the list's slot +0x20, then its selected line.
				if (FindList(WeaponList) is { } downList) {
					downList.SelectNext();
					SelectListLine(downList.GetValue());
				}

				break;
			case NextHerc:
				// 0002e6e1.
				StepHerc(true);
				break;
			case PrevHerc:
				// 0002e896.
				StepHerc(false);
				break;
			case Es1ShellMouse.HangarAreaTarget:
				// 0002ea0f: Hangar_SelectMount(value), the area pressed or −1.
				Bay?.SelectMount(message.Value);
				ListMode = WeaponsMode;
				Refresh();
				break;
			case NextMount:
				// 0002ea2c.
				Bay?.NextMount();
				ListMode = WeaponsMode;
				Refresh();
				break;
			case PrevMount:
				// 0002ea43.
				Bay?.PrevMount();
				ListMode = WeaponsMode;
				Refresh();
				break;
			case MissileType:
				// 0002ea5a.
				ToggleListMode();
				break;
			case Done:
				// 0002ec7f.
				message = message with { Target = Finish() };
				break;
		}
	}

	/// <summary>
	/// The HERCS cases (docs/es1/shell/arming.md#hercs): <c>Hangar_SelectNext</c> or <c>Hangar_SelectPrev</c> with 2 in
	/// <c>Shell_Mode</c> 0 and 0 otherwise, in mode 0 then <c>Hangar_SelectedSlot</c> stored in the slot <c>+8</c> of
	/// <c>SingleMission_Record</c>'s pilot entry <c>+0x12</c>; the pilot of the slot's HERC made
	/// <c>CareerRecord_Current</c>'s current pilot (<see cref="Es1CareerRecord.SelectPilot"/> of
	/// <see cref="Es1CareerRecord.FindPilotBySlot"/>); PILOT selects <see cref="PilotLine"/>(the roster's current record,
	/// that pilot) and HERC the HERC's chassis, −1 for none (slot <c>+0x24</c>); <c>Herc_MountHighlightShown</c> cleared;
	/// <see cref="Refresh"/>. A handler is no iterator: the steps' wipes, whose waits are the frame's, are passed over, as
	/// <see cref="Es1CrewAssignMenu"/> passes the bay's script waits. Retail writes the current pilot's entry with no
	/// bounds test; one outside the four is not written here, and with no hangar the selection is not stepped, engine
	/// guards.
	/// </summary>
	private void StepHerc(bool next) {
		var mission = Context.Mission;
		short mode = mission.Mode == 0 ? (short)2 : (short)0;
		if (Bay is { } bay) {
			foreach (var _ in next ? bay.SelectNext(mode) : bay.SelectPrev(mode)) {
			}
		}

		if (mission.Hangar is not { } hangar) {
			return;
		}

		if (mission.Mode == 0 && mission.Record is { } single && single.CurrentPilot is >= 0 and < Es1CareerRecord.PilotCount) {
			single.Pilots[single.CurrentPilot].Slot = hangar.SelectedSlot;
		}

		short pilot = -1;
		if (mission.CurrentRecord is { } record) {
			pilot = record.FindPilotBySlot(hangar.SelectedSlot);
			record.SelectPilot(pilot, hangar);
		}

		FindList(PilotList)?.SelectLine(PilotLine(mission.Roster.CurrentRecord, pilot));
		FindList(ChassisList)?.SelectLine(hangar.HercIn(hangar.SelectedSlot)?.Chassis ?? -1);
		Context.Data.HercDiagrams.MountHighlightShown = false;
		Refresh();
	}

	/// <summary>
	/// PILOT's line for <paramref name="entry"/> of <paramref name="record"/>: <c>PilotEntry_NameIndex</c> (<c>00015f50</c>)
	/// + 1, so 0 for entry 0, 37 for entry −1, a HERC no pilot has, whose index is 36 with no entry read (UNASSIGNED, line
	/// <c>0x7a</c>), and 1 + 3 × the name group + the name otherwise. Retail reads any other entry outside the four before
	/// or past the record's entries; such an entry gives line 0 here, as does one of the four with no record, an engine guard.
	/// </summary>
	public static short PilotLine(Es1CareerRecord? record, short entry) => entry switch {
		UnassignedEntry => UnassignedLine,
		>= 0 and < Es1CareerRecord.PilotCount when record != null => (short)(record.Pilots[entry].NameIndex(entry) + 1),
		_ => 0,
	};

	/// <summary>The entry of a HERC no pilot has, −1, and PILOT's line for it, <c>PilotEntry_NameIndex</c>'s 36 + 1.</summary>
	private const short UnassignedEntry = -1, UnassignedLine = 37;

	/// <summary>
	/// DONE's case (docs/es1/shell/arming.md#done): <c>Hangar_ClearSelectedMount</c>, <c>CareerRecord_SaveCurrent</c>
	/// (<see cref="Es1ShellMission.SaveCurrentRecord"/>), and in <c>Shell_Mode</c> 0 the hangar written to
	/// <c>car\sm_init.dat</c> (<c>00080da8</c>, through <c>Path_ApplyAltDrive</c>) with <c>Hangar_SaveFile</c>
	/// (<see cref="Es1SingleMission.SaveHangar"/>) and the target 1501, otherwise the target 1700. Retail asserts at
	/// <c>mechman.cpp</c> line <c>0x132</c> when the file does not open; here that is reported, and with no hangar nothing
	/// is written, engine guards.
	/// </summary>
	private short Finish() {
		var mission = Context.Mission;
		Bay?.ClearSelectedMount();
		mission.SaveCurrentRecord();
		if (mission.Mode != 0) {
			return ServiceBay;
		}

		if (mission.Hangar is { } hangar) {
			try {
				Es1SingleMission.SaveHangar(mission.InstallRoot, Es1SingleMission.InitHangarName, hangar);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"car\\{Es1SingleMission.InitHangarName} does not open for writing ({ex.Message}; mechman.cpp line 0x132 asserts here).");
			}
		}

		return WarMap;
	}

	/// <summary>
	/// MISSILE TYPE's case (docs/es1/shell/arming.md#missile-type). With <see cref="ListMode"/> 0:
	/// <see cref="Es1ArmingGlobals.ShownMissileType"/> the current mount's type (<see cref="SelectedMountAmmo"/>); the list
	/// given <c>Menu_ListLines</c>(1809) (slot <c>+0x44</c>) and the type's line selected (slot <c>+0x24</c>); the animation
	/// given 14 + the type; hotspot 1811 redrawn; the button's text line 1, WEAPON TYPE; the mode 1. With the mode 1: the
	/// animation given <see cref="Es1ArmingGlobals.ShownWeapon"/> itself; hotspot 1803 redrawn; <see cref="Refresh"/>.
	/// </summary>
	private void ToggleListMode() {
		var arming = Arming;
		if (ListMode == WeaponsMode) {
			arming.ShownMissileType = SelectedMountAmmo();
			if (FindList(WeaponList) is { } list) {
				list.SetLines(Es1MenuBuild.ListLines(Context, MissileTypeList));
				list.SelectLine(arming.ShownMissileType);
			}

			Animate((short)(arming.ShownMissileType + FirstMissileScript));
			MarkHotspot(MissilePanel);
			SetMissileTypeText(WeaponTypeText);
			ListMode = MissileTypesMode;
		} else {
			Animate(arming.ShownWeapon);
			MarkHotspot(WeaponPanel);
			Refresh();
		}
	}

	/// <summary>
	/// <c>Arming_SelectListLine</c> (<c>0002e126</c>)(menu, <paramref name="line"/>): <see cref="SelectMissileType"/> while
	/// <see cref="ListMode"/> is 1, <see cref="SelectWeapon"/> otherwise.
	/// </summary>
	private void SelectListLine(short line) {
		if (ListMode == MissileTypesMode) {
			SelectMissileType(line);
		} else {
			SelectWeapon(line);
		}
	}

	/// <summary>
	/// <c>Arming_SelectWeapon</c> (<c>0002e157</c>)(menu, <paramref name="line"/>) (docs/es1/shell/arming.md#picking-a-line):
	/// <i>w</i> = <see cref="Es1ArmingGlobals.WeaponAtListPosition"/>(line) into <see cref="Es1ArmingGlobals.ShownWeapon"/>.
	/// When <i>w</i> differs from <see cref="Es1ArmingGlobals.AnimatedWeapon"/>: the animation given <i>w</i>, −1 for 17;
	/// hotspot 1803 redrawn; with a mount selected, <i>w</i> fitted (<see cref="Es1HangarBay.FitWeapon"/>), the list's
	/// lines (<see cref="BuildLines"/>) kept in <see cref="Es1ArmingGlobals.WeaponLines"/>, their counts written
	/// (<see cref="Es1ArmingGlobals.FormatCounts"/>), given to the list (slot <c>+0x44</c>) and <i>w</i>'s line selected
	/// (<see cref="Es1ArmingGlobals.WeaponListPosition"/>, slot <c>+0x24</c>); and <see cref="Es1ArmingGlobals.AnimatedWeapon"/>
	/// set to <i>w</i>. For a missile rack, <see cref="Es1ArmingGlobals.ShownMissileType"/> the current mount's type and
	/// hotspot 1803 redrawn. With a mount selected, MISSILE TYPE up for a missile rack and inactive otherwise.
	/// </summary>
	private void SelectWeapon(short line) {
		var arming = Arming;
		short mode = Context.Mission.Mode;
		short weapon = arming.WeaponAtListPosition(mode, line);
		arming.ShownWeapon = weapon;
		if (weapon != arming.AnimatedWeapon) {
			Animate(weapon == Es1ArmingGlobals.EmptyMount ? (short)-1 : weapon);
			MarkHotspot(WeaponPanel);
			if (MountSelected) {
				Bay?.FitWeapon(weapon);
				var lines = BuildLines();
				arming.WeaponLines = lines;
				arming.FormatCounts(Context, lines);
				if (FindList(WeaponList) is { } list) {
					list.SetLines(lines);
					list.SelectLine(arming.WeaponListPosition(mode, weapon));
				}
			}

			arming.AnimatedWeapon = weapon;
		}

		if (weapon is >= FirstRack and <= LastRack) {
			arming.ShownMissileType = SelectedMountAmmo();
			MarkHotspot(WeaponPanel);
		}

		if (MountSelected) {
			SetMissileTypeState(MissileTypeState(weapon));
		}
	}

	/// <summary>
	/// <c>Arming_SelectMissileType</c> (<c>0002e47f</c>)(menu, <paramref name="type"/>)
	/// (docs/es1/shell/arming.md#missile-type): <see cref="Es1ArmingGlobals.ShownMissileType"/> the type; with a HERC in the
	/// selected slot, <c>Herc_SetCurrentMountAmmo</c> (<see cref="Es1HercDiagrams.SetCurrentMountAmmo"/>) of it; the
	/// animation given 14 + the type; hotspot 1811 redrawn.
	/// </summary>
	private void SelectMissileType(short type) {
		Arming.ShownMissileType = type;
		if (Bay is { } bay && SelectedHerc is { } herc) {
			bay.Diagrams.SetCurrentMountAmmo(herc, type);
		}

		Animate((short)(type + FirstMissileScript));
		MarkHotspot(MissilePanel);
	}

	/// <summary>
	/// <c>Arming_Refresh</c> (<c>0002dc54</c>)(menu) (docs/es1/shell/arming.md#refreshing):
	/// <see cref="Es1ArmingGlobals.ShownMissileType"/> the current mount's type; the list's lines (<see cref="BuildLines"/>,
	/// no counts written) kept in <see cref="Es1ArmingGlobals.WeaponLines"/> and given to the list. With no mount selected:
	/// <see cref="Es1ArmingGlobals.ShownWeapon"/> 0 and, when <see cref="Es1ArmingGlobals.AnimatedWeapon"/> is not 0, line
	/// 0 selected, the animation given 0, hotspot 1803 redrawn and <see cref="Es1ArmingGlobals.AnimatedWeapon"/> 0;
	/// MISSILE TYPE inactive. With one: <see cref="Es1ArmingGlobals.ShownWeapon"/> the mount's weapon
	/// (<c>Herc_CurrentMountWeapon</c>, −1 with no HERC) and its line selected; the animation given −1 for 17, or the
	/// weapon when it differs from <see cref="Es1ArmingGlobals.AnimatedWeapon"/>; hotspot 1803 redrawn;
	/// <see cref="Es1ArmingGlobals.AnimatedWeapon"/> the weapon; MISSILE TYPE up for a missile rack and inactive
	/// otherwise. Then MISSILE TYPE's text line 0 and <see cref="ListMode"/> 0.
	/// </summary>
	private void Refresh() {
		var arming = Arming;
		short mode = Context.Mission.Mode;
		arming.ShownMissileType = SelectedMountAmmo();
		var lines = BuildLines();
		arming.WeaponLines = lines;
		var list = FindList(WeaponList);
		list?.SetLines(lines);
		if (!MountSelected) {
			arming.ShownWeapon = 0;
			if (arming.AnimatedWeapon != 0) {
				list?.SelectLine(0);
				Animate(arming.ShownWeapon);
				MarkHotspot(WeaponPanel);
				arming.AnimatedWeapon = arming.ShownWeapon;
			}

			SetMissileTypeState(Es1DUIControl.Inactive);
		} else {
			short weapon = Bay is { } bay && SelectedHerc is { } herc ? bay.Diagrams.CurrentMountWeapon(herc) : (short)-1;
			arming.ShownWeapon = weapon;
			list?.SelectLine(arming.WeaponListPosition(mode, weapon));
			if (weapon == Es1ArmingGlobals.EmptyMount) {
				Animate(-1);
			} else if (weapon != arming.AnimatedWeapon) {
				Animate(weapon);
			}

			MarkHotspot(WeaponPanel);
			arming.AnimatedWeapon = weapon;
			SetMissileTypeState(MissileTypeState(weapon));
		}

		SetMissileTypeText(MissileTypeText);
		ListMode = WeaponsMode;
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ArmingMenu_Dtor</c> (<c>000405ae</c>) (docs/es1/shell/arming.md#done):
	/// <see cref="Es1ArmingGlobals.LowMemPictures"/>, when set, freed with <c>Shell_FreeBitmapArraySlot</c>
	/// (<see cref="Es1MenuBanks.Free"/>) and left as it is; the base vtables stored and the menu freed.
	/// </summary>
	public override void Dtor() {
		if (Arming.LowMemPictures is { } pictures) {
			Context.Banks.Free(pictures);
		}
	}

	/// <summary>The font slots of the panel: <c>nm_white</c> (<c>000aacee</c>) for the labels and <c>nm_ylw</c> (<c>000aacf6</c>) for the values.</summary>
	private const int WhiteFont = 10, YellowFont = 12;

	/// <summary>The panel's clear color, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte ClearColor = 0x1f;

	/// <summary>The fixed corners of the weapon panel's two bands, (122, 59) and (12, 98).</summary>
	private const int BandRight = 122, BandTop = 59, LowerBandLeft = 12, LowerBandTop = 98;

	/// <summary>Lines of <c>eng\watext.bin</c>: TYPE:, RANGE:, MISSILES:, POWER: and SIZE:.</summary>
	private const short TypeLabel = 0xcf, RangeLabel = 0xd1, MissilesLabel = 0xd2, PowerLabel = 0xd3, SizeLabel = 0xd4;

	/// <summary>
	/// The first lines of <c>eng\watext.bin</c> the values read: a weapon's size or power, its range and its name; a missile
	/// type's name and range; and NONE for a rack with no type.
	/// </summary>
	private const short SizeLines = 0xf8, WeaponRangeLines = 0xea, WeaponNameLines = 0xd5, MissileTypeLines = 0x106,
		MissileRangeLines = 0x10b, NoMissileTypeLine = 0x10a;

	/// <summary>
	/// <c>Arming_PicturePoints</c> (<c>000a04dc</c>), which <c>MenuHandlers_StaticInit</c> (<c>00030ee9</c>) fills: where
	/// a weapon's still picture goes, 0 to 13, and a missile type's, 14 to 17.
	/// </summary>
	private static readonly (int X, int Y)[] PicturePoints = [
		(37, 72), (29, 69), (23, 72), (19, 66), (21, 63), (0, 0), (34, 72), (26, 72), (18, 70), (36, 67), (32, 66), (32, 60),
		(17, 71), (42, 71), (28, 75), (17, 74), (25, 74), (29, 69),
	];

	/// <summary>The handler <c>Menu_BuildFromDlg</c> gives hotspot 1803, <c>Arming_DrawWeaponInfo</c> (<c>0002d5bf</c>), over <paramref name="context"/>.</summary>
	public static Es1HotspotHandler WeaponInfoHandler(Es1MenuContext context) => (render, rect, id) => DrawWeaponInfo(context, render, rect);

	/// <summary>The handler <c>Menu_BuildFromDlg</c> gives hotspot 1811, <c>Arming_DrawMissileInfo</c> (<c>0002dacf</c>), over <paramref name="context"/>.</summary>
	public static Es1HotspotHandler MissileInfoHandler(Es1MenuContext context) => (render, rect, id) => DrawMissileInfo(context, render, rect);

	/// <summary>
	/// <c>Arming_DrawWeaponInfo</c> (<c>0002d5bf</c>)(<paramref name="rect"/>, id) (docs/es1/shell/arming.md#the-panel), with
	/// <i>w</i> <see cref="Es1ArmingGlobals.ShownWeapon"/> and <i>l</i>, <i>t</i> the rect's corner: nothing for 17;
	/// otherwise the text cleared, the bands from the corner to (122, 59) and from (12, 98) to the far corner, or the whole
	/// rect while memory is low, each the box their two corners span; the alignment 0; TYPE: and RANGE: at (<i>l</i> + 5,
	/// <i>t</i> + 6) and (<i>l</i> + 5, <i>t</i> + 15) in <c>nm_white</c>; at (<i>l</i> + 5, <i>t</i> + 59) by <i>w</i>:
	/// 0 to 2 SIZE: and 25 right <c>0xf8</c> + <i>w</i>, 9 to 11 MISSILES: and 35 right <c>0x10a</c> NONE for type 5 or
	/// <c>0x106</c> + the type, 5 and 13 nothing, any other POWER: and 30 right <c>0xf8</c> + <i>w</i>, the label in
	/// <c>nm_white</c> and the value in <c>nm_ylw</c>; in <c>nm_ylw</c> the name <c>0xd5</c> + <i>w</i> at (<i>l</i> + 30,
	/// <i>t</i> + 6) and the range, <c>0x10b</c> + the type for 9 to 11 and <c>0xea</c> + <i>w</i> otherwise, at
	/// (<i>l</i> + 30, <i>t</i> + 15); while memory is low, frame <i>w</i> of <see cref="Es1ArmingGlobals.LowMemPictures"/>
	/// at <see cref="PicturePoints"/>[<i>w</i>]. Each line number is a 16-bit sum.
	/// </summary>
	private static void DrawWeaponInfo(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect) {
		var arming = context.Arming;
		short weapon = arming.ShownWeapon;
		if (weapon == Es1ArmingGlobals.EmptyMount) {
			return;
		}

		var canvas = render.Canvas;
		if (!Es1ArmingGlobals.LowMemory) {
			Clear(canvas, rect.X0, rect.Y0, BandRight, BandTop);
			Clear(canvas, LowerBandLeft, LowerBandTop, rect.X1, rect.Y1);
		} else {
			Clear(canvas, rect.X0, rect.Y0, rect.X1, rect.Y1);
		}

		var data = context.Data;
		var white = data.Fonts[WhiteFont];
		var yellow = data.Fonts[YellowFont];
		render.Alignment = 0;
		int x = rect.X0 + 5, y = rect.Y0 + 6;
		render.DrawString(white, Line(data, TypeLabel), x, y);
		y += 9;
		render.DrawString(white, Line(data, RangeLabel), x, y);
		y += 0x2c;
		short type = arming.ShownMissileType;
		switch (weapon) {
			case 0 or 1 or 2:
				render.DrawString(white, Line(data, SizeLabel), x, y);
				render.DrawString(yellow, Line(data, (short)(SizeLines + weapon)), x + 25, y);
				break;
			case 5 or 13:
				break;
			case >= FirstRack and <= LastRack:
				render.DrawString(white, Line(data, MissilesLabel), x, y);
				render.DrawString(yellow, Line(data, type == NoMissileType ? NoMissileTypeLine : (short)(MissileTypeLines + type)), x + 35, y);
				break;
			default:
				render.DrawString(white, Line(data, PowerLabel), x, y);
				render.DrawString(yellow, Line(data, (short)(SizeLines + weapon)), x + 30, y);
				break;
		}

		x = rect.X0 + 30;
		y = rect.Y0 + 6;
		render.DrawString(yellow, Line(data, (short)(WeaponNameLines + weapon)), x, y);
		y += 9;
		short range = weapon is >= FirstRack and <= LastRack ? (short)(MissileRangeLines + type) : (short)(WeaponRangeLines + weapon);
		render.DrawString(yellow, Line(data, range), x, y);
		if (Es1ArmingGlobals.LowMemory) {
			DrawPicture(render, arming.LowMemPictures, weapon);
		}
	}

	/// <summary>
	/// <c>Arming_DrawMissileInfo</c> (<c>0002dacf</c>)(<paramref name="rect"/>, id) (docs/es1/shell/arming.md#the-panel),
	/// with <i>m</i> <see cref="Es1ArmingGlobals.ShownMissileType"/>: only while a mount is selected
	/// (<c>Herc_MountHighlightShown</c>) and <i>m</i> is not 5; while memory is low the rect cleared; the alignment 0;
	/// TYPE: and RANGE: as <see cref="DrawWeaponInfo"/> draws them; in <c>nm_ylw</c> <c>0x106</c> + <i>m</i> at (<i>l</i>
	/// + 30, <i>t</i> + 6) and <c>0x10b</c> + <i>m</i> at (<i>l</i> + 30, <i>t</i> + 15); while memory is low, frame
	/// <i>m</i> + 14 of <see cref="Es1ArmingGlobals.LowMemPictures"/> at <see cref="PicturePoints"/>[<i>m</i> + 14].
	/// </summary>
	private static void DrawMissileInfo(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect) {
		var arming = context.Arming;
		short type = arming.ShownMissileType;
		if (!context.Data.HercDiagrams.MountHighlightShown || type == NoMissileType) {
			return;
		}

		if (Es1ArmingGlobals.LowMemory) {
			Clear(render.Canvas, rect.X0, rect.Y0, rect.X1, rect.Y1);
		}

		var data = context.Data;
		var white = data.Fonts[WhiteFont];
		var yellow = data.Fonts[YellowFont];
		render.Alignment = 0;
		int x = rect.X0 + 5, y = rect.Y0 + 6;
		render.DrawString(white, Line(data, TypeLabel), x, y);
		y += 9;
		render.DrawString(white, Line(data, RangeLabel), x, y);
		x = rect.X0 + 30;
		y = rect.Y0 + 6;
		render.DrawString(yellow, Line(data, (short)(MissileTypeLines + type)), x, y);
		y += 9;
		render.DrawString(yellow, Line(data, (short)(MissileRangeLines + type)), x, y);
		if (Es1ArmingGlobals.LowMemory) {
			DrawPicture(render, arming.LowMemPictures, (short)(type + FirstMissileScript));
		}
	}

	/// <summary>
	/// <c>Raster_FillFrameRectColours</c> (<c>0004b8d8</c>)(<c>0x1f</c>, <c>0x1f</c>) of the box the two corners span,
	/// each coordinate the smaller and the larger of the two: filled under the brush {0, fill}, then its edges under
	/// {4, frame}.
	/// </summary>
	private static void Clear(Es1PanelCanvas canvas, int xa, int ya, int xb, int yb) {
		int x0 = Math.Min(xa, xb), y0 = Math.Min(ya, yb), x1 = Math.Max(xa, xb), y1 = Math.Max(ya, yb);
		canvas.FillRect(x0, y0, x1, y1, ClearColor);
		canvas.OutlineRect(x0, y0, x1, y1, ClearColor);
	}

	/// <summary>
	/// <c>Bitmap_Blit</c>(frame <paramref name="index"/> of <paramref name="pictures"/>, 0, <see cref="PicturePoints"/>[index]).
	/// Retail reads the frame and the point unchecked; an index outside either draws nothing here, an engine guard.
	/// </summary>
	private static void DrawPicture(Es1ShellRenderContext render, IReadOnlyList<DynamixBitmap>? pictures, short index) {
		if (index < 0 || index >= PicturePoints.Length || pictures == null || index >= pictures.Count) {
			return;
		}

		render.Blit(pictures[index], PicturePoints[index].X, PicturePoints[index].Y);
	}

	/// <summary>
	/// <c>Text_GetString</c>(<c>0009afa8</c>, <paramref name="number"/>), a line of <c>eng\watext.bin</c>; none, so nothing
	/// drawn, outside the table or without it, an engine guard for its missing bounds test (arming.md's Open).
	/// </summary>
	private static string? Line(Es1ShellData data, short number) => data.Text is { } text && number >= 0 && number < text.Length ? text[number] : null;
}
