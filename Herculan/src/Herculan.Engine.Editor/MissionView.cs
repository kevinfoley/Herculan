using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of the mission as a whole: its name and zone, then the theater it is played on and the time
/// of day, changed through <see cref="TheaterChanger"/>, then the campaign state it is shown in (<see cref="CampaignStateView"/>),
/// then its text (<see cref="MissionTextView"/>).
/// </summary>
internal sealed class MissionView {
	private readonly Func<string> _name;
	private readonly Func<int> _zone;
	private readonly TheaterChanger _theaters;
	private readonly IReadOnlyList<string> _labels;
	private readonly CampaignStateView _state;
	private readonly MissionTextView _text;

	/// <param name="name">What the window title calls the mission, which a Save As changes.</param>
	/// <param name="zone">The zone the mission is played in, as generated.</param>
	/// <param name="labels">Each theater's label, in theater order (<see cref="TheaterNames.Labels"/>).</param>
	public MissionView(Func<string> name, Func<int> zone, TheaterChanger theaters, IReadOnlyList<string> labels,
			CampaignStateView state, MissionTextView text) {
		_name = name;
		_zone = zone;
		_theaters = theaters;
		_labels = labels;
		_state = state;
		_text = text;
	}

	public void Draw() {
		ImGui.TextWrapped($"Mission {_name()}");
		ImGui.Separator();
		ImGui.Text($"Zone: ZONE{_zone()}");

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

		_state.Draw();
		_text.Draw();
	}

	// The combo's label for <theater>: its own, or its number for one past the list.
	private string Label(int theater) => theater >= 0 && theater < _labels.Count ? _labels[theater] : $"{theater}";
}
