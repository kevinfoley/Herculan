using System.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Shell;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// One mission a browser lists, by file name, and whether it is a loose file the editor saved into the install. One no
/// archive holds is a custom mission.
/// </summary>
/// <param name="File">The <c>.MSN</c> file name, which the load is asked for.</param>
/// <param name="Saved">Whether the install has it as a loose file, which is what loads (<see cref="SavedMissions"/>).</param>
/// <param name="InArchive">Whether an archive holds it too, which the loose file overrides.</param>
internal sealed record BrowsedMission(string File, bool Saved, bool InArchive) {
	/// <summary>The list's line: the file name, marked "(saved)" over an archive mission.</summary>
	public string Label => Saved && InArchive ? $"{File} (saved)" : File;
}

/// <summary>
/// A floating window opened from File that lists missions, one of which the user picks to open: "Load from VOL", every
/// <c>.MSN</c> in the install's archives, or "Load custom mission", every one saved into the install that no archive holds.
/// The window only asks; the host does the load (<see cref="TakeRequest"/>) and reports back through <see cref="Loading"/>
/// and <see cref="Failure"/>.
/// </summary>
internal sealed class MissionBrowser {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 320f;
	private const float ListHeight = 420f;

	private readonly string _title;
	private readonly string _none;
	private readonly Func<IReadOnlyList<BrowsedMission>> _listMissions;
	private IReadOnlyList<BrowsedMission>? _missions;
	private string? _highlighted;
	private string? _request;
	private bool _open;

	/// <param name="title">The window's title, as File names it.</param>
	/// <param name="none">What the window says when it lists no missions.</param>
	/// <param name="listMissions">Lists the missions; called the first time the window opens, and after <see cref="Refresh"/>.</param>
	public MissionBrowser(string title, string none, Func<IReadOnlyList<BrowsedMission>> listMissions) {
		_title = title;
		_none = none;
		_listMissions = listMissions;
	}

	/// <summary>The archive's missions of <paramref name="missions"/> (<see cref="Merge"/>), "Load from VOL"'s list.</summary>
	public static IReadOnlyList<BrowsedMission> Archived(IEnumerable<BrowsedMission> missions) =>
		missions.Where(mission => mission.InArchive).ToList();

	/// <summary>The custom missions of <paramref name="missions"/> (<see cref="Merge"/>), "Load custom mission"'s list.</summary>
	public static IReadOnlyList<BrowsedMission> Custom(IEnumerable<BrowsedMission> missions) =>
		missions.Where(mission => !mission.InArchive).ToList();

	/// <summary>
	/// The archives' missions <paramref name="archive"/> and the install's loose ones <paramref name="saved"/>, by file
	/// name, as one list in name order, each name once.
	/// </summary>
	public static IReadOnlyList<BrowsedMission> Merge(IEnumerable<string> archive, IEnumerable<string> saved) {
		var inArchive = new HashSet<string>(archive, StringComparer.OrdinalIgnoreCase);
		var loose = new HashSet<string>(saved, StringComparer.OrdinalIgnoreCase);
		return inArchive.Union(loose, StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.Select(file => new BrowsedMission(file, loose.Contains(file), inArchive.Contains(file)))
			.ToList();
	}

	/// <summary>
	/// The mission of <paramref name="missions"/> named <paramref name="name"/>, as the list names it, with or without its
	/// <c>.MSN</c> and in any case; null for none.
	/// </summary>
	public static BrowsedMission? Find(IEnumerable<BrowsedMission> missions, string name) {
		string file = Path.GetExtension(name).Equals(MissionFileLaunch.MissionExtension, StringComparison.OrdinalIgnoreCase)
			? name
			: name + MissionFileLaunch.MissionExtension;
		return missions.FirstOrDefault(mission => mission.File.Equals(file, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The listed mission named <paramref name="name"/>, as <see cref="Find(IEnumerable{BrowsedMission}, string)"/> finds it.</summary>
	public BrowsedMission? Find(string name) => Find(_missions ??= _listMissions(), name);

	/// <summary>Lists the missions again the next time the window is drawn, after a save has added one.</summary>
	public void Refresh() => _missions = null;

	/// <summary>The mission being loaded, which the window names in place of its list until the load ends.</summary>
	public string? Loading { get; set; }

	/// <summary>Why the last load failed, shown under the list until another is asked for.</summary>
	public string? Failure { get; set; }

	/// <summary>Whether the window is up.</summary>
	public bool IsOpen => _open;

	public void Open() {
		_missions ??= _listMissions();
		_open = true;
	}

	public void Close() {
		_open = false;
		Failure = null;
	}

	/// <summary>The mission the user asked to open since the last call, or null.</summary>
	public string? TakeRequest() {
		string? request = _request;
		_request = null;
		return request;
	}

	/// <summary>Draws the window, if it is open. Call once per frame, inside the ImGui frame.</summary>
	public void Draw() {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(_title, ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			if (Loading != null) {
				ImGui.TextUnformatted($"Loading {Loading}...");
			} else {
				DrawList();
			}
		}

		ImGui.End();

		if (!stayOpen && Loading == null) {
			Close();
		}
	}

	private void DrawList() {
		var missions = _missions ??= _listMissions();
		if (missions.Count == 0) {
			ImGui.TextWrapped(_none);
		}

		if (ImGui.BeginListBox("##missions", new Vector2(-1f, ScaledImGui.Scaled(ListHeight)))) {
			foreach (var mission in missions) {
				if (ImGui.Selectable(mission.Label, mission.File == _highlighted, ImGuiSelectableFlags.AllowDoubleClick)) {
					_highlighted = mission.File;
					if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
						Ask(mission.File);
					}
				}
			}

			ImGui.EndListBox();
		}

		ImGui.BeginDisabled(_highlighted == null);
		if (ImGui.Button("Load", new Vector2(-1f, 0f)) && _highlighted != null) {
			Ask(_highlighted);
		}

		ImGui.EndDisabled();

		if (Failure != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, FailureColor);
			ImGui.TextWrapped(Failure);
			ImGui.PopStyleColor();
		}
	}

	private void Ask(string mission) {
		_request = mission;
		Failure = null;
	}

	private static readonly Vector4 FailureColor = new(1f, 0.45f, 0.4f, 1f);
}
