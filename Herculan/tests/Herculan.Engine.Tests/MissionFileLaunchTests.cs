using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Engine.Install;
using Herculan.Engine.Shell;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MissionFileLaunch.Open"/>, and the editor's open through it, against a real install. Skips silently without one, as the rest of the suite does.
/// </summary>
public class MissionFileLaunchTests {
	/// <summary>
	/// Catches a document whose generation differs from the handoff written beside it — VSHELL's generator copied before
	/// the flag seeding's draws, or the practice header left out — so the scene the editor builds from the document would
	/// not be the mission the handoff's squad was built for. TRAIN5 draws two variant tables.
	///
	/// <para>Also catches a squad the editor builds again (<see cref="MissionSquad.PlayerMec"/>) that is not the one the
	/// handoff's <c>player.mec</c> holds — its new career drawing from VSHELL's generator copied after the open's draws
	/// began, or <see cref="ShellTrainingLaunch.BuildSquad"/> parting from the launch's — so an edit would rewrite the
	/// squad the player takes into the mission. On TRAIN5 the player pilots the Herc Type's stock fit, with two wingmen from its group 0.</para>
	/// </summary>
	[Fact]
	public void OpenedDocumentGeneratesTheHandoffItOpened() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", nameof(OpenedDocumentGeneratesTheHandoffItOpened));
		// The shell's writer does not truncate, so a longer file left by an earlier run would keep its tail.
		foreach (string file in new[] { "script.dat", "mission.str" }) {
			if (File.Exists(Path.Combine(directory, file))) {
				File.Delete(Path.Combine(directory, file));
			}
		}

		// The game's draws, not the editor's lowest ones, under which a generator copied at the wrong moment draws the same.
		string? script = MissionFileLaunch.Open(GameContent.MountShell(root), root, "TRAIN5.MSN", directory,
			ShellTrainingLaunch.StartupRandom(), out var document, out var squad, out string? failure);

		Assert.True(script != null, failure);
		Assert.Equal(File.ReadAllBytes(script), document!.ScriptBytes);
		Assert.Equal(File.ReadAllBytes(Path.Combine(directory, "mission.str")), document.MissionText);
		Assert.Equal(File.ReadAllBytes(Path.Combine(directory, "player.mec")), squad!.PlayerMec(document.Generator));
	}

	/// <summary>
	/// The editor's open of a practice mission (<see cref="MissionOpener.Open"/>), whose document shows the theater and
	/// time of day the mission's row 2 patches while the handoff the launch wrote keeps theater 1 and the rest of the
	/// practice header. Catches the editor's generation writing theater 1 over a mission that is not a campaign one again,
	/// a game launch losing that retail overwrite, and the editor dropping the practice screen's other header words.
	/// TRAIN5 patches a theater other than 1.
	/// </summary>
	[Fact]
	public void EditorShowsAPracticeMissionsOwnTheaterWhereTheLaunchWritesTheater1() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", nameof(EditorShowsAPracticeMissionsOwnTheaterWhereTheLaunchWritesTheater1));
		foreach (string file in new[] { "script.dat", "mission.str" }) {
			if (File.Exists(Path.Combine(directory, file))) {
				File.Delete(Path.Combine(directory, file));
			}
		}

		string? script = MissionOpener.Open(GameContent.MountShell(root), root, "TRAIN5.MSN", directory, out var document, out _,
			out string? failure);

		Assert.True(script != null, failure);
		var handoff = (ScriptDat)new ScriptDatTransformer().Parse(File.ReadAllBytes(script))!;
		var shown = document!.Script;
		Assert.Equal(ShellTrainingLaunch.PracticeTheater, handoff.TheaterIndex);
		Assert.Equal(Patched(document, MissionOpener.TheaterWord), shown.TheaterIndex);
		Assert.NotEqual(handoff.TheaterIndex, shown.TheaterIndex);
		Assert.Equal(Patched(document, MissionOpener.VariantWord), shown.TheaterVariant);
		for (int offset = 0; offset < ScriptDat.HeaderSize; offset++) {
			if (offset / 2 != MissionOpener.TheaterWord && offset / 2 != MissionOpener.VariantWord) {
				Assert.Equal(handoff.HeaderBytes[offset], shown.HeaderBytes[offset]);
			}
		}
	}

	/// <summary>
	/// The editor's open of a campaign mission and a sync after it (<see cref="SceneSync.Sync"/>). Catches a sync that builds
	/// every object again instead of standing the one already built for the same record (the open's objects not filed for
	/// reuse), which leaves the drawing and the undo history holding objects no longer in the scene, and an object standing
	/// anywhere but where the layout places it. C1_07's hercs 69 to 71 carry variations that hold as it opens.
	/// </summary>
	[Fact]
	public void ASyncKeepsEachObjectWhereTheLayoutPlacesIt() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", nameof(ASyncKeepsEachObjectWhereTheLayoutPlacesIt));
		foreach (string file in new[] { "script.dat", "mission.str" }) {
			if (File.Exists(Path.Combine(directory, file))) {
				File.Delete(Path.Combine(directory, file));
			}
		}

		var shell = GameContent.MountShell(root);
		string? script = MissionOpener.Open(shell, root, "C1_07.MSN", directory, out var document, out var squad, out string? failure);
		Assert.True(script != null, failure);
		var simulator = GameContent.MountSimulator(root);
		var archived = new ArchivedMissions(() => shell);
		var mission = EditorMission.Open(simulator, script, "C1_07", directory, document!, squad!, () => shell,
			new CybridWeapons(simulator, archived), new ZoneCatalog(simulator, archived));
		Assert.All(new short[] { 69, 70, 71 }, guid => Assert.NotNull(mission.Index.ObjectOf(new ObjectKey(12, guid))));

		var opened = mission.Index.Objects.ToList();
		mission.Sync.Sync();
		Assert.Equal(opened.Count, mission.Index.Objects.Count);
		for (int i = 0; i < opened.Count; i++) {
			var placed = mission.Index.Objects[i];
			Assert.Same(opened[i], placed);
			var planned = mission.Index.Planned(placed)!.Place.Position;
			Assert.Equal((planned.X, planned.Y), (placed.Object.Position.X, placed.Object.Position.Y));
		}
	}

	// Header word <word> as the document's row 2 patches it: the supplying record's, else the word's unset value.
	private static short Patched(MissionDocument document, int word) =>
		document.Provenance.HeaderWord(word) is { } supplier
			? document.Mission.SettingsPatches![supplier.Index].Data[MissionSettingsPatch.HeaderWordsStart + word]
			: MissionSettingsPatch.Unset(word);
}
