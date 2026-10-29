using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// One-line text fields for the mission's text, applied when a field lets go after an edit, as the position fields are.
/// The text being typed is held here until then, one field at a time, keyed by what the field edits.
///
/// <para>Only the characters the game's fonts have glyphs for are kept: every retail <c>.DFN</c> and <c>.HFN</c> starts
/// at code 32, and the smallest holds 217 glyphs, so codes 32 to 248 draw in all of them
/// (docs/retail/formats/dfn-hfn-dci.md#layout). Anything else typed is shown as a problem and left out when the field
/// lets go.</para>
/// </summary>
internal sealed class LineField {
	/// <summary>The first and last character codes every game font draws.</summary>
	public const char FirstDrawn = (char)32;
	public const char LastDrawn = (char)248;

	// What a field takes in bytes of UTF-8: the editor's limit, far past any retail line.
	private const uint MaxBytes = 2048;

	private string? _key;
	private string _draft = string.Empty;

	/// <summary>Whether every character of <paramref name="text"/> is one the game's fonts draw.</summary>
	public static bool Drawn(string text) => text.All(c => c is >= FirstDrawn and <= LastDrawn);

	/// <summary>
	/// Draws a field, the width of the panel, showing <paramref name="text"/>, or what is being typed into it. Answers the text
	/// to apply once the field lets go after an edit that changed it, with the characters no font draws left out; null
	/// otherwise. <paramref name="key"/> names what the field edits, and <paramref name="id"/> is its ImGui id.
	/// </summary>
	public string? Draw(string id, string key, string text) {
		string shown = _key == key ? _draft : text;
		bool wrong = !Drawn(shown);
		if (wrong) {
			EditorText.PushProblemField();
		}

		ImGui.SetNextItemWidth(-1f);
		if (ImGui.InputText(id, ref shown, MaxBytes)) {
			_key = key;
			_draft = shown;
		}

		bool active = ImGui.IsItemActive();
		bool deactivated = ImGui.IsItemDeactivated();
		bool edited = ImGui.IsItemDeactivatedAfterEdit();
		if (wrong) {
			EditorText.PopProblemField();
			string missing = string.Concat(shown.Where(c => c is < FirstDrawn or > LastDrawn).Distinct());
			EditorText.Problem($"The game's fonts have no letter for {missing}: it is dropped when you leave the field.");
		}

		if (_key != key) {
			return null;
		}

		if (!deactivated) {
			// A draft whose field is no longer being typed in, as when the selection changed under it, is dropped.
			if (!active) {
				_key = null;
			}

			return null;
		}

		string typed = new(_draft.Where(c => c is >= FirstDrawn and <= LastDrawn).ToArray());
		_key = null;
		_draft = string.Empty;
		return edited && typed != text ? typed : null;
	}
}
