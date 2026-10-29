using System.Numerics;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// How the panels draw a <see cref="RecordName"/>: on one line, its text cut with "..." to fit and its number dimmed after it,
/// the whole name on hover when it is cut; never wrapped across lines in a list or picker.
/// </summary>
internal static class NameText {
	/// <summary><paramref name="text"/> cut, with "...", to fit <paramref name="width"/> pixels in the current font.</summary>
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

	/// <summary><paramref name="name"/>'s text cut to leave room for its number in <paramref name="width"/> pixels.</summary>
	public static string FitText(RecordName name, float width) {
		if (name.Number is not { } number) {
			return Fit(name.Text, width);
		}

		float numberWidth = ImGui.CalcTextSize(number).X + ImGui.GetStyle().ItemInnerSpacing.X;
		return Fit(name.Text, MathF.Max(width - numberWidth, ImGui.CalcTextSize("M...").X));
	}

	/// <summary><paramref name="name"/> as one string cut to fit <paramref name="width"/> pixels, its number kept whole.</summary>
	public static string Fit(RecordName name, float width) {
		string text = FitText(name, width);
		return name.Number is { } number ? $"{text} {number}" : text;
	}

	/// <summary>
	/// <paramref name="name"/> in the panel's text color with its number dimmed, cut to the space left on the line less
	/// <paramref name="reserve"/> pixels for what follows it there.
	/// </summary>
	public static void Draw(RecordName name, float reserve = 0f) {
		string text = FitText(name, ImGui.GetContentRegionAvail().X - reserve);
		ImGui.TextUnformatted(text);
		Hover(name, text);
		Number(name);
	}

	/// <summary>
	/// A row of a list or a picker that selects when clicked: <paramref name="name"/> cut to <paramref name="width"/> pixels
	/// (the space left on the line when null), its number dimmed, the whole name on hover when cut. <paramref name="id"/> is its
	/// ImGui ID, unique among its siblings.
	/// </summary>
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
	/// Opens a picker whose closed box shows <paramref name="name"/> cut to fit, the whole name on hover when cut; true while its
	/// list is open, then end it with <see cref="ImGui.EndCombo"/>.
	/// </summary>
	public static bool BeginCombo(string id, RecordName name) {
		var style = ImGui.GetStyle();
		float item = ImGui.CalcItemWidth();
		string preview = Fit(name, item - ImGui.GetFrameHeight() - style.FramePadding.X * 2f);

		// The list opens at least as wide as the Properties panel, so a name in it is cut as little as its preview is, and as
		// tall as twelve rows, as an ImGui combo's own constraint would make it.
		float wide = MathF.Max(item, Platform.ScaledImGui.Scaled(PropertiesPanel.PanelWidth - 20f));
		float tall = ImGui.GetTextLineHeightWithSpacing() * 12f + style.WindowPadding.Y * 2f;
		ImGui.SetNextWindowSizeConstraints(new Vector2(wide, 0f), new Vector2(float.MaxValue, tall));
		bool open = ImGui.BeginCombo(id, preview);
		if (!open && preview != name.Full && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(name.Full);
		}

		return open;
	}

	/// <summary>The whole of <paramref name="name"/> on hover of the item just drawn, when <paramref name="shown"/> is cut.</summary>
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
