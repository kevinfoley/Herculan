using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Ttm;

namespace Herculan.Es1.Shell.Hangar;

/// <summary>
/// The service bay's display and the hangar's selection (docs/es1/shell/hangar.md#the-service-bays-display,
/// docs/es1/shell/repair-and-build.md#the-hangars-selection): the bay's scripts given their bays and run, the selected
/// bay's HERC drawn into the backdrop and taken out of it, the HERCS arrows' steps with their wipes, and the mount
/// selectors, on the hangar <see cref="Es1ShellMission.Hangar"/> and <c>CareerRecord_Current</c>
/// (<see cref="Es1ShellMission.CurrentRecord"/>). The page words <c>000aa7b0</c> and <c>000aa7c0</c> are its own. The class
/// is this engine's bundle of the scripts' object file and <c>mechman.cpp</c>'s display functions; every member is
/// retail's. Each method that runs a script answers the waits of the script's fades.
/// </summary>
public sealed class Es1HangarBay {
	/// <summary>The bay slots the page words have room for, eight words each.</summary>
	private const int PageSlots = 8;

	/// <summary>The words <c>0007e700</c> + 2 × chassis: the script entry a HERC being built under 51% shows, chassis −1 reading the word before them, 0.</summary>
	private static readonly short[] BuildingEntries = [22, 17, 20, 23, 20, 21, 18, 24];

	/// <summary>The build progress below which the bay adds the chassis' building script, <c>0x33</c>.</summary>
	private const short BuildingBelow = 0x33;

	private readonly Es1ShellBackdrop _backdrop;
	private readonly Func<Es1TtmManager?> _scripts;
	private readonly Es1ShellMission _mission;
	private readonly Es1ShellData _data;
	private readonly Es1SpriteSurface _surface;
	private readonly Es1ScreenBlinds _blinds;
	private readonly Func<short> _backdropId;

	/// <param name="backdropId"><c>Shell_BackdropId</c> (<see cref="Es1ShellScreens.BackdropId"/>), which picks how the scripts run.</param>
	public Es1HangarBay(Es1ShellBackdrop backdrop, Func<Es1TtmManager?> scripts, Es1ShellMission mission, Es1ShellData data,
			Es1ShellDevices devices, Es1DirtyList dirty, Func<short> backdropId) {
		_backdrop = backdrop;
		_scripts = scripts;
		_mission = mission;
		_data = data;
		_surface = new Es1SpriteSurface(devices.Display, dirty);
		_blinds = new Es1ScreenBlinds(devices);
		_backdropId = backdropId;
	}

	/// <summary>The HERCs' diagrams, the shared data's (<see cref="Es1ShellData.HercDiagrams"/>).</summary>
	public Es1HercDiagrams Diagrams => _data.HercDiagrams;

	/// <summary>The display and dirty list the HERC's sprites draw with.</summary>
	public Es1SpriteSurface Surface => _surface;

	/// <summary>The wipes the HERCS arrows run (<see cref="SelectNext"/>, <see cref="SelectPrev"/>).</summary>
	public Es1ScreenBlinds Blinds => _blinds;

	/// <summary>
	/// The words <c>000aa7b0</c>[slot], 0 in the image: the page, less 1, <see cref="ShowSelectedHerc"/> sets group script
	/// 0 to for the slot; <see cref="AssignSlotScripts"/> draws them.
	/// </summary>
	public short[] FirstPages { get; } = new short[PageSlots];

	/// <summary>The words <c>000aa7c0</c>[slot], 0 in the image: as <see cref="FirstPages"/>, for group script 1.</summary>
	public short[] SecondPages { get; } = new short[PageSlots];

	private Es1Hangar? Hangar => _mission.Hangar;

	private Es1TtmEntry[]? Entries => _backdrop.GroupTtms;

	/// <summary><c>Math_RandomBelow</c>(<c>Shell_RandomState</c>, <paramref name="bound"/>); a draw with no generator, before the data's load, is 0, an engine guard.</summary>
	private short RandomBelow(short bound) => (short)(_data.Random?.Below(bound) ?? 0);

	/// <summary>The handle of group script <paramref name="entry"/>; retail reads past the entries unchecked, none here, an engine guard.</summary>
	public byte Handle(int entry) => Entries is { } entries && entry >= 0 && entry < entries.Length ? entries[entry].Handle : Es1TtmManager.NoHandle;

	/// <summary>The HERC in the selected slot <c>Hangar_SelectedSlot</c> (<c>00080894</c>), or null for −1, an empty slot or no hangar.</summary>
	public Es1HercRecord? SelectedHerc => Hangar is { } hangar ? hangar.HercIn(hangar.SelectedSlot) : null;

	/// <summary>The chassis of the selected HERC, −1 for none, the type the hangar's tests read.</summary>
	public short SelectedHercType => SelectedHerc?.Chassis ?? -1;

	/// <summary>The diagram of the selected HERC, built the first time (<see cref="Es1HercDiagrams.Of"/>); null for none.</summary>
	public Es1HercDiagram? SelectedDiagram => Hangar is { } hangar && SelectedHerc is { } herc ? Diagrams.Of(hangar, hangar.SelectedSlot, herc) : null;

	/// <summary>
	/// <c>TtmEntry_AssignSlot</c> (<c>00012047</c>)(<paramref name="slot"/>, <paramref name="entry"/>): the slot stored in
	/// the entry's word <c>+1</c> when that is −99, answering true; otherwise false with nothing changed. Retail writes past
	/// the entries unchecked; such an entry answers false here, an engine guard.
	/// </summary>
	public bool AssignSlot(short slot, short entry) {
		if (Entries is not { } entries || entry < 0 || entry >= entries.Length || entries[entry].Slot != Es1TtmEntry.NoSlot) {
			return false;
		}

		entries[entry].Slot = slot;
		return true;
	}

	/// <summary><c>TtmEntry_FreeAll</c> (<c>0001208e</c>): every entry's word <c>+1</c> −99.</summary>
	public void FreeAll() {
		foreach (var entry in Entries ?? []) {
			entry.Slot = Es1TtmEntry.NoSlot;
		}
	}

	/// <summary><c>TtmEntry_FreeSlot</c> (<c>000120ba</c>)(<paramref name="slot"/>): −99 in every entry holding the slot.</summary>
	public void FreeSlot(short slot) {
		foreach (var entry in Entries ?? []) {
			if (entry.Slot == slot) {
				entry.Slot = Es1TtmEntry.NoSlot;
			}
		}
	}

	/// <summary>
	/// <c>HangarBay_AssignSlotScripts</c> (<c>000120f9</c>)(<paramref name="slot"/>, <paramref name="mode"/>)
	/// (docs/es1/shell/repair-and-build.md#the-hangars-selection): the slot given its scripts and its two page words, by
	/// mode, the draws on <c>Shell_RandomState</c> in this order. 0: entry 6; <see cref="FirstPages"/> a draw below 5,
	/// <see cref="SecondPages"/> 2. 2: entry slot + 9; the first page 3 plus a draw below 2 for slot 0 and a draw below 5
	/// for any other, the second a draw below 5 for the slot's chassis below 5 (−1 for none) and below 3 for the others.
	/// 1: the slot's entries freed; with no marked HERC, or the selected HERC 0% built
	/// (<see cref="SelectedBuildProgress"/>), entry 8 and the first page 3 plus a draw below 2; otherwise entry 7, while the
	/// selected HERC is below 51% built the entry <see cref="BuildingEntries"/> names for its chassis, and the first page a
	/// draw below 3; then the second page a draw below 5. Any other mode does nothing. Retail writes the page words of a
	/// slot past the eight unchecked; such a slot's words are not kept here, an engine guard, its draws still made.
	/// </summary>
	public void AssignSlotScripts(short slot, short mode) {
		switch (mode) {
			case 0:
				AssignSlot(slot, 6);
				SetPage(FirstPages, slot, RandomBelow(5));
				SetPage(SecondPages, slot, 2);
				break;
			case 2:
				AssignSlot(slot, (short)(slot + 9));
				SetPage(FirstPages, slot, slot == 0 ? (short)(RandomBelow(2) + 3) : RandomBelow(5));
				short chassis = Hangar?.HercIn(slot)?.Chassis ?? -1;
				SetPage(SecondPages, slot, RandomBelow(chassis < 5 ? (short)5 : (short)3));
				break;
			case 1:
				FreeSlot(slot);
				if (Hangar?.HasMarkedHerc() != true || SelectedBuildProgress() == 0) {
					AssignSlot(slot, 8);
					SetPage(FirstPages, slot, (short)(RandomBelow(2) + 3));
				} else {
					AssignSlot(slot, 7);
					if (SelectedBuildProgress() < BuildingBelow) {
						short type = SelectedHercType;
						AssignSlot(slot, type is >= 0 and < Es1HercArt.ChassisCount ? BuildingEntries[type] : (short)0);
					}

					SetPage(FirstPages, slot, RandomBelow(3));
				}

				SetPage(SecondPages, slot, RandomBelow(5));
				break;
		}

		static void SetPage(short[] pages, short slot, short page) {
			if (slot >= 0 && slot < pages.Length) {
				pages[slot] = page;
			}
		}
	}

	/// <summary>
	/// <c>Hangar_SelectedBuildProgress</c> (<c>0002a2a1</c>): (<i>c</i> − <see cref="Es1Hangar.MarkedSlotWord"/>) × 100 /
	/// <i>c</i>, <i>c</i> the word of <c>Chassis_BuildMissions</c> (<c>000808d8</c>) for the selected HERC's chassis.
	/// Retail reads the word before the table for no HERC, and past it for a chassis past 7; either answers 0 here, an
	/// engine guard.
	/// </summary>
	public short SelectedBuildProgress() {
		short chassis = SelectedHercType;
		if (Hangar is not { } hangar || chassis is < 0 or >= Es1HercArt.ChassisCount) {
			return 0;
		}

		int missions = Es1CareerAfterMission.MarkedHercMissions[chassis];
		return (short)((missions - hangar.MarkedSlotWord) * 100 / missions);
	}

	/// <summary>
	/// <c>HangarBay_AssignScripts</c> (<c>000122e0</c>) (docs/es1/shell/hangar.md#giving-the-scripts-their-bays), which
	/// <see cref="Es1ShellBackdrop.Show"/> runs for group 4000: every entry freed (<see cref="FreeAll"/>) and the slot of the
	/// current record's player entry taken; entries 0 to 3 given 99, and entry 4 when <c>Mission_Kind</c> is 4, in
	/// <c>Shell_Mode</c> 0 at once and in any other only while the roster's current record's byte <c>+1</c> is set; for
	/// each of the hangar's HERCs the selected slot stepped (<see cref="Es1Hangar.NextHercSlot"/>) and given its scripts,
	/// mode 0 for the player's slot and 2 for any other; the player's entry made the current pilot
	/// (<see cref="Es1CareerRecord.SelectPilot"/>), which puts the selection back on its slot; and the scripts run
	/// (<see cref="RunScripts"/>). Retail reads the player's entry with no bounds test, and the hangar and the record as
	/// they lie; an entry past the four reads slot −1, and with no record or hangar only the scripts run, engine guards.
	/// </summary>
	public IEnumerable<Es1ShellWait> AssignScripts() {
		FreeAll();
		if (_mission.CurrentRecord is { } record && Hangar is { } hangar) {
			short playerSlot = PlayerSlot(record);
			for (short entry = 0; entry < 4; entry++) {
				AssignSlot(Es1TtmEntry.Always, entry);
			}

			if (_mission.MissionKind == 4 && (_mission.Mode == 0 || _mission.Roster.CurrentRecord.Byte1 != 0)) {
				AssignSlot(Es1TtmEntry.Always, 4);
			}

			for (int i = 0; i < hangar.HercCount; i++) {
				hangar.NextHercSlot();
				AssignSlotScripts(hangar.SelectedSlot, hangar.SelectedSlot == playerSlot ? (short)0 : (short)2);
			}

			record.SelectPilot(record.PlayerEntry, hangar);
		}

		foreach (var wait in RunScripts()) {
			yield return wait;
		}
	}

	/// <summary>The slot <c>+8</c> of <paramref name="record"/>'s player entry <c>+0xe</c>; −1 for an entry past the four, an engine guard.</summary>
	private static short PlayerSlot(Es1CareerRecord record) =>
		record.PlayerEntry is >= 0 and < Es1CareerRecord.PilotCount ? record.Pilots[record.PlayerEntry].Slot : (short)-1;

	/// <summary>
	/// <c>ShellBackdrop_PlayAllScripts</c> (<c>0001240b</c>) (docs/es1/shell/hangar.md#running-the-scripts): each group
	/// script set playing (<c>Ttm_SetMode</c>(0)) and run once (<c>Ttm_RunScript</c>).
	/// </summary>
	public IEnumerable<Es1ShellWait> PlayAllScripts() {
		var scripts = _scripts();
		foreach (var entry in Entries ?? []) {
			scripts?.SetMode(Es1TtmPlayer.Playing, entry.Handle);
			foreach (var wait in Run(entry.Handle)) {
				yield return wait;
			}
		}
	}

	/// <summary>
	/// <c>ShellBackdrop_RunAllScripts</c> (<c>000126c9</c>): each group script run once, no mode set. <c>es2_xref.py</c>
	/// finds no caller (hangar.md's Open).
	/// </summary>
	public IEnumerable<Es1ShellWait> RunAllScripts() {
		foreach (var entry in Entries ?? []) {
			foreach (var wait in Run(entry.Handle)) {
				yield return wait;
			}
		}
	}

	/// <summary>
	/// <c>ShellBackdrop_RunScripts</c> (<c>00012467</c>) (docs/es1/shell/hangar.md#running-the-scripts): by the group
	/// <c>Shell_BackdropId</c> / 1000 × 1000, each group script set playing or stopped (<c>Ttm_SetMode</c>(0 or 2)) and run
	/// once: 3000, for <i>k</i> 0 to 3 the pair 2<i>k</i>, 2<i>k</i> + 1 both set, then both run, playing when <i>k</i> is
	/// the current record's campaign word in <c>Shell_Mode</c> 1 and 0 in any other mode; 5000, script
	/// <c>Shell_BackdropId</c> mod 1000 playing; 4000, each script whose slot word is the selected slot or 99 playing; any
	/// other <see cref="PlayAllScripts"/>. Retail reads past the entries for a group 3000 list shorter than eight; such an
	/// entry has no script here, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> RunScripts() {
		var scripts = _scripts();
		short backdrop = _backdropId();
		int group = backdrop / 1000 * 1000;
		var entries = Entries ?? [];
		switch (group) {
			case 3000:
				short campaign = _mission.Mode == 1 ? _backdrop.CampaignWord : (short)0;
				for (short k = 0; k < 4; k++) {
					int mode = k == campaign ? Es1TtmPlayer.Playing : Es1TtmPlayer.Hidden;
					scripts?.SetMode(mode, Handle(2 * k));
					scripts?.SetMode(mode, Handle(2 * k + 1));
					foreach (var wait in Run(Handle(2 * k)).Concat(Run(Handle(2 * k + 1)))) {
						yield return wait;
					}
				}

				break;
			case 5000:
				int playing = backdrop % 1000;
				for (int i = 0; i < entries.Length; i++) {
					scripts?.SetMode(i == playing ? Es1TtmPlayer.Playing : Es1TtmPlayer.Hidden, entries[i].Handle);
					foreach (var wait in Run(entries[i].Handle)) {
						yield return wait;
					}
				}

				break;
			case 4000:
				for (int i = 0; i < entries.Length; i++) {
					short slot = entries[i].Slot;
					bool shown = (Hangar != null && slot == Hangar.SelectedSlot) || slot == Es1TtmEntry.Always;
					scripts?.SetMode(shown ? Es1TtmPlayer.Playing : Es1TtmPlayer.Hidden, entries[i].Handle);
					foreach (var wait in Run(entries[i].Handle)) {
						yield return wait;
					}
				}

				break;
			default:
				foreach (var wait in PlayAllScripts()) {
					yield return wait;
				}

				break;
		}
	}

	/// <summary><c>Ttm_RunScript</c>(<paramref name="handle"/>), nothing without the scripts.</summary>
	private IEnumerable<Es1ShellWait> Run(byte handle) => _scripts()?.RunScript(handle) ?? [];

	/// <summary>
	/// <c>Herc_DrawInBay</c> (<c>000241f9</c>)(<paramref name="herc"/>, <paramref name="remaining"/>)
	/// (docs/es1/shell/hangar.md#drawing-the-herc-into-its-bay): the body drawn, the damage marks
	/// (<see cref="Es1HercDiagrams.DrawDamageMarks"/>), one run of group script 1, the mounts
	/// (<see cref="Es1HercDiagrams.DrawMounts"/>), and one run each of scripts 0 and 2. With <c>Shell_LowMemory</c>
	/// (<c>0007fd09</c>) set it first builds the diagram with <paramref name="remaining"/>; this engine never sets that
	/// byte, so the diagram drawn is the one the hangar keeps (<see cref="Es1HercDiagrams.Of"/>).
	/// </summary>
	public IEnumerable<Es1ShellWait> DrawInBay(Es1HercRecord herc, short slot, short remaining) {
		_ = remaining;
		var diagram = Hangar is { } hangar ? Diagrams.Of(hangar, slot, herc) : null;
		diagram?.Body.Draw(_surface);
		Diagrams.DrawDamageMarks(herc, _surface);
		foreach (var wait in Run(Handle(1))) {
			yield return wait;
		}

		Diagrams.DrawMounts(herc, _surface);
		foreach (var wait in Run(Handle(0)).Concat(Run(Handle(2)))) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Herc_EraseFromBay</c> (<c>000241cd</c>)(<paramref name="herc"/>): the mounts erased
	/// (<see cref="Es1HercDiagrams.EraseMounts"/>) and the body. With <c>Shell_LowMemory</c> set it then frees the
	/// diagram, which this engine never does.
	/// </summary>
	public void EraseFromBay(Es1HercRecord herc, short slot) {
		var diagram = Hangar is { } hangar ? Diagrams.Of(hangar, slot, herc) : null;
		Diagrams.EraseMounts(herc, _surface);
		diagram?.Body.Erase(_surface);
	}

	/// <summary><c>Hangar_LeaveSelectedHerc</c> (<c>000295e7</c>): the selected HERC, when there is one, taken out of the bay (<see cref="EraseFromBay"/>).</summary>
	public void LeaveSelectedHerc() {
		if (Hangar is { } hangar && hangar.HercIn(hangar.SelectedSlot) is { } herc) {
			EraseFromBay(herc, hangar.SelectedSlot);
		}
	}

	/// <summary>
	/// <c>Hangar_ShowSelectedHerc</c> (<c>000294d8</c>): for a selected slot other than −1, group scripts 0, 1 and 2 moved
	/// to the pages <see cref="FirstPages"/>[slot] + 1, <see cref="SecondPages"/>[slot] + 1 and slot mod 8 + 1
	/// (<c>Ttm_SetPage</c>), and the slot's HERC, when there is one, drawn into the bay (<see cref="DrawInBay"/>) with the
	/// marked slot's word for the marked slot and 0 for any other; then the scripts run (<see cref="RunScripts"/>). Retail
	/// reads the page words of a slot past the eight unchecked; such a slot reads page 1 here, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> ShowSelectedHerc() {
		if (Hangar is { } hangar && hangar.SelectedSlot != -1) {
			short slot = hangar.SelectedSlot;
			var scripts = _scripts();
			bool kept = slot >= 0 && slot < PageSlots;
			scripts?.SetPage((short)((kept ? FirstPages[slot] : 0) + 1), Handle(0));
			scripts?.SetPage((short)((kept ? SecondPages[slot] : 0) + 1), Handle(1));
			scripts?.SetPage((short)(slot % 8 + 1), Handle(2));
			if (hangar.HercIn(slot) is { } herc) {
				foreach (var wait in DrawInBay(herc, slot, slot == hangar.MarkedSlot ? hangar.MarkedSlotWord : (short)0)) {
					yield return wait;
				}
			}
		}

		foreach (var wait in RunScripts()) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Hangar_ScrapSelectedHerc</c> (<c>0002a046</c>) (docs/es1/shell/between-missions.md#lost-squadmates): nothing
	/// unless the selected slot holds a HERC. The marked slot's HERC is scrapped
	/// (<see cref="Es1CareerAfterMission.ScrapToSalvage"/>) with <see cref="Es1Hangar.Dword"/> × 1000 and any other with
	/// 0, its frame's value; the HERC is taken out of the bay (<see cref="LeaveSelectedHerc"/>); for any other slot the slot
	/// of the pilot <see cref="Es1CareerRecord.FindPilotBySlot"/> finds for it is set to −1; the HERC is destroyed
	/// (<c>Herc_Dtor</c> (<c>000232a4</c>)(HERC, 3), which frees its diagram, <see cref="Es1HercDiagrams.Destroy"/>), the
	/// slot emptied and the HERC count down 1. Then, for the marked slot, <see cref="Es1Hangar.MarkedSlotWord"/> and
	/// <see cref="Es1Hangar.Dword"/> are set to 0 and the slot given its scripts (<see cref="AssignSlotScripts"/>(slot, 1)),
	/// the marked slot keeping its number and the selection staying on it; for any other the selection steps on
	/// (<see cref="Es1Hangar.NextHercSlot"/>). Both close with the screen wiped to color 0
	/// (<see cref="Es1ScreenBlinds.Clear"/>(8, 0)) and the selection shown (<see cref="ShowSelectedHerc"/>). Retail reads
	/// <c>CareerRecord_Current</c> with no test; with no record no pilot is changed here, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> ScrapSelectedHerc() {
		if (Hangar is not { } hangar || hangar.HercIn(hangar.SelectedSlot) is not { } herc) {
			yield break;
		}

		short slot = hangar.SelectedSlot;
		bool marked = slot == hangar.MarkedSlot;
		new Es1CareerAfterMission(_mission, _data).ScrapToSalvage(herc, marked ? hangar.Dword * 1000 : 0);
		LeaveSelectedHerc();
		if (!marked && _mission.CurrentRecord is { } record && record.FindPilotBySlot(slot) is var entry and not -1) {
			record.Pilots[entry].Slot = -1;
		}

		_data.HercDiagrams.Destroy(herc);
		hangar.Slots[slot] = null;
		hangar.HercCount--;
		if (marked) {
			hangar.MarkedSlotWord = 0;
			hangar.Dword = 0;
			AssignSlotScripts(slot, 1);
		} else {
			hangar.NextHercSlot();
		}

		foreach (var wait in _blinds.Clear(8, 0)) {
			yield return wait;
		}

		foreach (var wait in ShowSelectedHerc()) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Hangar_ShowSlot</c> (<c>00029651</c>)(<paramref name="slot"/>) (docs/es1/shell/hangar.md#showing-a-bay): the
	/// selected HERC taken out (<see cref="LeaveSelectedHerc"/>); the marked slot −1 when it is the selected slot and that
	/// holds no HERC; for slot −1 the first empty slot (<see cref="Es1Hangar.FindFreeSlot"/>), and with none the selection
	/// stepped to a HERC no pilot holds (<see cref="Es1Hangar.NextUnpilotedHercSlot"/>) and taken; the slot selected and
	/// shown (<see cref="ShowSelectedHerc"/>); then no mount selected (<see cref="SelectMount"/>(−1)). With no hangar
	/// nothing is shown here, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> ShowSlot(short slot) {
		if (Hangar is not { } hangar) {
			yield break;
		}

		LeaveSelectedHerc();
		if (hangar.SelectedSlot == hangar.MarkedSlot && hangar.HercIn(hangar.SelectedSlot) == null) {
			hangar.MarkedSlot = -1;
		}

		if (slot == -1) {
			slot = hangar.FindFreeSlot();
		}

		if (slot == -1) {
			if (_mission.CurrentRecord is { } record) {
				hangar.NextUnpilotedHercSlot(record);
			}

			slot = hangar.SelectedSlot;
		}

		hangar.SelectedSlot = slot;
		foreach (var wait in ShowSelectedHerc()) {
			yield return wait;
		}

		SelectMount(-1);
	}

	/// <summary>
	/// <c>Shell_DrawScreen</c>'s HERC diagram (docs/es1/shell/menus.md#drawing-a-screen): for a new backdrop 4000
	/// (<paramref name="newBackdrop"/>), <see cref="PlayAllScripts"/> and the player's slot shown
	/// (<see cref="ShowSlot"/>); on screens 1700 and 1710 with no new backdrop the same slot shown, between
	/// <c>Screen_BlindsClear</c>(8, 0, 1) and <c>Screen_BlindsCopy</c>(8, the visible page, the draw page, 1) when the
	/// record's current pilot <c>+0x12</c> is not its player entry <c>+0xe</c> and the message box is not up
	/// (<paramref name="messageBoxUp"/>); then, either way, the player's entry made the current pilot
	/// (<see cref="Es1CareerRecord.SelectPilot"/>). With no record nothing is drawn here, an engine guard.
	/// </summary>
	public IEnumerable<Es1ShellWait> ShowPlayerBay(bool newBackdrop, bool messageBoxUp) {
		if (newBackdrop) {
			foreach (var wait in PlayAllScripts()) {
				yield return wait;
			}
		}

		if (_mission.CurrentRecord is not { } record) {
			yield break;
		}

		bool blinds = !newBackdrop && record.CurrentPilot != record.PlayerEntry && !messageBoxUp;
		if (blinds) {
			foreach (var wait in _blinds.Clear(8, 0)) {
				yield return wait;
			}
		}

		foreach (var wait in ShowSlot(PlayerSlot(record))) {
			yield return wait;
		}

		if (blinds) {
			var display = _surface.Display;
			foreach (var wait in _blinds.Copy(8, display.VisiblePage, display.DrawPage)) {
				yield return wait;
			}
		}

		if (Hangar is { } hangar) {
			record.SelectPilot(record.PlayerEntry, hangar);
		}
	}

	/// <summary>
	/// <c>Hangar_SelectNext</c> (<c>0002986f</c>)(<paramref name="mode"/>) (docs/es1/shell/repair-and-build.md#the-hangars-selection):
	/// the selected HERC taken out; by mode, 0 <see cref="Es1Hangar.NextHercSlot"/>, 1 <see cref="Es1CareerRecord.NextPilot"/>
	/// of the roster's current record, 2 <see cref="Es1Hangar.NextUnpilotedHercSlot"/>, 3
	/// <see cref="Es1Hangar.SelectMarkedSlot"/> and, with a marked slot then, <see cref="AssignSlotScripts"/>(it, 1); then the
	/// screen wiped to color 0 (<see cref="Es1ScreenBlinds.Clear"/>(8, 0)), the new HERC shown
	/// (<see cref="ShowSelectedHerc"/>), no mount selected and the picture wiped in (<see cref="Es1ScreenBlinds.Copy"/>(8,
	/// the visible page, the draw page)).
	/// </summary>
	public IEnumerable<Es1ShellWait> SelectNext(short mode) {
		LeaveSelectedHerc();
		if (Hangar is { } hangar) {
			switch (mode) {
				case 0:
					hangar.NextHercSlot();
					break;
				case 1:
					_mission.Roster.CurrentRecord.NextPilot(hangar);
					break;
				case 2:
					if (_mission.CurrentRecord is { } record) {
						hangar.NextUnpilotedHercSlot(record);
					}

					break;
				case 3:
					hangar.SelectMarkedSlot();
					if (hangar.MarkedSlot != -1) {
						AssignSlotScripts(hangar.MarkedSlot, 1);
					}

					break;
			}
		}

		foreach (var wait in WipeToSelected()) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Hangar_SelectPrev</c> (<c>00029946</c>)(<paramref name="mode"/>): <see cref="SelectNext"/> stepping back for modes 0
	/// to 2 (<see cref="Es1Hangar.PrevHercSlot"/>, <see cref="Es1CareerRecord.PrevPilot"/> of the roster's current record,
	/// <see cref="Es1Hangar.PrevUnpilotedHercSlot"/>), with no mode 3, and wiping and showing only when the selected slot
	/// is then not −1.
	/// </summary>
	public IEnumerable<Es1ShellWait> SelectPrev(short mode) {
		LeaveSelectedHerc();
		if (Hangar is not { } hangar) {
			yield break;
		}

		switch (mode) {
			case 0:
				hangar.PrevHercSlot();
				break;
			case 1:
				_mission.Roster.CurrentRecord.PrevPilot(hangar);
				break;
			case 2:
				if (_mission.CurrentRecord is { } record) {
					hangar.PrevUnpilotedHercSlot(record);
				}

				break;
		}

		if (hangar.SelectedSlot != -1) {
			foreach (var wait in WipeToSelected()) {
				yield return wait;
			}
		}
	}

	/// <summary><c>Hangar_SelectNext</c>'s and <c>Hangar_SelectPrev</c>'s close: the wipe out, the HERC shown, no mount selected, the wipe in.</summary>
	private IEnumerable<Es1ShellWait> WipeToSelected() {
		foreach (var wait in _blinds.Clear(8, 0)) {
			yield return wait;
		}

		foreach (var wait in ShowSelectedHerc()) {
			yield return wait;
		}

		SelectMount(-1);
		var display = _surface.Display;
		foreach (var wait in _blinds.Copy(8, display.VisiblePage, display.DrawPage)) {
			yield return wait;
		}
	}

	/// <summary>
	/// <c>Hangar_SelectMount</c> (<c>00029a14</c>)(<paramref name="mount"/>): on the selected HERC, when there is one,
	/// <see cref="Es1HercDiagrams.HighlightMount"/>(HERC, mount).
	/// </summary>
	public void SelectMount(short mount) {
		if (SelectedHerc is { } herc && SelectedDiagram != null) {
			Diagrams.HighlightMount(herc, mount, _surface);
		}
	}

	/// <summary><c>Hangar_NextMount</c> (<c>00029a52</c>): on the selected HERC, when there is one, <see cref="Es1HercDiagrams.HighlightNextMount"/>.</summary>
	public void NextMount() {
		if (SelectedHerc is { } herc && SelectedDiagram != null) {
			Diagrams.HighlightNextMount(herc, _surface);
		}
	}

	/// <summary><c>Hangar_PrevMount</c> (<c>00029a87</c>): on the selected HERC, when there is one, <see cref="Es1HercDiagrams.HighlightPreviousMount"/>.</summary>
	public void PrevMount() {
		if (SelectedHerc is { } herc && SelectedDiagram != null) {
			Diagrams.HighlightPreviousMount(herc, _surface);
		}
	}

	/// <summary><c>Hangar_ClearSelectedMount</c> (<c>0002961c</c>): on the selected HERC, when there is one, <see cref="Es1HercDiagrams.HideMountHighlight"/>.</summary>
	public void ClearSelectedMount() {
		if (SelectedHerc is { } herc && SelectedDiagram != null) {
			Diagrams.HideMountHighlight(herc, _surface);
		}
	}

	/// <summary>The condition an empty mount's unit counts as when <see cref="FitWeapon"/> fits 17, 100.</summary>
	private const short EmptyMountUnit = 100;

	/// <summary>
	/// <c>Hangar_FitWeapon</c> (<c>00029abc</c>)(<paramref name="weapon"/>) (docs/es1/shell/arming.md#fitting-a-weapon), on
	/// the selected HERC's current mount and the stock <c>[000aad62]</c>, the roster's stock 0: nothing when the mount
	/// carries the weapon already; the incoming unit 100 for 17 and otherwise <see cref="Es1WeaponStock.Take"/> of the
	/// weapon's stack, nothing when that is −1; the mounts erased; unless the mount was empty, its condition
	/// (<see cref="Es1HercDiagrams.CurrentMountStatus"/>) pushed onto the old weapon's stack
	/// (<see cref="Es1WeaponStock.Add"/>); the mount's condition set to the unit and its weapon to the weapon
	/// (<see cref="Es1HercDiagrams.SetCurrentMountWeapon"/>, which makes it 17 for a unit of 0); the mounts drawn. Retail
	/// reads the selected slot's HERC before testing it and pops the unit before it finds the slot empty; with no HERC
	/// nothing is done here, an engine guard.
	/// </summary>
	public void FitWeapon(short weapon) {
		if (SelectedHerc is not { } herc || SelectedDiagram == null) {
			return;
		}

		short current = Diagrams.CurrentMountWeapon(herc);
		if (current == weapon) {
			return;
		}

		var stock = _mission.Roster.Stocks[0];
		short unit = weapon == Es1HercArt.EmptyMount ? EmptyMountUnit : stock.Take(weapon);
		if (unit == -1) {
			return;
		}

		Diagrams.EraseMounts(herc, _surface);
		if (current != Es1HercArt.EmptyMount) {
			stock.Add(current, Diagrams.CurrentMountStatus(herc));
		}

		Diagrams.SetCurrentMountStatus(herc, unit);
		Diagrams.SetCurrentMountWeapon(herc, weapon);
		Diagrams.DrawMounts(herc, _surface);
	}

	/// <summary>The entry of the file-name table <c>0009afb8</c> that names <c>car\sm_play.mec</c>.</summary>
	private const int SavedHangarEntry = 10;

	/// <summary>
	/// <c>Hangar_ReloadSelectedLoadout</c> (<c>00028ec9</c>) (docs/es1/shell/repair-and-build.md#the-hangars-selection):
	/// nothing in <c>Shell_Mode</c> 1; otherwise the file-name table's entry 10, <c>car\sm_play.mec</c>, through
	/// <c>Path_ApplyAltDrive</c>, opened as a <c>FileRStream</c> and read with <see cref="RefitSelectedFrom"/>.
	/// <c>es2_xref.py</c> finds no caller (repair-and-build.md's Open). Retail reads a file that does not open from the
	/// failed stream; with no table, no entry or a file that does not read nothing is done here, an engine guard.
	/// </summary>
	public void ReloadSelectedLoadout() {
		if (_mission.Mode == 1 || _data.FileNames is not { } names || SavedHangarEntry >= names.Length) {
			return;
		}

		string path = Es1ShellData.InstallPath(_mission.InstallRoot, names[SavedHangarEntry]);
		if (File.Exists(path) && new Es1HercListTransformer().Parse(File.ReadAllBytes(path)) is { } list) {
			RefitSelectedFrom(list);
		}
	}

	/// <summary>
	/// <c>HercList_RefitSelectedFromStream</c> (<c>00028f16</c>)(stream), over the HERC list the stream holds: for every
	/// HERC whose slot is <c>Hangar_SelectedSlot</c>, read as it stands in the file into a HERC of its own, each mount below
	/// its count selected (<see cref="SelectMount"/>) and given that HERC's weapon (<c>Herc_MountWeapon</c>,
	/// <see cref="FitWeapon"/>); the other HERCs are passed over.
	/// </summary>
	public void RefitSelectedFrom(Es1HercList list) {
		if (Hangar is not { } hangar) {
			return;
		}

		foreach (var entry in list.Hercs) {
			if (entry.Slot != hangar.SelectedSlot) {
				continue;
			}

			for (short mount = 0; mount < entry.Herc.Weapons.Length; mount++) {
				SelectMount(mount);
				FitWeapon(entry.Herc.Weapons[mount]);
			}
		}
	}
}
