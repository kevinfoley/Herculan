using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A block-4 trigger area as <c>TriggerArea_Resolve</c> (<c>ES1DBSIM 0001cb48</c>) resolves it to its 10 bytes
/// (docs/es1/simulation/mission-groups.md#trigger-areas).
/// </summary>
/// <param name="Shape"><c>+0</c>, the type: 0 a box between two points, any other a circle about one.</param>
/// <param name="FirstPoint"><c>+2</c>, the block-1 point the second word indexes.</param>
/// <param name="SecondPoint"><c>+6</c> for type 0, the block-1 point the third word indexes; the origin for any other type.</param>
/// <param name="Radius"><c>+6</c> for any other type, the third word × 10 in 32 bits; 0 for type 0.</param>
public sealed record Es1TriggerArea(short Shape, Vec3i FirstPoint, Vec3i SecondPoint, int Radius) {
	/// <summary>The type that is a box between two points.</summary>
	public const short Box = 0;

	/// <summary>
	/// <c>TriggerArea_Resolve</c>(area, n, <paramref name="points"/>, record) over a record's three words. Retail points past
	/// block 1 for a ref outside it; such a point is the origin here, an engine guard.
	/// </summary>
	public static Es1TriggerArea Resolve(short shape, short pointRef, short third, Vec3i[] points) => shape == Box
		? new(shape, PointAt(points, pointRef), PointAt(points, third), 0)
		: new(shape, PointAt(points, pointRef), Vec3i.Zero, third * 10);

	/// <summary>
	/// <c>TriggerArea_ContainsPoint</c> (<c>0001cb94</c>)(area, <paramref name="position"/>): for a <see cref="Box"/>,
	/// whether the position lies strictly between the two points in X and in Y, either corner order; for any other type,
	/// whether <c>Math_DistanceBetweenPoints2D</c> from the first point is below <see cref="Radius"/>, both signed
	/// (docs/es1/simulation/mission-groups.md#trigger-areas).
	/// </summary>
	public bool ContainsPoint(Vec3i position) {
		if (Shape != Box) {
			return Es1PlayerThink.DistanceBetweenPoints2D(FirstPoint, position) < Radius;
		}

		return Math.Min(FirstPoint.X, SecondPoint.X) < position.X && position.X < Math.Max(FirstPoint.X, SecondPoint.X)
			&& Math.Min(FirstPoint.Y, SecondPoint.Y) < position.Y && position.Y < Math.Max(FirstPoint.Y, SecondPoint.Y);
	}

	private static Vec3i PointAt(Vec3i[] points, short index) => index >= 0 && index < points.Length ? points[index] : Vec3i.Zero;
}

/// <summary>
/// A block-5 action as the first pass builds its 16-byte record (docs/es1/formats/script-dat.md#reading), and the three
/// functions that fire it (docs/es1/simulation/mission-groups.md#actions).
/// </summary>
public sealed class Es1Action {
	/// <summary>The operations <see cref="Activate"/> knows: add 1 to the flag, and set it to 0.</summary>
	public const short AddOne = 6, Clear = 5;

	/// <inheritdoc cref="Es1ScriptAction.Type"/>
	public short Type { get; init; }

	/// <inheritdoc cref="Es1ScriptAction.Verb"/>
	public short Verb { get; init; }

	/// <summary>
	/// <c>+4</c> and <c>+6</c>, the trigger areas the refs before the first negative one name. Retail points past block 4
	/// for a ref outside it; such a ref names no area here, an engine guard.
	/// </summary>
	public Es1TriggerArea[] Areas { get; init; } = [];

	/// <summary><c>+0xa</c>, set once the action has fired.</summary>
	public bool Activated { get; set; }

	/// <summary><c>+0xc</c>, the flag the action writes, from <c>.MSN</c> <c>0x1a</c>.</summary>
	public short Flag { get; init; }

	/// <summary><c>+0xe</c>, the operation, from <c>.MSN</c> <c>0x1c</c>.</summary>
	public short Operation { get; init; }

	/// <summary>
	/// <c>Action_Activate</c> (<c>0001cc5c</c>)(action): once only, <see cref="Activated"/> set and, when <see cref="Flag"/>
	/// is not negative, that flag of <paramref name="vars"/> (<c>MissionVars</c>) up by 1 for <see cref="AddOne"/> or set
	/// to 0 for <see cref="Clear"/>; any other operation writes nothing. Retail writes a flag past the array unchecked;
	/// nothing is written for one here.
	/// </summary>
	public void Activate(short[] vars) {
		if (Activated) {
			return;
		}

		Activated = true;
		if (Flag < 0 || Flag >= vars.Length) {
			return;
		}

		if (Operation == AddOne) {
			vars[Flag]++;
		} else if (Operation == Clear) {
			vars[Flag] = 0;
		}
	}

	/// <summary>
	/// <c>Action_TestTrigger</c> (<c>0001ccb4</c>)(action, <paramref name="position"/>): true at once when already
	/// <see cref="Activated"/>; otherwise the position offered to each area in turn, and on the first that contains it the
	/// action activated and true; false when none does.
	/// </summary>
	public bool TestTrigger(Vec3i position, short[] vars) {
		if (Activated) {
			return true;
		}

		foreach (var area in Areas) {
			if (area.ContainsPoint(position)) {
				Activate(vars);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// <c>Action_TestTriggerForGroup</c> (<c>0001cd30</c>)(<paramref name="group"/>, action): <see cref="TestTrigger"/> with
	/// each member's position in turn, true at the first that answers. A member the slice does not build, a base, is
	/// passed over until the plan's module 2. PLACEHOLDER: with no member retail answers the word its frame leaves at <c>[EBP−0x14]</c>,
	/// which nothing in the function writes, and it is not known; false here.
	/// </summary>
	public bool TestTriggerForGroup(Es1MissionGroup group, short[] vars) {
		foreach (var entry in group.Members) {
			if (entry is IEs1SimObject member && TestTrigger(member.Position, vars)) {
				return true;
			}
		}

		return false;
	}
}

/// <summary>
/// <c>Actions_EvaluateTriggers</c> (<c>ES1DBSIM 0001f87c</c>), the tick's test of every action's trigger
/// (docs/es1/simulation/world.md#trigger-areas--actions_evaluatetriggers-0001f87c).
/// </summary>
public static class Es1Actions {
	/// <summary>
	/// <c>Actions_EvaluateTriggers</c>(<paramref name="subject"/>), over <paramref name="actions"/> (<c>MissionActions</c>,
	/// <c>000bc496</c>) in order, by type: 0, the subject's position (<see cref="Es1Action.TestTrigger"/>); 1, the
	/// subject's group (<see cref="Es1Action.TestTriggerForGroup"/>); 2 and 3, each group of <paramref name="groups"/>
	/// (<c>MissionGroups</c>, <c>000bc476</c>) whose side is 0 or 1, and 4 and 5 each whose members' kind is 0 (mechs) or 1
	/// (flyers), those four passing over a group that holds an action and stopping at the first group that answers; any
	/// other type, nothing. An answer activates the action (<see cref="Es1Action.Activate"/>). Retail reads the subject's
	/// group with no test; a subject with none offers type 1 nothing here, an engine guard.
	/// </summary>
	public static void EvaluateTriggers(IReadOnlyList<Es1Action> actions, IReadOnlyList<Es1MissionGroup> groups,
			IEs1SimObject subject, short[] vars) {
		foreach (var action in actions) {
			bool answered = action.Type switch {
				0 => action.TestTrigger(subject.Position, vars),
				1 => subject.Group is { } group && action.TestTriggerForGroup(group, vars),
				2 => AnyGroupAnswers(action, groups, group => group.Side == 0, vars),
				3 => AnyGroupAnswers(action, groups, group => group.Side == 1, vars),
				4 => AnyGroupAnswers(action, groups, group => group.MemberKind == 0, vars),
				5 => AnyGroupAnswers(action, groups, group => group.MemberKind == 1, vars),
				_ => false,
			};

			if (answered) {
				action.Activate(vars);
			}
		}
	}

	/// <summary>The sweep of types 2 to 5: each group the test takes that holds no action, until one answers.</summary>
	private static bool AnyGroupAnswers(Es1Action action, IReadOnlyList<Es1MissionGroup> groups,
			Func<Es1MissionGroup, bool> takes, short[] vars) {
		foreach (var group in groups) {
			if (takes(group) && group.Action == null && action.TestTriggerForGroup(group, vars)) {
				return true;
			}
		}

		return false;
	}
}
