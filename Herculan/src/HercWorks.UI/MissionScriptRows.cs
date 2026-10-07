using HercWorks.Core.Data.File.Msn.Script;

namespace HercWorks.UI;

/// <summary>
/// Formats/parses the short arrays that several script.dat records carry as a single editable
/// comma-separated grid cell. Fixed-length arrays (member ref slots, ...) are written
/// back in place and must keep their exact element count — the on-disk record stride depends on it —
/// so a wrong-length edit is rejected rather than silently padded; -1 is the format's own
/// "unused slot" sentinel and is what an empty slot should be set to.
/// </summary>
internal static class ShortCsv {
	public static string Format(short[] values) => string.Join(", ", values);

	public static short[] Parse(string? text) {
		if (string.IsNullOrWhiteSpace(text)) {
			return [];
		}

		return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(part => short.TryParse(part, out short v)
				? v
				: throw new FormatException($"'{part}' is not a valid 16-bit value."))
			.ToArray();
	}

	/// <summary>Parses into an array that must stay exactly <paramref name="target"/>.Length long.</summary>
	public static void ParseInto(string? text, short[] target) {
		var parsed = Parse(text);
		if (parsed.Length != target.Length) {
			throw new FormatException(
				$"This field holds exactly {target.Length} values ({parsed.Length} given). " +
				"Use -1 for unused slots rather than leaving them out.");
		}
		Array.Copy(parsed, target, target.Length);
	}
}

/// <summary>
/// Base for every script.dat grid row: rows wrap a live model record and write edits straight
/// through to it, so there is no separate apply step when saving. Index is the record's position in
/// its own block — the number every other block's refs use to point at it.
/// </summary>
internal abstract class ScriptRow {
	public int Index { get; init; }
}

/// <summary>Block 1 — a world position other blocks reference by index.</summary>
internal sealed class ScriptPointRow : ScriptRow {
	public required ScriptCoordinate Source { get; init; }

	/// <inheritdoc cref="ScriptCoordinate.X"/>
	public int X { get => Source.X; set => Source.X = value; }

	/// <inheritdoc cref="ScriptCoordinate.Y"/>
	public int Y { get => Source.Y; set => Source.Y = value; }

	/// <inheritdoc cref="ScriptCoordinate.Z"/>
	public int Z { get => Source.Z; set => Source.Z = value; }
}

/// <summary>
/// Block 2 — a heading in degrees. DBSIM multiplies this by 182 into BAM at load; the BAM column
/// shows what it becomes there but is not itself stored.
/// </summary>
internal sealed class ScriptHeadingRow : ScriptRow {
	public required ScriptHeading Source { get; init; }

	/// <inheritdoc cref="ScriptHeading.Degrees"/>
	public short Degrees { get => Source.Degrees; set => Source.Degrees = value; }
	public int Bam => Source.Degrees * 182;
}

/// <summary>Block 3 — an ordered list of block-1 point refs forming one route.</summary>
internal sealed class ScriptRouteRow : ScriptRow {
	public required ScriptWaypointGroup Source { get; init; }

	public int Count => Source.Waypoints.Length;

	/// <inheritdoc cref="ScriptWaypointGroup.Waypoints"/>
	/// <remarks>Variable-length by format, so unlike the fixed ref arrays this may be any length.</remarks>
	public string Waypoints {
		get => ShortCsv.Format(Source.Waypoints);
		set => Source.Waypoints = ShortCsv.Parse(value);
	}
}

/// <summary>
/// Block 4 — a trigger area. Type 0 is a box between two block-1 points; any other type is a
/// circle round <see cref="PointRef"/> whose radius is <see cref="SecondPointOrRadius"/> × 10.
/// </summary>
internal sealed class ScriptTriggerAreaRow : ScriptRow {
	public required ScriptTriggerArea Source { get; init; }

	/// <inheritdoc cref="ScriptTriggerArea.Shape"/>
	public short Shape { get => Source.Shape; set => Source.Shape = value; }

	/// <inheritdoc cref="ScriptTriggerArea.PointRef"/>
	public short PointRef { get => Source.PointRef; set => Source.PointRef = value; }

	/// <inheritdoc cref="ScriptTriggerArea.SecondPointOrRadius"/>
	public short SecondPointOrRadius { get => Source.SecondPointOrRadius; set => Source.SecondPointOrRadius = value; }
}

/// <summary>
/// Block 5 — a mission action. <see cref="MessageId"/> is the message it posts plus one (0 for
/// none), <see cref="AreaRefs"/> its block-4 trigger areas, and the two counter arrays the mission
/// counters it writes when it activates; DBSIM reads <see cref="TextRefs"/> and throws it away. See
/// docs/retail/formats/script-dat.md and docs/retail/simulation/mission-deployment.md.
/// </summary>
internal sealed class ScriptActionRow : ScriptRow {
	public required ScriptAction Source { get; init; }

	/// <inheritdoc cref="ScriptAction.Type"/>
	public short Type { get => Source.Type; set => Source.Type = value; }

	/// <inheritdoc cref="ScriptAction.Verb"/>
	public short Verb { get => Source.Verb; set => Source.Verb = value; }

	/// <inheritdoc cref="ScriptAction.MessageId"/>
	public short MessageId { get => Source.MessageId; set => Source.MessageId = value; }

	/// <inheritdoc cref="ScriptAction.TargetRef"/>
	public short TargetRef { get => Source.TargetRef; set => Source.TargetRef = value; }

	/// <inheritdoc cref="ScriptAction.AreaRefs"/>
	public string AreaRefs {
		get => ShortCsv.Format(Source.AreaRefs);
		set => ShortCsv.ParseInto(value, Source.AreaRefs);
	}

	/// <inheritdoc cref="ScriptAction.TextRefs"/>
	public string TextRefs {
		get => ShortCsv.Format(Source.TextRefs);
		set => ShortCsv.ParseInto(value, Source.TextRefs);
	}

	/// <inheritdoc cref="ScriptAction.CounterRefs"/>
	public string CounterRefs {
		get => ShortCsv.Format(Source.CounterRefs);
		set => ShortCsv.ParseInto(value, Source.CounterRefs);
	}

	/// <inheritdoc cref="ScriptAction.CounterOps"/>
	public string CounterOps {
		get => ShortCsv.Format(Source.CounterOps);
		set => ShortCsv.ParseInto(value, Source.CounterOps);
	}
}

/// <summary>
/// Block 6 — a mission timer: the action that arms it (-1 runs it from mission start), a delay in
/// seconds, and the ten actions fired when it expires.
/// </summary>
internal sealed class ScriptActionTimerRow : ScriptRow {
	public required ScriptActionTimer Source { get; init; }

	/// <inheritdoc cref="ScriptActionTimer.PrimaryActionRef"/>
	public short PrimaryActionRef { get => Source.PrimaryActionRef; set => Source.PrimaryActionRef = value; }

	/// <inheritdoc cref="ScriptActionTimer.Delay"/>
	public short Delay { get => Source.Delay; set => Source.Delay = value; }

	/// <inheritdoc cref="ScriptActionTimer.SequenceRefs"/>
	public string SequenceRefs {
		get => ShortCsv.Format(Source.SequenceRefs);
		set => ShortCsv.ParseInto(value, Source.SequenceRefs);
	}
}

/// <summary>
/// Block 7 — one mech roster slot. A slot only spawns if some group (block 11) names it, and in
/// every retail file Position/Heading are -1, meaning the mech takes its group's instead.
/// </summary>
internal sealed class ScriptMechRow : ScriptRow {
	public required ScriptMechRecord Source { get; init; }

	/// <inheritdoc cref="ScriptMechRecord.TypeIndex"/>
	/// <remarks>Presented as a name via <see cref="HercTypeOption"/>.</remarks>
	public short TypeIndex { get => Source.TypeIndex; set => Source.TypeIndex = value; }

	/// <inheritdoc cref="ScriptMechRecord.PositionRef"/>
	public short PositionRef { get => Source.PositionRef; set => Source.PositionRef = value; }

	/// <inheritdoc cref="ScriptMechRecord.HeadingRef"/>
	public short HeadingRef { get => Source.HeadingRef; set => Source.HeadingRef = value; }

	/// <inheritdoc cref="ScriptMechRecord.AiRadarActive"/>
	public short AiRadarActive { get => Source.AiRadarActive; set => Source.AiRadarActive = value; }

	/// <inheritdoc cref="ScriptMechRecord.AiCruiseSpeed"/>
	public short AiCruiseSpeed { get => Source.AiCruiseSpeed; set => Source.AiCruiseSpeed = value; }

	/// <inheritdoc cref="ScriptMechRecord.StartingCondition"/>
	public short StartingCondition { get => Source.StartingCondition; set => Source.StartingCondition = value; }

	/// <inheritdoc cref="ScriptMechRecord.EngagementActionRef"/>
	public short EngagementActionRef { get => Source.EngagementActionRef; set => Source.EngagementActionRef = value; }

	/// <inheritdoc cref="ScriptMechRecord.DefeatActionRef"/>
	public short DefeatActionRef { get => Source.DefeatActionRef; set => Source.DefeatActionRef = value; }

	/// <summary>
	/// The fit as words, for the roster grid — the slots themselves are edited one at a time in the
	/// loadout panel (see <see cref="ScriptWeaponSlotRow"/>), since a raw id list is unreadable and
	/// says nothing about what a launcher is loaded with. Empty slots are left out.
	/// </summary>
	public string WeaponFit => WeaponFitOption.Summarize(Source.WeaponRefs, Source.WeaponSecondary);
}

/// <summary>
/// One of the ten hardpoint slots of the Herc selected in the roster grid — the two parallel
/// loadout arrays presented a slot at a time, so each one can be picked by name.
/// <see cref="AmmoType"/> only means anything on a launcher (see
/// <see cref="WeaponFitOption.IsLauncher"/>); every other mount ignores it and retail leaves the
/// filler 5 there.
/// </summary>
internal sealed class ScriptWeaponSlotRow {
	public required ScriptMechRecord Source { get; init; }

	/// <summary>Position in both loadout arrays. Slot order is the hardpoint order the Herc's own fit uses.</summary>
	public required int Slot { get; init; }

	public short WeaponId {
		get => Source.WeaponRefs[Slot];
		set => Source.WeaponRefs[Slot] = value;
	}

	public short AmmoType {
		get {
			short[] ammo = Source.WeaponSecondary;
			return Slot < ammo.Length ? ammo[Slot] : AmmoTypeOption.Filler;
		}
		set => Source.SetWeaponSecondary(Slot, value);
	}

	public bool IsLauncher => WeaponFitOption.IsLauncher(WeaponId);
}

/// <summary>Block 8 — one flyer roster slot.</summary>
internal sealed class ScriptFlyerRow : ScriptRow {
	public required ScriptFlyerRecord Source { get; init; }

	/// <inheritdoc cref="ScriptFlyerRecord.TypeIndex"/>
	public short TypeIndex { get => Source.TypeIndex; set => Source.TypeIndex = value; }

	/// <inheritdoc cref="ScriptFlyerRecord.PositionRef"/>
	public short PositionRef { get => Source.PositionRef; set => Source.PositionRef = value; }

	/// <inheritdoc cref="ScriptFlyerRecord.HeadingRef"/>
	public short HeadingRef { get => Source.HeadingRef; set => Source.HeadingRef = value; }

	/// <inheritdoc cref="ScriptFlyerRecord.EngagementActionRef"/>
	public short EngagementActionRef { get => Source.EngagementActionRef; set => Source.EngagementActionRef = value; }

	/// <inheritdoc cref="ScriptFlyerRecord.DefeatActionRef"/>
	public short DefeatActionRef { get => Source.DefeatActionRef; set => Source.DefeatActionRef = value; }
}

/// <summary>Block 9 — one base/structure roster slot.</summary>
internal sealed class ScriptBaseRow : ScriptRow {
	public required ScriptBaseRecord Source { get; init; }

	/// <inheritdoc cref="ScriptBaseRecord.TypeIndex"/>
	public short TypeIndex { get => Source.TypeIndex; set => Source.TypeIndex = value; }

	/// <inheritdoc cref="ScriptBaseRecord.PositionRef"/>
	public short PositionRef { get => Source.PositionRef; set => Source.PositionRef = value; }

	/// <inheritdoc cref="ScriptBaseRecord.HeadingRef"/>
	public short HeadingRef { get => Source.HeadingRef; set => Source.HeadingRef = value; }

	/// <inheritdoc cref="ScriptBaseRecord.EngagementActionRef"/>
	public short EngagementActionRef { get => Source.EngagementActionRef; set => Source.EngagementActionRef = value; }

	/// <inheritdoc cref="ScriptBaseRecord.DefeatActionRef"/>
	public short DefeatActionRef { get => Source.DefeatActionRef; set => Source.DefeatActionRef = value; }
}

/// <summary>
/// Block 10 — a group order, one of the ten a block-11 group works through in slot order.
/// <see cref="Verb"/> is search/destroy, ram, guard, patrol, sleep, travel or follow, the
/// subject pair what it is about, and <see cref="ActionRef"/> an action that moves the
/// group on to its next order. A group's route and its fallback spawn point come from its slot-0
/// order's <see cref="RouteRef"/>. See docs/retail/simulation/ai-goals.md.
/// </summary>
internal sealed class ScriptOrderRow : ScriptRow {
	public required ScriptOrder Source { get; init; }

	/// <inheritdoc cref="ScriptOrder.Verb"/>
	public short Verb { get => Source.Verb; set => Source.Verb = value; }

	/// <inheritdoc cref="ScriptOrder.FormationId"/>
	public short FormationId { get => Source.FormationId; set => Source.FormationId = value; }

	/// <inheritdoc cref="ScriptOrder.PointRef"/>
	public short PointRef { get => Source.PointRef; set => Source.PointRef = value; }

	/// <inheritdoc cref="ScriptOrder.RouteRef"/>
	public short RouteRef { get => Source.RouteRef; set => Source.RouteRef = value; }

	/// <inheritdoc cref="ScriptOrder.SubjectKind"/>
	public short SubjectKind { get => Source.SubjectKind; set => Source.SubjectKind = value; }

	/// <inheritdoc cref="ScriptOrder.SubjectRef"/>
	public short SubjectRef { get => Source.SubjectRef; set => Source.SubjectRef = value; }

	/// <inheritdoc cref="ScriptOrder.ActionRef"/>
	public short ActionRef { get => Source.ActionRef; set => Source.ActionRef = value; }
}

/// <summary>
/// Block 11 — a group: the record that actually decides what exists and where.
/// <see cref="MemberKind"/> picks which roster block MemberRefs indexes (0 mechs / 1 flyers /
/// 2 bases), and every record past record 0 activates the slots it names. Record 0 is the player
/// squad's placeholder — it activates nothing and DBSIM fills its members from data\player.mec — so
/// it is shown but its member list is meaningless. <see cref="Side"/> is 0 human, 1 Cybrid; a set
/// <see cref="DeploymentActionRef"/> keeps the group out of the mission until that action fires.
/// </summary>
internal sealed class ScriptGroupRow : ScriptRow {
	public required ScriptGroup Source { get; init; }

	public bool IsPlayerSquad => Index == 0;

	/// <inheritdoc cref="ScriptGroup.PaintsGround"/>
	public short PaintsGround { get => Source.PaintsGround; set => Source.PaintsGround = value; }

	/// <inheritdoc cref="ScriptGroup.MemberKind"/>
	public short MemberKind { get => Source.MemberKind; set => Source.MemberKind = value; }

	/// <inheritdoc cref="ScriptGroup.FormationId"/>
	public short FormationId { get => Source.FormationId; set => Source.FormationId = value; }

	/// <inheritdoc cref="ScriptGroup.PositionRef"/>
	public short PositionRef { get => Source.PositionRef; set => Source.PositionRef = value; }

	/// <inheritdoc cref="ScriptGroup.HeadingRef"/>
	public short HeadingRef { get => Source.HeadingRef; set => Source.HeadingRef = value; }

	/// <inheritdoc cref="ScriptGroup.RouteRef"/>
	public short RouteRef { get => Source.RouteRef; set => Source.RouteRef = value; }

	/// <inheritdoc cref="ScriptGroup.Side"/>
	public short Side { get => Source.Side; set => Source.Side = value; }

	/// <inheritdoc cref="ScriptGroup.DeploymentActionRef"/>
	public short DeploymentActionRef { get => Source.DeploymentActionRef; set => Source.DeploymentActionRef = value; }

	/// <inheritdoc cref="ScriptGroup.MemberRefs"/>
	/// <remarks>Reordering the members moves them.</remarks>
	public string MemberRefs {
		get => ShortCsv.Format(Source.MemberRefs);
		set => ShortCsv.ParseInto(value, Source.MemberRefs);
	}

	/// <inheritdoc cref="ScriptGroup.OrderRefs"/>
	public string OrderRefs {
		get => ShortCsv.Format(Source.OrderRefs);
		set => ShortCsv.ParseInto(value, Source.OrderRefs);
	}
}

/// <summary>
/// Block 12 — a mission objective. DBSIM's first pass discards it and the spawn pass comes back to
/// build the objectives from it; see docs/retail/simulation/mission-objectives.md.
/// </summary>
internal sealed class ScriptObjectiveRow : ScriptRow {
	public required ScriptObjective Source { get; init; }

	/// <inheritdoc cref="ScriptObjective.Required"/>
	public short Required { get => Source.Required; set => Source.Required = value; }

	/// <inheritdoc cref="ScriptObjective.ConditionCode"/>
	public short ConditionCode { get => Source.ConditionCode; set => Source.ConditionCode = value; }

	/// <inheritdoc cref="ScriptObjective.SubjectKind"/>
	public short SubjectKind { get => Source.SubjectKind; set => Source.SubjectKind = value; }

	/// <inheritdoc cref="ScriptObjective.SubjectRef"/>
	public short SubjectRef { get => Source.SubjectRef; set => Source.SubjectRef = value; }

	/// <inheritdoc cref="ScriptObjective.PointRef"/>
	public short PointRef { get => Source.PointRef; set => Source.PointRef = value; }

	/// <inheritdoc cref="ScriptObjective.RouteRef"/>
	public short RouteRef { get => Source.RouteRef; set => Source.RouteRef = value; }

	/// <inheritdoc cref="ScriptObjective.TextRef"/>
	public short TextRef { get => Source.TextRef; set => Source.TextRef = value; }

	/// <inheritdoc cref="ScriptObjective.CounterRefs"/>
	public string CounterRefs {
		get => ShortCsv.Format(Source.CounterRefs);
		set => ShortCsv.ParseInto(value, Source.CounterRefs);
	}

	/// <inheritdoc cref="ScriptObjective.CounterOps"/>
	public string CounterOps {
		get => ShortCsv.Format(Source.CounterOps);
		set => ShortCsv.ParseInto(value, Source.CounterOps);
	}
}

/// <summary>
/// Block 13 — one line of the objective list the player is shown, as a <c>data\mission.str</c> line
/// index. This block is a plain count-prefixed list with nothing referencing it, so rows here can be
/// added and removed freely; the whole array is rebuilt from the grid on save.
/// </summary>
internal sealed class ScriptObjectiveLineRow {
	public short Value { get; set; }
}
