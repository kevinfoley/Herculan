using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;

namespace HercWorks.Query;

/// <summary>One ES1 row-11 group and the order it holds itself (docs/es1/formats/msn-mission-file.md#groups).</summary>
/// <param name="Index">The group's position in row 11; 0 stands for the player's squad.</param>
/// <param name="Members">How many of the eight member refs are set.</param>
/// <param name="Order">The order, <c>0x32</c>.</param>
/// <param name="Formation">The formation, <c>0x30</c>.</param>
/// <param name="MemberKind">The member kind, <c>0x2e</c>: 0 mechs, 1 flyers, 2 bases.</param>
/// <param name="SideWord">The word at <c>0x4a</c>, whose low byte is the side.</param>
/// <param name="TargetRow">The row <paramref name="TargetRef"/> names, <c>0x4c</c>.</param>
/// <param name="TargetRef">The target, <c>0x4e</c>.</param>
/// <param name="ActionRef">The action ref, <c>0x50</c>.</param>
internal sealed record Es1GroupOrder(
	int Index, short Guid, short ConditionRef, string? Condition, short MemberKind, int Members, short Order, short Formation,
	short SideWord, short TargetRow, short TargetRef, short ActionRef, short WaypointGroupRef) {
	public bool PlayerSquad => Index == 0;
}

internal sealed record Es1OrderQueryMission(string Mission, bool OtherLayout, IReadOnlyList<Es1GroupOrder> Groups);

/// <summary>How many groups hold one order value, and in how many missions.</summary>
internal sealed record Es1OrderCount(short Order, int Groups, int Missions);

/// <summary>The result of <c>orders</c> over an ES1 install.</summary>
/// <param name="OtherLayoutMissions">The missions searched that are in the other layout; their groups are read and counted as the rest are.</param>
internal sealed record Es1OrderQueryResult(
	int MissionsSearched, int Groups, IReadOnlyList<string> OtherLayoutMissions, IReadOnlyList<Es1OrderCount> OrderCensus,
	IReadOnlyList<Es1OrderQueryMission> Missions);

/// <summary>One block-9 group as the mode-0 load leaves it in <c>data\script.dat</c> (docs/es1/formats/script-dat.md#writing).</summary>
/// <param name="Index">The group's block-9 index, the one every ref to it resolves to; 0 stands for the player's squad.</param>
/// <param name="TargetRef">The target, resolved to an index into the block <paramref name="TargetRow"/> names.</param>
/// <param name="WaypointGroupRef">The block-3 index of its waypoint group, or -1.</param>
/// <param name="ActionRef">The block-5 index of its action, or -1.</param>
internal sealed record Es1LoadedGroup(
	int Index, short MemberKind, int Members, short Order, short Formation, short SideWord, short TargetRow, short TargetRef,
	short WaypointGroupRef, short ActionRef) {
	public bool PlayerSquad => Index == 0;
}

/// <summary>One mission loaded, or why it was not.</summary>
/// <param name="HeaderWord3">The loaded header word 3, which the simulator's mission status reads; null when not loaded.</param>
/// <param name="HeaderWord7">The loaded header word 7, the difficulty; null when not loaded.</param>
/// <param name="Choices">How many of the load's draws had more than one outcome; each was taken as 0.</param>
/// <param name="NotLoaded">The load's reason for refusing the mission, null when it loaded.</param>
internal sealed record Es1LoadedMission(string Mission, short? HeaderWord3, short? HeaderWord7, int Choices, string? NotLoaded,
	IReadOnlyList<Es1LoadedGroup> Groups);

/// <summary>The result of <c>orders --loaded</c> over an ES1 install.</summary>
/// <param name="Difficulty">DIFFICULTY, <c>data\prefs.cfg</c>'s option 14, which the load writes to every mission's header word 7.</param>
/// <param name="PrefsFound">Whether the install has a <c>data\prefs.cfg</c>; without one the five MISSION PARAMS words are 0.</param>
internal sealed record Es1LoadedOrderResult(
	int MissionsSearched, int Groups, short Difficulty, bool PrefsFound, IReadOnlyList<Es1OrderCount> OrderCensus,
	IReadOnlyList<Es1LoadedMission> Missions);

/// <summary>
/// <c>orders</c> over an ES1 install. An ES1 group holds its order, formation and target itself, where ES2's names up to
/// ten order records (docs/es1/formats/msn-mission-file.md#groups), so this lists each group's fields and counts the
/// order values over every group searched. A file in the other layout has the same group record
/// (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout) and is listed with the rest.
/// </summary>
internal static class Es1OrderQuery {
	public static Es1OrderQueryResult Run(RetailData data, IReadOnlyCollection<string> missions) {
		var searched = data.Es1Missions
			.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))
			.Select(m => new Es1OrderQueryMission(m.Name, m.OtherLayout, m.File.Groups.Select((g, i) => new Es1GroupOrder(
				i, g.GUID, g.ConditionRef, Conditions.Describe(m.File, g.ConditionRef), g.MemberKind, g.MemberRefs.Count(r => r != -1),
				g.Word32, g.Word30, g.Word4A, g.Ref4ERow, g.Ref4E, g.ActionRef, g.WaypointGroupRef)).ToList()))
			.ToList();

		var census = searched
			.SelectMany(m => m.Groups.Select(g => (m.Mission, g.Order)))
			.GroupBy(x => x.Order)
			.OrderBy(g => g.Key)
			.Select(g => new Es1OrderCount(g.Key, g.Count(), g.Select(x => x.Mission).Distinct().Count()))
			.ToList();

		return new Es1OrderQueryResult(searched.Count, searched.Sum(m => m.Groups.Count),
			searched.Where(m => m.OtherLayout).Select(m => m.Mission).ToList(), census, searched);
	}

	public static void Write(TextWriter o, Es1OrderQueryResult result) {
		o.WriteLine($"{result.Groups} row 11 groups across {result.MissionsSearched} missions" +
			(result.OtherLayoutMissions.Count == 0 ? "." : $", {result.OtherLayoutMissions.Count} of them in the other layout " +
				$"({string.Join(", ", result.OtherLayoutMissions)}), whose groups are counted with the rest."));
		o.WriteLine("Order (0x32)  Groups  Missions");
		foreach (var c in result.OrderCensus) {
			o.WriteLine($"{c.Order,12}  {c.Groups,6}  {c.Missions,8}");
		}

		foreach (var m in result.Missions) {
			o.WriteLine();
			o.WriteLine($"{m.Mission}: {m.Groups.Count} groups{(m.OtherLayout ? " (other layout)" : "")}");
			foreach (var g in m.Groups) {
				var line = new List<string> {
					g.Guid == -1 ? $"group #{g.Index} (no GUID)" : $"group {g.Guid} (#{g.Index})",
					$"{g.Members} {OrderQuery.MemberKind(g.MemberKind)}{(g.Members == 1 ? "" : "s")}" + (g.PlayerSquad ? ", the player's squad" : ""),
					$"order {g.Order}",
					$"formation {g.Formation}",
					$"side word {g.SideWord}",
				};
				if (g.TargetRef != -1) {
					line.Add($"target {OrderQuery.SubjectKind(g.TargetRow)} {g.TargetRef}");
				}

				if (g.WaypointGroupRef != -1) {
					line.Add($"waypoint group {g.WaypointGroupRef}");
				}

				if (g.ActionRef != -1) {
					line.Add($"action {g.ActionRef}");
				}

				if (g.ConditionRef != -1) {
					line.Add(TextReport.Cond(g.ConditionRef, g.Condition, true));
				}

				o.WriteLine("  " + string.Join(", ", line));
			}
		}
	}

	/// <summary>The MISSION PARAMS file the load reads, the install's <c>data\prefs.cfg</c>.</summary>
	private const string PrefsFolder = "DATA";
	private const string PrefsName = "prefs.cfg";

	/// <summary>
	/// <c>orders --loaded</c>: each mission through <see cref="Es1MissionLoad"/>, the shell's mode-0 load, with the install's
	/// <c>data\prefs.cfg</c>, <c>car\SM_INIT.DAT</c> and <c>bst\TECH_SNG.DAT</c> as a SINGLE MISSION start reads them, and
	/// block 9 of the <c>data\script.dat</c> it writes: the groups after the conditions' filter and the repeated GUIDs'
	/// overlay (docs/es1/formats/msn-mission-file.md#filtering-variants-and-repeats), refs resolved. Every draw is taken as 0,
	/// as <c>lint</c> loads ES2's missions with every roll 0, so a mission whose draws pick between records shows the
	/// first; the count of draws with more than one outcome says when that applies.
	/// </summary>
	/// <exception cref="InvalidDataException">The shell archive lacks the hangar or the equipment file.</exception>
	public static Es1LoadedOrderResult RunLoaded(RetailData data, IReadOnlyCollection<string> missions) {
		if (data.ShellFile("car", "SM_INIT.DAT") is not { } hangarBytes
				|| new Es1HercListTransformer().Parse(hangarBytes) is not Es1HercList hangar) {
			throw new InvalidDataException($"No car\\SM_INIT.DAT in the archives the shell mounts.");
		}

		if (data.ShellFile("bst", "TECH_SNG.DAT") is not { } techBytes
				|| new Es1TechFileTransformer().Parse(techBytes) is not Es1TechFile tech) {
			throw new InvalidDataException($"No bst\\TECH_SNG.DAT in the archives the shell mounts.");
		}

		string prefsPath = CaseInsensitivePath.Combine(data.InstallRoot, PrefsFolder, PrefsName);
		bool prefsFound = File.Exists(prefsPath);
		var missionParams = Es1MissionParams.FromPrefs(prefsFound ? File.ReadAllBytes(prefsPath) : null);

		var loaded = new List<Es1LoadedMission>();
		foreach (var m in data.Es1Missions.Where(m => missions.Count == 0 || missions.Contains(m.Name, StringComparer.OrdinalIgnoreCase))) {
			int choices = 0;
			int Roll(short bound) {
				if (bound > 1) {
					choices++;
				}

				return 0;
			}

			try {
				var script = Es1MissionLoad.Load(m.File, missionParams, hangar, tech, Roll).Script;
				loaded.Add(new Es1LoadedMission(m.Name, script.Header[3], script.Header[7], choices, null, script.Groups.Select((g, i) =>
					new Es1LoadedGroup(i, g.MemberKind, g.MemberRefs.Count(r => r != -1), g.Word32, g.Word30, g.Word4A, g.Ref4ERow, g.Ref4E,
						g.WaypointGroupRef, g.ActionRef)).ToList()));
			} catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) {
				loaded.Add(new Es1LoadedMission(m.Name, null, null, choices, ex.Message, []));
			}
		}

		var census = loaded
			.SelectMany(m => m.Groups.Select(g => (m.Mission, g.Order)))
			.GroupBy(x => x.Order)
			.OrderBy(g => g.Key)
			.Select(g => new Es1OrderCount(g.Key, g.Count(), g.Select(x => x.Mission).Distinct().Count()))
			.ToList();

		return new Es1LoadedOrderResult(loaded.Count, loaded.Sum(m => m.Groups.Count), missionParams.Difficulty, prefsFound, census, loaded);
	}

	public static void WriteLoaded(TextWriter o, Es1LoadedOrderResult result) {
		var refused = result.Missions.Where(m => m.NotLoaded != null).ToList();
		o.WriteLine($"{result.Groups} block 9 groups across {result.MissionsSearched - refused.Count} missions loaded as SINGLE MISSION " +
			"loads them, every draw taken as 0" + (refused.Count == 0 ? "." : $"; {refused.Count} not loaded, listed below."));
		o.WriteLine(result.PrefsFound
			? $"Header word 7, the difficulty, is data\\prefs.cfg's option 14 in every mission: {result.Difficulty}."
			: "The install has no data\\prefs.cfg, so header words 5 to 9, the difficulty (word 7) among them, are 0.");
		o.WriteLine("Order (0x32)  Groups  Missions");
		foreach (var c in result.OrderCensus) {
			o.WriteLine($"{c.Order,12}  {c.Groups,6}  {c.Missions,8}");
		}

		foreach (var m in result.Missions) {
			o.WriteLine();
			if (m.NotLoaded != null) {
				o.WriteLine($"{m.Mission}: not loaded: {m.NotLoaded}");
				continue;
			}

			o.WriteLine($"{m.Mission}: header word 3 = {m.HeaderWord3}, {m.Groups.Count} groups" +
				(m.Choices == 0 ? "" : $", {m.Choices} draw{(m.Choices == 1 ? "" : "s")} with more than one outcome"));
			foreach (var g in m.Groups) {
				var line = new List<string> {
					$"#{g.Index}",
					$"{g.Members} {OrderQuery.MemberKind(g.MemberKind)}{(g.Members == 1 ? "" : "s")}" + (g.PlayerSquad ? ", the player's squad" : ""),
					$"order {g.Order}",
					$"formation {g.Formation}",
					$"side word {g.SideWord}",
				};
				if (g.TargetRef != -1) {
					line.Add($"target {OrderQuery.SubjectKind(g.TargetRow)} #{g.TargetRef}");
				}

				if (g.WaypointGroupRef != -1) {
					line.Add($"waypoint group #{g.WaypointGroupRef}");
				}

				if (g.ActionRef != -1) {
					line.Add($"action #{g.ActionRef}");
				}

				o.WriteLine("  " + string.Join(", ", line));
			}
		}
	}
}
