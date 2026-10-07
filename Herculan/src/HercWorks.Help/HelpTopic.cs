namespace HercWorks.Help;

/// <summary>
/// One topic: its header and the paragraph runs and table rows that follow it in the topic chain
/// (docs/retail/formats/winhelp.md#topic-header).
/// </summary>
/// <param name="Number">The topic number, 0 upward in chain order.</param>
/// <param name="Title">The title; empty for the untitled pop-up and <c>overview</c> topics.</param>
/// <param name="Offset">The topic offset of its header, the value <c>|CONTEXT</c> and the browse fields name it by.</param>
/// <param name="BrowseBack">The previous topic in the browse sequence, or null.</param>
/// <param name="BrowseForward">The next topic in the browse sequence, or null.</param>
/// <param name="EntryMacros">The macros run when the topic is shown, in order.</param>
/// <param name="Blocks">The topic's content, in chain order.</param>
/// <param name="ScrollingStart">
/// Index of the first block of the scrolling region. The blocks before it are the non-scrolling
/// region; 0 when the topic has none.
/// </param>
public sealed record HelpTopic(
	int Number, string Title, uint Offset, uint? BrowseBack, uint? BrowseForward,
	IReadOnlyList<string> EntryMacros, IReadOnlyList<HelpBlock> Blocks, int ScrollingStart);

/// <summary>A paragraph run or a table row, with the topic offset of its first character.</summary>
public abstract record HelpBlock(uint Offset);

/// <summary>A <c>0x20</c> record: one paragraph format shared by every paragraph its inlines contain.</summary>
public sealed record HelpParagraphRun(uint Offset, HelpParagraphFormat Format, IReadOnlyList<HelpInline> Inlines)
	: HelpBlock(Offset);

/// <summary>A <c>0x23</c> record: one table row (docs/retail/formats/winhelp.md#tables).</summary>
public sealed record HelpTableRow(uint Offset, IReadOnlyList<HelpColumn> Columns, IReadOnlyList<HelpCell> Cells)
	: HelpBlock(Offset);

/// <summary>A table column: its width and the second value stored with it, both in file units.</summary>
public readonly record struct HelpColumn(int Width, int Second);

/// <summary>One cell record. A column may have several, each a paragraph run of its own.</summary>
public sealed record HelpCell(int Column, HelpParagraphFormat Format, IReadOnlyList<HelpInline> Inlines);

/// <summary>Horizontal alignment, from paragraph format bits <c>0x0400</c> and <c>0x0800</c>.</summary>
public enum HelpAlignment {
	Left,
	Right,
	Centre,
}

/// <summary>
/// A paragraph format. Spacing, indents and tab stops are in the file's own unit, which
/// docs/retail/formats/winhelp.md#open lists as unresolved; a field the format leaves unset is 0.
/// </summary>
public sealed record HelpParagraphFormat(
	int SpaceAbove, int SpaceBelow, int LineSpacing, int LeftIndent, int RightIndent, int FirstLineIndent,
	IReadOnlyList<int> TabStops, HelpAlignment Alignment);

/// <summary>One step of a paragraph run's content: a string or a command.</summary>
public abstract record HelpInline;

/// <summary>Text, in the current font.</summary>
public sealed record HelpText(string Text) : HelpInline;

/// <summary><c>0x80</c>: switch to font descriptor <paramref name="Descriptor"/>.</summary>
public sealed record HelpFontChange(int Descriptor) : HelpInline;

/// <summary><c>0x81</c>.</summary>
public sealed record HelpLineBreak : HelpInline;

/// <summary><c>0x82</c>.</summary>
public sealed record HelpParagraphEnd : HelpInline;

/// <summary><c>0x83</c>.</summary>
public sealed record HelpTab : HelpInline;

/// <summary>Where a picture sits: <c>0x86</c> inline as a character, <c>0x88</c> at the right margin.</summary>
public enum HelpPicturePlacement {
	Inline,
	Right,
}

/// <summary>A picture command naming <c>|bm</c><paramref name="Number"/>.</summary>
public sealed record HelpPictureRef(HelpPicturePlacement Placement, int Number) : HelpInline;

/// <summary>A type 5 picture: a button with a label that runs a macro (docs/retail/formats/winhelp.md#embedded-buttons).</summary>
public sealed record HelpButton(string Label, string Macro) : HelpInline;

/// <summary>The start of a hotspot, which runs to the next <see cref="HelpHotspotEnd"/>.</summary>
public sealed record HelpHotspotStart(HelpLink Link) : HelpInline;

/// <summary><c>0x89</c>.</summary>
public sealed record HelpHotspotEnd : HelpInline;

/// <summary>What a hotspot does.</summary>
public abstract record HelpLink;

/// <summary>
/// <c>0xE7</c>, or <c>0xEF</c> with a window: jump to the topic whose context string hashes to
/// <paramref name="Hash"/>, in window <paramref name="Window"/> (an index into
/// <see cref="HelpFile.Windows"/>) or, when null, the window the jump is made from.
/// </summary>
public sealed record HelpJump(uint Hash, int? Window) : HelpLink;

/// <summary>A pop-up of the topic whose context string hashes to <paramref name="Hash"/>.</summary>
public sealed record HelpPopup(uint Hash) : HelpLink;

/// <summary><c>0xCC</c>: run <paramref name="Macro"/>.</summary>
public sealed record HelpMacroLink(string Macro) : HelpLink;
