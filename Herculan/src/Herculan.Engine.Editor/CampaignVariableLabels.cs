using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Editor;

/// <summary>
/// What a campaign variable the game sets holds, and what the game does with one a mission sets, where the retail docs say, for
/// the campaign variable picker's lists, the Present when sentence (docs/herculan/handoff-editor-conditions.md#present-when)
/// and the sentences of the writes (<see cref="CampaignVariableWrites"/>). The UI calls a campaign flag a campaign variable.
/// Flag 3 is not named: what writes it is open, and no retail condition tests it
/// (docs/retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store).
/// </summary>
internal static class CampaignVariableLabels {
	// The first and last of the flags the debrief reads for its grants (docs/retail/formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be
	// and docs/retail/formats/herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5): unit grants from 21, chassis
	// unlocks up to 63.
	private const int FirstGrantFlag = 21;
	private const int LastGrantFlag = 63;

	// The flags every load draws (ShellTrainingLaunch's SeedDrawnFlags), below this bound.
	private const int FirstDrawnFlag = 4;
	private const int DrawnFlagBound = 12;

	// The flag 0 value of a won mission: the results' outcome is whether the last objective check answered success
	// (docs/retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c).
	private const short Won = 1;
	private const short NotWon = 0;

	private static readonly string[] Ordinals = ["First", "Second", "Third"];

	/// <summary>The variables the game sets, in number order: those <see cref="Label"/> names.</summary>
	public static IReadOnlyList<short> Set { get; } = [0, 1, 2, 4, 5, 6, 8, 9, 10];

	/// <summary>What variable <paramref name="flag"/> holds, as a list entry names it, where the retail docs say; null otherwise.</summary>
	public static string? Label(int flag) => flag switch {
		// docs/retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store: the debrief writes 0, 8 and
		// 9 after each mission, a campaign load writes 1 and 2 from the career position, and every load 4 to 6 from a draw.
		0 => "Outcome of the previous mission",
		1 => "Campaign chapter (0 on practice missions)",
		2 => "Mission index in chapter (first is 0)",
		>= FirstDrawnFlag and < FirstDrawnFlag + 3 =>
			$"{Ordinals[flag - FirstDrawnFlag]} random number (0 to {DrawnFlagBound - 1}, picked at each load)",
		// The debrief's write-offs, a squad HERC back below 30 mean condition; not the Scrap button's
		// (docs/retail/shell/armory.md#scrapping).
		8 => "Squad HERCs written off after the previous mission",
		// Pilots whose condition reached 0 (docs/retail/shell/campaign-loop.md#pilot-progression).
		9 => "Squadmates killed in the previous mission",
		// The player disabling or destroying a machine of its own group, counted during a mission
		// (docs/retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4).
		10 => "Squadmates the player took down in the previous mission",
		_ => null,
	};

	/// <summary>
	/// Variable <paramref name="flag"/> as a sentence names it: what it holds where <see cref="Label"/> says, else "campaign
	/// variable N", with what the game does with it after (<see cref="Effect"/>) where it reads it.
	/// </summary>
	public static string Phrase(int flag) => flag switch {
		0 => "the outcome of the previous mission",
		1 => "the campaign chapter",
		2 => "the mission index in its chapter",
		>= FirstDrawnFlag and < FirstDrawnFlag + 3 => $"the {Ordinals[flag - FirstDrawnFlag].ToLowerInvariant()} random number",
		8 => "the number of squad HERCs written off after the previous mission",
		9 => "the number of squadmates killed in the previous mission",
		10 => "the number of squadmates the player took down in the previous mission",
		_ => Effect(flag) is { } effect ? $"campaign variable {flag} ({Lower(effect)})" : $"campaign variable {flag}",
	};

	/// <summary>Variable <paramref name="flag"/> as a picker shows it chosen: its label, else "Campaign variable N", with its <see cref="Effect"/>.</summary>
	public static string Name(int flag) => Label(flag) ?? Numbered(flag, Effect(flag));

	/// <summary>
	/// The variables the debrief reads after the mission to pay or grant something, in number order: those <see cref="Effect"/>
	/// names.
	/// </summary>
	public static IReadOnlyList<short> ReadAfter { get; } = Enumerable.Range(0, LastGrantFlag + 1)
		.Where(flag => Effect(flag) != null).Select(flag => (short)flag).ToList();

	/// <summary>
	/// What the debrief does with variable <paramref name="flag"/> as the mission left it, in a campaign: pays the salvage bonus
	/// (<see cref="Sim.MissionResults.SalvageBonusKilograms"/>), adds units of a weapon to the armory, or unlocks a weapon or a
	/// chassis when it holds a set value (<see cref="Shell.ShellHangar.UnitGrantWeapon"/>, <see cref="Shell.ShellHangar.WeaponUnlock"/>,
	/// <see cref="Shell.ShellHangar.ChassisUnlock"/>); null for a variable it does not read. A weapon is named as the armory
	/// names it, a chassis as the shell does. Flags 43 to 49, which the unit-grant loop reads past its table, are not named:
	/// they name no weapon, and no retail mission writes them.
	/// </summary>
	public static string? Effect(int flag) {
		if (flag == World.MissionLoader.SalvageBonusCounter) {
			return $"Salvage paid at the debrief: {Sim.MissionResults.SalvageBonusKilograms / KilogramsPerTon} tons for each 1";
		}

		if (flag is < FirstGrantFlag or > LastGrantFlag) {
			return null;
		}

		if (Shell.ShellHangar.UnitGrantWeapon(flag) is { } weapon) {
			return $"{WeaponName(weapon)} units the debrief adds to the armory";
		}

		if (Shell.ShellHangar.WeaponUnlock(flag) is { } unlock) {
			return $"Unlocks {WeaponName(unlock.Weapon)} at the debrief when it holds {unlock.Value}";
		}

		return Shell.ShellHangar.ChassisUnlock(flag) is { } chassis
			? $"Unlocks the {HercLUT.GetById((short)chassis.Chassis)?.Name ?? $"chassis {chassis.Chassis}"} at the debrief when it holds {chassis.Value}"
			: null;
	}

	/// <summary>
	/// What a mission's write to variable <paramref name="flag"/> amounts to, for one the game sets: what it holds and what
	/// replaces the value written (docs/retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store), or
	/// for 10 what it counts while the mission runs; null for any other.
	/// </summary>
	public static string? WriteLabel(int flag) => flag switch {
		0 => "Outcome of the previous mission; the debrief replaces it with this one's",
		1 => "Campaign chapter; the next mission's load sets it again",
		2 => "Mission index in chapter; the next mission's load sets it again",
		>= FirstDrawnFlag and < FirstDrawnFlag + 3 => $"{Ordinals[flag - FirstDrawnFlag]} random number; the next mission's load sets it again",
		8 => "Squad HERCs written off after the previous mission; the debrief replaces it",
		9 => "Squadmates killed in the previous mission; the debrief replaces it",
		World.MissionLoader.SquadmatesDownedCounter => "Squadmates the player has taken down in this mission",
		_ => null,
	};

	/// <summary>Variable <paramref name="flag"/> as a write's sentence names it: "campaign variable N", with what a write to it amounts to where the game sets or reads it.</summary>
	public static string WritePhrase(int flag) =>
		(WriteLabel(flag) ?? Effect(flag)) is { } meaning ? $"campaign variable {flag} ({Lower(meaning)})" : $"campaign variable {flag}";

	/// <summary>Variable <paramref name="flag"/> as the picker for a write shows it chosen: its <see cref="WriteLabel"/>, else as <see cref="Name"/>.</summary>
	public static string WriteName(int flag) => WriteLabel(flag) ?? Numbered(flag, Effect(flag));

	private const int KilogramsPerTon = 1000;

	// "Campaign variable N", with <meaning> after a colon when there is one.
	private static string Numbered(int flag, string? meaning) =>
		meaning == null ? $"Campaign variable {flag}" : $"Campaign variable {flag}: {meaning}";

	// <text> with its first letter lower case, for the middle of a sentence; a leading acronym ("ECM", "HERC") is kept.
	private static string Lower(string text) =>
		text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

	// Weapon id <weapon> as the armory names it, the shell catalog's code (WeaponLUT).
	private static string WeaponName(int weapon) => WeaponLUT.GetById(weapon)?.Name ?? $"weapon {weapon}";

	/// <summary>
	/// The values of variable <paramref name="flag"/> the retail docs give a meaning, each with the test written for it; empty
	/// for a number. Won is written <c>&gt; 0</c> and Not won <c>&lt; 1</c>, the two tests of flag 0 retail writes most, 53 and
	/// 24 of its 90 (<c>HercWorks.Query flag 0</c>).
	/// </summary>
	public static IReadOnlyList<VariableMeaning> Meanings(int flag) => flag == 0
		? [
			new VariableMeaning(Won, "Won", "the previous mission was won", MissionGenerator.OpGreater, 0),
			new VariableMeaning(NotWon, "Not won", "the previous mission was not won", MissionGenerator.OpLess, 1),
		]
		: [];

	/// <summary>
	/// The meaning <paramref name="clause"/> tests for: the one of its variable's <see cref="Meanings"/> whose value alone of
	/// theirs the test holds for (<c>&gt; 0</c>, <c>= 1</c> and <c>&gt;= 1</c> all test for Won); null when there is none.
	/// </summary>
	public static VariableMeaning? MeaningOf(VariableClause clause) {
		var meanings = Meanings(clause.Variable);
		var held = meanings.Where(meaning => Holds(clause, meaning.Value)).ToList();
		return held.Count == 1 ? held[0] : null;
	}

	// Whether <clause> holds with its variable at <value>, flag on the left (ConditionEditing.OperatorText).
	private static bool Holds(VariableClause clause, short value) => clause.Operator switch {
		MissionGenerator.OpEqual => value == clause.Value,
		MissionGenerator.OpNotEqual => value != clause.Value,
		MissionGenerator.OpLess => value < clause.Value,
		MissionGenerator.OpLessOrEqual => value <= clause.Value,
		MissionGenerator.OpGreater => value > clause.Value,
		MissionGenerator.OpGreaterOrEqual => value >= clause.Value,
		_ => false,
	};
}

/// <summary>
/// A value of a campaign variable that the retail docs give a meaning (<see cref="CampaignVariableLabels.Meanings"/>): its
/// <paramref name="Name"/> in a choice, the <paramref name="Sentence"/> saying the variable holds it, and the test written
/// for it, <paramref name="Operator"/> and <paramref name="Operand"/>.
/// </summary>
internal sealed record VariableMeaning(short Value, string Name, string Sentence, short Operator, short Operand);
