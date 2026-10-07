using HercWorks.Core.Data.File.Sav;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The campaign loop's steps — a training launch, a new career, a save loaded back, the simulator's return — each
/// adopting its game as the game in progress and saying what follows, over a scratch install root so the real
/// install's <c>data\</c> and saves are never written. The game content is the real install's.
///
/// <para>Skips silently when no Earthsiege 2 install can be found.</para>
/// </summary>
public sealed class ShellCampaignLoopTests : IDisposable {
	private const int StrikeTrainingRow = 4;
	private const int CampaignModeOption = 42;

	private readonly string _root = Path.Combine(Path.GetTempPath(), "herculan-tests", "campaign-" + Guid.NewGuid().ToString("N"));
	private readonly List<(PlayerSave Game, ShellWorkingFiles Files)> _adopted = new();
	private readonly List<ShellSaveSlot> _loaded = new();

	public ShellCampaignLoopTests() {
		Directory.CreateDirectory(ShellWorkingFiles.DataDirectory(_root));
		Directory.CreateDirectory(ShellSaveSlots.Directory(_root));
	}

	public void Dispose() {
		try {
			Directory.Delete(_root, recursive: true);
		} catch (IOException) {
		}
	}

	/// <summary>
	/// A training launch writes its handoff into <c>data\</c>, adopts the TRAINEE career as the game in progress, and
	/// hands back the launch rather than closing anything.
	/// </summary>
	[Fact]
	public void ATrainingLaunchAdoptsItsCareerAndReturnsTheHandoff() {
		if (Loop(ShellCampaignMode.Training) is not var (loop, game)) {
			return;
		}

		var launch = loop.LaunchTraining(StrikeTrainingRow, "test");

		Assert.NotNull(launch);
		Assert.Equal(ShellWorkingFiles.DataDirectory(_root), launch.DataDirectory);
		Assert.True(File.Exists(launch.ScriptPath));
		Assert.True(game.InProgress);
		Assert.Same(game.LoadedGame, Assert.Single(_adopted).Game);
	}

	/// <summary>
	/// ACCEPT's new career starts on stage 1's first mission, with its working files in <c>data\</c>, and is the
	/// game in progress — SAVE ungated — without anything having been saved.
	/// </summary>
	[Fact]
	public void ANewCareerIsAdoptedOnTheFirstMission() {
		if (Loop(ShellCampaignMode.Campaign) is not var (loop, game)) {
			return;
		}

		Assert.True(loop.StartCampaign("TESTER", 1));

		Assert.True(game.InProgress);
		Assert.Equal(1, game.CampaignStage);
		Assert.Equal(0, game.MissionInStage);
		Assert.True(File.Exists(game.WorkingFiles.Script));
		Assert.Single(_adopted);
		Assert.Empty(Directory.GetFiles(ShellSaveSlots.Directory(_root), "GAME_*.SAV"));
	}

	/// <summary>A game saved to a slot loads back from it, and the load says which slot it read.</summary>
	[Fact]
	public void ASavedGameLoadsBackFromItsSlot() {
		if (Loop(ShellCampaignMode.Campaign) is not var (loop, game)) {
			return;
		}

		Assert.True(loop.StartCampaign("TESTER", 1));
		var saved = game.Save(3, " 4. TESTER");
		Assert.NotNull(saved);

		Assert.True(loop.LoadSlot(3));

		Assert.Equal(saved.FileName, Assert.Single(_loaded).FileName);
		Assert.Equal(2, _adopted.Count);
		Assert.Same(game.LoadedGame, _adopted[1].Game);
	}

	/// <summary>
	/// Slot 10, the current game, is slot 11 in training — to the load as to the autosave that wrote it.
	/// </summary>
	[Fact]
	public void TheCurrentGameIsSlotElevenInTraining() {
		if (Loop(ShellCampaignMode.Training) is not var (loop, game)) {
			return;
		}

		Assert.NotNull(loop.LaunchTraining(StrikeTrainingRow, "test"));
		game.AutoSave();

		Assert.True(loop.LoadSlot(GameInProgress.CurrentGameSlot));
		Assert.Equal("GAME_T.SAV", Assert.Single(_loaded).FileName);
	}

	/// <summary>
	/// Back from a mission with no slot 10 and no results there is nothing to debrief: the step says so, and the
	/// game in progress is not touched.
	/// </summary>
	[Fact]
	public void AReturnWithNothingToDebriefLeavesTheGameAlone() {
		if (Loop(ShellCampaignMode.Campaign) is not var (loop, game)) {
			return;
		}

		Assert.Null(loop.ReturnFromMission());
		Assert.False(game.InProgress);
		Assert.Empty(_adopted);
	}

	/// <summary>
	/// Shell_SetCampaignMode writes the mode into option 42; and with no game in progress there is nothing to save.
	/// </summary>
	[Fact]
	public void TheModeIsAnOptionAndNoGameSavesNothing() {
		if (Loop(ShellCampaignMode.Campaign) is not var (_, game)) {
			return;
		}

		game.SetMode(ShellCampaignMode.Training);
		Assert.Equal(ShellCampaignMode.Training, game.Mode);
		Assert.Equal((byte)ShellCampaignMode.Training, game.Options[CampaignModeOption]);

		Assert.Null(game.Save(3, " 4. NOBODY"));
		Assert.Empty(Directory.GetFiles(ShellSaveSlots.Directory(_root), "GAME_*.SAV"));
	}

	private (ShellCampaignLoop Loop, GameInProgress Game)? Loop(ShellCampaignMode mode) {
		if (GameInstall.Locate(null) is not { } install) {
			return null;
		}

		var slots = new List<ShellSaveSlot>();
		for (int i = 0; i < SaveSlotDirectory.PlayerSlotCount; i++) {
			slots.Add(new ShellSaveSlot($"GAME_{i}.SAV", $"{i + 1,2}. EMPTY", false, null));
		}

		slots.Add(new ShellSaveSlot("GAME_R.SAV", "RESUME", false, null));
		slots.Add(new ShellSaveSlot("GAME_T.SAV", "TRAINING", false, null));

		var saveScreen = new ShellSaveScreen(slots, canSave: false);
		var options = SimulatorPreferences.Defaults();
		var game = new GameInProgress(_root, saveScreen, new ShellMainMenu(false), options, mode, null, ShellHangar.From(null),
			ShellWorkingFiles.ForSlot(_root, -1));
		var loop = new ShellCampaignLoop(_root, GameContent.MountShell(install), game, saveScreen);
		loop.Adopted += (adopted, files) => _adopted.Add((adopted, files));
		loop.SlotLoaded += _loaded.Add;
		return (loop, game);
	}
}
