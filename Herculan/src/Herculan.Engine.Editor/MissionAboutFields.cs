using System.Globalization;
using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The fields of a mission's about file (<see cref="MissionAbout"/>): author, version, date, with a button that writes
/// today's, and description, which wraps at the field's width (<see cref="WrappedTextInput"/>). A field's edit is answered when the field lets go, as the editor's other text fields are; what
/// is being typed is held here until then, one field at a time. The game's Custom Missions window draws the about file with
/// ImGui, never in the game's fonts, so a field takes any character.
/// </summary>
internal sealed class MissionAboutFields {
	// What a one-line field takes, in bytes of UTF-8.
	private const uint LineBytes = 256;

	// The description's height, in lines of text.
	private const float DescriptionLines = 6f;

	private const string TodayLabel = "Today";

	private readonly WrappedTextInput _description = new();
	private string? _field;
	private string _draft = string.Empty;

	/// <summary>Today's date as the Today button writes it.</summary>
	public static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	/// <summary>
	/// Draws the fields showing <paramref name="about"/>. Answers <paramref name="about"/> with the edit made, once a field lets
	/// go after one or the Today button is clicked; null otherwise. An emptied field says nothing.
	/// </summary>
	public MissionAbout? Draw(MissionAbout about) {
		var style = ImGui.GetStyle();
		float fieldStart = new[] { "Author:", "Version:", "Date:" }.Max(label => ImGui.CalcTextSize(label).X) + style.ItemSpacing.X;
		MissionAbout? edited = null;

		if (Line("Author:", "author", about.Author, fieldStart, 0f, ImGuiInputTextFlags.None, out string? author)) {
			edited = about with { Author = author };
		}

		EditorText.Tooltip("Who made the mission.");

		string? shownVersion = about.Version?.ToString(CultureInfo.InvariantCulture);
		if (Line("Version:", "version", shownVersion, fieldStart, 0f, ImGuiInputTextFlags.CharsDecimal, out string? version)) {
			edited = about with {
				Version = int.TryParse(version, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ? number : null,
			};
		}

		EditorText.Tooltip("For keeping track of newer versions of the mission.");

		if (Line("Date:", "date", about.Date, fieldStart, EditorText.ButtonRoom(TodayLabel), ImGuiInputTextFlags.None, out string? date)) {
			edited = about with { Date = date };
		}

		EditorText.Tooltip("When the mission was made or last changed, written as you like.");
		ImGui.SameLine();
		if (ImGui.Button(TodayLabel)) {
			edited = about with { Date = Today };
		}

		ImGui.Text("Description:");
		if (Description(about.Description, out string? description)) {
			edited = about with { Description = description };
		}

		return edited;
	}

	// A labelled one-line field, the width of the panel less <reserve>; true with the text to apply, null for none, once it
	// lets go after an edit that changed it.
	private bool Line(string label, string field, string? text, float fieldStart, float reserve, ImGuiInputTextFlags flags,
			out string? value) {
		ImGui.AlignTextToFramePadding();
		ImGui.Text(label);
		ImGui.SameLine(fieldStart);
		ImGui.SetNextItemWidth(-reserve - 1f);
		string shown = _field == field ? _draft : text ?? string.Empty;
		if (ImGui.InputText("##" + field, ref shown, LineBytes, flags)) {
			_field = field;
			_draft = shown;
		}

		return Let(field, text, out value);
	}

	// The description field, wrapped at its width.
	private bool Description(string? text, out string? value) {
		string shown = _field == "description" ? _draft : text ?? string.Empty;
		var size = new Vector2(-1f, ImGui.GetTextLineHeight() * DescriptionLines + ImGui.GetStyle().FramePadding.Y * 2f);
		if (_description.Draw("##description", ref shown, MissionAbout.MaxSize, size)) {
			_field = "description";
			_draft = shown;
		}

		return Let("description", text, out value);
	}

	// Whether <field>, the item just drawn, let go after an edit that changed <text>, and the edit as <value>.
	private bool Let(string field, string? text, out string? value) {
		value = null;
		bool active = ImGui.IsItemActive();
		bool deactivated = ImGui.IsItemDeactivated();
		bool edited = ImGui.IsItemDeactivatedAfterEdit();
		if (_field != field) {
			return false;
		}

		if (!deactivated) {
			// A draft whose field is no longer being typed in, as when another mission opened under it, is dropped.
			if (!active) {
				_field = null;
			}

			return false;
		}

		string typed = _draft.Trim();
		_field = null;
		_draft = string.Empty;
		value = typed.Length > 0 ? typed : null;
		return edited && value != (string.IsNullOrEmpty(text) ? null : text);
	}
}
