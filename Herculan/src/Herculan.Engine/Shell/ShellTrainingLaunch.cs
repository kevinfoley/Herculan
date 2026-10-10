using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct.Vshell.Sav;
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
	private const int DifficultyOption = 0x27;
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
			int row, bool instantAction, SimRandom random, short[] clearList, PlayerSave? held, out string? failure) {
		int Roll(short bound) => random.NextBelow(bound);
		return NewCareer(content, options, row, Roll, held, out failure) is var (game, hangar)
			? Load(directory, content, options, row, MissionPath(content, 0, row), instantAction, Roll, clearList, game, hangar,
				out failure)
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
	/// <c>InstantAction_Active</c> (<c>0047363c</c>), which <c>INSTANT ACTION</c> sets. Null, with the reason, when the
	/// install lacks a file the original would open.
	/// </summary>
	public static ShellTrainingHandoff? Load(string directory, GameContent content, SimulatorPreferences options, int row,
			string? missionPath, bool instantAction, Func<short, int> roll, short[] clearList, PlayerSave game,
			ShellHangar hangar, out string? failure) {
		if (missionPath == null) {
			failure = $"gam\\career.dat or missions.bin has no stage-0 mission {row}.";
			return null;
		}

		if (ReadMission(content, missionPath) is not (byte[] msn, var text)) {
			failure = $"{missionPath} is not in any mounted archive.";
			return null;
		}

		// MsnGen_SeedCampaignFlags: a training load clears the flag array first.
		var flags = new short[MissionGenerator.CampaignFlagCount];
		SeedDrawnFlags(flags, roll);

		var mission = MissionGenerator.Load(msn, text, flags, clearList, roll);
		var header = mission.Header;
		header[5] = options[AmmoOption];
		header[6] = options[DamageOption];
		header[7] = options[DifficultyOption];
		header[8] = 0;
		header[9] = options[TimeOfDayOption];
		header[0] = 1;

		Directory.CreateDirectory(directory);
		string scriptPath = Path.Combine(directory, MissionLoader.ScriptFileName);
		ShellWorkingFiles.WriteMissionFile(scriptPath, mission.WriteScriptDat());
		ShellWorkingFiles.WriteMissionFile(Path.Combine(directory, MissionLoader.TextFileName), mission.WriteMissionText());

		int positions = mission.SquadPositions;
		hangar.SetPositionsInPlay(positions);

		// MsnGen_BuildPlayerHerc (0041c58d): the first four rows, and INSTANT ACTION, fly group 0's first member; the rest fly
		// a stock fit of the chassis the Herc Type option holds.
		var player = row < FirstChosenChassisRow || instantAction
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

		// The flag array as the load left it is the career's.
		for (int i = 0; i < flags.Length; i++) {
			game.SetCampaignFlag(i, flags[i]);
		}

		var flagBytes = new byte[flags.Length * 2];
		Buffer.BlockCopy(flags, 0, flagBytes, 0, flagBytes.Length);
		File.WriteAllBytes(Path.Combine(directory, ShellMissionLaunch.MissionVarFileName), flagBytes);
		File.WriteAllBytes(Path.Combine(directory, MissionLoader.PlayerFileName), ShellMissionLaunch.ExportPlayerMec(hangar));

		failure = null;
		return new ShellTrainingHandoff(scriptPath, missionPath, game, hangar, positions);
	}

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
	/// <c>Squad_GenerateRoster</c> (<c>0040fa31</c>) — three squads of twelve, each from a shuffle of four
	/// and a shuffle of three and one skill draw per pilot, <c>Pilot_Init</c> (<c>0040fcd8</c>) writing each
	/// record with its rank seeded from the skill and <c>Pilot_SetDefaults</c> (<c>0040fd17</c>)'s bay
	/// <c>-1</c>, off strength, position <c>-1</c> and condition 100. The records come back in the save's
	/// order, squad by squad and row by row. See
	/// docs/retail/shell/campaign-loop.md#the-pilot-roster-is-generated-not-authored--squad_generateroster-0040fa31.
	/// </summary>
	internal static PilotEntry[] GenerateRoster(ShellText? names, Func<short, int> roll) {
		var records = new PilotEntry[SquadCount * PilotsPerSquad];
		for (int squad = 0; squad < SquadCount; squad++) {
			short[] columns = Shuffle(4, roll);
			short[] rows = Shuffle(3, roll);
			for (int row = 0; row < 3; row++) {
				for (int column = 0; column < 4; column++) {
					int rosterId = columns[column] + squad * 4;
					int nameIndex = rows[row] + rosterId * 3;
					int skill = SkillDraw(roll);
					records[squad * PilotsPerSquad + row * 4 + column] = NewPilot(names?.Text(nameIndex) ?? string.Empty,
						rosterId, nameIndex, skill, RankForSkill[skill]);
				}
			}
		}

		return records;
	}

	/// <summary>
	/// <c>Pilot_Init</c> (<c>0040fcd8</c>): the record's roster id, name index, name, skill and rank, then
	/// <c>Pilot_SetDefaults</c> (<c>0040fd17</c>) — no bay, off strength, no squad position, condition 100
	/// and every counter 0.
	/// </summary>
	internal static PilotEntry NewPilot(string name, int rosterId, int nameIndex, int skill, int rank) => new() {
		RosterId = (short)rosterId,
		NameIndex = (short)nameIndex,
		Name = name,
		Bay = -1,
		OnStrength = 0,
		Skill = PilotSkill.GetById((short)skill),
		SquadPosition = -1,
		Rank = PilotRank.GetById((short)rank),
		Condition = 100,
	};

	/// <summary>The squad block's shape: three squads of twelve pilot records.</summary>
	internal const int SquadCount = 3;
	internal const int PilotsPerSquad = 12;

	/// <summary>
	/// <c>Player_Create</c>'s name-index draw and <c>Game_NewCareer</c>'s salvage draw, both below 11 —
	/// the pool is <c>draw * 1000 + 100000</c> kilograms.
	/// </summary>
	internal const short PlayerNameIndexCount = 11;
	internal const short SalvageDrawCount = 11;

	/// <summary>The rank a roster pilot starts at for each skill, <c>0046f5f4</c> through <c>Pilot_RankForSkill</c> (<c>0040fa21</c>).</summary>
	private static readonly int[] RankForSkill = { 0, 1, 2, 2 };

	/// <summary>
	/// <c>Util_Shuffle</c> (<c>0040f95c</c>): each value in turn goes into the empty slot a draw counts to.
	/// </summary>
	private static short[] Shuffle(short length, Func<short, int> roll) {
		var slots = Enumerable.Repeat((short)-1, length).ToArray();
		for (short value = 0; value < length; value++) {
			int skip = roll((short)(length - value));
			int empty = 0;
			int at = 0;
			if (skip > 0) {
				do {
					if (slots[at] == -1) {
						empty++;
					}

					at++;
				} while (empty < skip);
			}

			while (slots[at] != -1) {
				at++;
			}

			slots[at] = value;
		}

		return slots;
	}

	/// <summary>
	/// <c>Pilot_DrawSkill</c> (<c>0040f9dc</c>) — a draw below 100 against the thresholds at <c>0046f5ec</c>: 70, 85, 95 and
	/// 100, so skill 0 seven times in ten.
	/// </summary>
	private static int SkillDraw(Func<short, int> roll) {
		int draw = roll(100);
		for (int skill = 0; skill < SkillThresholds.Length; skill++) {
			if (draw < SkillThresholds[skill]) {
				return skill;
			}
		}

		return 0;
	}

	private static readonly int[] SkillThresholds = { 70, 85, 95, 100 };

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

	/// <summary>The stock fit of a chassis, <c>gam\ini_*.dat</c> read through <c>HercRecord_ReadCatalogForm</c>.</summary>
	private static ShellBayMachine? StockFit(GameContent content, int chassis) =>
		chassis >= 0 && chassis < StockFits.Length
			&& content.Read(ShellRepairCosts.CatalogFolder, StockFits[chassis]) is { } bytes
			&& new InitHercTransformer().Parse(bytes)?.Data is { } record
			? ShellBayMachine.FromCatalog(record)
			: null;
}
