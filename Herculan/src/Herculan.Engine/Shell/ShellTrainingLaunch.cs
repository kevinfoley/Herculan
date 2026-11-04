using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// What the practice screen's <c>Begin Mission</c>, or <c>INSTANT ACTION</c>, hands the simulator: the <c>script.dat</c> written
/// beside the rest of the handoff, the mission it came from, and the career it was built for — the game, which the
/// shell's exit autosaves as slot 11, and its hangar, which <see cref="ShellHangar.Store"/> writes back into it.
/// </summary>
public sealed record ShellTrainingHandoff(string ScriptPath, string MissionPath, PlayerSave Game, ShellHangar Hangar,
	int SquadPositions);

/// <summary>
/// Told what a mission load is about to hand <see cref="MissionGenerator.Load"/>: the <c>.MSN</c>, its <c>.ENG</c> or
/// null, the campaign flags as seeded, before the load's row 2 clears any, and the header words the load writes over
/// once it has generated (<see cref="MissionGeneration.WriteHeader"/>). The flags are the load's own array, which it
/// goes on to change.
/// </summary>
public delegate void MissionGenerating(byte[] msn, byte[]? text, short[] flags, short?[] header);

/// <summary>
/// <c>Begin Mission</c>'s path from the button to the simulator, in training mode, which
/// <c>INSTANT ACTION</c> takes too on a row past the list: a new career
/// (<c>Game_NewCareer</c>, <c>0040e2ed</c>) on stage 0 at the lit row, that mission loaded
/// (<c>MsnGen_LoadMission</c>, <c>0041c73d</c>) with the squad built from its own group 0, and the
/// handoff exported (<c>Game_ExportMissionHandoff</c>, <c>0040f0d4</c>). The sequence is
/// docs/retail/shell/main-menu.md#starting-a-practice-mission's; the mission load is
/// <see cref="MissionGenerator"/>.
///
/// <para>Every random draw goes through the one generator VSHELL keeps for its whole run, in the
/// original's order, so a given seed builds the same squad and the same mission the original does.</para>
/// </summary>
public static class ShellTrainingLaunch {
	/// <summary>The pilot name both training paths pass <c>Game_NewCareer</c>.</summary>
	public const string TraineeName = "TRAINEE";

	/// <summary>
	/// VSHELL's generator at <c>0x482325</c> as its startup leaves it: DBSIM's own seed table
	/// (<c>ShellRandom_Reset</c> (<c>00465950</c>) copies the same 112 bytes from <c>0047f7b4</c>), advanced
	/// <c>GetTickCount() &amp; 0x7f</c> times (<c>ShellRandom_Seed</c> (<c>0046597c</c>), called at <c>004075a1</c>).
	/// </summary>
	public static SimRandom StartupRandom() {
		var random = new SimRandom();
		for (int step = Environment.TickCount & 0x7f; step > 0; step--) {
			random.Next();
		}

		return random;
	}

	/// <summary>
	/// The practice screen's five options, as <c>MsnGen_LoadMission</c> copies them into the header:
	/// ammunition, damage, difficulty and time of day, <c>prefs.cfg</c> options <c>0x25</c>-<c>0x27</c> and
	/// <c>0x29</c>. The Herc Type option, <c>0x28</c>, picks the player's machine instead.
	/// </summary>
	private const int AmmoOption = 0x25;
	private const int DamageOption = 0x26;
	private const int DifficultyOption = ShellPracticeScreen.DifficultyOption;
	private const int TimeOfDayOption = 0x29;

	/// <summary>The first practice row whose player takes the Herc Type option's chassis rather than the mission's own.</summary>
	private const int FirstChosenChassisRow = 4;

	/// <summary>
	/// The nine stock fits a chosen chassis is built from — the table at <c>004706fc</c>, in type order.
	/// See docs/retail/formats/herc-catalogs.md.
	/// </summary>
	private static readonly string[] StockFits = {
		"INI_OUTL.DAT", "INI_RAPT.DAT", "INI_TOMA.DAT", "INI_SAMS.DAT", "INI_COLO.DAT",
		"INI_APOC.DAT", "INI_OGRE.DAT", "INI_MAVR.DAT", "INI_RAZR.DAT",
	};

	/// <summary>
	/// Both steps in one, as the shell takes them without <c>-@</c>: <see cref="NewCareer"/> on the row, then
	/// <see cref="Load"/> of its mission. <paramref name="random"/> is VSHELL's generator, which both draw from.
	/// </summary>
	public static ShellTrainingHandoff? Write(string directory, GameContent content, SimulatorPreferences options,
			int row, bool instantAction, SimRandom random, short[] clearList, PlayerSave? held, out string? failure,
			MissionGenerating? generating = null, bool ownWorld = false) {
		int Roll(short bound) => random.NextBelow(bound);
		return NewCareer(content, options, row, Roll, held, out failure) is var (game, hangar)
			? Load(directory, content, options, row, MissionPath(content, 0, row), instantAction, Roll, clearList, game, hangar,
				out failure, generating, ownWorld)
			: null;
	}

	/// <summary>
	/// <c>Game_NewCareer("TRAINEE", option 0x27)</c> in training mode, its position step
	/// <c>Career_SeedPosition</c> (<c>00412a2f</c>) putting it on stage 0 at <paramref name="row"/>, and the hangar it
	/// leaves. The step ends by showing the mission-name dialog, whose button the mission load is (<see cref="Load"/>).
	/// <paramref name="held"/> is the game the shell's memory holds, as <see cref="ShellCampaignLaunch.NewCareer"/> takes
	/// it. Null, with the reason, when the install lacks a file the original would open.
	/// </summary>
	public static (PlayerSave Game, ShellHangar Hangar)? NewCareer(GameContent content, SimulatorPreferences options, int row,
			Func<short, int> roll, PlayerSave? held, out string? failure) {
		if (ShellCampaignLaunch.NewCareer(content, TraineeName, options[DifficultyOption], ShellCampaignMode.Training, roll,
				held, out failure) is not { } game) {
			return null;
		}

		game.CampaignStage = 0;
		game.MissionInStage = (short)row;
		return (game, ShellHangar.From(game));
	}

	/// <summary>
	/// The training half of <c>MsnGen_LoadMission</c> (<c>0041c73d</c>) for <paramref name="game"/>, which
	/// <see cref="NewCareer"/> started, with the handoff exported (<c>Game_ExportMissionHandoff</c>, <c>0040f0d4</c>) into
	/// <paramref name="directory"/>. <paramref name="missionPath"/> is the career position's mission
	/// (<see cref="MissionPath"/>), as <c>Career_LoadCurrentMission</c> (<c>0044d4cc</c>) loads it, or the DEBUG
	/// dialog's typed one. <paramref name="row"/> is the practice screen's lit row, <c>PracticeScreen_SelectedRow</c>
	/// (<c>00479bb8</c>), which picks the player's machine. <paramref name="clearList"/> is the row-2 clear list the shell
	/// keeps across loads (<see cref="MissionGenerator.Load"/>), and <paramref name="instantAction"/> is
	/// <c>InstantAction_Active</c> (<c>0047363c</c>), which <c>INSTANT ACTION</c> sets. <paramref name="generating"/> is
	/// told what the mission load is handed, and <paramref name="ownWorld"/> keeps the mission's own theater and time of day
	/// (<see cref="PracticeHeader"/>). Null, with the reason, when the install lacks a file the original would open.
	/// </summary>
	public static ShellTrainingHandoff? Load(string directory, GameContent content, SimulatorPreferences options, int row,
			string? missionPath, bool instantAction, Func<short, int> roll, short[] clearList, PlayerSave game,
			ShellHangar hangar, out string? failure, MissionGenerating? generating = null, bool ownWorld = false) {
		if (missionPath == null) {
			failure = $"gam\\career.dat or missions.bin has no stage-0 mission {row}.";
			return null;
		}

		if (ReadMission(content, missionPath) is not (byte[] msn, var text)) {
			failure = $"{missionPath} is not in any mounted archive.";
			return null;
		}

		return Load(directory, content, options, row, missionPath, msn, text, instantAction, roll, clearList, game, hangar,
			out failure, generating, ownWorld);
	}

	/// <summary>
	/// <see cref="Load(string, GameContent, SimulatorPreferences, int, string?, bool, Func{short, int}, short[], PlayerSave,
	/// ShellHangar, out string?, MissionGenerating?)"/> for a mission already read: the <c>.MSN</c> <paramref name="msn"/>
	/// and its text <paramref name="text"/>, or null, named <paramref name="missionPath"/>. The original reads every mission
	/// from its archives; this engine's mission editor hands one it holds in memory (<see cref="MissionFileLaunch.OpenNew"/>).
	/// <paramref name="ownWorld"/> keeps the mission's own theater and time of day (<see cref="PracticeHeader"/>), and
	/// <paramref name="wholeSquad"/> fields every squadmate (<see cref="BuildSquad"/>).
	/// </summary>
	public static ShellTrainingHandoff? Load(string directory, GameContent content, SimulatorPreferences options, int row,
			string missionPath, byte[] msn, byte[]? text, bool instantAction, Func<short, int> roll, short[] clearList,
			PlayerSave game, ShellHangar hangar, out string? failure, MissionGenerating? generating = null, bool ownWorld = false,
			bool wholeSquad = false) {
		// MsnGen_SeedCampaignFlags: a training load clears the flag array first.
		var flags = new short[MissionGenerator.CampaignFlagCount];
		SeedDrawnFlags(flags, roll);

		var header = PracticeHeader(options, ownWorld);
		generating?.Invoke(msn, text, flags, header);
		var mission = MissionGenerator.Load(msn, text, flags, clearList, roll);
		MissionGeneration.WriteHeader(mission.Header, header);

		string scriptPath = CaseInsensitivePath.Combine(directory, MissionLoader.ScriptFileName);
		if (!ShellWorkingFiles.TryWrite(() => {
				Directory.CreateDirectory(directory);
				ShellWorkingFiles.WriteMissionFile(scriptPath, mission.WriteScriptDat());
				ShellWorkingFiles.WriteMissionFile(CaseInsensitivePath.Combine(directory, MissionLoader.TextFileName), mission.WriteMissionText());
			}, out failure)) {
			return null;
		}

		int positions = BuildSquad(content, options, row, instantAction, mission, hangar, wholeSquad);

		// The flag array as the load left it is the career's.
		for (int i = 0; i < flags.Length; i++) {
			game.SetCampaignFlag(i, flags[i]);
		}

		var flagBytes = new byte[flags.Length * 2];
		Buffer.BlockCopy(flags, 0, flagBytes, 0, flagBytes.Length);
		if (!ShellWorkingFiles.TryWrite(() => {
				File.WriteAllBytes(CaseInsensitivePath.Combine(directory, ShellMissionLaunch.MissionVarFileName), flagBytes);
				File.WriteAllBytes(CaseInsensitivePath.Combine(directory, MissionLoader.PlayerFileName), ShellMissionLaunch.ExportPlayerMec(hangar));
			}, out failure)) {
			return null;
		}

		return new ShellTrainingHandoff(scriptPath, missionPath, game, hangar, positions);
	}

	/// <summary>
	/// The squad half of the training load, between the write of <c>script.dat</c> and the export: the positions in play from
	/// <paramref name="mission"/>'s group 0, the player's machine into bay 0, and a machine, a bay and a position for each
	/// wingman, into <paramref name="hangar"/>, which <see cref="NewCareer"/> left. Returns the positions in play. See
	/// docs/retail/shell/main-menu.md#starting-a-practice-mission.
	///
	/// <para><paramref name="wholeSquad"/> puts the last squadmate on strength too, which retail's build never does: this
	/// engine's departure for a mission <c>gam\career.dat</c> does not list, which retail has no path for (the user's
	/// choice, <c>KNOWN_ISSUES.md</c>). The practice missions and the demos keep the retail build.</para>
	/// </summary>
	internal static int BuildSquad(GameContent content, SimulatorPreferences options, int row, bool instantAction,
			MissionGenerator mission, ShellHangar hangar, bool wholeSquad) {
		int positions = mission.SquadPositions;
		hangar.SetPositionsInPlay(positions);

		var player = PilotsMissionMachine(row, instantAction)
			? Machine(mission.Herc(mission.SquadMember(0)))
			: StockFit(content, options[ShellPracticeScreen.HercTypeOption]);
		if (player != null) {
			hangar.Deliver(0, player);
		}

		// The wingmen, one per further squad position: squad member i takes bay i + 1 and position i + 1.
		// The on-strength update between runs on position i, where member i - 1 now stands, so each
		// member goes on strength one iteration late and the last never does. See the doc.
		var squad = hangar.SquadMembers;
		for (int member = 0; member < positions - 1 && member < squad.Count; member++) {
			if (Machine(mission.Herc(mission.SquadMember(member + 1))) is { } machine) {
				hangar.Deliver(member + 1, machine);
			}

			ShellHangar.SetBay(squad[member], member + 1);
			hangar.UpdateOnStrength(member);
			hangar.SetSquadPosition(member, member + 1);
		}

		// The update the loop owes the last member, on the position it was given.
		int lastPosition = Math.Min(positions - 1, squad.Count);
		if (wholeSquad && lastPosition > 0) {
			hangar.UpdateOnStrength(lastPosition);
		}

		return positions;
	}

	/// <summary>
	/// <c>MsnGen_BuildPlayerHerc</c> (<c>0041c58d</c>)'s choice: on the first four rows, and in <c>INSTANT ACTION</c>, the
	/// player pilots group 0's first member; on the rest, a stock fit of the chassis the Herc Type option holds.
	/// </summary>
	public static bool PilotsMissionMachine(int row, bool instantAction) => row < FirstChosenChassisRow || instantAction;

	/// <summary>The theater a training load writes over the mission's own, whatever it patched.</summary>
	public const short PracticeTheater = 1;

	/// <summary>
	/// The header words a training load writes over the loaded header: word 0 set to <see cref="PracticeTheater"/>, then the ammunition, damage,
	/// difficulty and time-of-day options into words 5 to 7 and 9, and word 8 cleared. <paramref name="ownWorld"/> leaves words
	/// 0 and 9 as the mission sets them, so it plays on its own theater and time of day: this engine's departure for a mission
	/// played from the mission editor (docs/herculan/editor-design.md#saving-and-playing), which retail has no path for.
	/// </summary>
	private static short?[] PracticeHeader(SimulatorPreferences options, bool ownWorld) => [
		ownWorld ? null : PracticeTheater, null, null, null, null,
		options[AmmoOption], options[DamageOption], options[DifficultyOption], 0, ownWorld ? null : options[TimeOfDayOption],
	];

	/// <summary>
	/// Flag 3's source, <c>00482606</c>, which the startup memset from <c>MissionScreenView</c> clears.
	/// Whether anything writes it is open — see docs/retail/shell/campaign-loop.md#open.
	/// </summary>
	private const short UnknownFlag3 = 0;

	/// <summary>
	/// The half of <c>MsnGen_SeedCampaignFlags</c> (<c>0040e94e</c>) both modes run: flag 3 from
	/// <c>00482606</c>, then flags 4, 5 and 6 a draw below 12 each.
	/// </summary>
	internal static void SeedDrawnFlags(short[] flags, Func<short, int> roll) {
		flags[3] = UnknownFlag3;
		for (int flag = 4; flag <= 6; flag++) {
			flags[flag] = (short)roll(12);
		}
	}

	/// <summary>
	/// The career position's mission — <c>Career_LoadCurrentMission</c> (<c>0044d4cc</c>) reads the name
	/// <c>CareerDat_ReadStage</c> resolved through <c>missions.bin</c>, a path such as <c>MSN\TRAIN1.MSN</c>.
	/// Null for a position <c>gam\career.dat</c> does not have.
	/// </summary>
	internal static string? MissionPath(GameContent content, int stage, int mission) =>
		CareerStages(content) is { } stages && stage >= 0 && stage < stages.Count
			&& mission >= 0 && mission < stages[stage].Missions.Length
			? ShellText.Load(content, "MISSIONS.BIN")?.Text(stages[stage].Missions[mission])
			: null;

	/// <summary><c>gam\career.dat</c>'s stages, as <c>LoadCareerDat</c> (<c>00412906</c>) reads them, or null without one.</summary>
	internal static List<(short CampaignIndex, int[] Missions)>? CareerStages(GameContent content) =>
		content.Read(ShellRepairCosts.CatalogFolder, "CAREER.DAT") is { } bytes
			? new CareerDataTransformer().Parse(bytes)?.Stages
			: null;

	/// <summary>
	/// A mission and its text as <c>MsnGen_ParseMsnFile</c> opens them: the <c>.MSN</c> at
	/// <paramref name="missionPath"/>, and the text <c>Msn_LoadEngText</c> (<c>0041768c</c>) finds by
	/// swapping everything from the name's first <c>.</c> for the shell language's extension, or null text when
	/// there is none. Null when the mission itself is not in the archives.
	/// </summary>
	internal static (byte[] Msn, byte[]? Text)? ReadMission(GameContent content, string missionPath) {
		int split = missionPath.LastIndexOf('\\');
		string folder = split < 0 ? string.Empty : missionPath[..split];
		string name = missionPath[(split + 1)..];
		if (content.Read(folder, name) is not { } msn) {
			return null;
		}

		int dot = name.IndexOf('.');
		string stem = dot < 0 ? name : name[..dot];
		return (msn, content.Read(folder, stem + MissionTextExtension(content.Language)));
	}

	/// <summary><c>Msn_LoadEngText</c>'s extension for <paramref name="language"/>'s mission text.</summary>
	internal static string MissionTextExtension(GameLanguage language) => language switch {
		GameLanguage.French => ".FRE",
		GameLanguage.German => ".GER",
		_ => ".ENG",
	};

	/// <summary>
	/// A squad machine from a mission's roster record, as <c>MsnGen_LoadMission</c> builds one:
	/// <c>Herc_SetType</c>, then each slot below the capacity whose weapon is not 0 fitted with it and its
	/// ammunition type — a <c>-1</c> included, as the original does.
	/// </summary>
	private static ShellBayMachine? Machine(MissionHerc? herc) {
		if (herc == null) {
			return null;
		}

		var machine = ShellBayMachine.Delivered(herc.Chassis);
		for (int slot = 0; slot < machine.MountCapacity && slot < herc.Weapons.Length; slot++) {
			if (herc.Weapons[slot] != 0) {
				machine.Fit(slot, herc.Weapons[slot], herc.AmmoTypes[slot]);
			}
		}

		return machine;
	}

	/// <summary>
	/// The stock fit of a chassis, <c>gam\ini_*.dat</c> read through <c>HercRecord_ReadCatalogForm</c>; null for a chassis
	/// past the nine, or an install without the file.
	/// </summary>
	public static ShellBayMachine? StockFit(GameContent content, int chassis) =>
		chassis >= 0 && chassis < StockFits.Length
			&& content.Read(ShellRepairCosts.CatalogFolder, StockFits[chassis]) is { } bytes
			&& new InitHercTransformer().Parse(bytes)?.Data is { } record
			? ShellBayMachine.FromCatalog(record)
			: null;
}
