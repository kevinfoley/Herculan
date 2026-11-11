using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Content;
using Herculan.Engine.Editor;
using Herculan.Es2.Install;
using Herculan.Tests.Shared;
using ImGuiNET;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The editor's Properties panel, laid out at its own width for every record of every retail mission, as the editor opens it,
/// with the editor's fonts and style and without a window or a GPU (<see cref="ImGuiLayoutCheck"/>). Catches a row whose
/// content does not fit the panel: a button or a label drawn after a name on the same line with no room kept for it, which the
/// panel's edge cuts off. Skips silently without an install, as the rest of the suite does.
///
/// <para>The missions are split among the nested <see cref="Part1"/>–<see cref="Part4"/>, which xUnit runs side by side
/// while it keeps each class's cases in turn, since opening a mission takes most of a case's time. Each part has its own
/// install and its own folder, so no two cases share anything they write. The layout itself goes one case at a time,
/// because ImGui keeps its current context in a single process-wide global.</para>
/// </summary>
public abstract class PropertiesPanelLayoutTests {
	// Tall enough that every view is drawn whole, so the draw data holds every row; the panel takes the screen's height.
	private static readonly Vector2 Screen = new(1920f, 10000f);

	// Frames drawn per selection before the last is checked: a view's first frame after the selection changes lays out against
	// the last view's content size.
	private const int Frames = 2;

	// How many classes the missions are split among: Part1 to Part4.
	private const int Parts = 4;

	// Each part's install mounts and what is read once from its missions, shared by that part's cases.
	private static readonly Lazy<Install?>[] Opened = [
		.. Enumerable.Range(0, Parts).Select(_ =>
			new Lazy<Install?>(() => GameInstall.Locate(null) is { } root ? new Install(root) : null)),
	];

	// Held while a case has an ImGui context current.
	private static readonly object ImGuiLock = new();

	/// <summary>Every fourth mission in the archives' order, from the first (<see cref="MissionsOf"/>).</summary>
	public sealed class Part1 : PropertiesPanelLayoutTests {
		public static IEnumerable<object[]> Missions() => MissionsOf(0);

		[Theory]
		[MemberData(nameof(Missions))]
		public void EveryRowFitsThePanel(string name) => Check(0, name);
	}

	/// <summary>Every fourth mission, from the second.</summary>
	public sealed class Part2 : PropertiesPanelLayoutTests {
		public static IEnumerable<object[]> Missions() => MissionsOf(1);

		[Theory]
		[MemberData(nameof(Missions))]
		public void EveryRowFitsThePanel(string name) => Check(1, name);
	}

	/// <summary>Every fourth mission, from the third.</summary>
	public sealed class Part3 : PropertiesPanelLayoutTests {
		public static IEnumerable<object[]> Missions() => MissionsOf(2);

		[Theory]
		[MemberData(nameof(Missions))]
		public void EveryRowFitsThePanel(string name) => Check(2, name);
	}

	/// <summary>Every fourth mission, from the fourth.</summary>
	public sealed class Part4 : PropertiesPanelLayoutTests {
		public static IEnumerable<object[]> Missions() => MissionsOf(3);

		[Theory]
		[MemberData(nameof(Missions))]
		public void EveryRowFitsThePanel(string name) => Check(3, name);
	}

	private sealed class Install {
		public Install(string root) {
			Root = root;
			Shell = GameInstall.MountShell(root);
			Simulator = GameInstall.MountSimulator(root);
			Archived = new ArchivedMissions(() => Shell);
			Cybrid = new CybridWeapons(Simulator, Archived);
			Zones = new ZoneCatalog(Simulator, Archived);
		}

		public string Root { get; }

		public GameContent Shell { get; }

		public GameContent Simulator { get; }

		public ArchivedMissions Archived { get; }

		public CybridWeapons Cybrid { get; }

		public ZoneCatalog Zones { get; }
	}

	/// <summary>
	/// Every mission of the install's archives whose place in their order falls to <paramref name="part"/>, the parts taking
	/// turns so that each gets a share of every campaign; one case that skips without an install.
	/// </summary>
	private static IEnumerable<object[]> MissionsOf(int part) =>
		Opened[part].Value is { } install
			? install.Archived.All.Where((_, index) => index % Parts == part).Select(mission => new object[] { mission.Name })
			: [["(no install)"]];

	private static void Check(int part, string name) {
		if (Opened[part].Value is not { } install) {
			return;
		}

		string directory = Path.Combine(Path.GetTempPath(), "herculan-tests", $"{nameof(PropertiesPanelLayoutTests)}{part + 1}");
		// The shell's writer does not truncate, so a longer file left by an earlier case would keep its tail.
		foreach (string file in new[] { "script.dat", "mission.str" }) {
			if (File.Exists(Path.Combine(directory, file))) {
				File.Delete(Path.Combine(directory, file));
			}
		}

		string? script = MissionOpener.Open(install.Shell, install.Root, $"{name}.MSN", directory, out var document, out var squad,
			out string? failure);
		Assert.True(script != null, failure);
		var mission = EditorMission.Open(install.Simulator, script, name, directory, document!, squad!, () => install.Shell,
			install.Cybrid, install.Zones);

		var cut = new List<string>();
		lock (ImGuiLock) {
			var context = ImGui.CreateContext();
			try {
				var (glyphs, bold) = ImGuiLayoutCheck.SetUp(Screen, "OpenSans-Regular.ttf", null, "OpenSans-Bold.ttf");
				EditorText.BoldFont = () => bold;
				foreach (var selection in Selections(mission.Source.Mission)) {
					mission.Selection.Current = selection;
					var found = ImGuiLayoutCheck.CutOffs(() => mission.Properties.Draw(Screen, 0f), Frames, glyphs);
					if (found.Count > 0) {
						cut.Add($"{selection}:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
					}
				}
			} finally {
				EditorText.BoldFont = null;
				ImGui.DestroyContext(context);
			}
		}

		Assert.True(cut.Count == 0, $"{name}:{Environment.NewLine}{string.Join(Environment.NewLine, cut)}");
	}

	// Every record the panel has a view for: each GUID of the rows a selection names, a route also by its first waypoint,
	// and each objective by its index.
	private static IEnumerable<EditorSelection> Selections(MissionFile mission) {
		foreach (short guid in Guids(mission.Groups)) {
			yield return new GroupSelection(guid);
		}

		foreach (short guid in Guids(mission.Mechs)) {
			yield return new ObjectSelection(12, guid);
		}

		foreach (short guid in Guids(mission.Flyers)) {
			yield return new ObjectSelection(13, guid);
		}

		foreach (short guid in Guids(mission.Bases)) {
			yield return new ObjectSelection(14, guid);
		}

		foreach (short guid in Guids(mission.WaypointGroups)) {
			yield return new RouteSelection(guid);
			yield return new RouteSelection(guid, 0);
		}

		foreach (short guid in Guids(mission.TriggerAreas)) {
			yield return new AreaSelection(guid);
		}

		foreach (short guid in Guids(mission.Actions)) {
			yield return new ActionSelection(guid);
		}

		foreach (short guid in Guids(mission.ActionTimers)) {
			yield return new TimerSelection(guid);
		}

		foreach (short guid in Guids(mission.Conditions)) {
			yield return new ConditionSelection(guid);
		}

		for (int record = 0; record < (mission.Objectives?.Length ?? 0); record++) {
			yield return new ObjectiveSelection(record);
		}
	}

	// The GUIDs a row's records carry, less a template's -1, each once.
	private static IEnumerable<short> Guids(IEnumerable<MapObject>? row) =>
		(row ?? []).Select(record => record.GUID).Where(guid => guid != -1).Distinct();
}
