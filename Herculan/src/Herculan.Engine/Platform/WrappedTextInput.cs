using System.Numerics;
using System.Text;
using ImGuiNET;

namespace Herculan.Engine.Platform;

/// <summary>
/// A multi-line ImGui text field whose text wraps at the field's width, which the <c>InputTextMultiline</c> of the ImGui
/// that ImGui.NET 1.90 wraps does not do. The field shows the text with a line break put in wherever a line wraps, a soft
/// break, and takes each edit out of its buffer as a change to the text without them, which it then wraps again and puts
/// back, the cursor kept at the same place in the text. A line break typed or pasted is the text's own. Backspace over a
/// soft break takes out the character before it, and Delete the one after it, as across a wrapped line in any editor.
///
/// <para>One instance draws one field.</para>
/// </summary>
public sealed class WrappedTextInput {
	private readonly ImGuiInputTextCallback _callback;

	// The text, what the field shows of it, the indices of the soft breaks in that, and the width it was wrapped to.
	private string _text = string.Empty;
	private string _shown = string.Empty;
	private List<int> _soft = [];
	private float _width = -1f;

	// The width the field has this frame, which a callback wraps to when it differs; the most bytes the text takes; and
	// whether the field was being typed in at the last frame, while which ImGui keeps its own copy of what it shows.
	private float _wantWidth;
	private int _maxBytes;
	private bool _editing;

	public unsafe WrappedTextInput() {
		_callback = Callback;
	}

	/// <summary>
	/// Draws the field, <paramref name="size"/> as <c>InputTextMultiline</c> takes it, showing <paramref name="text"/>, or what
	/// is being typed into it. Answers whether an edit changed the text this frame, and the text then, without soft breaks,
	/// in <paramref name="text"/>. The text takes at most <paramref name="maxBytes"/> bytes of UTF-8; an edit past that is
	/// undone.
	/// </summary>
	public bool Draw(string id, ref string text, int maxBytes, Vector2 size) {
		var style = ImGui.GetStyle();
		float itemWidth = size.X > 0f ? size.X : ImGui.GetContentRegionAvail().X + size.X;
		_wantWidth = itemWidth - style.FramePadding.X * 2f - style.ScrollbarSize;
		_maxBytes = maxBytes;
		if (!_editing && (text != _text || _wantWidth != _width)) {
			Rewrap(text);
		}

		string shown = _shown;
		bool changed = ImGui.InputTextMultiline(id, ref shown, (uint)maxBytes * 2 + 1, size,
			ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways, _callback);
		_editing = ImGui.IsItemActive();
		if (!changed || _text == text) {
			return false;
		}

		text = _text;
		return true;
	}

	/// <summary>
	/// <paramref name="text"/> wrapped at <paramref name="width"/>, as <paramref name="measure"/> sizes a line: what a field
	/// shows of it, and the indices there of the soft breaks put in. A line breaks after the spaces that follow a word, and a
	/// word wider than the line breaks where it reaches the edge.
	/// </summary>
	public static (string Shown, List<int> Soft) Wrap(string text, float width, Func<string, float> measure) {
		var shown = new StringBuilder(text.Length + 16);
		var soft = new List<int>();
		if (width <= 0f) {
			return (text, soft);
		}

		var line = new StringBuilder();
		int i = 0;
		while (i < text.Length) {
			if (text[i] == '\n') {
				shown.Append(line).Append('\n');
				line.Clear();
				i++;
				continue;
			}

			int wordEnd = i;
			while (wordEnd < text.Length && text[wordEnd] is not (' ' or '\n')) {
				wordEnd++;
			}

			int spacesEnd = wordEnd;
			while (spacesEnd < text.Length && text[spacesEnd] == ' ') {
				spacesEnd++;
			}

			string word = text[i..wordEnd];
			if (line.Length > 0 && measure(line + word) > width) {
				Break();
			}

			if (line.Length == 0 && measure(word) > width) {
				// Split where the edge falls, never inside a surrogate pair.
				for (int c = 0; c < word.Length; c++) {
					int take = char.IsHighSurrogate(word[c]) && c + 1 < word.Length ? 2 : 1;
					string piece = word.Substring(c, take);
					if (line.Length > 0 && measure(line + piece) > width) {
						Break();
					}

					line.Append(piece);
					c += take - 1;
				}
			} else {
				line.Append(word);
			}

			line.Append(text, wordEnd, spacesEnd - wordEnd);
			i = spacesEnd;
		}

		shown.Append(line);
		return (shown.ToString(), soft);

		void Break() {
			shown.Append(line);
			soft.Add(shown.Length);
			shown.Append('\n');
			line.Clear();
		}
	}

	private void Rewrap(string text) {
		_text = text;
		_width = _wantWidth;
		(_shown, _soft) = Wrap(text, _width, piece => ImGui.CalcTextSize(piece).X);
	}

	private unsafe int Callback(ImGuiInputTextCallbackData* raw) {
		var data = new ImGuiInputTextCallbackDataPtr(raw);
		if (data.EventFlag == ImGuiInputTextFlags.CallbackEdit) {
			Edit(data);
		} else if (data.EventFlag == ImGuiInputTextFlags.CallbackAlways && _wantWidth != _width) {
			// The field's width changed while it is typed in.
			int at = Stored(_soft, CharIndex(data, data.CursorPos));
			Rewrap(_text);
			Put(data, Shown(_soft, at));
		}

		return 0;
	}

	// Takes the edit the field's buffer holds as an edit of the text, from what the buffer held before it.
	private unsafe void Edit(ImGuiInputTextCallbackDataPtr data) {
		string now = Encoding.UTF8.GetString((byte*)data.Buf, data.BufTextLen);
		int cursor = CharIndex(data, data.CursorPos);
		string before = _shown;

		// The edit is what lies between what the two have in common at either end.
		int limit = Math.Min(before.Length, now.Length);
		int head = 0;
		while (head < limit && before[head] == now[head]) {
			head++;
		}

		int tail = 0;
		while (tail < limit - head && before[^(tail + 1)] == now[^(tail + 1)]) {
			tail++;
		}

		int from = Stored(_soft, head);
		int to = Stored(_soft, before.Length - tail);
		string inserted = now[head..(now.Length - tail)];
		if (inserted.Length == 0 && from == to && before.Length - tail > head) {
			// Only soft breaks went: the character across from the one taken out goes instead.
			if (ImGui.IsKeyDown(ImGuiKey.Delete)) {
				to = Math.Min(_text.Length, to + 1);
			} else {
				from = Math.Max(0, from - 1);
			}
		}

		string text = _text[..from] + inserted + _text[to..];
		if (Encoding.UTF8.GetByteCount(text) > _maxBytes) {
			Put(data, before, head);
			return;
		}

		// Where the cursor is in the text: before the edit, inside what it put in, or after it.
		int at = cursor <= head ? Stored(_soft, cursor)
			: cursor >= now.Length - tail ? Stored(_soft, cursor - now.Length + before.Length) - (to - from) + inserted.Length
			: from + (cursor - head);
		if (inserted.Length == 0 && from != to && cursor == head) {
			at = from;
		}

		_text = text;
		(_shown, _soft) = Wrap(text, _width, piece => ImGui.CalcTextSize(piece).X);
		Put(data, Shown(_soft, Math.Clamp(at, 0, text.Length)));
	}

	// Puts what the field shows into its buffer with the cursor at <cursor>, a character index in it.
	private void Put(ImGuiInputTextCallbackDataPtr data, int cursor) => Put(data, _shown, cursor);

	private static void Put(ImGuiInputTextCallbackDataPtr data, string shown, int cursor) {
		if (Encoding.UTF8.GetByteCount(shown) >= data.BufSize) {
			return;
		}

		data.DeleteChars(0, data.BufTextLen);
		data.InsertChars(0, shown);
		data.CursorPos = Encoding.UTF8.GetByteCount(shown.AsSpan(0, Math.Clamp(cursor, 0, shown.Length)));
		data.SelectionStart = data.CursorPos;
		data.SelectionEnd = data.CursorPos;
	}

	// The character index of <bytes> bytes into the buffer.
	private static unsafe int CharIndex(ImGuiInputTextCallbackDataPtr data, int bytes) =>
		Encoding.UTF8.GetCharCount((byte*)data.Buf, Math.Clamp(bytes, 0, data.BufTextLen));

	// The index in the text of index <shown> in what the field shows, past the soft breaks before it.
	private static int Stored(List<int> soft, int shown) => shown - soft.Count(index => index < shown);

	// The index in what the field shows of index <stored> in the text, after a soft break that falls at it.
	private static int Shown(List<int> soft, int stored) {
		int shown = stored;
		for (int k = 0; k < soft.Count; k++) {
			if (soft[k] - k <= stored) {
				shown++;
			}
		}

		return shown;
	}
}
