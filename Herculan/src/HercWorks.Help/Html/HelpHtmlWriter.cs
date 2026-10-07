using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HercWorks.Help.Html;

/// <summary>
/// Turns a <see cref="HelpFile"/> into one self-contained HTML page that a browser shows in place of
/// WinHelp. The design and its security rules are docs/herculan/online-manual.md; the format is
/// docs/retail/formats/winhelp.md.
///
/// <para>Nothing from the file reaches the page except as HTML-encoded text or as a number this class
/// formats. Font names map through a fixed table, colours and sizes are written from integers, link
/// targets are topic numbers this class assigned, and pictures are PNGs this class encoded. No path,
/// URL or file name read from the help file is emitted as a link, and no macro is run: the handful the
/// manual uses are recognised and mapped to fixed actions, and the rest are left as plain text. The
/// readme a caller supplies is likewise HTML-encoded text, one more topic in a monospace block.</para>
/// </summary>
public static class HelpHtmlWriter {
	/// <summary>
	/// File units per CSS point for spacing, indents, tab stops and column widths. The unit is an Open
	/// item in docs/retail/formats/winhelp.md; this takes it as the half-point the font sizes use.
	/// </summary>
	private const double UnitsPerPoint = 2;

	/// <summary>
	/// Writes the page. <paramref name="language"/> is the page's <c>lang</c>, a two- or three-letter
	/// code; anything else is left out. <paramref name="readme"/> is the text the help file's <c>SH</c>
	/// action shows, from <see cref="WriteDocument.ReadText"/>; without it that action shows a note.
	/// </summary>
	public static string Write(HelpFile help, string? language = null, string? readme = null) {
		ArgumentNullException.ThrowIfNull(help);
		bool valid = language is { Length: 2 or 3 } && language.All(char.IsAsciiLetterLower);
		return new Writer(help, valid ? language : null, readme).Write();
	}

	/// <summary>The Content-Security-Policy hash of the page's script, as the page's own policy names it.</summary>
	internal static string ScriptHash() =>
		"sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ManualScript.Text)));

	private sealed class Writer(HelpFile help, string? language, string? readme) {
		private readonly StringBuilder _html = new();
		private readonly Dictionary<int, (HelpPicture Picture, string Uri)?> _pictures = [];
		private readonly Dictionary<string, int> _keywordIndex = new(StringComparer.Ordinal);
		private int _maps;
		private int _popupColour = 0xFFFFFF;

		public string Write() {
			for (int i = 0; i < help.Keywords.Count; i++) {
				_keywordIndex.TryAdd(help.Keywords[i].Keyword, i);
			}

			var startup = help.StartupMacros.Select(HelpMacro.Parse).OfType<IReadOnlyList<HelpMacroCall>>().SelectMany(c => c).ToList();
			foreach (var call in startup.Where(c => c.Name == "SPC" && c.Arguments.Count == 1)) {
				if (int.TryParse(call.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bgr)) {
					// SetPopupColor takes a Windows COLORREF, 0x00BBGGRR.
					_popupColour = ((bgr & 0xFF) << 16) | (bgr & 0xFF00) | ((bgr >> 16) & 0xFF);
				}
			}

			int contents = help.Locate(help.ContentsOffset)?.Topic.Number ?? 0;
			_html.Append("<!doctype html>\n<html").Append(language != null ? " lang=\"" + language + "\"" : "")
				.Append("><head><meta charset=\"utf-8\">\n");
			_html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src '")
				.Append(ScriptHash()).Append("'; base-uri 'none'; form-action 'none'\">\n");
			_html.Append("<meta name=\"referrer\" content=\"no-referrer\">\n<title>").Append(Encode(help.Title)).Append("</title>\n<style>\n");
			WriteStyles();
			_html.Append("</style></head>\n<body data-contents=\"").Append(Num(contents)).Append("\">\n");
			_html.Append("<noscript><p class=\"noscript\">This manual needs JavaScript to show its pages.</p></noscript>\n");
			// The stage is the main window at its defined size, in CSS pixels, which the script zooms; the
			// bar sits directly above it at its zoomed width, and the desk round both is the rest of the
			// browser window. Secondary windows and pop-ups sit on the stage, so they zoom with it and keep
			// their place over the main window.
			var (width, height) = MainSize();
			_html.Append("<div id=\"desk\"><div id=\"frame\">\n");
			WriteBar(startup);
			_html.Append("<div id=\"stage\" data-width=\"").Append(Num(width)).Append("\" data-height=\"").Append(Num(height))
				.Append("\" style=\"width:").Append(Num(width)).Append("px;height:").Append(Num(height)).Append("px\">\n")
				.Append("<div id=\"main\" class=\"box\"></div>\n");
			WriteWindows();
			_html.Append("<div id=\"popup\" class=\"box\" hidden></div>\n</div></div></div>\n");

			foreach (var topic in help.Topics) {
				WriteTopic(topic);
			}

			WriteIndex();
			WriteReadme();
			_html.Append("<script>").Append(ManualScript.Text).Append("</script>\n</body></html>\n");
			return _html.ToString();
		}

		// The main window's defined size; 640x480 in all three retail files.
		private (int Width, int Height) MainSize() {
			var main = help.Windows.Count > 0 ? help.Windows[0] : null;
			return main is { Width: > 0 and <= 4096, Height: > 0 and <= 4096 } ? (main.Width, main.Height) : (640, 480);
		}

		private void WriteStyles() {
			var main = help.Windows.Count > 0 ? help.Windows[0] : null;
			_html.Append("[hidden]{display:none!important;}\nhtml,body{margin:0;height:100%;}\n")
				.Append("body{display:flex;flex-direction:column;font:10pt Arial,Helvetica,sans-serif;color:#000;background:#303030;}\n")
				.Append("#bar{display:flex;flex-wrap:wrap;gap:4px;padding:4px;box-sizing:border-box;background:#c0c0c0;border-bottom:1px solid #808080;}\n")
				.Append("#bar a,.wbuttons a,a.button{font:9pt Arial,Helvetica,sans-serif;color:#000;text-decoration:none;background:#c0c0c0;")
				.Append("border:2px outset #fff;padding:1px 8px;cursor:pointer;white-space:nowrap;}\n")
				.Append("#bar a.off{color:#808080;pointer-events:none;}\n")
				.Append("#bar details{position:relative;}\n#bar summary{font:9pt Arial,sans-serif;padding:3px 6px;cursor:pointer;list-style:none;}\n")
				.Append("#bar details div{position:absolute;z-index:9;display:flex;flex-direction:column;background:#c0c0c0;border:1px solid #808080;}\n")
				.Append("#zoom{margin-left:auto;display:flex;gap:4px;align-items:center;font:9pt Arial,sans-serif;}\n")
				.Append("#zoom span{min-width:3.5em;text-align:center;}\n")
				.Append("#desk{flex:1;min-height:0;overflow:auto;display:flex;}\n")
				.Append("#frame{margin:auto;display:flex;flex-direction:column;}\n")
				.Append("#stage{position:relative;flex:none;}\n#stage.pixels img{image-rendering:pixelated;}\n")
				.Append(".box{overflow:auto;box-sizing:border-box;}\n")
				.Append("#main{position:absolute;inset:0;z-index:1;background:").Append(Colour(main?.Background ?? 0xFFFFFF)).Append(";}\n")
				.Append("#main.front{z-index:3;}\n")
				.Append("#main .nsr{background:").Append(Colour(main?.NonScrollingBackground ?? main?.Background ?? 0xFFFFFF)).Append(";}\n")
				.Append(".win{position:absolute;display:flex;flex-direction:column;box-sizing:border-box;border:2px outset #c0c0c0;z-index:2;}\n")
				.Append(".wcaption{min-height:1.4em;padding:1px 4px;background:#000080;color:#fff;font:bold 9pt Arial,sans-serif;}\n")
				.Append(".wbuttons{display:flex;gap:4px;padding:3px 4px;background:#c0c0c0;border-bottom:1px solid #808080;}\n")
				.Append(".wbody{flex:1;position:relative;}\n")
				.Append("#popup{position:absolute;z-index:8;max-width:420px;max-height:90%;padding:4px 8px;border:1px solid #000;background:")
				.Append(Colour(_popupColour)).Append(";box-shadow:4px 4px 0 rgba(0,0,0,.5);}\n")
				.Append(".nsr{position:sticky;top:0;z-index:1;}\n")
				// An 8-pixel margin inside the main and secondary windows. The help file sets none, and WinHelp's
				// own margin is not measured, so the width is this viewer's choice; it sits on the regions
				// rather than the window so a non-scrolling region's background still reaches the edges.
				.Append("#main>.nsr,#main>.scroll,.wbody>.nsr,.wbody>.scroll{padding-left:8px;padding-right:8px;}\n")
				.Append("#main>:first-child,.wbody>:first-child{padding-top:8px;}\n#main>.scroll,.wbody>.scroll{padding-bottom:8px;}\n")
				.Append("p{margin:0;}\na{color:inherit;cursor:pointer;}\nimg{vertical-align:bottom;}\n")
				.Append("table{border-collapse:collapse;table-layout:fixed;}\ntd{vertical-align:top;padding:0;overflow:visible;}\n")
				.Append(".tab{display:inline-block;text-indent:0;}\n")
				.Append(".index{padding:8px 16px;color:#c0c0c0;font:10pt Arial,sans-serif;}\n.index a{color:#0ff;display:block;}\n")
				// The readme has no colours of its own. It takes the light grey of the retail manual's body
				// text, which sits on the main window's black, so it reads as part of the manual.
				.Append(".readme{margin:0;font:10pt 'Courier New',Courier,monospace;white-space:pre;color:#c0c0c0;}\n")
				.Append(".noscript{color:#fff;padding:16px;}\n");

			for (int i = 0; i < help.Fonts.Count; i++) {
				var font = help.Fonts[i];
				_html.Append(".f").Append(Num(i)).Append("{font-family:").Append(Family(font)).Append(";font-size:")
					.Append(Num(font.HalfPoints / 2.0)).Append("pt;font-weight:").Append(font.Bold ? "bold" : "normal")
					.Append(";font-style:").Append(font.Italic ? "italic" : "normal")
					.Append(";text-decoration:").Append(font.Underline ? "underline" : "none")
					.Append(";font-variant:").Append(font.SmallCaps ? "small-caps" : "normal")
					.Append(";color:").Append(Colour(font.Colour)).Append(";}\n");
			}
		}

		// A fixed table: a face name from the file is never written into the page.
		private static string Family(HelpFont font) => font.Face switch {
			"Arial" => "Arial,Helvetica,sans-serif",
			"Times New Roman" or "Tms Rmn" => "'Times New Roman',Times,serif",
			"MS Sans Serif" or "Helv" => "'MS Sans Serif',Arial,sans-serif",
			"Courier" => "'Courier New',Courier,monospace",
			_ => font.Family == 2 ? "serif" : font.Family == 1 ? "monospace" : "sans-serif",
		};

		// The button bar: this viewer's own Contents, Index and Back, then what the file's startup macros
		// add — the browse buttons, its own buttons, and its Help menu items.
		private void WriteBar(List<HelpMacroCall> startup) {
			_html.Append("<nav id=\"bar\"><a href=\"#\" data-go=\"t").Append(Num(help.Locate(help.ContentsOffset)?.Topic.Number ?? 0))
				.Append("\">Contents</a><a href=\"#\" data-go=\"index\">Index</a><a href=\"#\" data-go=\"back\">Back</a>");
			if (startup.Any(c => c.Name == "BrowseButtons")) {
				_html.Append("<a href=\"#\" data-go=\"prev\">&lt;&lt;</a><a href=\"#\" data-go=\"next\">&gt;&gt;</a>");
			}

			foreach (var call in startup.Where(c => c.Name == "CB" && c.Arguments.Count == 3)) {
				WriteMacroAnchor(call.Arguments[2], call.Arguments[1], null);
			}

			var items = startup.Where(c => c.Name == "AI" && c.Arguments.Count == 4).ToList();
			if (items.Count > 0) {
				_html.Append("<details><summary>Help</summary><div>");
				foreach (var item in items) {
					WriteMacroAnchor(item.Arguments[3], item.Arguments[2].Replace("&", ""), null);
				}

				_html.Append("</div></details>");
			}

			// This viewer's own: a button per secondary window that brings it back in front of the main
			// window, shown while it is open and behind, then the zoom control.
			for (int w = 1; w < help.Windows.Count; w++) {
				var window = help.Windows[w];
				_html.Append("<a href=\"#\" data-go=\"raise\" data-w=\"").Append(Num(w)).Append("\" hidden>")
					.Append(Encode(window.Caption.Length > 0 ? window.Caption : window.Name)).Append("</a>");
			}

			_html.Append("<span id=\"zoom\"><a href=\"#\" data-go=\"smaller\" title=\"Smaller\">&minus;</a><span></span>")
				.Append("<a href=\"#\" data-go=\"larger\" title=\"Larger\">+</a></span>");
			_html.Append("</nav>\n");
		}

		// The secondary windows, placed over the main window as their definitions place them, clipped to
		// it. Each is a window frame: a title bar with its caption, then a button bar holding the buttons
		// its |CF macros create, as WinHelp's own windows have.
		private void WriteWindows() {
			var (mainWidth, mainHeight) = MainSize();
			for (int w = 1; w < help.Windows.Count; w++) {
				var window = help.Windows[w];
				int x = Math.Clamp(window.X, 0, mainWidth - 1), y = Math.Clamp(window.Y, 0, mainHeight - 1);
				int width = Math.Clamp(window.Width, 1, mainWidth - x), height = Math.Clamp(window.Height, 1, mainHeight - y);
				_html.Append("<div class=\"win\" id=\"w").Append(Num(w)).Append("\" hidden style=\"left:").Append(Num(x))
					.Append("px;top:").Append(Num(y)).Append("px;width:").Append(Num(width)).Append("px;height:").Append(Num(height))
					.Append("px;background:").Append(Colour(window.Background ?? 0xFFFFFF)).Append("\"><div class=\"wcaption\">")
					.Append(Encode(window.Caption)).Append("</div>");
				var buttons = (help.WindowMacros.TryGetValue(w, out var list) ? list : [])
					.Select(HelpMacro.Parse).OfType<IReadOnlyList<HelpMacroCall>>().SelectMany(c => c)
					.Where(c => c.Name == "CB" && c.Arguments.Count == 3).ToList();
				if (buttons.Count > 0) {
					_html.Append("<div class=\"wbuttons\">");
					foreach (var call in buttons) {
						WriteMacroAnchor(call.Arguments[2], call.Arguments[1], w);
					}

					_html.Append("</div>");
				}

				_html.Append("<div class=\"wbody box\"></div></div>\n");
			}
		}

		private void WriteTopic(HelpTopic topic) {
			_html.Append("<template id=\"t").Append(Num(topic.Number)).Append('"');
			AppendBrowse("prev", topic.BrowseBack);
			AppendBrowse("next", topic.BrowseForward);
			var closes = topic.EntryMacros.Select(HelpMacro.Parse).OfType<IReadOnlyList<HelpMacroCall>>().SelectMany(c => c)
				.Where(c => c.Name is "CW" or "CloseWindow" && c.Arguments.Count == 1)
				.Select(c => WindowIndex(c.Arguments[0])).OfType<int>().ToList();
			if (closes.Count > 0) {
				_html.Append(" data-close=\"").Append(string.Join(' ', closes.Select(Num))).Append('"');
			}

			_html.Append('>');
			if (topic.ScrollingStart > 0) {
				_html.Append("<div class=\"nsr\">");
				for (int i = 0; i < topic.ScrollingStart; i++) {
					WriteBlock(topic.Blocks[i], i);
				}

				_html.Append("</div>");
			}

			_html.Append("<div class=\"scroll\">");
			for (int i = topic.ScrollingStart; i < topic.Blocks.Count; i++) {
				WriteBlock(topic.Blocks[i], i);
			}

			_html.Append("</div></template>\n");
		}

		private void AppendBrowse(string name, uint? offset) {
			if (offset is { } o && help.Locate(o) is { } target) {
				_html.Append(" data-").Append(name).Append("=\"").Append(Num(target.Topic.Number)).Append('"');
			}
		}

		private int? WindowIndex(string name) {
			for (int i = 0; i < help.Windows.Count; i++) {
				if (string.Equals(help.Windows[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
					return i;
				}
			}

			return null;
		}

		private void WriteBlock(HelpBlock block, int index) {
			_html.Append("<div data-b=\"").Append(Num(index)).Append("\">");
			switch (block) {
				case HelpParagraphRun run:
					new Run(this, run.Format, run.Inlines).Write();
					break;
				case HelpTableRow row:
					WriteRow(row);
					break;
			}

			_html.Append("</div>");
		}

		// A row's columns keep their stored widths whatever they hold: the heading banners are a 610-pixel
		// picture in a column a few points wide, drawn under the heading text in the next column, so a
		// column must not grow to fit its content. Cells for one column stack inside it.
		//
		// The second value stored with each column is drawn as space before the column's content. That
		// reading is this viewer's: the format doc lists the value as Open, and it is 1 for the first
		// column and 11 for every later one, the pattern a gap between columns would leave.
		private void WriteRow(HelpTableRow row) {
			int Gap(HelpColumn column) => Math.Max(0, column.Second);
			_html.Append("<table style=\"width:").Append(Num(row.Columns.Sum(c => c.Width + Gap(c)) / UnitsPerPoint)).Append("pt\"><colgroup>");
			foreach (var column in row.Columns) {
				_html.Append("<col style=\"width:").Append(Num((column.Width + Gap(column)) / UnitsPerPoint)).Append("pt\">");
			}

			_html.Append("</colgroup><tr>");
			for (int c = 0; c < row.Columns.Count; c++) {
				_html.Append("<td style=\"padding-left:").Append(Num(Gap(row.Columns[c]) / UnitsPerPoint)).Append("pt\">");
				foreach (var cell in row.Cells.Where(cell => cell.Column == c)) {
					new Run(this, cell.Format, cell.Inlines).Write();
				}

				_html.Append("</td>");
			}

			_html.Append("</tr></table>");
		}

		private void WriteIndex() {
			_html.Append("<template id=\"index\"><div class=\"index\">");
			for (int i = 0; i < help.Keywords.Count; i++) {
				var keyword = help.Keywords[i];
				_html.Append("<a href=\"#\" data-go=\"").Append(KeywordTarget(i)).Append("\">").Append(Encode(keyword.Keyword)).Append("</a>");
			}

			_html.Append("</div></template>\n");

			// A keyword naming several topics gets a list of them, as WinHelp's Topics Found does.
			for (int i = 0; i < help.Keywords.Count; i++) {
				var keyword = help.Keywords[i];
				if (keyword.Targets.Count < 2) {
					continue;
				}

				_html.Append("<template id=\"k").Append(Num(i)).Append("\"><div class=\"index\"><p>")
					.Append(Encode(keyword.Keyword)).Append("</p>");
				foreach (uint target in keyword.Targets) {
					if (help.Locate(target) is { } at) {
						_html.Append("<a href=\"#\" data-go=\"").Append(TopicTarget(at)).Append("\">")
							.Append(Encode(at.Topic.Title.Length > 0 ? at.Topic.Title : "(untitled)")).Append("</a>");
					}
				}

				_html.Append("</div></template>\n");
			}
		}

		// The readme as one more main-window topic, its line breaks kept and nothing in it made a link.
		// A newline straight after <pre> is dropped by the HTML parser, so one is written there for it to
		// drop rather than the text's own.
		private void WriteReadme() {
			if (readme != null) {
				_html.Append("<template id=\"readme\"><div class=\"scroll\"><pre class=\"readme\">\n").Append(Encode(readme))
					.Append("</pre></div></template>\n");
			}
		}

		private string KeywordTarget(int keyword) {
			var targets = help.Keywords[keyword].Targets;
			if (targets.Count == 1 && help.Locate(targets[0]) is { } at) {
				return TopicTarget(at);
			}

			return "k" + Num(keyword);
		}

		private static string TopicTarget((HelpTopic Topic, int Block) at) =>
			at.Block == 0 ? "t" + Num(at.Topic.Number) : "t" + Num(at.Topic.Number) + "." + Num(at.Block);

		/// <summary>
		/// The opening tag for a macro, or null when the macro is not one this viewer acts on. JI jumps to
		/// a context string in this file, JK to a keyword's topics, and CW closes a window. SH, which
		/// starts a program, opens the readme topic in the main window, or without a readme shows a note
		/// saying the program is not started.
		/// </summary>
		private string? MacroAnchor(string macro, int? inWindow) {
			if (HelpMacro.Parse(macro) is not [var call]) {
				return null;
			}

			switch (call.Name) {
				case "JI" or "JumpId" when call.Arguments.Count == 2 && call.Arguments[0].Length == 0
					&& ContextHash.TryCompute(call.Arguments[1], out uint hash) && help.LocateContext(hash) is { } at:
					return "<a href=\"#\" data-go=\"" + TopicTarget(at) + "\">";
				case "JK" or "JumpKeyword" when call.Arguments.Count == 2 && call.Arguments[0].Length == 0
					&& _keywordIndex.TryGetValue(call.Arguments[1], out int keyword):
					return "<a href=\"#\" data-go=\"" + KeywordTarget(keyword) + "\">";
				case "CW" or "CloseWindow" when call.Arguments.Count == 1 && WindowIndex(call.Arguments[0]) == inWindow && inWindow != null:
					return "<a href=\"#\" data-go=\"close\">";
				case "SH" or "ShortCut" when call.Arguments.Count >= 2:
					// Retail WinHelp starts the named program, the readme in every retail file. This viewer
					// starts nothing: it shows the readme's text, or says why not; docs/herculan/online-manual.md#the-page.
					return readme != null
						? "<a href=\"#\" data-go=\"readme\" data-w=\"0\">"
						: "<a href=\"#\" data-go=\"note\" data-note=\"" + Encode("This manual's viewer does not start programs. The help file asks for "
							+ call.Arguments[1] + ".") + "\">";
				default:
					return null;
			}
		}

		private void WriteMacroAnchor(string macro, string label, int? inWindow) {
			if (MacroAnchor(macro, inWindow) is { } open) {
				_html.Append(open).Append(Encode(label)).Append("</a>");
			}
		}

		private string? LinkAnchor(HelpLink link) {
			switch (link) {
				case HelpJump jump when help.LocateContext(jump.Hash) is { } at:
					return jump.Window is { } w && w < help.Windows.Count
						? "<a href=\"#\" data-go=\"" + TopicTarget(at) + "\" data-w=\"" + Num(w) + "\">"
						: "<a href=\"#\" data-go=\"" + TopicTarget(at) + "\">";
				case HelpPopup popup when help.LocateContext(popup.Hash) is { } at:
					return "<a href=\"#\" data-go=\"p" + Num(at.Topic.Number) + "\">";
				case HelpMacroLink macro:
					return MacroAnchor(macro.Macro, null);
				default:
					return null;
			}
		}

		// Each picture is decoded and encoded once, however many topics show it.
		private (HelpPicture Picture, string Uri)? Picture(int number) {
			if (!_pictures.TryGetValue(number, out var entry)) {
				entry = help.TryGetPicture(number, out _) is { } picture
					? (picture, "data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(picture.Width, picture.Height, picture.Rgb)))
					: null;
				_pictures[number] = entry;
			}

			return entry;
		}

		private void WritePicture(HelpPictureRef reference, StringBuilder into) {
			if (Picture(reference.Number) is not var (picture, uri)) {
				return;
			}

			string map = picture.Hotspots.Count > 0 ? "m" + Num(++_maps) : "";
			into.Append("<img alt=\"\" width=\"").Append(Num(picture.Width)).Append("\" height=\"").Append(Num(picture.Height))
				.Append("\" src=\"").Append(uri).Append('"');
			if (reference.Placement == HelpPicturePlacement.Right) {
				into.Append(" style=\"float:right\"");
			}

			if (map.Length > 0) {
				into.Append(" usemap=\"#").Append(map).Append("\"><map name=\"").Append(map).Append("\">");
				foreach (var spot in picture.Hotspots) {
					if (help.LocateContext(spot.Hash) is not { } at) {
						continue;
					}

					into.Append("<area shape=\"rect\" coords=\"").Append(Num(spot.Left)).Append(',').Append(Num(spot.Top)).Append(',')
						.Append(Num(spot.Left + spot.Width)).Append(',').Append(Num(spot.Top + spot.Height))
						.Append("\" href=\"#\" alt=\"\" data-go=\"p").Append(Num(at.Topic.Number)).Append("\">");
				}

				into.Append("</map>");
			} else {
				into.Append('>');
			}
		}

		/// <summary>
		/// One paragraph run's inlines as paragraphs. Fonts are spans and hotspots are anchors; both are
		/// closed and reopened round every boundary they cross, so the markup stays nested whatever order
		/// the commands come in.
		///
		/// <para>Every tab in the corpus is a hanging indent — one tab stop at the left indent and a
		/// negative first-line indent — so the first tab of a paragraph's first line becomes an
		/// inline-block as wide as the gap to that stop. Any other tab is a space.</para>
		/// </summary>
		private sealed class Run(Writer writer, HelpParagraphFormat format, IReadOnlyList<HelpInline> inlines) {
			private readonly StringBuilder _head = new();
			private readonly StringBuilder _rest = new();
			private StringBuilder _into = null!;
			private int? _font;
			private string? _anchor;
			private bool _tabbed;
			private bool _brokenLine;
			private bool _hasText;
			private bool _visible;

			public void Write() {
				Begin();
				foreach (var inline in inlines) {
					switch (inline) {
						case HelpText text:
							_into.Append(Encode(text.Text));
							_hasText |= text.Text.Length > 0;
							_visible |= !string.IsNullOrWhiteSpace(text.Text);
							break;
						case HelpFontChange change:
							CloseSpan();
							_font = change.Descriptor;
							OpenSpan();
							break;
						case HelpLineBreak:
							_into.Append("<br>");
							_brokenLine = true;
							break;
						case HelpTab:
							Tab();
							break;
						case HelpParagraphEnd:
							End();
							Begin();
							break;
						case HelpPictureRef picture:
							writer.WritePicture(picture, _into);
							_hasText = _visible = true;
							break;
						case HelpButton button:
							CloseSpan();
							if (writer.MacroAnchor(button.Macro, null) is { } open) {
								_into.Append(open.Replace("<a ", "<a class=\"button\" ")).Append(Encode(button.Label)).Append("</a>");
							}

							OpenSpan();
							_hasText = _visible = true;
							break;
						case HelpHotspotStart start:
							CloseSpan();
							CloseAnchor();
							_anchor = writer.LinkAnchor(start.Link);
							OpenAnchor();
							OpenSpan();
							break;
						case HelpHotspotEnd:
							CloseSpan();
							CloseAnchor();
							_anchor = null;
							OpenSpan();
							break;
					}
				}

				if (_hasText) {
					End();
				}
			}

			private void Begin() {
				_head.Clear();
				_rest.Clear();
				_into = _head;
				_tabbed = false;
				_brokenLine = false;
				_hasText = false;
				_visible = false;
				OpenAnchor();
				OpenSpan();
			}

			// A paragraph of nothing but spaces still takes a line, as an empty one does in WinHelp, so it
			// gets a non-breaking space in its own font.
			private void End() {
				if (!_visible) {
					_into.Append("&nbsp;");
				}

				CloseSpan();
				CloseAnchor();
				var html = writer._html;
				html.Append("<p style=\"padding:").Append(Pt(format.SpaceAbove)).Append(' ').Append(Pt(format.RightIndent)).Append(' ')
					.Append(Pt(format.SpaceBelow)).Append(' ').Append(Pt(format.LeftIndent)).Append(";text-indent:")
					.Append(Pt(format.FirstLineIndent)).Append(";text-align:")
					.Append(format.Alignment switch { HelpAlignment.Right => "right", HelpAlignment.Centre => "center", _ => "left" })
					.Append("\">");
				if (_tabbed) {
					int stop = format.TabStops.FirstOrDefault(s => s > format.LeftIndent + format.FirstLineIndent, -1);
					html.Append("<span class=\"tab\" style=\"min-width:")
						.Append(Pt(stop < 0 ? 0 : stop - format.LeftIndent - format.FirstLineIndent)).Append("\">")
						.Append(_head).Append("</span>").Append(_rest);
				} else {
					html.Append(_head);
				}

				html.Append("</p>");
			}

			private void Tab() {
				if (_tabbed || _brokenLine) {
					_into.Append(' ');
					return;
				}

				CloseSpan();
				CloseAnchor();
				_tabbed = true;
				_into = _rest;
				OpenAnchor();
				OpenSpan();
			}

			private void OpenSpan() {
				if (_font is { } f) {
					_into.Append("<span class=\"f").Append(Num(f)).Append("\">");
				}
			}

			private void CloseSpan() {
				if (_font != null) {
					_into.Append("</span>");
				}
			}

			private void OpenAnchor() {
				if (_anchor != null) {
					_into.Append(_anchor);
				}
			}

			private void CloseAnchor() {
				if (_anchor != null) {
					_into.Append("</a>");
				}
			}

			private static string Pt(int units) => Num(units / UnitsPerPoint) + "pt";
		}
	}

	private static string Encode(string text) => WebUtility.HtmlEncode(text);

	private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Colour(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
}
