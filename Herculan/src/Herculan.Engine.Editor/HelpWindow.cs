using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The floating "Help" window opened from the menu bar: the editor's manual, docs/herculan/editor-manual.md, embedded in
/// this assembly (see <c>Herculan.Engine.Editor.csproj</c>) and drawn from its Markdown (<see cref="MarkdownDocument"/>),
/// with its sections listed down the left. Choosing a section there, or following a link to one in the text, scrolls the
/// manual to it, and the list marks the section the manual is scrolled to.
/// </summary>
internal sealed class HelpWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float StartWidth = 920f;
	private const float StartHeight = 720f;
	private const float ContentsWidth = 240f;

	// The share of the window the contents may take when the window is narrow.
	private const float ContentsShare = 0.35f;

	private const string ManualResource = "editor-manual.md";

	private static readonly Vector4 LinkColor = new(0.45f, 0.7f, 1f, 1f);
	private static readonly Vector4 CodeColor = new(0.92f, 0.8f, 0.58f, 1f);
	private static readonly Vector4 CodeBackground = new(1f, 1f, 1f, 0.09f);
	private static readonly Vector4 QuoteBar = new(0.45f, 0.7f, 1f, 0.8f);

	private readonly Lazy<MarkdownDocument?> _manual = new(ReadManual);
	private readonly Func<ImFontPtr?> _headingFont;
	private readonly Func<ImFontPtr?> _italicFont;
	private bool _open;

	// The heading to scroll the manual to, by anchor, once it is next drawn.
	private string? _scrollTo;

	// The section the manual is scrolled to, which the contents mark, and the one this frame's drawing has reached so far.
	private string? _current;
	private string? _reached;

	// The link under the mouse last frame, whose every word is underlined, and the one under it this frame.
	private string? _hoveredLink;
	private string? _hovering;

	/// <param name="headingFont">The larger face the top two levels of heading are drawn in; null for the window's own.</param>
	/// <param name="italicFont">The face italic runs are drawn in; null for the window's own.</param>
	public HelpWindow(Func<ImFontPtr?> headingFont, Func<ImFontPtr?> italicFont) {
		_headingFont = headingFont;
		_italicFont = italicFont;
	}

	public void Open() => _open = true;

	/// <summary>Draws the window, if it is open. Call once per frame, inside the ImGui frame.</summary>
	public void Draw() {
		if (!_open) {
			return;
		}

		ImGui.SetNextWindowSize(ScaledImGui.Scaled(new Vector2(StartWidth, StartHeight)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (ImGui.Begin("Help", ref _open, ImGuiWindowFlags.NoCollapse)) {
			if (_manual.Value is { } manual) {
				float contents = MathF.Min(ScaledImGui.Scaled(ContentsWidth), ImGui.GetContentRegionAvail().X * ContentsShare);
				if (ImGui.BeginChild("##contents", new Vector2(contents, 0f), ImGuiChildFlags.Border)) {
					DrawContents(manual);
				}

				ImGui.EndChild();
				ImGui.SameLine();
				if (ImGui.BeginChild("##manual", Vector2.Zero, ImGuiChildFlags.Border)) {
					DrawManual(manual);
				}

				ImGui.EndChild();
			} else {
				EditorText.Note("This build carries no manual.");
			}
		}

		ImGui.End();
	}

	private static MarkdownDocument? ReadManual() {
		using var stream = typeof(HelpWindow).Assembly.GetManifestResourceStream(ManualResource);
		if (stream == null) {
			return null;
		}

		using var reader = new StreamReader(stream);
		return MarkdownDocument.Parse(reader.ReadToEnd());
	}

	// The manual's sections, a level of heading below its title to an indent, the top level in bold.
	private void DrawContents(MarkdownDocument manual) {
		foreach (var heading in manual.Headings.Where(heading => heading.Level >= 2)) {
			float indent = (heading.Level - 2) * ImGui.GetStyle().IndentSpacing;
			if (indent > 0f) {
				ImGui.Indent(indent);
			}

			var bold = heading.Level == 2 ? EditorText.BoldFont?.Invoke() : null;
			if (bold is { } font) {
				ImGui.PushFont(font);
			}

			if (EditorText.WrappedSelectable(heading.Text.Plain, heading.Anchor, heading.Anchor == _current)) {
				GoTo(heading.Anchor);
			}

			if (bold != null) {
				ImGui.PopFont();
			}

			if (indent > 0f) {
				ImGui.Unindent(indent);
			}
		}
	}

	private void DrawManual(MarkdownDocument manual) {
		bool scrolling = _scrollTo != null;
		_reached = null;
		_hovering = null;
		DrawBlocks(manual.Blocks);
		_hoveredLink = _hovering;

		// While a scroll is on its way the old position says nothing. At the bottom the last sections cannot reach the
		// top, so the section last reached, or chosen, stays marked.
		if (!scrolling && _scrollTo == null && ImGui.GetScrollY() < ImGui.GetScrollMaxY() - 1f) {
			_current = _reached;
		}
	}

	// Scrolls the manual to the heading <anchor> names, if one does.
	private void GoTo(string anchor) {
		if (_manual.Value?.Headings.Any(heading => heading.Anchor == anchor) == true) {
			_scrollTo = anchor;
			_current = anchor;
		}
	}

	private void DrawBlocks(IReadOnlyList<MarkdownBlock> blocks) {
		for (int i = 0; i < blocks.Count; i++) {
			var block = blocks[i];

			// A gap between blocks, except between the items of one list.
			if (i > 0 && !(block is MarkdownListItem && blocks[i - 1] is MarkdownListItem)) {
				ImGui.Spacing();
			}

			ImGui.PushID(i);
			switch (block) {
				case MarkdownHeading heading:
					DrawHeading(heading, first: i == 0);
					break;
				case MarkdownParagraph paragraph:
					DrawInline(paragraph.Text);
					break;
				case MarkdownListItem item:
					DrawListItem(item);
					break;
				case MarkdownQuote quote:
					DrawQuote(quote);
					break;
				case MarkdownTable table:
					DrawTable(table);
					break;
				case MarkdownCode code:
					DrawCode(code);
					break;
				case MarkdownRule:
					ImGui.Separator();
					break;
			}

			ImGui.PopID();
		}
	}

	// The top two levels in the larger face, ruled off underneath, and the rest in bold, with more room above the larger.
	private void DrawHeading(MarkdownHeading heading, bool first) {
		float top = ImGui.GetCursorPosY();
		if (top <= ImGui.GetScrollY() + ImGui.GetFontSize()) {
			_reached = heading.Anchor;
		}

		if (heading.Anchor == _scrollTo) {
			ImGui.SetScrollY(top);
			_scrollTo = null;
		}

		bool large = heading.Level <= 2;
		if (large && !first) {
			ImGui.Dummy(new Vector2(0f, ImGui.GetFontSize() * 0.6f));
		}

		if (large) {
			DrawInline(heading.Text, _headingFont());
			ImGui.Separator();
		} else {
			DrawInline(heading.Text, bold: true);
		}
	}

	// A bullet, or the item's number, in the gutter of an indent per level, and the item's text wrapped beside it.
	private void DrawListItem(MarkdownListItem item) {
		float gutter = ImGui.GetFontSize() * 1.4f;
		float indent = gutter * (item.Depth + 1);
		ImGui.Indent(indent);
		var at = ImGui.GetCursorScreenPos();
		float size = ImGui.GetFontSize();
		uint color = ImGui.GetColorU32(ImGuiCol.Text);
		var drawList = ImGui.GetWindowDrawList();
		if (item.Number is { } number) {
			string label = number + ".";
			drawList.AddText(new Vector2(at.X - ImGui.CalcTextSize(label).X - size * 0.35f, at.Y), color, label);
		} else {
			drawList.AddCircleFilled(new Vector2(at.X - gutter * 0.5f, at.Y + size * 0.5f), size * 0.15f, color);
		}

		DrawInline(item.Text);
		ImGui.Unindent(indent);
	}

	// The quote's blocks indented, with a bar down their left.
	private void DrawQuote(MarkdownQuote quote) {
		float indent = ImGui.GetFontSize() * 0.9f;
		var top = ImGui.GetCursorScreenPos();
		ImGui.Indent(indent);
		DrawBlocks(quote.Blocks);
		ImGui.Unindent(indent);
		float bottom = ImGui.GetItemRectMax().Y;
		ImGui.GetWindowDrawList().AddRectFilled(top, new Vector2(top.X + ScaledImGui.Scaled(4f), bottom),
			ImGui.GetColorU32(QuoteBar));
	}

	// The header row in bold on the header color, and each column as wide as its longest cell's share of the whole.
	private void DrawTable(MarkdownTable table) {
		int columns = table.Header.Count;
		if (columns == 0 || !ImGui.BeginTable("##table", columns,
				ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) {
			return;
		}

		for (int column = 0; column < columns; column++) {
			int longest = table.Rows.Select(row => row[column].Plain.Length).Append(table.Header[column].Plain.Length).Max();
			ImGui.TableSetupColumn($"##column{column}", ImGuiTableColumnFlags.WidthStretch, MathF.Max(longest, 1f));
		}

		ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
		ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
		for (int column = 0; column < columns; column++) {
			ImGui.TableSetColumnIndex(column);
			DrawInline(table.Header[column], bold: true);
		}

		foreach (var row in table.Rows) {
			ImGui.TableNextRow();
			for (int column = 0; column < columns; column++) {
				ImGui.TableSetColumnIndex(column);
				DrawInline(row[column]);
			}
		}

		ImGui.EndTable();
	}

	private static void DrawCode(MarkdownCode code) {
		float indent = ImGui.GetFontSize() * 0.9f;
		ImGui.Indent(indent);
		ImGui.PushStyleColor(ImGuiCol.Text, CodeColor);
		ImGui.TextUnformatted(code.Text);
		ImGui.PopStyleColor();
		ImGui.Unindent(indent);
	}

	// Draws <text> wrapped at the right edge of the region it starts in, a word at a time so that each run keeps its own
	// face and color: <font> for plain runs (the window's own for null), the bold face for bold runs, or for all of them
	// when <bold>, and the italic face for italic ones. There is no bold italic face, so a run that is both is bold.
	private void DrawInline(MarkdownInline text, ImFontPtr? font = null, bool bold = false) {
		float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
		bool lineStart = true;
		foreach (var span in text.Spans) {
			var face = span.Bold || bold ? EditorText.BoldFont?.Invoke() ?? font
				: span.Italic ? _italicFont() ?? font
				: font;
			if (face is { } pushed) {
				ImGui.PushFont(pushed);
			}

			foreach (string word in Words(span.Text)) {
				if (!lineStart && ImGui.GetItemRectMax().X + ImGui.CalcTextSize(word.TrimEnd()).X > right) {
					lineStart = true;
				}

				string shown = lineStart ? word.TrimStart() : word;
				if (shown.Length == 0) {
					continue;
				}

				if (!lineStart) {
					ImGui.SameLine(0f, 0f);
				}

				DrawWord(shown, span);
				lineStart = false;
			}

			if (face != null) {
				ImGui.PopFont();
			}
		}
	}

	// <text>'s words, each with the spaces after it; spaces before the first are a word of their own.
	private static IEnumerable<string> Words(string text) {
		int start = 0;
		for (int i = 0; i < text.Length; i++) {
			if (text[i] == ' ' && (i + 1 == text.Length || text[i + 1] != ' ')) {
				yield return text[start..(i + 1)];
				start = i + 1;
			}
		}

		if (start < text.Length) {
			yield return text[start..];
		}
	}

	// One word of <span>: code on a tinted ground, and a link in the link color. A link to a section of the manual is
	// underlined while the mouse is over any word of it, and a click follows it.
	private void DrawWord(string shown, MarkdownSpan span) {
		var at = ImGui.GetCursorScreenPos();
		float lead = ImGui.CalcTextSize(shown[..(shown.Length - shown.TrimStart().Length)]).X;
		float width = ImGui.CalcTextSize(shown).X;
		float height = ImGui.GetFontSize();
		var drawList = ImGui.GetWindowDrawList();
		if (span.Code) {
			float pad = ScaledImGui.Scaled(2f);
			drawList.AddRectFilled(new Vector2(at.X + lead - pad, at.Y), new Vector2(at.X + width + pad, at.Y + height),
				ImGui.GetColorU32(CodeBackground), pad);
		}

		var color = span.Link != null ? LinkColor : span.Code ? CodeColor : (Vector4?)null;
		if (color is { } tint) {
			ImGui.PushStyleColor(ImGuiCol.Text, tint);
		}

		ImGui.TextUnformatted(shown);
		if (color != null) {
			ImGui.PopStyleColor();
		}

		if (span.Link is not { } link) {
			return;
		}

		// A link out of the manual is not followed, only named.
		if (!link.StartsWith('#')) {
			if (ImGui.IsItemHovered()) {
				ScaledImGui.Tooltip(link);
			}

			return;
		}

		if (ImGui.IsItemHovered()) {
			_hovering = link;
			ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
		}

		if (link == _hoveredLink) {
			drawList.AddLine(new Vector2(at.X + lead, at.Y + height), new Vector2(at.X + width, at.Y + height),
				ImGui.GetColorU32(LinkColor));
		}

		if (ImGui.IsItemClicked()) {
			GoTo(link[1..]);
		}
	}
}
