using HercWorks.Core.Data.Struct.Vshell.Hercs;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Host.Shell.Tabs;

/// <summary>
/// The tabs that work on the hangar — REPAIR, WEAPONS, BUILD, CREW and ARMORY — with the squad panel they share and
/// the scrap dialogs their SCRAP buttons open. Each screen but the repair screen is built on its tab's first entry
/// and kept, as the original's widgets are; each entry re-runs its entry routine.
/// </summary>
sealed class HangarTabs {
	private readonly GameInProgress _game;
	private readonly ShellDialogs _dialogs;
	private readonly ShellScreen _screen;
	private readonly WidgetEvents _widgets;
	private readonly Action _repaint;
	private readonly int _startBay;

	private readonly ShellRepairCosts? _repairCosts;
	private readonly ShellRepairDiagrams? _repairDiagrams;

	// The armory's prices, names and stat lines, and the preferences byte that says whether weapons are
	// built by hand (prefs.cfg option 45), which gates CLEAR. The prices are also what the repair and
	// build screens deduct the save's build queue at. The screen is built on first entry and kept.
	private readonly ShellArmoryCatalog _armoryCatalog;
	private ShellArmoryScreen? _armoryScreen;

	// Option 44 is the repair mode the repair screen's readout names. It and option 45 are read where the original
	// reads them, on the tab's entry and the armory's clicks, since the preferences screen changes them.
	private ShellRepairScreen _repairScreen;

	// The squad panel's three-quarter view, which the crew tab shows (and WEAPONS and BUILD would), and
	// the crew screen's two portrait banks.
	private readonly ShellBayPictures _bayPictures;
	private readonly ShellCrewPortraits _crewPortraits;
	private ShellCrewScreen? _crewScreen;

	// The build screen's chassis figures and prices, and the same body layouts the repair diagram
	// draws, which its blueprints reuse.
	private readonly IReadOnlyList<HercInfEntry>? _chassisCatalog;
	private ShellBuildScreen? _buildScreen;

	// The weapons screen's pictures and prose.
	private readonly ShellWeaponsArt _weaponsArt;
	private ShellWeaponsScreen? _weaponsScreen;

	public HangarTabs(GameContent content, GameInProgress game, ShellDialogs dialogs, ShellScreen screen, WidgetEvents widgets,
			int startBay, Action repaint) {
		_game = game;
		_dialogs = dialogs;
		_screen = screen;
		_widgets = widgets;
		_startBay = startBay;
		_repaint = repaint;

		_repairCosts = ShellRepairCosts.Load(content);
		_repairDiagrams = ShellRepairDiagrams.Load(content);
		_armoryCatalog = ShellArmoryCatalog.Load(content);
		_repairScreen = NewRepairScreen();
		_bayPictures = ShellBayPictures.Load(content);
		_crewPortraits = ShellCrewPortraits.Load(content);
		_chassisCatalog = ShellBuildScreen.LoadCatalog(content);
		_weaponsArt = ShellWeaponsArt.Load(content);
		if (_repairCosts == null) {
			Console.Error.WriteLine($"No gam\\{ShellRepairCosts.ValuesResourceName} or gam\\{ShellRepairCosts.ChassisResourceName}"
				+ " — the repair screen draws its labels and no cost figures.");
		}

		widgets.Handle(ShellWidgetKind.RepairRow, widget => SelectRepair(widget.Index, widget.Sub));
		widgets.Handle(ShellWidgetKind.RepairHotspot, widget => SelectRepair(widget.Index, widget.Sub));
		widgets.Handle(ShellWidgetKind.RepairButton, widget => {
			if ((ShellRepairButton)widget.Index == ShellRepairButton.Scrap) {
				OpenScrapDialog(_repairScreen.SelectedBay);
			} else {
				ClickRepairButton((ShellRepairButton)widget.Index);
			}
		});
		widgets.Handle(ShellWidgetKind.SquadRow, widget => ClickRoster(widget.Index));
		widgets.Handle(ShellWidgetKind.BuildChassisRow, widget => SelectChassis(widget.Index));
		widgets.Handle(ShellWidgetKind.BuildButton, widget => ClickBuildButton((ShellBuildButton)widget.Index));
		widgets.Handle(ShellWidgetKind.WeaponsRow, widget => SelectWeaponsRow(widget.Index));
		widgets.Handle(ShellWidgetKind.WeaponsButton, widget => ClickWeaponsButton((ShellWeaponsButton)widget.Index));
		widgets.Handle(ShellWidgetKind.WeaponsHotspot, widget => SelectHardpoint(widget.Index));
		widgets.Handle(ShellWidgetKind.ArmoryRow, widget => ClickArmoryRow(widget.Index));
		widgets.Handle(ShellWidgetKind.ArmoryButton, widget => ClickArmoryButton((ShellArmoryButton)widget.Index));
		widgets.Handle(ShellWidgetKind.ScrapDialogButton, widget => ClickScrapDialogButton((ShellScrapDialogButton)widget.Index));
		widgets.Handle(ShellWidgetKind.CrewRow, ClickCrew);
		widgets.Handle(ShellWidgetKind.CrewRowPortrait, ClickCrew);
		widgets.Handle(ShellWidgetKind.CrewSquadPortrait, ClickCrew);
		widgets.Handle(ShellWidgetKind.CrewClear, ClickCrew);
	}

	/// <summary>The bay the repair screen has selected, which the other hangar tabs start from.</summary>
	public int RepairBay => _repairScreen.SelectedBay;

	/// <summary>
	/// A tab's entry, for the tabs this owns; returns whether <paramref name="tab"/> is one. Every entry
	/// requotes the repair screen first: the repair and build screens quote the pool net of the queue, which
	/// the armory tab changes.
	/// </summary>
	public bool Enter(int tab) {
		_repairScreen.QueuedKilograms = _armoryCatalog.QueuedTotal(_game.Hangar);

		if (tab == ShellScreen.RepairTab) {
			_repairScreen.RepairMode = _game.Options[GameInProgress.RepairOption];
			_repairScreen.Enter();
		} else if (tab == ShellScreen.CrewTab) {
			EnterCrew();
		} else if (tab == ShellScreen.WeaponsTab) {
			EnterWeapons();
		} else if (tab == ShellScreen.BuildTab) {
			EnterBuild();
		} else if (tab == ShellScreen.ArmoryTab) {
			EnterArmory();
		} else {
			return false;
		}

		return true;
	}

	/// <summary>A new game in progress: the repair screen is built again over its hangar.</summary>
	public void OnAdopted() => _repairScreen = NewRepairScreen();

	/// <summary>The widget under a canvas point on one of these tabs, below the strip.</summary>
	public ShellHit? HitAt(int tab, float canvasX, float canvasY) => tab switch {
		ShellScreen.RepairTab => ShellSquadPanel.HitAt(canvasX, canvasY)
			?? _repairScreen.HitAt(canvasX, canvasY),
		ShellScreen.BuildTab when _buildScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
			?? _buildScreen.HitAt(canvasX, canvasY),
		ShellScreen.WeaponsTab when _weaponsScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
			?? _weaponsScreen.HitAt(canvasX, canvasY),
		ShellScreen.CrewTab when _crewScreen != null => ShellSquadPanel.HitAt(canvasX, canvasY)
			?? ShellCrewScreen.HitAt(canvasX, canvasY),
		ShellScreen.ArmoryTab when _armoryScreen != null => _armoryScreen.HitAt(canvasX, canvasY),
		_ => null,
	};

	/// <summary>Paints the tab's screen; returns false for a tab with no screen to paint.</summary>
	public bool Paint(int tab, ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		switch (tab) {
			case ShellScreen.RepairTab:
				_repairScreen.Paint(surface, text, sprites, lit);
				return true;
			case ShellScreen.BuildTab when _buildScreen != null:
				_buildScreen.Paint(surface, text, sprites, lit);
				return true;
			case ShellScreen.WeaponsTab when _weaponsScreen != null:
				_weaponsScreen.Paint(surface, text, sprites, lit);
				return true;
			case ShellScreen.CrewTab when _crewScreen != null:
				_crewScreen.Paint(surface, text, sprites, lit);
				return true;
			case ShellScreen.ArmoryTab when _armoryScreen != null:
				_armoryScreen.Paint(surface, text, sprites, lit);
				return true;
			default:
				return false;
		}
	}

	private ShellRepairScreen NewRepairScreen() => new(_game.Hangar, _repairCosts, _startBay, _repairDiagrams) {
		QueuedKilograms = _armoryCatalog.QueuedTotal(_game.Hangar),
		RepairMode = _game.Options[GameInProgress.RepairOption],
	};

	// A repair row or hotspot's handler, Repair_SelectHotspot (00433eb9): the selection moves and the
	// detail panel follows. A hardpoint row past the machine's capacity, or one holding no weapon,
	// refuses the selection outright — nothing moves and nothing repaints.
	private void SelectRepair(int column, int row) {
		if (!_repairScreen.Select(column, row)) {
			return;
		}

		_repaint();
	}

	// REPAIR (00434b2d), REPAIR ALL (00434c59) and CANCEL (00434d73). Each refills the rows and the readout
	// panels after it, which the repaint does here.
	private void ClickRepairButton(ShellRepairButton button) {
		switch (button) {
			case ShellRepairButton.Repair:
				_repairScreen.Repair();
				break;
			case ShellRepairButton.RepairAll:
				_repairScreen.RepairAll();
				break;
			case ShellRepairButton.Cancel:
				_repairScreen.Cancel();
				break;
			default:
				return;
		}

		_repaint();
	}

	// A Squad Inventory row's handler, Squad_SelectBay (0043d64d), whose arm is the tab that is up.
	private void ClickRoster(int bay) {
		if (_screen.SelectedTab == ShellScreen.CrewTab) {
			if (_crewScreen?.ClickRoster(bay) == true) {
				_repaint();
			}

			return;
		}

		if (_screen.SelectedTab == ShellScreen.BuildTab) {
			if (_buildScreen?.ClickRoster(bay) == true) {
				_repaint();
			}

			return;
		}

		if (_screen.SelectedTab == ShellScreen.WeaponsTab) {
			if (_weaponsScreen?.ClickRoster(bay) == true) {
				_repaint();
			}

			return;
		}

		if (_repairScreen.SelectBay(bay)) {
			_repaint();
		}
	}

	// The crew panel's handlers. A row, or the portrait inside it, selects the row; a squad portrait
	// and CLEAR assign against the selected row. Every one of them repaints.
	private void ClickCrew(ShellWidget widget) {
		if (_crewScreen == null) {
			return;
		}

		switch (widget.Kind) {
			case ShellWidgetKind.CrewRow or ShellWidgetKind.CrewRowPortrait:
				_crewScreen.SelectRow(widget.Index);
				break;
			case ShellWidgetKind.CrewSquadPortrait:
				_crewScreen.ClickPortrait(widget.Index);
				break;
			case ShellWidgetKind.CrewClear:
				_crewScreen.Clear();
				break;
			default:
				return;
		}

		_repaint();
	}

	// A chassis row's handler, Build_SelectChassis (00446c3b); the chassis already selected is a no-op.
	private void SelectChassis(int chassis) {
		if (_buildScreen?.SelectChassis(chassis) == true) {
			_repaint();
		}
	}

	// BUILD's handler (00446f3e) orders the chassis; SCRAP's (00446ee0) puts the dialog up.
	private void ClickBuildButton(ShellBuildButton button) {
		if (_buildScreen == null) {
			return;
		}

		if (button == ShellBuildButton.Scrap) {
			OpenScrapDialog(_buildScreen.SelectedBay);
			return;
		}

		_buildScreen.Build();
		_repaint();
	}

	// Both SCRAP handlers, the build tab's (00446ee0) and the repair tab's (00434d15): 00447711 quotes
	// the selected bay's machine and shows the dialog.
	private void OpenScrapDialog(int bay) {
		_dialogs.Scrap.Open(bay, (_repairCosts?.ScrapValue(_game.Hangar.Bay(bay)) ?? 0) / ShellRepairCosts.KilogramsPerTon);
		_repaint();
	}

	// CANCEL (00447c38) only takes the dialog down. ACCEPT (00447c96) takes it down, scraps the bay
	// (0040e757), and on the repair tab moves to the first bay holding a finished machine; the build
	// tab keeps the bay, now empty, and regates.
	private void ClickScrapDialogButton(ShellScrapDialogButton button) {
		if (_dialogs.WeaponScrap.IsOpen) {
			ClickWeaponScrapDialogButton(button);
			return;
		}

		_dialogs.Scrap.Close();
		if (button == ShellScrapDialogButton.Accept) {
			_game.Hangar.Scrap(_dialogs.Scrap.Subject, _repairCosts);
			if (_screen.SelectedTab == ShellScreen.RepairTab) {
				_repairScreen.SelectBay(_game.Hangar.FirstBuiltBay());
			}
		}

		_repaint();
	}

	// The weapon dialog's CANCEL (00447d59) only takes it down. ACCEPT (WeaponScrapDialog_OnAccept,
	// 00447db7) takes it down, sells the whole stock (Armory_ScrapWeapons, 0040e7b2), trims or refills
	// the queue by the build mode (Armory_RefreshQueue, 00412413), and refreshes the rows and readout.
	private void ClickWeaponScrapDialogButton(ShellScrapDialogButton button) {
		_dialogs.WeaponScrap.Close();
		if (button == ShellScrapDialogButton.Accept && _armoryScreen != null) {
			int weapon = _dialogs.WeaponScrap.Subject;
			_game.Hangar.ScrapStock(weapon, _armoryCatalog.ScrapValueTons(_game.Hangar, weapon));
			_armoryCatalog.RefreshQueue(_game.Hangar, _game.ManualWeaponBuild());
			_armoryScreen.RefreshAfterScrap();
		}

		_repaint();
	}

	// Tab 4's entry, from the bay the repair screen has selected, as the crew tab's is.
	private void EnterBuild() {
		if (_buildScreen == null) {
			_buildScreen = new ShellBuildScreen(_game.Hangar, _repairScreen.SelectedBay, _chassisCatalog, _repairDiagrams,
				_bayPictures);
		} else {
			_buildScreen.Enter(_game.Hangar, _repairScreen.SelectedBay);
		}

		_buildScreen.QueuedKilograms = _armoryCatalog.QueuedTotal(_game.Hangar);
	}

	// Tab 2's entry, from the bay the repair screen has selected, as the build and crew tabs' are.
	private void EnterWeapons() {
		if (_weaponsScreen == null) {
			_weaponsScreen = new ShellWeaponsScreen(_game.Hangar, _repairScreen.SelectedBay, _weaponsArt, _bayPictures);
		} else {
			_weaponsScreen.Enter(_game.Hangar, _repairScreen.SelectedBay);
		}
	}

	// An inventory row's handler, one of the thunks from Arming_OnRow00 (00440300): Arming_SelectRow (0043f71c) with the
	// fit armed, so with a hardpoint selected the row's weapon goes into it.
	private void SelectWeaponsRow(int row) {
		if (_weaponsScreen?.SelectRow(row, fit: true) == true) {
			_repaint();
		}
	}

	// A hotspot over the bay picture, Arming_SelectHardpoint (0043dbb2).
	private void SelectHardpoint(int hardpoint) {
		if (_weaponsScreen?.SelectHardpoint(hardpoint) == true) {
			_repaint();
		}
	}

	// The four guidance buttons show their kind and write it to the mount, the rack's own button
	// selects its row again without fitting it (Arming_OnRackButton, 0044012a), and the steppers move the hardpoint.
	private void ClickWeaponsButton(ShellWeaponsButton button) {
		if (_weaponsScreen == null) {
			return;
		}

		bool changed = button switch {
			ShellWeaponsButton.Arm or ShellWeaponsButton.Arh or ShellWeaponsButton.Sarh or ShellWeaponsButton.Eo =>
				ShowWeaponsGuidance((int)button),
			ShellWeaponsButton.Weapon => _weaponsScreen.SelectRow(_weaponsScreen.SelectedRow, fit: false),
			ShellWeaponsButton.PreviousHardpoint => _weaponsScreen.PreviousHardpoint(),
			_ => _weaponsScreen.NextHardpoint(),
		};

		if (changed) {
			_repaint();
		}
	}

	private bool ShowWeaponsGuidance(int button) {
		_weaponsScreen!.ShowGuidance(button);
		return true;
	}

	// Tab 5's entry, Armory_Enter (004494f7). The armory has no squad panel and no bay.
	private void EnterArmory() {
		if (_armoryScreen == null) {
			_armoryScreen = new ShellArmoryScreen(_game.Hangar, _game.ManualWeaponBuild(), _armoryCatalog, _weaponsArt);
		} else {
			_armoryScreen.Enter(_game.Hangar, _game.ManualWeaponBuild());
		}
	}

	// A row's release: Armory_ClickRow (0044969f) for the left button, Armory_RightClickRow (004499de)
	// for the right.
	private void ClickArmoryRow(int row) {
		if (_armoryScreen == null) {
			return;
		}

		bool changed = _widgets.EventButton == ShellMouseButton.Left
			? _armoryScreen.ClickRow(row)
			: _armoryScreen.RightClickRow(row);
		if (!changed) {
			return;
		}

		_repaint();
	}

	// Clear (00449ef4) takes the lit weapon's units off the queue. Scrap (Armory_OnScrap, 00449e78) puts
	// the weapon scrap dialog up on the lit weapon's stock.
	private void ClickArmoryButton(ShellArmoryButton button) {
		if (_armoryScreen == null) {
			return;
		}

		if (button == ShellArmoryButton.Scrap) {
			if (_armoryScreen.SelectedRow != -1) {
				int weapon = _armoryScreen.SelectedWeapon;
				_dialogs.WeaponScrap.Open(weapon, _armoryCatalog.ScrapValueTons(_game.Hangar, weapon));
				_repaint();
			}

			return;
		}

		_armoryScreen.Clear();
		_repaint();
	}

	// Tab 6's entry. The bay it starts from is the one the previous tab left selected, SelectedBaySlot (00482ae5),
	// which here only the repair screen tracks; the entry then moves it.
	private void EnterCrew() {
		if (_crewScreen == null) {
			_crewScreen = new ShellCrewScreen(_game.Hangar, _repairScreen.SelectedBay, _bayPictures, _crewPortraits);
		} else {
			_crewScreen.Enter(_game.Hangar, _repairScreen.SelectedBay);
		}
	}
}
