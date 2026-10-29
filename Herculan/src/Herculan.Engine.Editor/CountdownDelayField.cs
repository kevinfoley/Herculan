using HercWorks.Core.Data.File.Msn;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A countdown's delay in seconds as a typed field, applied when it lets go and kept to 0 to <see cref="TimerEditing.MaxDelay"/>,
/// the countdown view's and each "Starts" entry of a trigger's Then list. One draft is held, for the countdown being typed.
/// </summary>
internal sealed class CountdownDelayField {
	// A delay being typed, and the countdown it is for, until its field lets go.
	private short? _draftFor;
	private int _draft;

	/// <summary>
	/// Draws the field for row-11 GUID <paramref name="countdown"/>, whose delay is <paramref name="seconds"/>, as
	/// <paramref name="id"/> in the current item width; answers the typed delay when the field lets go after a change to it.
	/// </summary>
	public int? Draw(string id, short countdown, short seconds) {
		if (_draftFor is { } draftFor && draftFor != countdown && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == countdown ? _draft : seconds;
		if (ImGui.InputInt(id, ref shown, 0, 0)) {
			_draftFor = countdown;
			_draft = shown;
		}

		if (!ImGui.IsItemDeactivatedAfterEdit() || _draftFor != countdown) {
			return null;
		}

		_draftFor = null;
		int typed = Math.Clamp(_draft, 0, TimerEditing.MaxDelay);
		return typed != seconds ? typed : null;
	}

	/// <summary>The note under the field: the delay in hours, minutes and seconds, and the field's bounds.</summary>
	public static string Note(short seconds) =>
		$"{MissionText.Duration(seconds)}. At most {TimerEditing.MaxDelay} s (about 9 hours); 0 fires at once.";
}
