using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace Herculan.Engine.Shell;

/// <summary>
/// The pilot records a new career starts with, campaign and training alike: the squadmate roster
/// (<c>Squad_GenerateRoster</c>, <c>0040fa31</c>) and the record initialisation every pilot goes through
/// (<c>Pilot_Init</c>, <c>0040fcd8</c>, and <c>Pilot_SetDefaults</c>, <c>0040fd17</c>). See
/// docs/retail/shell/campaign-loop.md#the-pilot-roster-is-generated-not-authored--squad_generateroster-0040fa31.
/// </summary>
internal static class ShellRoster {
	/// <summary>The squad block's shape: three squads of twelve pilot records.</summary>
	internal const int SquadCount = 3;
	internal const int PilotsPerSquad = 12;

	/// <summary><c>Player_Create</c>'s name-index draw, below 11.</summary>
	internal const short PlayerNameIndexCount = 11;

	/// <summary>The rank a roster pilot starts at for each skill, <c>0046f5f4</c> through <c>Pilot_RankForSkill</c> (<c>0040fa21</c>).</summary>
	private static readonly int[] RankForSkill = { 0, 1, 2, 2 };

	/// <summary><c>Pilot_DrawSkill</c>'s thresholds at <c>0046f5ec</c>.</summary>
	private static readonly int[] SkillThresholds = { 70, 85, 95, 100 };

	/// <summary>
	/// <c>Squad_GenerateRoster</c> (<c>0040fa31</c>) — three squads of twelve, each from a shuffle of four
	/// and a shuffle of three and one skill draw per pilot, <see cref="NewPilot"/> writing each
	/// record with its rank seeded from the skill. The records come back in the save's
	/// order, squad by squad and row by row.
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
	/// <see cref="SetDefaults"/>.
	/// </summary>
	internal static PilotEntry NewPilot(string name, int rosterId, int nameIndex, int skill, int rank) {
		var pilot = new PilotEntry {
			RosterId = (short)rosterId,
			NameIndex = (short)nameIndex,
			Name = name,
			Skill = PilotSkill.GetById((short)skill),
			Rank = PilotRank.GetById((short)rank),
		};
		SetDefaults(pilot);
		return pilot;
	}

	/// <summary>
	/// <c>Pilot_SetDefaults</c> (<c>0040fd17</c>): no bay, off strength, no squad position, condition 100 and every
	/// counter 0. Name, skill and rank are left as they are.
	/// </summary>
	internal static void SetDefaults(PilotEntry pilot) {
		pilot.Bay = -1;
		pilot.OnStrength = 0;
		pilot.SquadPosition = -1;
		pilot.Condition = 100;
		pilot.HercKills = pilot.FlyerKills = pilot.BaseKills = 0;
		pilot.TotalHercKills = pilot.TotalFlyerKills = pilot.TotalBaseKills = 0;
		pilot.MissionsFlown = 0;
	}

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
	/// <c>Pilot_DrawSkill</c> (<c>0040f9dc</c>) — a draw below 100 against <see cref="SkillThresholds"/>: 70, 85, 95 and
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
}
