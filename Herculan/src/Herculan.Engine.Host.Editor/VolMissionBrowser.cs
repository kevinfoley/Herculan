using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// The floating "Load from VOL" window opened from File: every <c>.MSN</c> in the install's archives, one of
/// which the user picks to open. The window only asks; the host does the load (<see cref="TakeRequest"/>) and
/// reports back through <see cref="Loading"/> and <see cref="Failure"/>.
/// </summary>
internal sealed class VolMissionBrowser {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 320f;
	private const float ListHeight = 420f;

	private readonly Func<IReadOnlyList<string>> _listMissions;
	private IReadOnlyList<string>? _missions;
	private string? _highlighted;
	private string? _request;
	private bool _open;

	/// <param name="listMissions">Lists the missions; called the first time the window opens.</param>
	public VolMissionBrowser(Func<IReadOnlyList<string>> listMissions) {
		_listMissions = listMissions;
	}

	/// <summary>The mission being loaded, which the window names in place of its list until the load ends.</summary>
	public string? Loading { get; set; }

	/// <summary>Why the last load failed, shown under the list until another is asked for.</summary>
	public string? Failure { get; set; }

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
		if (ImGui.Begin("Load from VOL", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
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
		var missions = _missions ?? Array.Empty<string>();
		if (missions.Count == 0) {
			ImGui.TextWrapped("The install's archives hold no missions.");
		}

		if (ImGui.BeginListBox("##missions", new Vector2(-1f, ScaledImGui.Scaled(ListHeight)))) {
			foreach (string mission in missions) {
				if (ImGui.Selectable(mission, mission == _highlighted, ImGuiSelectableFlags.AllowDoubleClick)) {
					_highlighted = mission;
					if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
						Ask(mission);
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
