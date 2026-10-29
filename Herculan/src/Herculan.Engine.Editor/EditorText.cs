using System.Numerics;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>How the editor's panels draw their notes: the explanations and asides beside a field or a list.</summary>
internal static class EditorText {
	// Lighter than ImGui's own disabled grey, which is hard to read at the panels' size.
	private static readonly Vector4 NoteColor = new(0.68f, 0.68f, 0.68f, 1f);

	/// <summary>The bold face to draw <see cref="Emphasis"/> in, read each frame since the fonts are rebuilt with the window's scale.</summary>
	public static Func<ImFontPtr?>? BoldFont { get; set; }

	/// <summary>A note the user must not miss, in the bold face (the ordinary one when there is none), wrapped.</summary>
	public static void Emphasis(string text) {
		var bold = BoldFont?.Invoke();
		if (bold is { } font) {
			ImGui.PushFont(font);
		}

		Wrapped(text);
		if (bold != null) {
			ImGui.PopFont();
		}
	}

	/// <summary>
	/// Plain text, wrapped at the window's edge. ImGui reads <c>TextWrapped</c>'s text as a printf format, where "50% of"
	/// is a conversion, so every <c>%</c> is doubled; a line that holds one goes through here or <c>TextUnformatted</c>.
	/// </summary>
	public static void Wrapped(string text) => ImGui.TextWrapped(text.Replace("%", "%%"));

	/// <summary>
	/// The width a small button labelled <paramref name="label"/> takes after <see cref="ImGui.SameLine()"/>, its spacing
	/// included: what to keep free before it so it stays inside the panel.
	/// </summary>
	public static float ButtonRoom(string label) {
		var style = ImGui.GetStyle();
		return ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f + style.ItemSpacing.X;
	}

	/// <summary>The width <paramref name="text"/> takes drawn after <see cref="ImGui.SameLine(float, float)"/> with no spacing.</summary>
	public static float TextRoom(string text) => ImGui.CalcTextSize(text).X;

	/// <summary>
	/// The room a link keeps free for <paramref name="text"/> drawn after it by <see cref="After"/>: all of it, up to half the
	/// space left on the line, so a long text wraps rather than cutting the link's name to nothing.
	/// </summary>
	public static float AfterRoom(string text) => MathF.Min(TextRoom(text), ImGui.GetContentRegionAvail().X / 2f);

	/// <summary><paramref name="text"/> right after the item just drawn, on its line, wrapped at the window's edge.</summary>
	public static void After(string text) {
		ImGui.SameLine(0f, 0f);
		Wrapped(text);
	}

	/// <summary>
	/// Opens a dropdown whose closed box shows <paramref name="preview"/> cut with "..." to fit, the whole of it on hover when cut;
	/// true while its list is open, then end it with <see cref="ImGui.EndCombo"/>. Every dropdown in the panels opens through here
	/// or <see cref="NameText.BeginCombo"/>: ImGui's own trims a long preview mid-letter at the box's edge.
	/// </summary>
	public static bool BeginCombo(string id, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None) {
		var style = ImGui.GetStyle();
		float arrow = (flags & ImGuiComboFlags.NoArrowButton) != 0 ? 0f : ImGui.GetFrameHeight();
		string shown = NameText.Fit(preview, ImGui.CalcItemWidth() - arrow - style.FramePadding.X * 2f);
		bool open = ImGui.BeginCombo(id, shown, flags);
		if (!open && shown != preview && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(preview.Replace("%", "%%"));
		}

		return open;
	}

	/// <summary>Why a control is disabled, as a note led by <paramref name="lead"/>; nothing for null.</summary>
	public static void Reason(string? reason, string lead = "") {
		if (reason != null) {
			Note(lead + reason);
		}
	}

	/// <summary>A note, wrapped at the window's edge.</summary>
	public static void Note(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, NoteColor);
		Wrapped(text);
		ImGui.PopStyleColor();
	}

	/// <summary>
	/// An explanation of the last item, shown while the mouse is over it, enabled or not, wrapped at a readable width;
	/// nothing for null.
	/// </summary>
	public static void Tooltip(string? text) {
		if (text == null || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			return;
		}

		ImGui.BeginTooltip();
		ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}

	/// <summary>Why an input is refused, wrapped, in a strong red, under the field <see cref="PushProblemField"/> tinted.</summary>
	public static void Problem(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, ProblemColor);
		Wrapped(text);
		ImGui.PopStyleColor();
	}

	/// <summary>Tints the next field's background as the one to change; <see cref="PopProblemField"/> after it.</summary>
	public static void PushProblemField() {
		ImGui.PushStyleColor(ImGuiCol.FrameBg, ProblemFieldColor);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ProblemFieldHoveredColor);
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ProblemFieldHoveredColor);
	}

	public static void PopProblemField() => ImGui.PopStyleColor(3);

	/// <summary>
	/// A <see cref="ImGui.Selectable(string, bool)"/> whose label wraps at the edge of the window or list it is in: a
	/// label-less selectable as tall as the wrapped text, with the text drawn over it.
	/// </summary>
	public static bool WrappedSelectable(string text, string id, bool selected, ImGuiSelectableFlags flags = ImGuiSelectableFlags.None) {
		float width = ImGui.GetContentRegionAvail().X;
		var start = ImGui.GetCursorScreenPos();
		float height = ImGui.CalcTextSize(text, false, width).Y;
		bool clicked = ImGui.Selectable($"##{id}", selected, flags, new Vector2(0f, height));
		var next = ImGui.GetCursorScreenPos();
		ImGui.SetCursorScreenPos(start);
		ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.SetCursorScreenPos(next);
		return clicked;
	}

	private static readonly Vector4 ProblemColor = new(1f, 0.27f, 0.2f, 1f);
	private static readonly Vector4 ProblemFieldColor = new(0.36f, 0.2f, 0.2f, 1f);
	private static readonly Vector4 ProblemFieldHoveredColor = new(0.44f, 0.25f, 0.25f, 1f);
}
