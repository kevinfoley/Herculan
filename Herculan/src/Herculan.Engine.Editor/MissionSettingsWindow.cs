using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The floating "Mission Settings" window, opened from Edit &gt; Mission Settings: the mission's name and zone, the zone with
/// its size and the retail missions played in it (<see cref="ZoneCatalog"/>), then the theater it is played on and the time
/// of day, changed through <see cref="TheaterChanger"/>, then its text (<see cref="MissionTextView"/>), with the briefing,
/// intelligence report and debrief (<see cref="DebriefView"/>) under "Campaign Only".
/// </summary>
internal sealed class MissionSettingsWindow {
	// Where it first opens and how big, in pixels on a 100% display (ScaledImGui.Scaled).
	private const float StartWidth = 460f;
	private const float StartHeight = 640f;

	private readonly Func<string> _name;
	private readonly Func<string> _zone;
	private readonly TheaterChanger _theaters;
	private readonly IReadOnlyList<string> _labels;
	private readonly MissionTextView _text;
	private readonly DebriefView _debrief;
	private bool _open;

	/// <param name="name">What the window title calls the mission, which a Save As changes.</param>
	/// <param name="zone">The zone the mission is played in, as the launch plays it (<see cref="EditorMission.Zone"/>), described by <see cref="ZoneCatalog.Describe"/>.</param>
	/// <param name="labels">Each theater's label, in theater order (<see cref="TheaterNames.Labels"/>).</param>
	public MissionSettingsWindow(Func<string> name, Func<string> zone, TheaterChanger theaters, IReadOnlyList<string> labels,
			MissionTextView text, DebriefView debrief) {
		_name = name;
		_zone = zone;
		_theaters = theaters;
		_labels = labels;
		_text = text;
		_debrief = debrief;
	}

	public void Open() => _open = true;

	/// <summary>Draws the window, if it is open. Call once per frame, inside the ImGui frame.</summary>
	public void Draw() {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(StartWidth), ScaledImGui.Scaled(StartHeight)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (ImGui.Begin("Mission Settings", ref _open, ImGuiWindowFlags.NoCollapse)) {
			DrawContents();
		}

		ImGui.End();
	}

	private void DrawContents() {
		ImGui.TextWrapped($"Mission {_name()}");
		ImGui.Separator();
		EditorText.Wrapped(_zone());

		int theater = _theaters.Theater;
		string? theaterFixed = _theaters.WhyTheaterFixed;
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Theater:");
		ImGui.SameLine();
		ImGui.BeginDisabled(theaterFixed != null);
		ImGui.SetNextItemWidth(-1f);
		if (ImGui.BeginCombo("##theater", Label(theater))) {
			for (int choice = 0; choice < _labels.Count; choice++) {
				if (ImGui.Selectable(_labels[choice], choice == theater) && choice != theater) {
					_theaters.RequestTheater(choice);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(theaterFixed);

		bool night = _theaters.Night;
		string? timeFixed = _theaters.WhyTimeFixed;
		ImGui.BeginDisabled(timeFixed != null);
		if (ImGui.RadioButton("Day", !night) && night) {
			_theaters.RequestNight(false);
		}

		ImGui.SameLine();
		if (ImGui.RadioButton("Night", night) && !night) {
			_theaters.RequestNight(true);
		}

		ImGui.EndDisabled();
		if (timeFixed != null && timeFixed != theaterFixed) {
			EditorText.Reason(timeFixed);
		}

		if (_theaters.Failure is { } failure) {
			EditorText.Note(failure);
		}

		_text.Draw();
		ImGui.SeparatorText("Campaign Only");
		_text.DrawCampaignOnly();
		_debrief.Draw();
	}

	// The combo's label for <theater>: its own, or its number for one past the list.
	private string Label(int theater) => theater >= 0 && theater < _labels.Count ? _labels[theater] : $"{theater}";
}
