using HercWorks.Core.Data.File.Sav;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// The shell's side of the campaign loop (docs/retail/shell/campaign-loop.md): where the game in progress comes from — a
/// slot, a new career, a training launch, the simulator's return — and the missions it hands the simulator. What the
/// screens do at each step is the host's; this returns the outcome of each instead of acting on a window.
/// </summary>
public sealed class ShellCampaignLoop {
	private readonly string _installRoot;
	private readonly GameContent _content;
	private readonly GameInProgress _game;
	private readonly ShellSaveScreen _saveScreen;

	// VSHELL's one generator, seeded once at startup, and the row-2 flag clear list, which the
	// original keeps from one mission load to the next.
	private readonly SimRandom _random = ShellTrainingLaunch.StartupRandom();
	private readonly short[] _clearList = new short[HercWorks.Core.Io.Transform.Common.MissionGenerator.ClearListLength];

	// INSTANT ACTION's InstantAction_Active (0047363c), which nothing clears.
	private bool _instantActionSet;

	public ShellCampaignLoop(string installRoot, GameContent content, GameInProgress game, ShellSaveScreen saveScreen) {
		_installRoot = installRoot;
		_content = content;
		_game = game;
		_saveScreen = saveScreen;
	}

	/// <summary>
	/// A game adopted as the game in progress, from a load or a new career: the screens that show it rebuild from it
	/// here, before anything else reads it.
	/// </summary>
	public event Action<PlayerSave, ShellWorkingFiles>? Adopted;

	/// <summary>A slot <see cref="LoadSlot"/> read, raised once its game is adopted.</summary>
	public event Action<ShellSaveSlot>? SlotLoaded;

	// The install's data\, where the working files and the mission handoff are written and the results come back.
	private string DataDirectory => ShellWorkingFiles.DataDirectory(_installRoot);

	/// <summary>Sets InstantAction_Active (0047363c), which the training launches read and nothing clears.</summary>
	public void SetInstantAction() => _instantActionSet = true;

	/// <summary>
	/// Game_LoadSlot (0040e4f2): a slot in use read in whole — the hangar, the career and its mission, and
	/// the game in progress that saving needs — and Career_LoadSlot's three working files copied into data\.
	/// Slot 10 is slot 11 in training, as it is to Game_SaveSlot. The mission map is rebuilt, here on the
	/// briefing's next visit, and the briefing's and debrief's movies play again (DAT_004778ab and
	/// DAT_004778ac cleared). Returns false for a slot not in use, which the original refuses, or one that
	/// cannot be read.
	/// </summary>
	public bool LoadSlot(int slot) {
		if (slot == GameInProgress.CurrentGameSlot && _game.Mode == ShellCampaignMode.Training) {
			slot = GameInProgress.CurrentGameSlot + 1;
		}

		if (_saveScreen.Slots.ElementAtOrDefault(slot) is not { InUse: true } entry
				|| ShellSaveSlots.LoadSave(_installRoot, entry.FileName) is not { } restored) {
			return false;
		}

		Adopt(restored, ShellHangar.From(restored), ShellSaveSlots.CopyWorkingFilesIn(_installRoot, slot));
		SlotLoaded?.Invoke(entry);
		return true;
	}

	/// <summary>
	/// Shell_BuildScreensAndStart's -X3 and -X4 arm (004012b0): Game_LoadSlot(10) and then
	/// Game_ProcessMissionResults (0040eae7) over the results.dat and mission.var the simulator left in data\,
	/// which the load's copies do not touch. Returns the debrief, whose state says where the debrief goes next
	/// (docs/retail/shell/campaign-loop.md#where-the-debrief-goes-next). A slot 10 not in use, or no results, cannot
	/// come from a mission this shell launched; that, and a debrief that fails, is reported and returns null, and
	/// the menu comes up.
	/// </summary>
	public ShellDebriefResult? ReturnFromMission() {
		string resultsPath = Path.Combine(DataDirectory, MissionResults.FileName);
		string countersPath = Path.Combine(DataDirectory, MissionLoader.CountersFileName);
		if (!LoadSlot(GameInProgress.CurrentGameSlot) || _game.LoadedGame == null || !File.Exists(resultsPath) || !File.Exists(countersPath)) {
			Console.WriteLine($"Back from the mission, but slot 10 or {resultsPath} could not be read — main menu.");
			return null;
		}

		var result = ShellDebrief.Process(_game.LoadedGame, _game.Hangar, _content, File.ReadAllBytes(countersPath),
			File.ReadAllBytes(resultsPath), _game.Mode == ShellCampaignMode.Campaign, _game.Options[GameInProgress.RepairOption],
			_game.ManualWeaponBuild(), bound => _random.NextBelow(bound), out string? failure);
		if (result == null) {
			Console.WriteLine($"Debrief: {failure} Main menu.");
			return null;
		}

		Console.WriteLine($"Debrief: {(result.Outcome != 0 ? "success" : "failure")}, {result.SalvageAwarded} kg salvage "
			+ $"and {result.SalvageItems} weapon(s) recovered, {result.MachinesScrapped} machine(s) scrapped, "
			+ $"{result.PilotsLost} pilot(s) lost; game state {result.State?.ToString() ?? "unchanged"}.");
		return result;
	}

	/// <summary>
	/// ACCEPT, Registration_OnAccept (0043c0fb), after the campaign map's first-show flag (DAT_004778aa) is cleared:
	/// gam\herc_inf.dat reloaded, Game_NewCareer(name, skill) in campaign mode, and MissionScreenView from the
	/// position — the map, on stage 1 mission 0. The career's position step posts the developer's mission-name
	/// dialog's Use Default click, which the original delivers once the handler has returned and which runs
	/// Career_LoadCurrentMission; this goes straight there, as LaunchTraining does. Its campaign end rebuilds the
	/// map and the texts (here on the adopt) and stages slot 10's summary, which no save row shows; the screen
	/// then puts the frame up with the strip regated and calls Mission_ShowView(MissionScreenView, 1). Nothing is
	/// saved: slot 10 is first written by the next autosave. Returns whether the career started.
	/// </summary>
	public bool StartCampaign(string name, int skill) {
		int Roll(short bound) => _random.NextBelow(bound);
		if (ShellCampaignLaunch.NewCareer(_content, name, skill, ShellCampaignMode.Campaign, Roll,
				_game.HeldGame(), out string? failure) is not { } game) {
			Console.WriteLine($"Accept: {failure} No career started; main menu.");
			return false;
		}

		var careerHangar = ShellHangar.From(game);
		if (ShellCampaignLaunch.LoadCareerMission(DataDirectory, _content, game, careerHangar, _clearList, Roll, out failure)
				is not { } mission) {
			Console.WriteLine($"Accept: {failure} No career started; main menu.");
			return false;
		}

		Adopt(game, careerHangar, ShellWorkingFiles.In(DataDirectory));
		Console.WriteLine($"New campaign for {name}, skill {skill}: {mission.MissionPath}, "
			+ $"{mission.SquadPositions} squad position(s), {game.SalvageTotal} kg salvage; working files in {DataDirectory}.");
		return true;
	}

	/// <summary>
	/// Game_NewCareer("TRAINEE", option 0x27) in training mode on stage 0's mission at row: the career
	/// started, its mission loaded and the handoff written, for the shell to close on exit code 2. The
	/// original gets from the career to the load through the developer's mission-name dialog, which
	/// clicks its own Use Default at once; this goes straight there. The career is the game in progress,
	/// which the loop exit's autosave writes as slot 11 with the handoff's three working files. Returns
	/// the launch, or null when it could not be written.
	/// </summary>
	public ShellLaunch? LaunchTraining(int row, string label) {
		var handoff = ShellTrainingLaunch.Write(DataDirectory, _content, _game.Options, row, _instantActionSet,
			_random, _clearList, _game.HeldGame(), out string? failure);
		if (handoff == null) {
			Console.WriteLine($"{label}: {failure}");
			return null;
		}

		Adopt(handoff.Game, handoff.Hangar, ShellWorkingFiles.In(DataDirectory));

		var squad = Enumerable.Range(0, ShellHangar.BayCount)
			.Select(bay => handoff.Hangar.Bay(bay) is { } machine
				? $"bay {bay} chassis {machine.ChassisType}" + (handoff.Hangar.PilotFor(bay) is { } pilot ? $" ({pilot.Name})" : string.Empty)
				: null)
			.OfType<string>();
		Console.WriteLine($"{label} — {handoff.MissionPath}, {handoff.SquadPositions} squad position(s): "
			+ $"{string.Join(", ", squad)}; {handoff.Hangar.MachinesOnStrength} machine(s) going. "
			+ $"Handoff written to {DataDirectory}; launching the mission.");
		return new ShellLaunch(handoff.ScriptPath, DataDirectory);
	}

	/// <summary>
	/// Career_LoadCurrentMission's campaign load after a debrief, as StartCampaign runs it for a new career. Returns
	/// whether it loaded.
	/// </summary>
	public bool LoadNextCareerMission() {
		if (_game.LoadedGame == null) {
			return false;
		}

		var game = _game.LoadedGame;
		var hangar = _game.Hangar;
		if (ShellCampaignLaunch.LoadCareerMission(DataDirectory, _content, game, hangar, _clearList,
				bound => _random.NextBelow(bound), out string? failure) is not { } mission) {
			Console.WriteLine($"Next mission: {failure} Main menu.");
			return false;
		}

		Adopt(game, hangar, ShellWorkingFiles.In(DataDirectory));
		Console.WriteLine($"Next mission: {mission.MissionPath}, {mission.SquadPositions} squad position(s); "
			+ $"working files in {DataDirectory}.");
		return true;
	}

	/// <summary>
	/// REPLAY MISSION?'s Yes (ReplayDialog_OnYes, 0044cb44), after the dialog is down: slot 10 loaded again, for the
	/// shell to end on exit code 2 — so the simulator flies what the load's Career_LoadSlot copied in: the slot's
	/// script.dat, mission.str and player.mec, beside the mission.var the simulator itself last wrote, which no one
	/// rewrites. Returns the launch, or null when the slot cannot be read.
	/// </summary>
	public ShellLaunch? Replay() {
		if (!LoadSlot(GameInProgress.CurrentGameSlot)) {
			Console.WriteLine("Replay: slot 10 could not be read — main menu.");
			return null;
		}

		Console.WriteLine($"Replay: yes — slot 10's mission copied to {DataDirectory}; launching it.");
		return new ShellLaunch(_game.WorkingFiles.Script!, DataDirectory);
	}

	// The game in progress from here on, from a load or a new career — maybe_HasGameInProgress (0048260a) set, the briefing's and
	// debrief's movies to play again (DAT_004778ab and DAT_004778ac cleared), and the mission map rebuilt on
	// the briefing's next visit.
	private void Adopt(PlayerSave game, ShellHangar hangar, ShellWorkingFiles files) {
		_game.Adopt(game, hangar, files);
		Adopted?.Invoke(game, files);
	}
}
