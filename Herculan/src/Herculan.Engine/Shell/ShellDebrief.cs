using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// What the debrief leaves besides the career itself: the game state it set, and the figures
/// <c>Debrief_WriteReport</c> (<c>0040f34c</c>) writes into the mission tab's report texts.
/// </summary>
/// <param name="State">
/// <c>0048260e</c> as the debrief leaves it — 3 the campaign over, 0 back to the shell, 1 the campaign won,
/// 2 on to the next mission's load — or null when a training debrief leaves it alone.
/// </param>
/// <param name="Outcome"><c>results.dat</c>'s outcome, which flag 0 now holds.</param>
/// <param name="SalvageAwarded">The <c>results.dat</c> salvage award added to the pool, in kilograms.</param>
/// <param name="SalvageItems">The salvage pairs read plus the weapon units the campaign grants added.</param>
/// <param name="PilotsLost"><c>Squad_ProgressAll</c>'s count, <c>00482aef</c>: the player if at 0 condition, and each squad member replaced.</param>
/// <param name="MachinesScrapped">How many machines the debrief scrapped, the hangar's <c>+0x24</c> count (<c>00482ae7</c>), which every hangar load zeroes; the original keeps it on the hangar, and here it is the debrief's own tally.</param>
/// <param name="Debrief">The flown mission's debrief text, when <c>Career_Advance</c> reloaded it.</param>
/// <param name="Report">The figures <c>Debrief_WriteReport</c> writes, when it ran: a campaign debrief the player survived.</param>
public sealed record ShellDebriefResult(short? State, short Outcome, int SalvageAwarded, int SalvageItems, int PilotsLost,
	int MachinesScrapped, MissionDebriefText? Debrief, ShellDebriefReport? Report);

/// <summary>
/// <c>Debrief_WriteReport</c> (<c>0040f34c</c>)'s figures, which it writes into the mission tab's report texts
/// (<see cref="ShellMissionScreen.WriteReport"/>).
/// </summary>
/// <param name="Outcome"><c>results.dat</c>'s outcome, 1 a success.</param>
/// <param name="SalvageAwarded">The award in kilograms; the report prints it in tons.</param>
/// <param name="WeaponsRecovered">The salvage pairs read plus the weapon units the campaign granted.</param>
/// <param name="PlayerKills">The player's mission Herc, Base and Flyer kills, pilot <c>+0x2d</c>, <c>+0x31</c>, <c>+0x2f</c>.</param>
/// <param name="SquadKills">
/// The same three summed over the player and every on-strength squad member at positions 1 up to the
/// positions in play — so a member replaced for being lost counts the new pilot's zeros.
/// </param>
/// <param name="PilotsLost"><c>Squad_ProgressAll</c>'s count.</param>
public sealed record ShellDebriefReport(short Outcome, int SalvageAwarded, int WeaponsRecovered,
	(int Hercs, int Bases, int Flyers) PlayerKills, (int Hercs, int Bases, int Flyers) SquadKills, int PilotsLost);

/// <summary>
/// <c>Game_ProcessMissionResults</c> (<c>0040eae7</c>), the shell's half of the debrief: what DBSIM's
/// <c>mission.var</c> and <c>results.dat</c> do to the career (docs/retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7).
/// It leaves out what the original does to the screen — the report texts, which it returns as figures, the
/// debrief view, the dialogs and the ending movies — and what follows it: the autosave to slot 10 when the
/// campaign is won, and the next mission's load when it goes on, which are <see cref="ShellDebriefResult.State"/>'s
/// to decide and the host's to do.
/// </summary>
public static class ShellDebrief {
	/// <summary>The game states <c>Game_ProcessMissionResults</c> writes to <c>0048260e</c>.</summary>
	public const short ShellState = 0;
	public const short CampaignWonState = 1;
	public const short NextMissionState = 2;
	public const short CampaignOverState = 3;

	/// <summary>
	/// The campaign flags the debrief writes from its own counts, which the flown mission's <c>.ENG</c> debrief
	/// lines test (docs/retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7): 8 the machines
	/// it scrapped, 9 the pilots <c>Squad_ProgressAll</c> counted lost.
	/// </summary>
	private const int MachinesScrappedFlag = 8;
	private const int PilotsLostFlag = 9;

	/// <summary>
	/// Runs the debrief on <paramref name="game"/> and <paramref name="hangar"/>, the hangar read from it,
	/// and stores the hangar back into the game. <paramref name="campaign"/> is <c>CampaignModeFlag == 1</c>;
	/// <paramref name="repairMode"/> is <c>prefs.cfg</c> option 44 and <paramref name="manualBuild"/> option 45
	/// set (docs/retail/simulation/preferences.md). <paramref name="roll"/> is <c>ShellRandom_Below</c> (<c>004659ec</c>),
	/// which the flown mission's text reload draws from. Null, with the reason, when the install lacks a
	/// catalog the original reads.
	/// </summary>
	public static ShellDebriefResult? Process(PlayerSave game, ShellHangar hangar, GameContent content, byte[] missionVar,
			byte[] results, bool campaign, int repairMode, bool manualBuild, Func<short, int> roll, out string? failure) {
		if (ShellRepairCosts.Load(content) is not { } costs
				|| content.Read(ShellRepairCosts.CatalogFolder, ShellRepairCosts.ChassisResourceName) is not { } chassisBytes
				|| new HercInfoTransformer().Parse(chassisBytes)?.Data is not { } chassis) {
			failure = $"gam\\{ShellRepairCosts.ValuesResourceName} or gam\\{ShellRepairCosts.ChassisResourceName} is not in any mounted archive.";
			return null;
		}

		var catalog = ShellArmoryCatalog.Load(content);

		// MissionVar_Read (0040ea59): the flag array as the mission left it.
		var flags = new short[PlayerSave.CampaignFlagCount];
		Buffer.BlockCopy(missionVar, 0, flags, 0, Math.Min(missionVar.Length, flags.Length * 2));

		var reader = new Reader(results);
		short outcome = reader.Short();
		int awarded = reader.Int();
		hangar.SalvageKilograms += awarded;
		flags[0] = outcome;

		// Each salvage pair is Armory_AddNewUnit(id, condition) (0041229d), a campaign's only; a training debrief reads past them.
		int pairs = reader.Short();
		for (int i = 0; i < pairs; i++) {
			short weaponId = reader.Short();
			short condition = reader.Short();
			if (campaign) {
				hangar.AddNewUnit(weaponId, condition, ShellWeaponUnit.NoGuidance);
			}
		}

		// The player, then each on-strength squad member at positions 1 up to the machines-on-strength count —
		// that count, 00482a7a, not the positions in play the export walks.
		int scrapped = 0;
		if (game.PlayerPilot is { } player && hangar.Player is { } playerView) {
			scrapped += SettleMachine(hangar, costs, playerView.Bay, player, reader);
		}

		for (int position = 1; position < hangar.MachinesOnStrength; position++) {
			int member = hangar.SquadMemberIndexAt(position);
			if (member != -1 && hangar.SquadMembers[member] is { OnStrength: true } view
					&& game.Squadmates?.ElementAtOrDefault(hangar.SquadRecordIndex(member)) is { } record) {
				scrapped += SettleMachine(hangar, costs, view.Bay, record, reader);
			}
		}

		int pilotsLost = ProgressAll(game, hangar);
		flags[PilotsLostFlag] = (short)pilotsLost;
		short? state = null;
		int granted = 0;
		MissionDebriefText? debrief = null;
		ShellDebriefReport? report = null;
		if (game.PlayerPilot?.Condition == 0) {
			state = CampaignOverState;
		} else {
			flags[MachinesScrappedFlag] = (short)scrapped;
		}

		if (state == null && campaign) {
			// Game_DeliverWeaponQueue (0040f324): the build queue delivered and charged.
			hangar.SalvageKilograms -= hangar.DeliverQueue(catalog.PriceKilograms);
			Repair(hangar, costs, repairMode);

			// HercList_BuildTickAll (00410a2b): every machine not yet built advances one mission.
			for (int bay = 0; bay < ShellHangar.BayCount; bay++) {
				if (hangar.Bay(bay) is { IsBuilt: false } machine) {
					machine.BuildTick(chassis.FirstOrDefault(entry => entry?.HercId == machine.ChassisType)?.BuildMissionCount ?? 0);
				}
			}

			hangar.GrantChassis(flags);
			granted = hangar.GrantCampaignWeapons(flags);
			catalog.RefreshQueue(hangar, manualBuild);
			report = WriteReport(game, hangar, outcome, awarded, pairs + granted, pilotsLost);
			(state, debrief) = Advance(game, hangar, content, flags, outcome, roll);
		}

		for (int i = 0; i < flags.Length; i++) {
			game.SetCampaignFlag(i, flags[i]);
		}

		if (state is { } written) {
			game.GameState = written;
		}

		hangar.Store(game);
		failure = null;
		return new ShellDebriefResult(state, outcome, awarded, pairs + granted, pilotsLost, scrapped, debrief, report);
	}

	/// <summary>
	/// <c>Debrief_WriteReport</c> (<c>0040f34c</c>)'s figures: the player's mission kills, and those summed with
	/// each on-strength squad member's at positions 1 up to the positions in play.
	/// </summary>
	private static ShellDebriefReport WriteReport(PlayerSave game, ShellHangar hangar, short outcome, int awarded,
			int weapons, int pilotsLost) {
		var player = game.PlayerPilot;
		var own = (Hercs: (int)(player?.HercKills ?? 0), Bases: (int)(player?.BaseKills ?? 0), Flyers: (int)(player?.FlyerKills ?? 0));
		var squad = own;
		for (int position = 1; position < hangar.SquadPositions; position++) {
			int member = hangar.SquadMemberIndexAt(position);
			if (member != -1 && hangar.SquadMembers[member].OnStrength
					&& game.Squadmates?.ElementAtOrDefault(hangar.SquadRecordIndex(member)) is { } record) {
				squad = (squad.Hercs + record.HercKills, squad.Bases + record.BaseKills, squad.Flyers + record.FlyerKills);
			}
		}

		return new ShellDebriefReport(outcome, awarded, weapons, own, squad, pilotsLost);
	}

	/// <summary>
	/// One machine's turn in the debrief's accounting: <c>Herc_ReadStatusBlock</c> (<c>00411720</c>) over the
	/// machine in <paramref name="bay"/> when there is one, the pilot's condition set from its overall slot or
	/// to 100 with none, <c>Pilot_AccumulateMissionStats</c> (<c>0041000e</c>), and
	/// <see cref="ShellHangar.SettleAfterMission"/>, whose salvage goes into the pool. Returns 1 when the
	/// machine was scrapped. An empty bay reads no status block, so the file's block for it is read as
	/// the pilot's counters, as the original reads it.
	/// </summary>
	private static int SettleMachine(ShellHangar hangar, ShellRepairCosts costs, int bay, PilotEntry pilot, Reader reader) {
		var machine = hangar.Bay(bay);
		if (machine != null) {
			machine.ReadStatusBlock(reader.Bytes(ShellBayMachine.StatusBlockLength));
		}

		pilot.Condition = (short)(machine?.OverallSlot ?? 100);

		// The three counters arrive Herc, Base, Flyer.
		pilot.HercKills = reader.Short();
		pilot.BaseKills = reader.Short();
		pilot.FlyerKills = reader.Short();
		pilot.TotalHercKills += pilot.HercKills;
		pilot.TotalBaseKills += pilot.BaseKills;
		pilot.TotalFlyerKills += pilot.FlyerKills;
		pilot.MissionsFlown++;

		var (salvage, wasScrapped) = hangar.SettleAfterMission(bay, costs);
		hangar.SalvageKilograms += salvage;
		return wasScrapped ? 1 : 0;
	}

	/// <summary>
	/// <c>Squad_ProgressAll</c> (<c>004103ba</c>) — <see cref="Progress"/> for the player when their condition is
	/// not 0; then for each on-strength squad member at positions 1 up to the positions in play, a member
	/// at 0 condition is replaced (<see cref="ShellHangar.ReplaceSquadMember"/>) and any other progressed.
	/// Returns how many were lost, the player counted when at 0.
	/// </summary>
	private static int ProgressAll(PlayerSave game, ShellHangar hangar) {
		int lost = 0;
		if (game.PlayerPilot is { } player) {
			if (player.Condition != 0) {
				Progress(player, isPlayer: true);
			} else {
				lost++;
			}
		}

		for (int position = 1; position < hangar.SquadPositions; position++) {
			int member = hangar.SquadMemberIndexAt(position);
			if (member == -1 || !hangar.SquadMembers[member].OnStrength
					|| game.Squadmates?.ElementAtOrDefault(hangar.SquadRecordIndex(member)) is not { } record) {
				continue;
			}

			if (record.Condition == 0) {
				lost++;
				hangar.ReplaceSquadMember(member, game);
			} else {
				Progress(record, isPlayer: false);
				hangar.SquadMembers[member].Skill = record.Skill?.Id ?? 0;
			}
		}

		return lost;
	}

	/// <summary>The top of both ladders.</summary>
	private const int LadderTop = 3;

	/// <summary>
	/// <c>Pilot_Progress</c> (<c>00410066</c>) — nothing for a pilot off strength; otherwise, with divisors 20 kills
	/// and 10 missions for position 0 and 10 and 15 for any other, a skill step for a non-player whose career
	/// Herc and Flyer kills are a multiple of the kill divisor, and a rank step when missions flown are a
	/// multiple of the mission divisor, each capped at 3 (docs/retail/shell/campaign-loop.md#pilot-progression).
	/// </summary>
	private static void Progress(PilotEntry pilot, bool isPlayer) {
		if (pilot.OnStrength == 0) {
			return;
		}

		(int kills, int missions) = pilot.SquadPosition == 0 ? (20, 10) : (10, 15);
		short skill = pilot.Skill?.Id ?? 0;
		if (skill < LadderTop && !isPlayer && (pilot.TotalHercKills + pilot.TotalFlyerKills) % kills == 0) {
			pilot.Skill = PilotSkill.GetById((short)(skill + 1));
		}

		short rank = pilot.Rank?.Id ?? 0;
		if (rank < LadderTop && pilot.MissionsFlown % missions == 0) {
			pilot.Rank = PilotRank.GetById((short)(rank + 1));
		}
	}

	/// <summary>
	/// <c>Game_AutoRepairSquad</c> (<c>0040e804</c>) — <see cref="ShellHangar.AutoRepair"/> on the player's machine in repair mode 0, and
	/// on each on-strength squad member's at positions 1 up to the positions in play in any mode but 2, each
	/// against one budget: the pool divided by the machines on strength as it stood when the pass began.
	/// Each repair is charged as it is made.
	/// </summary>
	private static void Repair(ShellHangar hangar, ShellRepairCosts costs, int repairMode) {
		// The original divides by 00482a7a unguarded; the player keeps it at 1 or more.
		uint budget = hangar.MachinesOnStrength == 0 ? 0 : (uint)(hangar.SalvageKilograms / hangar.MachinesOnStrength);
		if (repairMode == ShellRepairScreen.AutoRepairMode && hangar.Player is { } player && hangar.Bay(player.Bay) is { } own) {
			hangar.SalvageKilograms -= (int)ShellHangar.AutoRepair(own, budget, costs);
		}

		if (repairMode == NoRepairMode) {
			return;
		}

		for (int position = 1; position < hangar.SquadPositions; position++) {
			if (hangar.SquadMemberAt(position) is { OnStrength: true } member && hangar.Bay(member.Bay) is { } machine) {
				hangar.SalvageKilograms -= (int)ShellHangar.AutoRepair(machine, budget, costs);
			}
		}
	}

	/// <summary>Repair mode 2, which repairs nothing at debrief.</summary>
	private const int NoRepairMode = 2;

	/// <summary>The stage past which a failed mission ends the campaign, the final chapter's index.</summary>
	private const int FinalStage = 4;

	/// <summary>
	/// <c>Career_Advance</c> (<c>00412dc7</c>) — the mission at the career position reloaded for its debrief text
	/// (<see cref="MissionGenerator.LoadDebriefText"/>), then the position advanced whatever the outcome, rolling
	/// into the next stage past the stage's last mission. The campaign is won when the mission was and the
	/// stage has run out; it returns to the shell when the mission failed and the position is at a stage's
	/// first mission or in the final chapter. Otherwise the two scripted events fire on the new position —
	/// stage 1 mission 3 moves the player to bay 4 (<c>Player_SetBay</c>, <c>0040e6c8</c>), stage 1 mission 6 takes
	/// the first Razor out of the hangar and its pilot out of the bay (<c>Hangar_WithdrawChassis(8)</c> (<c>0040e7cd</c>)) — and the next
	/// mission loads.
	/// </summary>
	private static (short State, MissionDebriefText? Debrief) Advance(PlayerSave game, ShellHangar hangar, GameContent content,
			short[] flags, short outcome, Func<short, int> roll) {
		var stages = ShellTrainingLaunch.CareerStages(content);
		int stage = game.CampaignStage;
		int mission = game.MissionInStage;
		MissionDebriefText? debrief = null;
		if (stages != null && stage >= 0 && stage < stages.Count && mission < stages[stage].Missions.Length
				&& ShellTrainingLaunch.MissionPath(content, stage, mission) is { } path
				&& ShellTrainingLaunch.ReadMission(content, path) is (byte[] msn, var text)) {
			debrief = MissionGenerator.LoadDebriefText(msn, text, flags, roll);
		}

		mission++;
		if (stages != null && stage >= 0 && stage < stages.Count && mission >= stages[stage].Missions.Length) {
			mission = 0;
			stage++;
		}

		game.CampaignStage = (short)stage;
		game.MissionInStage = (short)mission;
		if (outcome != 0 && stages != null && stage >= stages.Count) {
			return (CampaignWonState, debrief);
		}

		if ((mission == 0 || stage > FinalStage) && outcome == 0) {
			return (ShellState, debrief);
		}

		if (stage == 1 && mission == 3) {
			if (hangar.Player is { } player) {
				ShellHangar.SetBay(player, 4);
			}
		}

		if (stage == 1 && mission == 6 && hangar.PilotFor(hangar.RemoveFirstOfType(RazorChassis)) is { } pilot) {
			pilot.Bay = -1;
		}

		return (NextMissionState, debrief);
	}

	/// <summary>The Razor's chassis type, which <c>Career_Advance</c> passes to <c>Hangar_WithdrawChassis</c> (<c>0040e7cd</c>).</summary>
	private const int RazorChassis = 8;

	/// <summary>
	/// The results stream: little-endian fields read in order. Past the end it reads zeros, where the
	/// original's failed read would leave its target as it was — a short file is not retail DBSIM's output.
	/// </summary>
	private sealed class Reader {
		private readonly byte[] _bytes;
		private int _at;

		public Reader(byte[] bytes) => _bytes = bytes;

		public short Short() => (short)(Byte() | Byte() << 8);

		public int Int() => (ushort)Short() | Short() << 16;

		public byte[] Bytes(int count) {
			var bytes = new byte[count];
			for (int i = 0; i < count; i++) {
				bytes[i] = Byte();
			}

			return bytes;
		}

		private byte Byte() => _at < _bytes.Length ? _bytes[_at++] : (byte)0;
	}
}
