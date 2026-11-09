using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Gau;
using Herculan.Es1.EnergyShields;
using Herculan.Es1.Gunsight;
using Herculan.Es1.Hdd;
using Herculan.Es1.Input;
using Herculan.Es1.Messages;
using Herculan.Es1.Mfd;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.Throttle;
using Herculan.Es1.Ttm;
using Herculan.Es1.WeaponRows;

namespace Herculan.Es1.View;

/// <summary>
/// The cockpit object, <c>panel.cpp</c>'s <c>0x23d</c> bytes (docs/es1/simulation/cockpit-panel.md#the-cockpit-object), as
/// far as the view keys, the mouse and the cursors read it: <c>CockpitView_Ctor</c> (<c>ES1DBSIM 0002ae5c</c>), the
/// state <c>ColorSchemePanels_LoadAll</c> leaves and <c>Cockpit_LoadPanel</c>'s root widget and canvas context; the
/// widgets-off byte and the view device's button latch, with <c>CockpitWidgets_HandleCommand</c> (<c>0002d24c</c>) and
/// <c>CockpitView_PollViewDevice</c> (<c>0002d1a0</c>) (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device);
/// and the screen-edge strips and the cursor slots (docs/es1/simulation/cockpit-panel.md#the-screen-edges-and-the-cursors),
/// with <c>Cockpit_DrawFrame</c>'s steps that reach them; the weapon rows <c>+0x70</c>, the pod row <c>+0x1d8</c> and
/// the console buttons <c>+0x1dd</c> (docs/es1/simulation/cockpit-panel.md#the-weapon-rows,
/// docs/es1/simulation/cockpit-weapon-gauges.md); the energy meter <c>+0x1e1</c> and the shield display <c>+0x1e5</c>
/// (docs/es1/simulation/cockpit-energy-shields.md); the MFD <c>+0x1e9</c> (docs/es1/simulation/mfd-display.md); the
/// throttle gauge <c>+0x1ed</c> and the roving gunsight
/// <c>+0x1f1</c> with the canvas context they draw through and <c>Widget_DrawToCockpit</c>
/// (docs/es1/simulation/cockpit-panel.md#drawing-it); the clock <c>+0x1f9</c> and the <c>.TTM</c> manager
/// <c>CockpitTtmManager</c> (docs/es1/formats/ttm-scripts.md#the-simulator); the pilot channel <c>+0x203</c>
/// (<see cref="Es1PilotChannel"/>) and the computer's ticker <c>+0x207</c> (<see cref="Es1MessageTicker"/>) with the
/// squadmates the channel reads (docs/es1/simulation/cockpit-panel.md#squadmate-and-pilot-helpers); and the target
/// selection <c>+0x20c</c> (<see cref="Es1TargetSelection"/>); and the Heads-Down Display <c>+0x1f5</c>
/// (docs/es1/simulation/heads-down-display.md), with <c>Squad_SendOrderToSlot</c>, its <c>XMIT</c>'s.
///
/// <para><see cref="Es1ViewManager"/> builds it, as <c>CockpitViewManager_LoadViews</c> does, and it is
/// <c>CockpitViewInstance</c> (<c>00092d1c</c>), the first cockpit built.</para>
/// </summary>
public sealed class Es1Cockpit {
	/// <summary>The command codes <see cref="HandleCommand"/> takes by name: set-1 scancodes.</summary>
	public const ushort Esc = 0x01, Tab = 0x0f, R = 0x13, F1 = 0x3b, F6 = 0x40, F7 = 0x41, F8 = 0x42, F9 = 0x43, F10 = 0x44,
		F11 = 0x57;

	/// <summary>The number keys' codes, <c>1</c> (2) to <c>9</c> (10), rows 0 to 8.</summary>
	public const ushort Key1 = 0x02, Key9 = 0x0a;

	/// <summary>The screen's size, the strips' full-screen shift: 320 across and 200 down.</summary>
	private const int ScreenWidth = Es1CockpitViews.ScreenWidth, ScreenHeight = Es1CockpitViews.ScreenHeight;

	private readonly Es1ViewManager _views;
	private readonly Es1CockpitMouse _mouse;

	/// <summary>
	/// <c>CockpitView_Ctor</c>(0, <paramref name="machine"/>), <c>ColorSchemePanels_LoadAll</c>'s cursors and clears
	/// (the widgets on, the forward slot active), and <c>Cockpit_LoadPanel</c> on the manager's <c>.GAU</c> layout as
	/// far as this engine builds it.
	/// </summary>
	/// <param name="views">The manager whose current view the keys read and whose commands they queue, <c>CockpitViewManager_Published</c>.</param>
	/// <param name="machine"><c>+0x1ff</c>, the manager's machine.</param>
	/// <param name="mouse">The cockpit's mouse, whose root the panel's root becomes.</param>
	/// <param name="globals">The simulation's globals, whose live object list the target selection walks.</param>
	/// <param name="preferences">The simulator's options, whose <see cref="Es1Preferences.MessagePortShowEnabled"/> the message boxes read.</param>
	internal Es1Cockpit(Es1ViewManager views, Es1Mech? machine, Es1CockpitMouse mouse, Es1SimGlobals globals,
			Es1Preferences preferences) {
		_views = views;
		_mouse = mouse;
		Machine = machine;
		Globals = globals;
		Preferences = preferences;
		HitShake = new Es1HitShake(views, globals);
		Targets = new Es1TargetSelection(this);
		Cursors = new Es1CockpitCursors(views.Files.Cursors, mouse.Cursor);
		MessageTables = new Es1MessageTables(views.Files.MessageStrings);
		globals.CockpitViewInstance ??= this;
		LoadPanel(views.Files);
	}

	/// <summary>The simulator's options, of which the message boxes read <see cref="Es1Preferences.MessagePortShowEnabled"/>.</summary>
	public Es1Preferences Preferences { get; }

	/// <summary><c>+0x203</c>, the pilot channel, which <c>Cockpit_LoadPanel</c> builds on the <c>.GAU</c> record + 1668.</summary>
	public Es1PilotChannel PilotChannel { get; private set; }

	/// <summary><c>+0x207</c>, the computer's ticker, which <c>Cockpit_LoadPanel</c> builds on the <c>.GAU</c> record + 1684.</summary>
	public Es1MessageTicker Ticker { get; private set; }

	/// <summary>The message boxes' string tables, globals in retail, loaded from the cockpit's files' <c>str\</c>.</summary>
	public Es1MessageTables MessageTables { get; }

	/// <summary>
	/// <c>g_SquadmateMachines</c> (<c>000bcc16</c>), a global: the three squadmates by comm-box slot, which
	/// <c>ColorSchemePanels_LoadAll</c> zeroes and <c>Cockpit_LoadSquadmatePilots</c> (<c>0002b308</c>) fills at the
	/// mission's start (docs/es1/simulation/cockpit-panel.md#the-squadmates, <see cref="LoadSquadmatePilots"/>).
	/// </summary>
	public Es1Mech?[] SquadmateMachines { get; } = new Es1Mech?[Es1MessageTables.PilotSlots];

	/// <summary>
	/// <c>CockpitSquadGroup</c> (<c>00092d6c</c>), a global: the player's machine's group, which
	/// <see cref="LoadSquadmatePilots"/> stores and <see cref="BroadcastOrder"/> reads; none before.
	/// </summary>
	public Es1MissionGroup? SquadGroup { get; private set; }

	/// <summary><c>SquadSlotColors</c> (<c>00092d14</c>), the four <c>int16</c> <c>HudColorTable_Get</c> reads.</summary>
	private static readonly short[] SquadSlotColors = [56, 59, 49, 0];

	/// <summary>
	/// <c>Squad_IndexOf</c> (<c>0002d5dc</c>)(<paramref name="subject"/>): the slot 0 to 2 of the subject in
	/// <see cref="SquadmateMachines"/>, or −1 for none or a null subject.
	/// </summary>
	public int SquadIndexOf(IEs1SimObject? subject) =>
		subject == null ? -1 : Array.FindIndex(SquadmateMachines, machine => ReferenceEquals(machine, subject));

	/// <summary>
	/// <c>Squad_PilotName</c> (<c>0002e2f4</c>)(<paramref name="slot"/>): <c>HddGauge_Name</c> (<c>0003e830</c>), the name
	/// <see cref="Es1HddCommBox.Name"/> of comm box <paramref name="slot"/> of <see cref="Es1SimGlobals.HddDisplayInstance"/>;
	/// none before a display is built or for a box with no pilot.
	/// </summary>
	public string? SquadPilotName(int slot) => Globals.HddDisplayInstance?.CommBoxes[slot].Name;

	/// <summary><c>HudColorTable_Get</c> (<c>0002e2e4</c>)(<paramref name="slot"/>): entry <paramref name="slot"/> of <c>SquadSlotColors</c>, a squadmate slot's color.</summary>
	public static int SquadSlotColor(int slot) => SquadSlotColors[slot];

	/// <summary><c>Pilot_PortraitOf</c> (<c>0002e2b4</c>)(<paramref name="pilot"/>): the pilot's portrait number, <paramref name="pilot"/> / 3, truncating.</summary>
	public static int PilotPortraitOf(int pilot) => pilot / 3;

	/// <summary><c>Pilot_VoiceBankOf</c> (<c>0002e2c4</c>)(<paramref name="portrait"/>): the voice bank, <paramref name="portrait"/> / 4 + 1, truncating, with 3 made 4.</summary>
	public static int PilotVoiceBankOf(int portrait) {
		int bank = portrait / 4 + 1;
		return bank == 3 ? 4 : bank;
	}

	/// <summary>
	/// <c>Cockpit_LoadSquadmatePilots</c> (<c>0002b308</c>)(cockpit), from <c>Sim_StartMission</c>'s step 13
	/// (docs/es1/simulation/cockpit-panel.md#the-squadmates): the group of the player's machine kept as
	/// <see cref="SquadGroup"/>; its members in order, up to the first null, put into <see cref="SquadmateMachines"/>
	/// past the cockpit's own machine until three are in; and for each, in slot order,
	/// <see cref="Es1HddDisplay.LoadPilotFrames"/> with its pilot index (<see cref="Es1Mech.PilotWord"/>). The player's
	/// machine is the first <c>Pool_Prev</c> reaches in <c>g_MechPool</c> with <c>+0x9d</c> set, the first such machine
	/// built, oldest first, which is the cockpit's machine, <c>LocalPlayerMech</c>. With no machine or group, where retail reads through
	/// a null pointer, no squadmate, an engine guard.
	/// </summary>
	public void LoadSquadmatePilots() {
		SquadGroup = Machine?.Group;
		int count = 0;
		if (SquadGroup != null) {
			foreach (var member in SquadGroup.Members) {
				if (count == SquadmateMachines.Length || member is not Es1Mech machine) {
					break;
				}

				if (!ReferenceEquals(machine, Machine)) {
					SquadmateMachines[count++] = machine;
				}
			}
		}

		for (int slot = 0; slot < count; slot++) {
			Hdd.LoadPilotFrames(SquadmateMachines[slot]!.PilotWord, slot);
		}
	}

	/// <summary>
	/// The in-mission panels' shared state, whose <c>AlertPanel_OpenCount</c> the message boxes' page copy reads; none,
	/// as before the host wires it, reads as no panel up.
	/// </summary>
	internal Es1AlertGlobals? Panels { get; set; }

	/// <summary><c>SimDisplay</c>, the display the cockpit draws on.</summary>
	internal Es1VideoMemory Display => _views.Display;

	/// <summary>The view manager's <c>+0x18</c>, set while a view change is staged.</summary>
	internal bool ViewChangeStaged => _views.Staged;

	/// <summary><c>+0x1ff</c>, the machine.</summary>
	public Es1Mech? Machine { get; }

	/// <summary><c>+0</c>, the root widget, built over the <c>.GAU</c> record's first 16 bytes.</summary>
	public Es1WidgetRoot Root { get; private set; }

	/// <summary>
	/// The canvas context's rect, <c>+0x210</c>–<c>+0x21c</c> of <c>+4</c>: the <c>.GAU</c> record's first 16 bytes, as
	/// <c>Cockpit_CreateCanvasContext</c> (<c>0002ad78</c>) sets it.
	/// </summary>
	public Es1Rect CanvasRect { get; private set; }

	/// <summary>
	/// The canvas context's origin, <c>+0x220</c>/<c>+0x224</c> of <c>+4</c>, which <see cref="TranslateForView"/> takes
	/// each view's move from. It starts as the display's render context's, which <c>GLViewport_Ctor</c> (<c>00069a84</c>)
	/// copies when the cockpit is built; this engine takes (0, 0), the origin its view manager's window holds before the
	/// first view is installed.
	/// </summary>
	public (int X, int Y) CanvasOrigin { get; private set; }

	/// <summary><c>+0x1e9</c>, the MFD, which <c>Gau_MfdPanelWidget</c> (<c>0002cdac</c>) builds on the <c>.GAU</c> block at 728.</summary>
	public Es1MfdDisplay Mfd { get; private set; }

	/// <summary><c>+0x1ed</c>, the throttle gauge, which <c>Gau_ThrottleWidget</c> (<c>0002cdf0</c>) builds on the <c>.GAU</c> block at 1000.</summary>
	public Es1ThrottleGauge Throttle { get; private set; }

	/// <summary><c>+0x1f1</c>, the roving gunsight, which <c>Gau_RovingGunsightWrapper</c> (<c>0002ce28</c>) builds on the <c>.GAU</c> block at 1104.</summary>
	public Es1Gunsight Gunsight { get; private set; }

	/// <summary><c>+0x1f5</c>, the Heads-Down Display, which <c>Gau_PilotRosterWidget</c> (<c>0002ce60</c>) builds on the <c>.GAU</c> block at 1212.</summary>
	public Es1HddDisplay Hdd { get; private set; }

	/// <summary><c>+0x1f9</c>, the clock, which <c>CockpitClock_Create</c> (<c>0002ce98</c>) builds on the <c>.GAU</c> block at 1604.</summary>
	public Es1CockpitClock Clock { get; private set; }

	/// <summary><c>CockpitTtmManager</c> (<c>00092d70</c>), the <c>.TTM</c> manager <c>Cockpit_LoadPanel</c> makes.</summary>
	public Es1TtmManager Scripts { get; private set; }

	/// <summary><c>+0x1dd</c>, the console buttons, which <c>Gau_WeaponButtonsWidget</c> (<c>0002ccd8</c>) builds on the <c>.GAU</c> block at 452.</summary>
	public Es1ConsoleButtons ConsoleButtons { get; private set; }

	/// <summary><c>+0x1e1</c>, the energy meter, which <c>Gau_EnergyMeterWidget</c> (<c>0002cd3c</c>) builds on the <c>.GAU</c> block at 548.</summary>
	public Es1EnergyMeter EnergyMeter { get; private set; }

	/// <summary><c>+0x1e5</c>, the shield display, which <c>Gau_ShieldDisplayWidget</c> (<c>0002cd74</c>) builds on the <c>.GAU</c> block at 616.</summary>
	public Es1ShieldDisplay Shields { get; private set; }

	/// <summary><c>+0x70</c>, the nine weapon rows' gauges by row, null where none is built.</summary>
	public IReadOnlyList<Es1WeaponRowGauge?> WeaponRows => _weaponRows;

	/// <summary><c>+0x94</c>, the weapon rows registered.</summary>
	public int WeaponRowCount { get; private set; }

	/// <summary>
	/// <c>+0x1d4</c>, the first weapon row registered while it was empty, whose key slot the key chain asks first.
	/// <c>Gau_WeaponButtonsWidget</c> first sets it to the cockpit's dword <c>+0x168</c>, a field nothing ported writes,
	/// which this engine reads as 0 (docs/es1/simulation/cockpit-panel.md#building-it).
	/// </summary>
	public Es1WeaponRowGauge? FirstWeaponRow { get; private set; }

	/// <summary><c>+0x1d8</c>, the pod row's gauge, the ECM's.</summary>
	public Es1PodGauge? PodRow { get; private set; }

	/// <summary><c>+0x23b</c>, the pod row's row, −1 for none.</summary>
	public short PodRowIndex { get; private set; } = -1;

	private readonly Es1WeaponRowGauge?[] _weaponRows = new Es1WeaponRowGauge?[Es1GauFile.WeaponRowSlots];

	/// <summary><c>STRINGS0.STR</c> group 2's <c>OFFLINE</c> (<c>000bd2c4</c>) and group 3's <c>ECM</c> (<c>000bd2c8</c>).</summary>
	private const int OfflineGroup = 2, EcmGroup = 3;

	/// <summary>
	/// <c>000b5bca</c>, a global: set while <c>Sim_StartMission</c> brings the cockpit up, from its step 13 to its step 15
	/// (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc), when <see cref="DrawToCockpit"/>
	/// copies nothing.
	/// </summary>
	public bool BringUp { get; set; }

	/// <summary>
	/// The rect <c>+0</c>–<c>+0xc</c> of the roving gunsight <c>+0x1f1</c>, the <c>.GAU</c> block at 1104's first 16
	/// bytes as <c>PanelGauge_Ctor</c> copies them (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight),
	/// which the gunsight drag tests the pointer against.
	/// </summary>
	public Es1Rect GunsightRect => Gunsight.Rect;

	/// <summary>The simulation's globals: the live object list the target selection walks, and the scan range's header word.</summary>
	public Es1SimGlobals Globals { get; }

	/// <summary>The target selection, <c>+0x20c</c>–<c>+0x214</c>, and the functions that walk it.</summary>
	public Es1TargetSelection Targets { get; }

	/// <inheritdoc cref="Es1TargetSelection.Selection"/>
	public IEs1SimObject? Selection => Targets.Selection;

	/// <summary>The view manager's current view <c>+0x10</c>, which the gunsight's update reads through <c>CockpitViewManager_Published</c>.</summary>
	internal int CurrentView => _views.CurrentView;

	/// <inheritdoc cref="Es1ViewManager.Projection"/>
	internal Es1ViewProjection? Projection => _views.Projection;

	/// <inheritdoc cref="Es1ViewManager.ShakeOffset"/>
	internal (int X, int Y) ShakeOffset => _views.ShakeOffset;

	/// <summary>The hit shake, which the frame's draw ticks and a hit on the player's cockpit starts.</summary>
	public Es1HitShake HitShake { get; }

	/// <summary>
	/// <c>View_GetActiveObject</c> (<c>00059280</c>): <c>ViewObjectPtr</c> (<c>000b5b8a</c>), the chain of views' current
	/// camera (<see cref="Es1ViewChain.Camera"/>); none before the chain is built.
	/// </summary>
	internal Es1Camera? ActiveViewObject => _views.Chain?.Camera;

	/// <inheritdoc cref="Es1ViewManager.SceneCanvas"/>
	internal Es1SceneCanvas SceneCanvas() => _views.SceneCanvas();

	/// <summary>The display library's cursor, which <c>Cursor_GetPosition</c> reads.</summary>
	internal Es1Cursor Cursor => _mouse.Cursor;

	/// <inheritdoc cref="Es1CockpitMouse.CanvasPoint"/>
	internal (int X, int Y) MouseCanvasPoint => _mouse.CanvasPoint;

	/// <inheritdoc cref="Es1CockpitMouse.DoubleClick"/>
	internal bool MouseDoubleClick => _mouse.DoubleClick;

	/// <summary>
	/// <c>Player_DependentCondition</c> (<c>0002e334</c>)(<paramref name="entry"/>)
	/// (docs/es1/simulation/cockpit-panel.md#squadmate-and-pilot-helpers): <see cref="Es1PaperDoll.ToConditionState"/> of
	/// the damage <c>Mech_ReadEntryDamage</c> (<c>00015868</c>)(the machine, entry, 0) reads (<see cref="Es1Mech.ReadEntryDamage"/>),
	/// the condition an instrument's feed is in. With no machine, where retail reads through a null pointer, intact, an
	/// engine guard.
	/// </summary>
	internal short DependentCondition(int entry) => Es1PaperDoll.ToConditionState(Machine?.ReadEntryDamage(entry, 0) ?? 0);

	/// <summary>
	/// <c>CockpitView_FindArmedMountGauge</c> (<c>0002e360</c>)(cockpit): the armed mount of the machine's weapon
	/// manager, none when its signed byte is negative, and the first of the first <see cref="WeaponRowCount"/> weapon-row
	/// slots that is set and whose mount it is; null for none. With no machine or manager, where retail reads through a
	/// null pointer, null, an engine guard.
	/// </summary>
	internal Es1WeaponRowGauge? FindArmedMountGauge() {
		if (Machine?.Mounts is not { } mounts) {
			return null;
		}

		int armed = (sbyte)mounts.Armed;
		var mount = armed >= 0 && armed < mounts.Mounts.Count ? mounts.Mounts[armed] : null;
		for (int i = 0; i < WeaponRowCount && i < _weaponRows.Length; i++) {
			if (_weaponRows[i] is { } gauge && ReferenceEquals(gauge.Mount, mount)) {
				return gauge;
			}
		}

		return null;
	}

	/// <summary>
	/// <c>Squad_BroadcastOrderFromCockpit</c> (<c>0002b448</c>)(cockpit, <paramref name="order"/>)
	/// (docs/es1/simulation/cockpit-panel.md#the-squadmates): the machine stored as the order's issuer, and
	/// <see cref="Es1MissionGroup.BroadcastOrder"/> of <see cref="SquadGroup"/> answered. With no group, where retail
	/// reads through a null pointer, false, an engine guard.
	/// </summary>
	internal bool BroadcastOrder(Es1SquadOrder order) {
		order.Issuer = Machine;
		return SquadGroup is { } group && group.BroadcastOrder(order, Globals);
	}

	/// <summary>
	/// <c>Squad_SendOrderToSlot</c> (<c>0002b3e8</c>)(cockpit, <paramref name="slot"/>, <paramref name="order"/>)
	/// (docs/es1/simulation/cockpit-panel.md#the-squadmates): for a squadmate in the slot that is not the machine and is
	/// not destroyed, the machine stored as the order's issuer and the squadmate's slot <c>+0x2c</c>
	/// (<see cref="Es1Mech.ReceiveSquadOrder"/>) with the reply 0; either way message
	/// <see cref="Es1HddDisplay.PilotSelectedMessage"/> about the squadmate withdrawn from the pilot channel.
	/// </summary>
	internal void SendOrderToSlot(int slot, Es1SquadOrder order) {
		var squadmate = SquadmateMachines[slot];
		if (squadmate != null && !ReferenceEquals(squadmate, Machine) && !squadmate.Destroyed) {
			order.Issuer = Machine;
			squadmate.ReceiveSquadOrder(0, order, Globals);
		}

		PilotChannel.Withdraw(Es1HddDisplay.PilotSelectedMessage, squadmate);
	}

	/// <summary><c>+0x216</c>, <c>+0x21a</c>, <c>+0x21e</c>, the bottom, left and right screen-edge strips, once built.</summary>
	public Es1ScrollTrigger? BottomStrip { get; private set; }

	/// <inheritdoc cref="BottomStrip"/>
	public Es1ScrollTrigger? LeftStrip { get; private set; }

	/// <inheritdoc cref="BottomStrip"/>
	public Es1ScrollTrigger? RightStrip { get; private set; }

	/// <summary><c>+0x222</c>–<c>+0x236</c>, the cursor slots.</summary>
	public Es1CockpitCursors Cursors { get; }

	/// <summary><c>+0x20b</c>, set while the widgets are off: in the external view, when every key the cockpit owns goes dead.</summary>
	public bool WidgetsOff { get; private set; }

	/// <summary><c>+0x23a</c>, set by <c>Cockpit_DrawFrame</c> once <c>CockpitView_BuildScrollTriggers</c> has run.</summary>
	public bool StripsBuilt { get; private set; }

	/// <summary><c>CockpitView_DeviceButtonLatch</c> (<c>00092f33</c>), a global: the mouse's <c>+0xb</c> has been pressed and not let go.</summary>
	public bool DeviceButtonLatch { get; private set; }

	/// <summary><c>CockpitView_DisableWidgets</c> (<c>0002b2d0</c>): <see cref="WidgetsOff"/> set.</summary>
	public void DisableWidgets() => WidgetsOff = true;

	/// <summary><c>CockpitView_EnableWidgets</c> (<c>0002b2ec</c>): <see cref="WidgetsOff"/> cleared.</summary>
	public void EnableWidgets() => WidgetsOff = false;

	/// <summary>
	/// <c>Cockpit_SkipFrameDraw</c> (<c>00092d7c</c>), a global: <see cref="DrawFrame"/> does nothing while it is set.
	/// <c>PreferencesPanel_Raise</c> sets it across one frame of its return to a cockpit view
	/// (docs/es1/simulation/cockpit-panel.md#drawing-it).
	/// </summary>
	public bool SkipFrameDraw { get; set; }

	/// <summary>
	/// <c>Cockpit_DrawFrame</c> (<c>0002cf28</c>)(cockpit), once a frame from <c>Sim_RenderFrame</c> before the 3D pass:
	/// nothing while <see cref="SkipFrameDraw"/> is set; the strips built the first time; at step 2 the clock's tick
	/// (<see cref="Es1CockpitClock.Tick"/>) and the hit shake's (<see cref="Es1HitShake.Tick"/>); with the widgets off the Heads-Down Display's (<see cref="Es1HddDisplay.Update"/>)
	/// and nothing more; otherwise, at step 4 the pilot channel's screen put back
	/// (<see cref="Es1PilotChannel.RestoreBackground"/>) while no view change is staged, at step 5 the per-frame slots of
	/// the weapon rows, the pod row, the console buttons, the energy meter, the shield display, the throttle gauge, the
	/// MFD and the Heads-Down Display inside the canvas push, at step 6 <see cref="RunTtmScripts"/>, the ticker's
	/// slot (<see cref="Es1MessageTicker.Update"/>) and the edge cursor, and at step 7 the selection dropped when
	/// <see cref="Es1TargetSelection.CanTarget"/> refuses it.
	/// </summary>
	public void DrawFrame() {
		if (SkipFrameDraw) {
			return;
		}

		// 1.
		if (!StripsBuilt) {
			BuildScrollTriggers();
			StripsBuilt = true;
		}

		// 2.
		Clock.Tick(Globals.Clock.Clock);
		HitShake.Tick();
		// 3.
		if (WidgetsOff) {
			Hdd.Update();
			return;
		}

		// 4.
		if (!ViewChangeStaged) {
			PilotChannel.RestoreBackground();
		}

		// 5. Between the canvas push and pop.
		foreach (var row in _weaponRows) {
			row?.Update();
		}

		PodRow?.Update();
		ConsoleButtons.Update();
		EnergyMeter.Update();
		Shields.Update();
		Throttle.Update();
		Mfd.Update();
		Hdd.Update();
		// 6.
		RunTtmScripts();
		Ticker.Update();
		Cursors.UpdateEdgeCursor();

		// 7.
		if (Targets.Selection is { } selection && !Targets.CanTarget(selection)) {
			Targets.Selection = null;
		}
	}

	/// <summary>
	/// <c>Cockpit_PushCanvasContext</c> (<c>0002b00c</c>): the canvas context, its rect and origin as they stand, over the
	/// display's draw page. The pop, <c>Cockpit_PopRenderContext</c>, has nothing to put back: the context is handed to
	/// what draws rather than installed.
	/// </summary>
	internal Es1PanelCanvas PushCanvasContext() => new(_views.Display, CanvasRect, CanvasOrigin);

	/// <summary>
	/// <c>CockpitView_RectCornerVisible</c> (<c>0002b214</c>)(<paramref name="rect"/>): false while the widgets are off;
	/// otherwise whether the corner (<i>x0</i>, <i>y0</i>) or (<i>x1</i>, <i>y1</i>) lies in the box whose corner is the
	/// canvas context's rect corner plus its origin, columns 0 to 319 and rows 0 to 200 from it, both bounds included.
	/// </summary>
	public bool RectCornerVisible(Es1Rect rect) {
		if (WidgetsOff) {
			return false;
		}

		int left = CanvasRect.X0 + CanvasOrigin.X, top = CanvasRect.Y0 + CanvasOrigin.Y;
		return Inside(rect.X0, rect.Y0) || Inside(rect.X1, rect.Y1);

		bool Inside(int x, int y) => left <= x && x <= left + ScreenWidth - 1 && top <= y && y <= top + ScreenHeight;
	}

	/// <summary>
	/// <c>Widget_DrawToCockpit</c> (<c>0002b058</c>)(0, <paramref name="rect"/>, <paramref name="which"/>), a widget's
	/// rect copied between the display's two pages (docs/es1/simulation/cockpit-panel.md#drawing-it): nothing, answering
	/// false, unless <see cref="RectCornerVisible"/> passes the rect and no view change is staged; <paramref name="which"/>
	/// 1 copies from <c>+0x8c</c>, the page drawn, to <c>+0x88</c>, the page on the screen, and answers false while the
	/// cockpit is being brought up (<see cref="BringUp"/>); any other value copies from <c>+0x88</c> to <c>+0x8c</c>.
	/// Inside the canvas push the source page is made the display's source page and the other its draw page, the rect
	/// copied between them as a type-0 region at the offset (0, 0), <c>Cockpit_ZeroOffset</c>, with the cursor taken
	/// off and drawn again around the copy (the driver's slots 31 and 30), and the draw page put back; the source page
	/// stays the one it set. So each such copy shows the cursor again when it was hidden.
	/// </summary>
	internal bool DrawToCockpit(Es1Rect rect, int which = 1) {
		if (_repaintingForward || !RectCornerVisible(rect) || _views.Staged || (which == 1 && BringUp)) {
			return false;
		}

		var display = _views.Display;
		var canvas = PushCanvasContext();
		int draw = display.DrawPage;
		display.SourcePage = which == 1 ? display.PairDrawn : display.PairVisible;
		display.DrawPage = which == 1 ? display.PairVisible : display.PairDrawn;
		display.Cursor.Hide();
		canvas.CopyRegion(rect, 0, 0);
		display.Cursor.Show();
		display.DrawPage = draw;
		return true;
	}

	/// <summary>
	/// <c>CockpitView_InvalidateWidgets</c> (<c>0002b4a0</c>)(cockpit), which <c>CockpitView_SetView</c> runs for views 0
	/// to 3: unless the widgets are off, the widgets' slot <c>+8</c>, of which the weapon rows', the pod row's, the
	/// console buttons', the energy meter's, <see cref="Es1EnergyMeter.Paint"/>, the shield display's,
	/// <see cref="Es1ShieldDisplay.Paint"/>, the throttle gauge's, <see cref="Es1ThrottleGauge.Paint"/>, the MFD's,
	/// <see cref="Es1MfdDisplay.Repaint"/>, the Heads-Down Display's, <see cref="Es1HddDisplay.Repaint"/>, the gunsight's,
	/// <see cref="Es1Gunsight.Paint"/>, and the clock's, <see cref="Es1CockpitClock.Update"/>, are ported; then
	/// <see cref="RunTtmScripts"/>.
	/// </summary>
	public void InvalidateWidgets() {
		if (WidgetsOff) {
			return;
		}

		// Between the canvas push and pop, then the gunsight and the clock.
		foreach (var row in _weaponRows) {
			row?.Paint();
		}

		PodRow?.Paint();
		ConsoleButtons.Paint();
		EnergyMeter.Paint();
		Shields.Paint();
		Throttle.Paint();
		Mfd.Repaint();
		Hdd.Repaint();
		Gunsight.Paint();
		Clock.Update();
		RunTtmScripts();
	}

	// Set while RepaintForward runs, when DrawToCockpit copies nothing.
	private bool _repaintingForward;

	/// <summary>
	/// <b>This engine's own</b> (plan decision 30): view 0's instruments repainted during a glance, into the display the
	/// view manager has put in place (<see cref="Es1ViewManager.DrawCanvasPanels"/>), with the canvas context's origin at
	/// <paramref name="forwardOrigin"/>, view 0's. It runs the slots of <see cref="InvalidateWidgets"/> that only draw.
	/// <see cref="DrawToCockpit"/> copies nothing while it runs, because the target is one page.
	///
	/// <para>The paints leave state behind them, such as the throttle knob's saved screen and the bars' painted values.
	/// That state does not matter: the update slots draw nothing in a glance, and the return to view 0
	/// (<see cref="Es1ViewManager.SetView"/>) runs <see cref="InvalidateWidgets"/> on the display before they draw again.</para>
	///
	/// <para>Left out: the clock's slot, which advances the clock, so its text is drawn without advancing
	/// (<see cref="Es1CockpitClock.PaintText"/>); <see cref="RunTtmScripts"/>, which steps the scripts, so the <c>.TTM</c>
	/// areas keep their last picture; and the Heads-Down Display, whose page's show changes its state, and of which view
	/// 0's page holds only the top rows. The gunsight's paint draws the state its last update left, because its update
	/// does nothing in a glance.</para>
	/// </summary>
	internal void RepaintForward((int X, int Y) forwardOrigin) {
		if (WidgetsOff) {
			return;
		}

		var origin = CanvasOrigin;
		CanvasOrigin = forwardOrigin;
		_repaintingForward = true;
		try {
			foreach (var row in _weaponRows) {
				row?.Paint();
			}

			PodRow?.Paint();
			ConsoleButtons.Paint();
			EnergyMeter.Paint();
			Shields.Paint();
			Throttle.Paint();
			Mfd.Repaint();
			Gunsight.Paint();
			Clock.PaintText();
		} finally {
			_repaintingForward = false;
			CanvasOrigin = origin;
		}
	}

	/// <summary>
	/// <c>Cockpit_RunTtmScripts</c> (<c>0002e0ec</c>): <c>Ttm_RunAll</c> on the manager
	/// (docs/es1/formats/ttm-scripts.md#each-frame). Retail first makes the installed render context, its translation set
	/// and its origin the view's canvas origin, the scripts' default context, and puts the three fields back after; the
	/// clock's script draws through its own (see <see cref="Es1TtmManager"/>).
	/// </summary>
	public void RunTtmScripts() => Scripts.RunAll();

	/// <summary>
	/// <c>CockpitWidgets_TranslateForView</c> (<c>0002ced0</c>)(cockpit, delta), from the view changes: the root widget
	/// moved by the delta, so a press is tested from the moved origin; the delta taken off the canvas context's origin;
	/// and once the strips exist, <see cref="FlipScrollTriggers"/>. The instruments' moves are plan step 6c's.
	/// </summary>
	public void TranslateForView(int dx, int dy) {
		Root.TranslateForView(dx, dy);
		CanvasOrigin = (CanvasOrigin.X - dx, CanvasOrigin.Y - dy);
		if (StripsBuilt) {
			FlipScrollTriggers(dx, dy);
		}
	}

	/// <summary>
	/// <c>CockpitView_HandleEdgeTrigger</c> (<c>0002dc2c</c>)(cockpit, strip), a strip's click: by the current view, the
	/// bottom strip queues command 0 from view 0 and 1 from view 1, the left 5 from view 0 and 6 from view 3, the right 4
	/// from view 0 and 6 from view 2.
	/// </summary>
	public void HandleEdgeTrigger(Es1ScrollTrigger strip) {
		int view = _views.CurrentView;
		int command = -1;
		if (ReferenceEquals(strip, BottomStrip)) {
			command = view switch { 0 => 0, 1 => 1, _ => -1 };
		} else if (ReferenceEquals(strip, LeftStrip)) {
			command = view switch { 0 => 5, 3 => 6, _ => -1 };
		} else if (ReferenceEquals(strip, RightStrip)) {
			command = view switch { 0 => 4, 2 => 6, _ => -1 };
		}

		Queue(command);
	}

	/// <summary>
	/// <c>Cockpit_PickEdgeCursor</c> (<c>0002dcc8</c>)(cockpit, point): the point rebased by the canvas context's rect and
	/// origin; no arrow, then for the first strip whose rect holds it, by the current view: the bottom
	/// <see cref="Es1CockpitCursors.South"/> in view 0 and <see cref="Es1CockpitCursors.North"/> in view 1, the left
	/// <see cref="Es1CockpitCursors.West"/> in view 0 and <see cref="Es1CockpitCursors.East"/> in view 3, the right
	/// <see cref="Es1CockpitCursors.East"/> in view 0 and <see cref="Es1CockpitCursors.West"/> in view 2. While a tape
	/// plays the point is the last replayed one, <c>Input_GetCursorPos</c>'s (<see cref="Input.Es1TapePlayback.CursorPos"/>).
	/// </summary>
	public void PickEdgeCursor(int x, int y) {
		if (_views.Mouse.Tape is { Playing: true } tape) {
			(x, y) = tape.CursorPos;
		}

		int canvasX = x - CanvasRect.X0 + CanvasOrigin.X;
		int canvasY = y - CanvasRect.Y0 + CanvasOrigin.Y;
		int view = _views.CurrentView;
		Cursors.EdgeArrow = 0;
		if (BottomStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.South, 1 => Es1CockpitCursors.North, _ => 0 };
		} else if (LeftStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.West, 3 => Es1CockpitCursors.East, _ => 0 };
		} else if (RightStrip?.Rect.Contains(canvasX, canvasY) == true) {
			Cursors.EdgeArrow = view switch { 0 => Es1CockpitCursors.East, 2 => Es1CockpitCursors.West, _ => 0 };
		}
	}

	/// <summary>
	/// <c>CockpitWidgets_HandleCommand</c>(cockpit, <paramref name="code"/>): 0 while the widgets are off; otherwise the
	/// case of the 24 codes at <c>CockpitWidgets_CommandCodes</c> (<c>00092f34</c>) by the manager's current view, and any
	/// other code to the instruments. Answers in <paramref name="answer"/> 1 for a code a case took or an instrument
	/// accepted, else 0. <c>F11</c>'s case runs the objectives panel's loop inside the tick, yielding a wait at each of its
	/// passes (plan decision 11), and answers once the panel is down.
	/// </summary>
	/// <param name="panels">The in-mission panels' shared state, from which <c>F11</c> builds its panel; with none it builds none and still answers 1, an engine guard as for <see cref="Es1Simulation.Panels"/>.</param>
	public IEnumerable<Es1Wait> HandleCommand(ushort code, Es1AlertGlobals? panels, StrongBox<short> answer) {
		answer.Value = 0;
		if (WidgetsOff) {
			yield break;
		}

		int view = _views.CurrentView;
		switch (code) {
			case 0:
				break;
			case >= Key1 and <= Key9:
				if (view == 1) {
					answer.Value = Unlisted(code, view);
					break;
				}

				SelectRow((short)(code - Key1));
				answer.Value = 1;
				break;
			case >= F1 and <= F6:
				Mfd.PressButton(code - F1);
				Queue(view switch { 1 => 1, 2 or 3 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case F7 or F8:
				Hdd.PressButton(code - F7);
				Queue(0);
				answer.Value = 1;
				break;
			case F9:
				Queue(view switch { 0 => 5, 2 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case F10:
				Queue(view switch { 0 => 4, 3 => 6, _ => -1 });
				answer.Value = 1;
				break;
			case Esc:
				Queue(view switch { 0 => 0, 1 => 1, 4 => -1, _ => 6 });
				answer.Value = 1;
				break;
			case Tab:
				if (view is not (1 or 4)) {
					Targets.Cycle();
				}

				answer.Value = view switch { 4 => 0, 1 => Unlisted(code, view), _ => 1 };
				break;
			case F11:
				foreach (var wait in RunObjectivesPanel(panels)) {
					yield return wait;
				}

				answer.Value = 1;
				break;
			case R:
				Machine?.ToggleRadarMode(Globals.Sound);
				answer.Value = 1;
				break;
			default:
				answer.Value = Unlisted(code, view);
				break;
		}
	}

	/// <summary>
	/// <c>CockpitView_PollViewDevice</c>(cockpit, <paramref name="block"/>), once a tick from <c>Sim_PollPlayerInput</c>:
	/// the input block's buttons 4 to 7 (<c>+0x19</c>–<c>+0x1c</c>) queue view commands 1, 0, 4 and 5; the press of the
	/// mouse's byte <c>+0xb</c>, latched, in view 1 sends <c>Tab</c> to the Heads-Down Display's key slot
	/// (<see cref="Es1HddDisplay.KeyDispatch"/>). It returns 1,
	/// which its caller discards.
	///
	/// <para>Retail's two stick models that make buttons 4–7 make them from a hat: option 3's from port axis 3 and
	/// option 5's from buttons 0–3 held together (docs/es1/simulation/joystick.md#hats-and-throttles). A modern
	/// controller's own hat is not read (docs/herculan/plan-es1-first-mission.md, "What the slice leaves out"), so on one
	/// these buttons come only from those two models: option 5's from its buttons, and option 3's from whatever the
	/// throttle role puts on axis 3.</para>
	/// </summary>
	public void PollViewDevice(Es1PlayerInputBlock block) {
		if (StripsBuilt) {
			PickEdgeCursor(block.PointerX, block.PointerY);
		}

		if (block.Buttons[4] != 0) {
			_views.QueueViewCommand(1);
		}

		if (block.Buttons[5] != 0) {
			_views.QueueViewCommand(0);
		}

		if (block.Buttons[6] != 0) {
			_views.QueueViewCommand(4);
		}

		if (block.Buttons[7] != 0) {
			_views.QueueViewCommand(5);
		}

		if (block.MouseButtons[1] == 0) {
			DeviceButtonLatch = false;
		} else {
			if (!DeviceButtonLatch && _views.CurrentView == 1) {
				Hdd.KeyDispatch(Tab);
			}

			DeviceButtonLatch = true;
		}
	}

	/// <summary>
	/// <c>Cockpit_LoadPanel</c> (<c>0002c9d8</c>) on <paramref name="files"/>' <c>.GAU</c> layout, as far as this engine
	/// builds it: over the record's first 16 bytes the canvas context's rect (<c>Cockpit_CreateCanvasContext</c>) and the
	/// root widget (<c>CTLWindow_Ctor</c>), made the root the mouse's events go to (<c>Widget_ClearPressed</c>); the
	/// <c>.TTM</c> manager, <c>Ttm_ManagerCtor</c>(0, 0, the installed render context, 10), its prefix <c>ttm\</c> and
	/// <c>Ttm_AllocHandles</c> with a timer count of 2, every second interrupt of the PC timer
	/// (docs/es1/simulation/timer.md#the-rate); the pilot channel <c>+0x203</c> on the record + 1668, whose build loads
	/// <c>str\system.str</c>, and the ticker <c>+0x207</c> on the record + 1684; then, of the instruments
	/// in retail's order, the console buttons <c>+0x1dd</c> on the block at 452, the energy meter <c>+0x1e1</c> on the
	/// block at 548, the shield display <c>+0x1e5</c> on the block at 616, the MFD <c>+0x1e9</c> on the block at 728,
	/// put on its STATUS screen (<see cref="Es1MfdDisplay.SetMode"/> with 0), the throttle gauge <c>+0x1ed</c> on the
	/// block at 1000, the gunsight <c>+0x1f1</c> on the block at 1104, the Heads-Down Display <c>+0x1f5</c> on the block
	/// at 1212 and the clock <c>+0x1f9</c> on the block at 1604, which opens the HERC's script; and last
	/// <c>Ttm_RunAll</c>. The console's buttons, the shield display's two buttons, the MFD's 13 buttons and its click
	/// surface, the throttle's slider, the gunsight and the Heads-Down Display's 15 buttons register their gadgets on the
	/// root in that order, all before the
	/// weapon rows <c>Sim_StartMission</c> builds and the strips the first frame builds, so each wins where a later one
	/// overlaps it.
	/// </summary>
	[MemberNotNull(nameof(Root), nameof(PilotChannel), nameof(Ticker), nameof(ConsoleButtons), nameof(EnergyMeter), nameof(Shields), nameof(Mfd),
		nameof(Throttle), nameof(Gunsight), nameof(Hdd), nameof(Clock), nameof(Scripts))]
	private void LoadPanel(Es1CockpitViews files) {
		var layout = files.Layout;
		var head = new Es1Rect(layout.Origin.X, layout.Origin.Y, BitConverter.ToInt32(layout.Bytes8, 0), BitConverter.ToInt32(layout.Bytes8, 4));
		CanvasRect = head;
		Root = new Es1WidgetRoot(head);
		_mouse.Widgets.ClearPressed(Root);

		Scripts = new Es1TtmManager(_views.Display, ScriptBackgroundPage, ScriptHandles, files.Resources) {
			PathPrefix = "ttm\\",
		};
		Scripts.AllocHandles(ScriptTimerCount, Globals.Clock);
		PilotChannel = new Es1PilotChannel(this, Es1Rect.From(layout.PilotChannelBox), MessageTables,
			files.Fonts[Es1CockpitViews.CpRed]);
		Ticker = new Es1MessageTicker(this, Es1Rect.From(layout.TickerBox), MessageTables, files.Fonts[Es1CockpitViews.CpRed]);
		// Gau_WeaponButtonsWidget: the console and ConsoleButtons_SetChainNumber(it, 2). It also sets +0x1dc to 1 and
		// +0x1d0 to 0, which only CockpitView_SetConsoleButtonsLatch and CockpitView_UnlightConsoleButtons read, to which
		// es2_xref.py finds no reference, and +0x1d4 to +0x168 (FirstWeaponRow).
		ConsoleButtons = new Es1ConsoleButtons(this, layout.ConsoleButtons, files.Fonts, files.PushButtons, files.SimulatorStrings);
		ConsoleButtons.SetChainNumber(2);
		EnergyMeter = new Es1EnergyMeter(this, layout.EnergyMeterBlock, files.Resources);
		Shields = new Es1ShieldDisplay(this, layout.ShieldDisplayBlock, files.Fonts, files.PushButtons);
		Mfd = new Es1MfdDisplay(this, layout.MfdPanelBlock, files.Fonts, files.PushButtons, files.SimulatorStrings,
			files.StatusFlyers, files.ScannerRadar, files.StatusBases);
		Mfd.SetMode(Es1MfdDisplay.Status);
		Throttle = new Es1ThrottleGauge(this, layout.ThrottleBlock, files.ThrottleLights, files.PushButtons);
		Gunsight = new Es1Gunsight(this, layout.Gunsight, files.GunsightBanks, files.Fonts, _mouse,
			files.SimulatorStrings?.Text(Es1Gunsight.AttGroup, 0));
		Hdd = new Es1HddDisplay(this, layout.HddBlock, files.Fonts, files.PushButtons, files.SimulatorStrings, files.Resources);
		Clock = new Es1CockpitClock(this, layout.ClockBlock, Scripts, files.Fonts[0], Globals.Clock.Clock);
		Scripts.RunAll();
	}

	/// <summary><c>Cockpit_LoadPanel</c>'s arguments to the <c>.TTM</c> manager: the page <i>a</i>, the handles, and <c>Ttm_AllocHandles</c>' timer count.</summary>
	private const int ScriptBackgroundPage = 0, ScriptTimerCount = 2;

	/// <inheritdoc cref="ScriptBackgroundPage"/>
	private const byte ScriptHandles = 10;

	/// <summary>
	/// <c>CockpitView_BuildScrollTriggers</c> (<c>0002d970</c>)(cockpit): the three strips on their fixed rects, each
	/// registered in the root's list, after whatever the panel registered.
	/// </summary>
	private void BuildScrollTriggers() {
		BottomStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(0, 197, 319, 200) };
		Root.RegisterClickable(BottomStrip);
		LeftStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(0, 0, 5, 200) };
		Root.RegisterClickable(LeftStrip);
		RightStrip = new Es1ScrollTrigger(this) { Rect = new Es1Rect(314, 0, 319, 200) };
		Root.RegisterClickable(RightStrip);
	}

	/// <summary>
	/// <c>CockpitView_FlipScrollTriggers</c> (<c>0002db1c</c>)(cockpit, delta): per axis, a delta of 6 or more sets the sign
	/// +1 and the shift 320 across or 200 down, one below −5 the sign −1 and −320 or −200. Each strip with a sign on an
	/// axis is reflected across its own edge there (+1: <i>x1</i> = <i>x0</i>, <i>x0</i> less the width; −1: <i>x0</i> =
	/// <i>x1</i>, <i>x1</i> plus the width), then moved by −(delta − shift), so each ends on the edge facing the view it
	/// leads to (docs/retail/simulation/cockpit-input.md#a-strip-changes-edge-with-the-view).
	/// </summary>
	private void FlipScrollTriggers(int dx, int dy) {
		var (signX, shiftX) = Classify(dx, ScreenWidth);
		var (signY, shiftY) = Classify(dy, ScreenHeight);
		foreach (var strip in new[] { BottomStrip, LeftStrip, RightStrip }) {
			if (strip == null) {
				continue;
			}

			var r = strip.Rect;
			(int x0, int x1) = Reflect(r.X0, r.X1, signX);
			(int y0, int y1) = Reflect(r.Y0, r.Y1, signY);
			strip.Rect = new Es1Rect(x0, y0, x1, y1).Offset(-(dx - shiftX), -(dy - shiftY));
		}

		static (int Sign, int Shift) Classify(int delta, int screen) =>
			delta >= 6 ? (1, screen) : delta < -5 ? (-1, -screen) : (0, 0);

		static (int Low, int High) Reflect(int low, int high, int sign) {
			int size = high - low;
			return sign switch {
				1 => (low - size, low),
				-1 => (high, high + size),
				_ => (low, high),
			};
		}
	}

	/// <summary>
	/// <c>F11</c>'s case: <c>ObjectivesPanel_Ctor</c>(0, 0, 1), run through its slot <c>+0xc</c>, whose answer the case
	/// does not read, then destroyed with <c>Alert_Dtor</c>(panel, 3)
	/// (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device). With no <paramref name="panels"/> nothing is raised.
	/// </summary>
	private static IEnumerable<Es1Wait> RunObjectivesPanel(Es1AlertGlobals? panels) {
		if (panels == null) {
			yield break;
		}

		var panel = new Es1ObjectivesPanel(panels);
		foreach (var wait in panel.Run(new StrongBox<int>())) {
			yield return wait;
		}

		panel.Destroy();
	}

	/// <summary><c>CockpitViewManager_QueueViewCommand</c> with <paramref name="command"/>, or nothing for −1.</summary>
	private void Queue(int command) {
		if (command != -1) {
			_views.QueueViewCommand(command);
		}
	}

	/// <summary>
	/// An unlisted code: <c>MfdFlashComm_HandleAltKey</c> (<c>0003a890</c>)(the MFD <c>+0x1e9</c>, code,
	/// <see cref="Es1MfdDisplay.HandleAltKey"/>); when that answers 0, in view 1 the Heads-Down Display's key slot
	/// (<see cref="Es1HddDisplay.KeyDispatch"/>) alone, and
	/// in any other view the key slots of <c>+0x1d4</c> when set, <c>+0x1dd</c>, <c>+0x1e1</c>, <c>+0x1e5</c>,
	/// <c>+0x1ed</c>, <c>+0x1f1</c> and <c>+0x1e9</c>, to the first that accepts; 1 when one does, else 0. A weapon
	/// row's, the energy meter's, the shield display's, the throttle gauge's and the gunsight's slots take no key; the
	/// console's (<see cref="Es1ConsoleButtons.HandleCommand"/>) takes four and the MFD's
	/// (<see cref="Es1MfdDisplay.KeyDispatch"/>) its buttons' and rows' keys.
	/// </summary>
	private short Unlisted(ushort code, int view) {
		if (Mfd.HandleAltKey(code) != 0) {
			return 1;
		}

		if (view == 1) {
			return Hdd.KeyDispatch(code);
		}

		if (FirstWeaponRow != null && Es1WeaponGauge.KeyDispatch(code) != 0) {
			return 1;
		}

		return ConsoleButtons.HandleCommand(code) != 0 || Es1EnergyMeter.KeyDispatch(code) != 0
			|| Es1ShieldDisplay.KeyDispatch(code) != 0 || Es1ThrottleGauge.KeyDispatch(code) != 0
			|| Es1Gunsight.KeyDispatch(code) != 0 || Mfd.KeyDispatch(code) != 0 ? (short)1 : (short)0;
	}

	/// <summary>
	/// The number keys' case for row <paramref name="row"/>, outside view 1: the pod row's
	/// (<see cref="PodRowIndex"/>) switch flipped in a copy of its block (<see cref="Es1PodGauge.GetStateBlock"/>) handed
	/// back to <see cref="Es1PodGauge.SetStateBlock"/>; another row's gauge, when set, armed through
	/// <see cref="ArmWeaponByGauge"/>.
	/// </summary>
	private void SelectRow(short row) {
		if (PodRowIndex == row) {
			if (PodRow != null) {
				var block = PodRow.GetStateBlock();
				PodRow.SetStateBlock(block with { Switch = !block.Switch });
			}

			return;
		}

		if (_weaponRows[row] is { } gauge) {
			ArmWeaponByGauge(gauge);
		}
	}

	/// <summary>
	/// <c>CockpitView_ArmWeaponByGauge</c> (<c>0002d098</c>)(cockpit, <paramref name="gauge"/>):
	/// <see cref="Es1WeaponMounts.SelectByGauge"/> on the machine's weapon manager; when that arms a mount,
	/// <see cref="Sound.Es1Sound.ActivateRow"/> played and true.
	/// </summary>
	public bool ArmWeaponByGauge(Es1WeaponGauge gauge) {
		if (Machine?.Mounts?.SelectByGauge(gauge) != true) {
			return false;
		}

		Globals.Sound.Play(Sound.Es1Sound.ActivateRow);
		return true;
	}

	/// <summary>
	/// <c>CockpitView_CreateEnergyWeaponGauge</c> (<c>0002cbbc</c>)(cockpit, <paramref name="mount"/>,
	/// <paramref name="name"/>, <paramref name="row"/>) (docs/es1/simulation/cockpit-panel.md#the-weapon-rows): the row's
	/// rect, the <c>.GAU</c>'s rect at 20 + 16 × row, copied into the energy block at 244, the gauge built on that block
	/// and registered (<see cref="RegisterWeaponGauge"/>). A row outside the nine, whose rect retail reads from the bytes
	/// before or after the rects, builds nothing, an engine guard; no retail <c>.GL</c> holds one.
	/// </summary>
	public Es1EnergyWeaponGauge? CreateEnergyWeaponGauge(Es1WeaponMount mount, string? name, short row) {
		if (RowRect(row) is not { } rect) {
			return null;
		}

		var files = _views.Files;
		var gauge = new Es1EnergyWeaponGauge(this, mount, name, row, rect, files.Layout.EnergyRow, files.Fonts,
			files.PushButtons, files.SimulatorStrings?.Text(OfflineGroup, 0));
		RegisterWeaponGauge(row, gauge);
		return gauge;
	}

	/// <summary>
	/// <c>CockpitView_CreateAmmoWeaponGauge</c> (<c>0002cc18</c>): <see cref="CreateEnergyWeaponGauge"/>'s steps on the
	/// ammunition block at 164.
	/// </summary>
	public Es1AmmoWeaponGauge? CreateAmmoWeaponGauge(Es1WeaponMount mount, string? name, short row) {
		if (RowRect(row) is not { } rect) {
			return null;
		}

		var files = _views.Files;
		var gauge = new Es1AmmoWeaponGauge(this, mount, name, row, rect, files.Layout.AmmunitionRow, files.Fonts,
			files.PushButtons, files.SimulatorStrings?.Text(OfflineGroup, 0));
		RegisterWeaponGauge(row, gauge);
		return gauge;
	}

	/// <summary>
	/// <c>CockpitView_CreatePodGauge</c> (<c>0002cc74</c>)(cockpit, <paramref name="mount"/>, <paramref name="row"/>): the
	/// row's rect copied into the pod block at 340, the gauge built on it, kept at <c>+0x1d8</c> and its row at
	/// <c>+0x23b</c>. Retail also adds 1 to the widget list's count without storing the gauge in the list, which this
	/// engine does not keep. A row outside the nine builds nothing, as for <see cref="CreateEnergyWeaponGauge"/>.
	/// </summary>
	public Es1PodGauge? CreatePodGauge(Es1WeaponMount mount, short row) {
		if (RowRect(row) is not { } rect) {
			return null;
		}

		var files = _views.Files;
		var strings = files.SimulatorStrings;
		PodRow = new Es1PodGauge(this, mount, row, rect, files.Layout.PodRow, files.Fonts, files.PushButtons,
			strings?.Text(EcmGroup, 0), strings?.Text(OfflineGroup, 0));
		PodRowIndex = row;
		return PodRow;
	}

	/// <summary>
	/// <c>CockpitView_RegisterWeaponGauge</c> (<c>0002cb54</c>)(cockpit, <paramref name="row"/>, <paramref name="gauge"/>):
	/// the gauge kept as <see cref="FirstWeaponRow"/> while that is empty and in its row's slot, and the count raised.
	/// Retail also appends it to the widget list, which this engine does not keep, and takes the first empty slot for a
	/// row of −1, which the factories' guard leaves no caller of.
	/// </summary>
	private void RegisterWeaponGauge(short row, Es1WeaponRowGauge gauge) {
		FirstWeaponRow ??= gauge;
		_weaponRows[row] = gauge;
		WeaponRowCount++;
	}

	/// <summary>The <c>.GAU</c>'s weapon-row rect <paramref name="row"/>, or null for a row outside the nine.</summary>
	private Es1Rect? RowRect(short row) =>
		row >= 0 && row < Es1GauFile.WeaponRowSlots ? Es1Rect.From(_views.Files.Layout.WeaponRows[row]) : null;
}
