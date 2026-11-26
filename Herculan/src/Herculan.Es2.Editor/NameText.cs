using System.Numerics;
using ImGuiNET;

namespace Herculan.Es2.Editor;

/// <summary>
/// How the panels draw a <see cref="RecordName"/>: on one line, its text cut with "..." to fit and its number dimmed after it,
/// the whole name on hover when it is cut; never wrapped across lines in a list or picker.
/// </summary>
internal static class NameText {
	/// <summary>A text cut, with "...", to fit a width in the current font.</summary>
	/// <param name="text">The text.</param>
	/// <param name="width">The width in pixels.</param>
	public static string Fit(string text, float width) {
		const string more = "...";
		if (ImGui.CalcTextSize(text).X <= width) {
			return text;
		}

		int length = text.Length;
		while (length > 0 && ImGui.CalcTextSize(text[..length] + more).X > width) {
			length--;
		}

		return text[..length].TrimEnd() + more;
	}

	/// <summary>A name's text cut to leave room for its number in a width.</summary>
	/// <param name="name">The name.</param>
	/// <param name="width">The width in pixels.</param>
	public static string FitText(RecordName name, float width) {
		if (name.Number is not { } number) {
			return Fit(name.Text, width);
		}

		float numberWidth = ImGui.CalcTextSize(number).X + ImGui.GetStyle().ItemInnerSpacing.X;
		return Fit(name.Text, MathF.Max(width - numberWidth, ImGui.CalcTextSize("M...").X));
	}

	/// <summary>A name as one string cut to fit a width, its number kept whole.</summary>
	/// <param name="name">The name.</param>
	/// <param name="width">The width in pixels.</param>
	public static string Fit(RecordName name, float width) {
		string text = FitText(name, width);
		return name.Number is { } number ? $"{text} {number}" : text;
	}

	/// <summary>
	/// A name in the panel's text color with its number dimmed, cut to the space left on the line less
	/// the room reserved for what follows it there.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <param name="reserve">The pixels reserved for what follows it.</param>
	public static void Draw(RecordName name, float reserve = 0f) {
		string text = FitText(name, ImGui.GetContentRegionAvail().X - reserve);
		ImGui.TextUnformatted(text);
		Hover(name, text);
		Number(name);
	}

	/// <summary>
	/// A row of a list or a picker that selects when clicked: a name cut to a width
	/// (the space left on the line when null), its number dimmed, the whole name on hover when cut.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <param name="id">The row's ImGui ID, unique among its siblings.</param>
	/// <param name="selected">Whether the row is selected.</param>
	/// <param name="width">The width in pixels, or null for the space left on the line.</param>
	/// <param name="flags">The selectable flags.</param>
	public static bool Selectable(RecordName name, string id, bool selected, float? width = null,
			ImGuiSelectableFlags flags = ImGuiSelectableFlags.None) {
		float room = width ?? ImGui.GetContentRegionAvail().X;
		string text = FitText(name, room);
		var start = ImGui.GetCursorScreenPos();
		bool clicked = ImGui.Selectable($"##{id}", selected, flags, new Vector2(room, 0f));
		bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
		var next = ImGui.GetCursorScreenPos();
		ImGui.SetCursorScreenPos(start);
		ImGui.BeginDisabled((flags & ImGuiSelectableFlags.Disabled) != 0);
		ImGui.TextUnformatted(text);
		ImGui.EndDisabled();
		Number(name);
		ImGui.SetCursorScreenPos(next);
		if (hovered && text != name.Text) {
			ImGui.SetTooltip(name.Full);
		}

		return clicked;
	}

	/// <summary>
	/// Opens a picker whose closed box shows a name cut to fit, the whole name on hover when cut; true while its
	/// list is open, then end it with <see cref="ImGui.EndCombo"/>.
	/// </summary>
	/// <param name="id">The picker's ImGui id.</param>
	/// <param name="name">The name shown.</param>
	public static bool BeginCombo(string id, RecordName name) {
		var style = ImGui.GetStyle();
		float item = ImGui.CalcItemWidth();
		string preview = Fit(name, item - ImGui.GetFrameHeight() - style.FramePadding.X * 2f);

		// The list opens at least as wide as the Properties panel, so a name in it is cut as little as its preview is, and as
		// tall as twelve rows, as an ImGui combo's own constraint would make it.
		float wide = MathF.Max(item, Engine.Platform.ScaledImGui.Scaled(PropertiesPanel.PanelWidth - 20f));
		float tall = ImGui.GetTextLineHeightWithSpacing() * 12f + style.WindowPadding.Y * 2f;
		ImGui.SetNextWindowSizeConstraints(new Vector2(wide, 0f), new Vector2(float.MaxValue, tall));
		bool open = ImGui.BeginCombo(id, preview);
		if (!open && preview != name.Full && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(name.Full);
		}

		return open;
	}

	/// <summary>The whole of a name on hover of the item just drawn, when the text shown is cut.</summary>
	/// <param name="name">The name.</param>
	/// <param name="shown">The text shown.</param>
	public static void Hover(RecordName name, string shown) {
		if (shown != name.Text && ImGui.IsItemHovered()) {
			ImGui.SetTooltip(name.Full);
		}
	}

	// The number after the item just drawn, dimmed, on the same line.
	private static void Number(RecordName name) {
		if (name.Number is { } number) {
			ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
			ImGui.TextDisabled(number);
		}
	}
}
