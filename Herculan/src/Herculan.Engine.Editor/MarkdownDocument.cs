using System.Text;
using System.Text.RegularExpressions;

namespace Herculan.Engine.Editor;

/// <summary>A run of inline text in one style: bold, code, or part of a link to <see cref="Link"/>.</summary>
internal sealed record MarkdownSpan(string Text, bool Bold = false, bool Code = false, string? Link = null);

/// <summary>A line or paragraph of inline text, as its styled runs.</summary>
internal sealed record MarkdownInline(IReadOnlyList<MarkdownSpan> Spans) {
	/// <summary>The text without its styling.</summary>
	public string Plain => string.Concat(Spans.Select(span => span.Text));
}

internal abstract record MarkdownBlock;

/// <summary>A heading of <see cref="Level"/> 1 (<c>#</c>) to 6, and the anchor a <c>#fragment</c> link names it by.</summary>
internal sealed record MarkdownHeading(int Level, MarkdownInline Text, string Anchor) : MarkdownBlock;

internal sealed record MarkdownParagraph(MarkdownInline Text) : MarkdownBlock;

/// <summary>One item of a list, <see cref="Depth"/> levels in; <see cref="Number"/> is its number in an ordered list, null for a bullet.</summary>
internal sealed record MarkdownListItem(int Depth, string? Number, MarkdownInline Text) : MarkdownBlock;

/// <summary>A <c>&gt;</c> block quote, which holds blocks of its own.</summary>
internal sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>A table: its header cells, and each row's cells, as many as the header has.</summary>
internal sealed record MarkdownTable(IReadOnlyList<MarkdownInline> Header, IReadOnlyList<IReadOnlyList<MarkdownInline>> Rows) : MarkdownBlock;

/// <summary>A fenced code block, its lines as written.</summary>
internal sealed record MarkdownCode(string Text) : MarkdownBlock;

internal sealed record MarkdownRule : MarkdownBlock;

/// <summary>
/// A Markdown document read into blocks for <see cref="HelpWindow"/> to draw. It reads the subset the editor's manual is
/// written in: ATX headings, paragraphs, bullet and numbered lists, block quotes, pipe tables, fenced code and rules, with
/// <c>**bold**</c>, <c>`code`</c> and <c>[links](target)</c> inside them. Anything else stays as its text. HTML comments
/// (the docs' lint markers) are dropped. Anchors are GitHub's, so the manual's <c>#fragment</c> links work here as on
/// GitHub.
/// </summary>
internal sealed class MarkdownDocument {
	private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline);
	private static readonly Regex OrderedMarker = new(@"^(\d{1,9})[.)] ");

	public IReadOnlyList<MarkdownBlock> Blocks { get; }

	/// <summary>Every top-level heading, in order.</summary>
	public IReadOnlyList<MarkdownHeading> Headings { get; }

	private MarkdownDocument(IReadOnlyList<MarkdownBlock> blocks) {
		Blocks = blocks;
		Headings = blocks.OfType<MarkdownHeading>().ToList();
	}

	public static MarkdownDocument Parse(string text) {
		string[] lines = Comment.Replace(text, "").ReplaceLineEndings("\n").Split('\n');
		return new MarkdownDocument(ParseBlocks(lines, new Dictionary<string, int>()));
	}

	// <anchors> counts each anchor given so far, which a later heading of the same name takes with "-1", "-2" and so on after it.
	private static List<MarkdownBlock> ParseBlocks(IReadOnlyList<string> lines, Dictionary<string, int> anchors) {
		var blocks = new List<MarkdownBlock>();
		var paragraph = new List<string>();

		for (int i = 0; i < lines.Count; i++) {
			string line = lines[i];
			string trimmed = line.Trim();
			if (trimmed.Length == 0) {
				EndParagraph();
				continue;
			}

			if (!StartsBlock(line) && ListItem(line) == null) {
				paragraph.Add(trimmed);
				continue;
			}

			EndParagraph();
			if (trimmed.StartsWith("```")) {
				var code = new List<string>();
				for (i++; i < lines.Count && !lines[i].Trim().StartsWith("```"); i++) {
					code.Add(lines[i]);
				}

				blocks.Add(new MarkdownCode(string.Join('\n', code)));
			} else if (HeadingLevel(trimmed) is int level and > 0) {
				var heading = Inline(trimmed[level..].Trim().TrimEnd('#').TrimEnd());
				blocks.Add(new MarkdownHeading(level, heading, Anchor(heading.Plain, anchors)));
			} else if (IsRule(trimmed)) {
				blocks.Add(new MarkdownRule());
			} else if (trimmed.StartsWith('>')) {
				var quoted = new List<string>();
				for (; i < lines.Count && lines[i].TrimStart().StartsWith('>'); i++) {
					string inner = lines[i].TrimStart()[1..];
					quoted.Add(inner.StartsWith(' ') ? inner[1..] : inner);
				}

				i--;
				blocks.Add(new MarkdownQuote(ParseBlocks(quoted, anchors)));
			} else if (trimmed.StartsWith('|')) {
				var rows = new List<string>();
				for (; i < lines.Count && lines[i].TrimStart().StartsWith('|'); i++) {
					rows.Add(lines[i].Trim());
				}

				i--;
				blocks.Add(Table(rows));
			} else if (ListItem(line) is var (depth, number, first)) {
				// The item runs on over every following line that would otherwise be a paragraph's.
				var text = new List<string> { first };
				for (; i + 1 < lines.Count && lines[i + 1].Trim().Length > 0 && !StartsBlock(lines[i + 1])
						&& ListItem(lines[i + 1]) == null; i++) {
					text.Add(lines[i + 1].Trim());
				}

				blocks.Add(new MarkdownListItem(depth, number, Inline(string.Join(' ', text))));
			}
		}

		EndParagraph();
		return blocks;

		void EndParagraph() {
			if (paragraph.Count > 0) {
				blocks.Add(new MarkdownParagraph(Inline(string.Join(' ', paragraph))));
				paragraph.Clear();
			}
		}
	}

	// Whether <line> starts a block other than a list item or a paragraph.
	private static bool StartsBlock(string line) {
		string trimmed = line.Trim();
		return trimmed.StartsWith("```") || HeadingLevel(trimmed) > 0 || IsRule(trimmed) || trimmed.StartsWith('>')
			|| trimmed.StartsWith('|');
	}

	// The level of the heading <trimmed> is, or 0 when it is not one: one to six '#' and then a space or the line's end.
	private static int HeadingLevel(string trimmed) {
		int level = 0;
		while (level < trimmed.Length && trimmed[level] == '#') {
			level++;
		}

		return level is >= 1 and <= 6 && (level == trimmed.Length || trimmed[level] == ' ') ? level : 0;
	}

	private static bool IsRule(string trimmed) =>
		trimmed.Length >= 3 && (trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_'));

	// The list item <line> starts: how deep it is, two spaces of indent to a level, its number in an ordered list, and its
	// text; null when it starts none.
	private static (int Depth, string? Number, string Text)? ListItem(string line) {
		int indent = 0;
		int at = 0;
		for (; at < line.Length && line[at] is ' ' or '\t'; at++) {
			indent += line[at] == '\t' ? 4 : 1;
		}

		string rest = line[at..];
		if (rest.Length >= 2 && rest[0] is '-' or '*' or '+' && rest[1] == ' ') {
			return (indent / 2, null, rest[2..].Trim());
		}

		var ordered = OrderedMarker.Match(rest);
		return ordered.Success ? (indent / 2, ordered.Groups[1].Value, rest[ordered.Length..].Trim()) : null;
	}

	// A pipe table from its lines; the line of dashes under the header, when there is one, is skipped.
	private static MarkdownTable Table(List<string> lines) {
		var header = Cells(lines[0]);
		int body = lines.Count > 1 && lines[1].All(c => c is '|' or '-' or ':' or ' ') ? 2 : 1;
		var rows = lines.Skip(body).Select(line => {
			var cells = Cells(line);
			return (IReadOnlyList<MarkdownInline>)Enumerable.Range(0, header.Count)
				.Select(column => column < cells.Count ? cells[column] : new MarkdownInline([])).ToList();
		}).ToList();
		return new MarkdownTable(header, rows);
	}

	// The cells of one table line, split at each '|' outside code and not escaped.
	private static List<MarkdownInline> Cells(string line) {
		var cells = new List<string>();
		var cell = new StringBuilder();
		bool code = false;
		for (int i = 0; i < line.Length; i++) {
			char c = line[i];
			if (c == '\\' && i + 1 < line.Length && line[i + 1] == '|') {
				cell.Append('|');
				i++;
			} else if (c == '|' && !code) {
				cells.Add(cell.ToString());
				cell.Clear();
			} else {
				code ^= c == '`';
				cell.Append(c);
			}
		}

		cells.Add(cell.ToString());

		// The empty cells before a leading '|' and after a trailing one.
		if (line.StartsWith('|')) {
			cells.RemoveAt(0);
		}

		if (line.EndsWith('|') && !line.EndsWith("\\|") && cells.Count > 0) {
			cells.RemoveAt(cells.Count - 1);
		}

		return cells.Select(text => Inline(text.Trim())).ToList();
	}

	// <text>'s styled runs: "**" turns bold on and off, `code` is a run of its own, and [label](target) a run per styled
	// part of its label; a backslash takes the punctuation after it as it is.
	private static MarkdownInline Inline(string text) {
		var spans = new List<MarkdownSpan>();
		var run = new StringBuilder();
		bool bold = false;

		for (int i = 0; i < text.Length; i++) {
			char c = text[i];
			if (c == '\\' && i + 1 < text.Length && (char.IsPunctuation(text[i + 1]) || char.IsSymbol(text[i + 1]))) {
				run.Append(text[++i]);
				continue;
			}

			if (c == '*' && i + 1 < text.Length && text[i + 1] == '*') {
				Flush();
				bold = !bold;
				i++;
				continue;
			}

			if (c == '`' && text.IndexOf('`', i + 1) is int codeEnd and > 0) {
				Flush();
				spans.Add(new MarkdownSpan(text[(i + 1)..codeEnd], bold, Code: true));
				i = codeEnd;
				continue;
			}

			if (c == '[' && text.IndexOf(']', i + 1) is int close and > 0 && close + 1 < text.Length && text[close + 1] == '('
					&& text.IndexOf(')', close + 2) is int linkEnd and > 0) {
				Flush();
				string target = text[(close + 2)..linkEnd].Trim();
				spans.AddRange(Inline(text[(i + 1)..close]).Spans.Select(part => part with { Bold = part.Bold || bold, Link = target }));
				i = linkEnd;
				continue;
			}

			run.Append(c);
		}

		Flush();
		return new MarkdownInline(spans);

		void Flush() {
			if (run.Length > 0) {
				spans.Add(new MarkdownSpan(run.ToString(), bold));
				run.Clear();
			}
		}
	}

	// GitHub's anchor for a heading titled <title>: lower case, spaces as hyphens, and every character but letters, digits,
	// hyphens and underscores dropped; a repeat takes "-1", "-2" and so on.
	private static string Anchor(string title, Dictionary<string, int> anchors) {
		var slug = new StringBuilder();
		foreach (char c in title.ToLowerInvariant()) {
			if (char.IsLetterOrDigit(c) || c is '-' or '_') {
				slug.Append(c);
			} else if (c == ' ') {
				slug.Append('-');
			}
		}

		string anchor = slug.ToString();
		if (anchors.TryGetValue(anchor, out int count)) {
			anchors[anchor] = count + 1;
			return $"{anchor}-{count}";
		}

		anchors[anchor] = 1;
		return anchor;
	}
}
