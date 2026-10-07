using HercWorks.Core.Data.File.Sav;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The game the shell's memory holds — the career, its hangar and its working files — with the campaign mode and
/// the preferences array, and the saves that write the game out.
/// </summary>
public sealed class GameInProgress {
	/// <summary>
	/// Slot 10, the current-game autosave, whose in-use byte (<c>00482a19</c>) gates CONTINUE GAME.
	/// </summary>
	public const int CurrentGameSlot = 10;

	/// <summary><c>prefs.cfg</c> option 44, VSHELL's <c>Repair Options:</c> (docs/retail/simulation/preferences.md).</summary>
	public const int RepairOption = 44;

	/// <summary><c>prefs.cfg</c> option 45, VSHELL's <c>Weapons Building:</c> — 1 builds weapons by hand (docs/retail/simulation/preferences.md).</summary>
	private const int WeaponsBuildingOption = 45;

	/// <summary><c>prefs.cfg</c> option 42, the campaign-or-training flag <c>Shell_SetCampaignMode</c> (<c>0040e69e</c>) writes.</summary>
	private const int CampaignModeOption = 42;

	private readonly string _installRoot;
	private readonly ShellSaveScreen _saveScreen;
	private readonly ShellMainMenu _mainMenu;

	public GameInProgress(string installRoot, ShellSaveScreen saveScreen, ShellMainMenu mainMenu, SimulatorPreferences options,
			ShellCampaignMode? forcedMode, PlayerSave? loadedGame, ShellHangar hangar, ShellWorkingFiles workingFiles) {
		_installRoot = installRoot;
		_saveScreen = saveScreen;
		_mainMenu = mainMenu;
		Options = options;
		LoadedGame = loadedGame;
		Hangar = hangar;
		WorkingFiles = workingFiles;
		if (loadedGame != null) {
			CampaignStage = loadedGame.CampaignStage;
			MissionInStage = loadedGame.MissionInStage;
		}

		Mode = forcedMode ?? (options[CampaignModeOption] == (byte)ShellCampaignMode.Campaign
			? ShellCampaignMode.Campaign : ShellCampaignMode.Training);
	}

	/// <summary>
	/// The preferences array: the options the shell's screens step and the startup and its handlers read and save.
	/// </summary>
	public SimulatorPreferences Options { get; }

	/// <summary>
	/// CampaignModeFlag (0048260c), which the startup (Shell_InitGameState, 0040e17e) seeds from option 42 so the mode
	/// survives a restart, and a return from a mission with it: it is what picks GAME_R or GAME_T as slot 10.
	/// </summary>
	public ShellCampaignMode Mode { get; private set; }

	/// <summary>
	/// maybe_HasGameInProgress (0048260a), whether there is a game in progress to save. The startup clears it and a load sets
	/// it; Game_SaveSlot writes nothing while it is clear, and SAVE is gated on it.
	/// </summary>
	public bool InProgress { get; private set; }

	public PlayerSave? LoadedGame { get; private set; }

	public ShellHangar Hangar { get; private set; }

	/// <summary>The game's script.dat, mission.str and player.mec, which a save copies out beside it.</summary>
	public ShellWorkingFiles WorkingFiles { get; set; }

	/// <summary>The campaign stage, the career's own 1-5, which picks the mission tab's palettes and movies.</summary>
	public int CampaignStage { get; private set; } = 1;

	/// <summary>The mission-within-stage counter, zero on the stage's first mission.</summary>
	public int MissionInStage { get; private set; }

	/// <summary>
	/// The game in progress from here on, from a load or a new career: maybe_HasGameInProgress (0048260a) set, and
	/// SAVE ungated.
	/// </summary>
	public void Adopt(PlayerSave game, ShellHangar hangar, ShellWorkingFiles files) {
		Hangar = hangar;
		LoadedGame = game;
		WorkingFiles = files;
		InProgress = true;
		CampaignStage = game.CampaignStage;
		MissionInStage = game.MissionInStage;
		_saveScreen.CanSave = true;
	}

	/// <summary>
	/// Shell_SetCampaignMode (0040e69e), the mode write the main menu's handlers make: the mode, and prefs.cfg option 42 set to
	/// it without its handler and written alone. The tabs are regated by the strip refresh the frame comes
	/// back up through (TabNavigation.ReturnToFrame), the strip being hidden until then.
	/// </summary>
	public void SetMode(ShellCampaignMode newMode) {
		Mode = newMode;
		Options.Set(CampaignModeOption, (byte)newMode, apply: false);
		Options.Save([CampaignModeOption]);
	}

	public bool ManualWeaponBuild() => Options[WeaponsBuildingOption] != 0;

	/// <summary>
	/// The game the shell's memory holds, which a new career keeps parts of: the last one loaded or started,
	/// or none since the startup. The slot opened at startup for the repair screen is not one.
	/// </summary>
	public PlayerSave? HeldGame() => InProgress ? LoadedGame : null;

	/// <summary>
	/// Game_SaveSlot (0040e37b): the live game, as the screens have left it, written as a slot. Nothing
	/// without a game in progress. Returns the slot's new directory entry, which the screen now shows.
	/// </summary>
	public ShellSaveSlot? Save(int slot, string? label) {
		if (!InProgress || LoadedGame == null) {
			return null;
		}

		Hangar.Store(LoadedGame);
		bool training = Mode == ShellCampaignMode.Training;
		var entry = ShellSaveSlots.SaveGame(_installRoot, _saveScreen.Slots, slot, label, training, LoadedGame,
			WorkingFiles, out string? failure);
		int written = slot == CurrentGameSlot && training ? CurrentGameSlot + 1 : slot;
		if (entry == null) {
			Console.WriteLine($"Could not save slot {written}: {failure}");
			return null;
		}

		_saveScreen.SetSlot(written, entry);
		if (written == CurrentGameSlot) {
			_mainMenu.CanContinue = true;
		}

		Console.WriteLine($"Saved {entry.FileName} as \"{entry.Label.Trim()}\" in {ShellSaveSlots.Directory(_installRoot)}.");
		return entry;
	}

	/// <summary>Game_SaveSlot(10, NULL), the current-game autosave.</summary>
	public void AutoSave() => Save(CurrentGameSlot, null);
}
