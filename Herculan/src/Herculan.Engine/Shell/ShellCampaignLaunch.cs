using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Engine.World;

namespace Herculan.Engine.Shell;

/// <summary>
/// The career block's text fields as <c>Career_SetBriefing</c> (<c>00412ece</c>) fills them from a loaded
/// mission: the objective, briefing and intelligence <c>mission.str</c> lines, <c>-1</c> for an empty slot,
/// and the briefing movie id. See docs/retail/formats/save-games.md#career-block--152-bytes.
/// </summary>
public sealed record ShellCareerBriefing(short[] Objectives, short[] Briefing, short[] Intelligence, short BriefingMovie) {
	/// <summary>The words of row 4's slot 0, as <see cref="MissionGenerator.TextPackage"/> gives them.</summary>
	public static ShellCareerBriefing From(short[] package) =>
		new(package[1..11], package[11..41], package[41..71], package[71]);
}

/// <summary>What a campaign mission load wrote, and what it hands back to the career.</summary>
public sealed record ShellCampaignMission(string ScriptPath, string MissionPath, ShellCareerBriefing? Briefing, int SquadPositions);

/// <summary>
/// The campaign half of <c>MsnGen_LoadMission</c> (<c>0041c73d</c>), the load <c>Career_LoadCurrentMission</c>
/// (<c>0044d4cc</c>) makes for the career position: the flags seeded with the position, the mission loaded
/// (<see cref="MissionGenerator"/>), the header's theater, cheat and difficulty fields set, <c>script.dat</c>
/// and <c>mission.str</c> written, and the career block's text taken from row 4. Unlike the training half
/// (<see cref="ShellTrainingLaunch"/>) it builds no machines: a campaign flies the hangar it has, with its
/// squad positions in play set from the mission's group 0. See docs/retail/shell/campaign-loop.md and
/// docs/retail/formats/script-dat.md#the-training-fields.
/// </summary>
public static class ShellCampaignLaunch {
	/// <summary>
	/// Loads the mission at career position (<paramref name="stage"/>, <paramref name="mission"/>) and writes
	/// its <c>script.dat</c> and <c>mission.str</c> into <paramref name="directory"/>, or returns null with the
	/// reason when the install lacks a file the original would open. <paramref name="flags"/> is the career's
	/// campaign flag array, which the load seeds and the mission's header patch clears in place;
	/// <paramref name="clearList"/> is the row-2 clear list the shell keeps across loads;
	/// <paramref name="skill"/> is the player pilot's skill, the mission's difficulty. <paramref name="roll"/> is
	/// VSHELL's <c>ShellRandom_Below</c> (<c>004659ec</c>), a draw in <c>[0, n)</c>.
	/// </summary>
	public static ShellCampaignMission? Write(string directory, GameContent content, int stage, int mission, short skill,
			short[] flags, short[] clearList, Func<short, int> roll, out string? failure) {
		if (ShellTrainingLaunch.CareerStages(content) is not { } stages || stage < 0 || stage >= stages.Count
				|| ShellTrainingLaunch.MissionPath(content, stage, mission) is not { } missionPath) {
			failure = $"gam\\career.dat or missions.bin has no stage {stage} mission {mission}.";
			return null;
		}

		if (ShellTrainingLaunch.ReadMission(content, missionPath) is not (byte[] msn, var text)) {
			failure = $"{missionPath} is not in any mounted archive.";
			return null;
		}

		// MsnGen_SeedCampaignFlags: a campaign load writes the position into flags 1 and 2.
		flags[1] = (short)stage;
		flags[2] = (short)mission;
		ShellTrainingLaunch.SeedDrawnFlags(flags, roll);

		var loaded = MissionGenerator.Load(msn, text, flags, clearList, roll);
		var header = loaded.Header;
		header[5] = 0;
		header[6] = 0;
		header[7] = skill;
		header[0] = stages[stage].CampaignIndex;

		Directory.CreateDirectory(directory);
		string scriptPath = Path.Combine(directory, MissionLoader.ScriptFileName);
		ShellWorkingFiles.WriteMissionFile(scriptPath, loaded.WriteScriptDat());
		ShellWorkingFiles.WriteMissionFile(Path.Combine(directory, MissionLoader.TextFileName), loaded.WriteMissionText());

		// The original asserts row 4 is there before it copies slot 0 into the career block.
		var briefing = loaded.TextPackage is { } package ? ShellCareerBriefing.From(package) : null;

		failure = null;
		return new ShellCampaignMission(scriptPath, missionPath, briefing, loaded.SquadPositions);
	}

	/// <summary>
	/// <c>Game_NewCareer</c> (<c>0040e2ed</c>) up to the mission load, as a save holding what it leaves in
	/// VSHELL's memory. In a campaign it is <c>Registration_OnAccept</c> (<c>0043c0fb</c>)'s, after
	/// <c>LoadHercInfDat</c> (<c>0041181c</c>); in training, <c>Begin Mission</c>'s and
	/// <c>INSTANT ACTION</c>'s, which read no <c>gam\hercs.dat</c> and leave the career position for the
	/// caller to set. See docs/retail/shell/screen-layout.md#starting-a-campaign and
	/// docs/retail/shell/campaign-loop.md#starting-a-campaign--game_newcareer-0040e2ed.
	///
	/// <para>Each weapon's units are listed in the order <c>gam\weapons.dat</c> gives them, which
	/// <see cref="ShellHangar.From"/> pushes back onto the head one by one, so the hangar it builds holds the
	/// last unit listed at the head, as <c>Armory_AddUnit</c> left it. A save written from that hangar
	/// lists them head first, as <c>Armory_Write</c> does.</para>
	///
	/// <para><paramref name="held"/> is the game the shell's memory holds, the last one loaded or started, or
	/// null for the startup's. The new career writes none of block 11, the 20 bytes at <c>004832c8</c>, so
	/// both modes keep that game's, or the startup's zeros. A training career also keeps its chassis flags,
	/// which only the startup's and the campaign's <c>LoadHercInfDat</c> put back to the file's, and the
	/// career block's text, which only a campaign load's <c>Career_SetBriefing</c> (<c>00412ece</c>) writes.</para>
	/// </summary>
	public static PlayerSave? NewCareer(GameContent content, string name, int skill, ShellCampaignMode mode,
			Func<short, int> roll, PlayerSave? held, out string? failure) {
		bool campaign = mode == ShellCampaignMode.Campaign;
		if (content.Read(ShellRepairCosts.CatalogFolder, "WEAPONS.DAT") is not { } weaponsBytes
				|| new WeaponsDatTransformer().Parse(weaponsBytes) is not { } weapons) {
			failure = "gam\\weapons.dat is not in any mounted archive.";
			return null;
		}

		// LoadHercsDat reads the file in a campaign only.
		var hercs = campaign && content.Read(ShellRepairCosts.CatalogFolder, "HERCS.DAT") is { } hercsBytes
			? new HercsStartTransformer().Parse(hercsBytes) : null;
		if (campaign && hercs == null) {
			failure = "gam\\hercs.dat is not in any mounted archive.";
			return null;
		}

		// The chassis flags come from the file after the campaign's LoadHercInfDat or the startup's.
		var chassis = (campaign || held == null)
			&& content.Read(ShellRepairCosts.CatalogFolder, ShellRepairCosts.ChassisResourceName) is { } chassisBytes
			? new HercInfoTransformer().Parse(chassisBytes)?.Data : null;
		if ((campaign || held == null) && chassis == null) {
			failure = $"gam\\{ShellRepairCosts.ChassisResourceName} is not in any mounted archive.";
			return null;
		}

		var game = new PlayerSave();

		// LoadWeaponsDat (00411fc4): each record's unlock byte, its stock cleared, then the trailing units
		// added one by one; and Armory_ResetQueue, five free slots, all empty.
		var units = (weapons.StartingWeapons ?? Array.Empty<UiWeaponEntry>())
			.Where(unit => unit != null).ToLookup(unit => (int)unit.WeaponId);
		var items = new Inventory.InventoryItem[ShellMissionLaunch.WeaponCatalogCount];
		for (int id = 0; id < items.Length; id++) {
			var stock = units[id].Select(unit => new ShellWeaponUnit(unit.WeaponId, condition: unit.Condition,
				guidance: unit.Guidance?.Id ?? ShellWeaponUnit.NoGuidance).ToEntry()).ToArray();
			items[id] = new Inventory.InventoryItem {
				Id = WeaponLUT.GetById(id),
				UnlockFlag = weapons.Data.FirstOrDefault(entry => entry?.Id == id)?.StartUnlock ?? 0,
				Quantity = (short)stock.Length,
				Units = stock,
			};
		}

		game.Inventory = new Inventory { Items = items };
		game.BuildQueueFreeSlots = ShellHangar.QueueSlots;
		Array.Fill(game.BuildQueue, WeaponLUT.None);

		// Squad_GenerateRoster, then Player_Create (00410107): each squad's record 0 taken as its member
		// (00483b48) and the member cursor (00483b4e) stepped to 1; the player's record at roster id 0 with
		// a drawn name index, the typed name and the skill, rank 0, bay 0, position 0, on strength; no
		// positions in play and one machine on strength.
		game.Squadmates = ShellTrainingLaunch.GenerateRoster(ShellText.Load(content, "ESNAMES.BIN"), roll);
		game.SquadTailAndPlayerHead = [0, 0, 0, 1, 1, 1, 0, 1];
		var player = ShellTrainingLaunch.NewPilot(name, 0, roll(ShellTrainingLaunch.PlayerNameIndexCount), skill, 0);
		player.Bay = 0;
		player.SquadPosition = 0;
		player.OnStrength = 1;
		game.PlayerPilot = player;

		// LoadHercsDat (004104ed) empties the hangar, and in a campaign puts gam\hercs.dat's machines in their bays.
		foreach (var entry in hercs?.Data ?? Array.Empty<Hercs.Entry>()) {
			if (entry?.Herc is { } record && entry.BayId is >= 0 and < ShellHangar.BayCount) {
				game.HercBay[entry.BayId] = ShellBayMachine.FromCatalog(record).ToEntry();
			}
		}

		// Each chassis's availability, the file's or the held game's.
		for (short id = 0; id < HercLUT.Mongoose.Id; id++) {
			var herc = HercLUT.GetById(id)!;
			game.ChassisAvailability[herc] = chassis != null
				? chassis.FirstOrDefault(entry => entry?.HercId == id)?.AvailabilityFlag ?? 0
				: held!.ChassisAvailability.GetValueOrDefault(herc);
		}

		// Career_SeedPosition (00412a2f): stage 1, mission 0 in a campaign, whose load writes the rest of the
		// career block; in training, stage 0 at the practice row, which the caller sets, and the rest as held.
		if (campaign) {
			game.CampaignStage = 1;
			game.MissionInStage = 0;
		} else if (held != null) {
			held.CareerBlock.AsSpan(2).CopyTo(game.CareerBlock.AsSpan(2));
		}

		game.SalvageTotal = roll(ShellTrainingLaunch.SalvageDrawCount) * ShellRepairCosts.KilogramsPerTon + StartingSalvage;

		// The flag array cleared (CampaignFlags_Clear), game state 2, and block 11 as memory holds it.
		const int heldOffset = PlayerSave.CampaignFlagCount * 2 + 2;
		game.CampaignStateTail = new byte[heldOffset + HeldBlockLength];
		game.GameState = ContinuingGameState;
		if (held?.CampaignStateTail is { Length: >= heldOffset + HeldBlockLength } tail) {
			tail.AsSpan(heldOffset, HeldBlockLength).CopyTo(game.CampaignStateTail.AsSpan(heldOffset));
		}

		failure = null;
		return game;
	}

	/// <summary>
	/// <c>Career_LoadCurrentMission</c> (<c>0044d4cc</c>)'s campaign load for <paramref name="game"/>'s
	/// position, which a new career's <c>Use Default</c> click runs: <see cref="Write"/> into
	/// <paramref name="directory"/> against the career's flags and its player's skill, the flags the load
	/// seeded and cleared written back, <c>Career_SetBriefing</c> (<c>00412ece</c>) into the career block,
	/// the squad positions in play set in <paramref name="hangar"/>, and <c>Game_ExportMissionHandoff</c>
	/// (<c>0040f0d4</c>)'s <c>mission.var</c> and <c>player.mec</c> beside the mission. Null, with the reason,
	/// when the install lacks a file or the mission has no row 4 — where the original asserts. See
	/// docs/retail/shell/campaign-loop.md#loading-the-careers-mission.
	/// </summary>
	public static ShellCampaignMission? LoadCareerMission(string directory, GameContent content, PlayerSave game,
			ShellHangar hangar, short[] clearList, Func<short, int> roll, out string? failure) {
		var flags = new short[PlayerSave.CampaignFlagCount];
		for (int i = 0; i < flags.Length; i++) {
			flags[i] = game.GetCampaignFlag(i);
		}

		var loaded = Write(directory, content, game.CampaignStage, game.MissionInStage, game.PlayerPilot?.Skill?.Id ?? 0,
			flags, clearList, roll, out failure);
		if (loaded == null) {
			return null;
		}

		if (loaded.Briefing is not { } briefing) {
			failure = $"{loaded.MissionPath} has no row 4 for the career block.";
			return null;
		}

		for (int i = 0; i < flags.Length; i++) {
			game.SetCampaignFlag(i, flags[i]);
		}

		// Career_SetBriefing: each array copied whole, its count the entries that are not -1.
		short[] career = game.CareerBlock;
		void Copy(int countIndex, short[] lines) {
			lines.CopyTo(career, countIndex + 1);
			career[countIndex] = (short)lines.Count(line => line != -1);
		}

		Copy(ObjectivesCountIndex, briefing.Objectives);
		Copy(BriefingCountIndex, briefing.Briefing);
		Copy(IntelligenceCountIndex, briefing.Intelligence);
		game.BriefingMovie = briefing.BriefingMovie;

		hangar.SetPositionsInPlay(loaded.SquadPositions);

		var flagBytes = new byte[flags.Length * 2];
		Buffer.BlockCopy(flags, 0, flagBytes, 0, flagBytes.Length);
		File.WriteAllBytes(Path.Combine(directory, ShellMissionLaunch.MissionVarFileName), flagBytes);
		File.WriteAllBytes(Path.Combine(directory, MissionLoader.PlayerFileName), ShellMissionLaunch.ExportPlayerMec(hangar));
		return loaded;
	}

	/// <summary>Where the career block keeps each text array's count, the array following it (docs/retail/formats/save-games.md#career-block--152-bytes).</summary>
	private const int ObjectivesCountIndex = 2;
	private const int BriefingCountIndex = 13;
	private const int IntelligenceCountIndex = 44;

	/// <summary>The salvage pool's floor, the <c>100000</c> in <c>Game_NewCareer</c>'s draw.</summary>
	private const int StartingSalvage = 100000;

	/// <summary><c>Game_NewCareer</c>'s game state, <c>0048260e = 2</c> — the one a game goes on from.</summary>
	private const short ContinuingGameState = 2;

	/// <summary>Block 11's length.</summary>
	private const int HeldBlockLength = 20;

	/// <summary>
	/// The first career position whose <c>missions.bin</c> name has <paramref name="name"/>'s file name — a
	/// bare <c>C1_03</c>, <c>C1_03.MSN</c> or <c>MSN\C1_03.MSN</c> — or null when <c>gam\career.dat</c> has
	/// none. The original only ever loads a mission by position; looking one up by name is this engine's.
	/// </summary>
	public static (int Stage, int Mission)? Find(GameContent content, string name) {
		if (ShellTrainingLaunch.CareerStages(content) is not { } stages || ShellText.Load(content, "MISSIONS.BIN") is not { } names) {
			return null;
		}

		string wanted = Stem(name);
		for (int stage = 0; stage < stages.Count; stage++) {
			for (int mission = 0; mission < stages[stage].Missions.Length; mission++) {
				if (names.Text(stages[stage].Missions[mission]) is { } path
						&& string.Equals(Stem(path), wanted, StringComparison.OrdinalIgnoreCase)) {
					return (stage, mission);
				}
			}
		}

		return null;
	}

	private static string Stem(string path) {
		string name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
		int dot = name.IndexOf('.');
		return dot < 0 ? name : name[..dot];
	}
}
