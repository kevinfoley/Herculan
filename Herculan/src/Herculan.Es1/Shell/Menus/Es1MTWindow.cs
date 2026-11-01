using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>One of a menu window's six labels, a <c>0x12</c>-byte entry at <c>+0x537</c>.</summary>
/// <param name="Text"><c>+0</c>, a copy of the record's text.</param>
/// <param name="X"><c>+4</c>, relative to the window.</param>
/// <param name="Y"><c>+8</c>.</param>
/// <param name="Width"><c>+0xc</c>, an <c>int16</c>: the record's right − left.</param>
/// <param name="Mode"><c>+0xe</c>: 0, 1 or 2 for kinds 1, 4 and 5, the alignment it is drawn with.</param>
public readonly record struct Es1MenuLabel(string Text, int X, int Y, short Width, int Mode);

/// <summary>
/// The menu window, class <c>MTWindow</c> (vtable <c>00081140</c>, <c>0x5a7</c> bytes)
/// (docs/es1/shell/menu-controls.md#the-window): a <see cref="Es1DUIWindow"/> with a picture, a title on a framed plate and up to
/// six labels.
/// </summary>
public sealed class Es1MTWindow : Es1DUIWindow {
	/// <summary>The labels a menu window keeps.</summary>
	public const int MaxLabels = 6;

	/// <summary>The window id whose picture is drawn at random from five, <c>res32\menu&lt;9999 − r&gt;.dba</c>.</summary>
	public const short LoadingWindow = 9999;

	/// <summary>The font slots of <c>title</c> (<c>000aacc6</c>) and of the labels: <c>cpylw</c> (<c>000aacca</c>), <c>active</c> (<c>000aacce</c>), <c>nm_green</c> (<c>000aacda</c>) and <c>cstats_2</c> (<c>000aacea</c>).</summary>
	private const int TitleFontSlot = 0, LabelFontSlot = 1, ActiveFontSlot = 2, GreenFontSlot = 5, Stats2FontSlot = 9;

	/// <summary>The plate's colors: its top and left edges, its right, its bottom.</summary>
	private const byte PlateColor = 0x10, PlateRightColor = 0x1b, PlateBottomColor = 0x1c;

	private readonly Es1MenuContext _context;

	/// <summary>
	/// <c>MTWindow_Ctor</c> (<c>00036874</c>)(window, <paramref name="id"/>, <paramref name="title"/>,
	/// <paramref name="rect"/>): <c>DUIWindow_Ctor</c> over the rect; <c>res32\menu&lt;id&gt;.dba</c> loaded, id 9999's
	/// number drawn with <c>Math_RandomBelow</c>(5) (<see cref="Es1ShellRandom.Below"/>); the title font; the label font,
	/// <c>nm_green</c> for the windows 999, 1240 and 4444, <c>cstats_2</c> for 1610 and 1800, <c>active</c> for 1920 (the six
	/// ids at <c>00081174</c>) and <c>cpylw</c> for any other; the title copied, empty for none; no labels and no hotspots.
	/// </summary>
	public Es1MTWindow(Es1MenuContext context, short id, string? title, Es1Rect rect) : base(context.Display, rect) {
		_context = context;
		Id = id;
		int number = id == LoadingWindow ? id - (context.Data.Random?.Below(5) ?? 0) : id;
		string name = $"MENU{number}.DBA";
		Bitmaps = context.Content.Read("res32", name) is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;
		if (Bitmaps == null) {
			// PLACEHOLDER: what Resource_LoadBitmapArray returns for a name with no file is dlg-menu-dialogs.md's Open
			// (windows 1501 and 1700 name one); here the window keeps no picture and draws none.
			Console.Error.WriteLine($"res32\\{name} is missing or unreadable: the menu window has no picture.");
		}

		TitleFont = context.Data.Fonts[TitleFontSlot];
		LabelFont = context.Data.Fonts[id switch {
			999 or 1240 or 4444 => GreenFontSlot,
			1610 or 1800 => Stats2FontSlot,
			1920 => ActiveFontSlot,
			_ => LabelFontSlot,
		}];
		Title = title ?? "";
	}

	/// <summary>The window record's id, which picked the bitmap array.</summary>
	public short Id { get; }

	/// <summary><c>+0x5a3</c>, <c>res32\menu&lt;id&gt;.dba</c>'s frames; none when it does not read.</summary>
	public IReadOnlyList<DynamixBitmap>? Bitmaps { get; }

	/// <summary><c>+0x4ed</c>, the title's font.</summary>
	public HudFont? TitleFont { get; }

	/// <summary><c>+0x4f1</c>, the labels' font.</summary>
	public HudFont? LabelFont { get; }

	/// <summary><c>+0x4f5</c>, the title.</summary>
	public string Title { get; }

	/// <summary><c>+0x537</c>, the labels, counted at <c>+0x535</c>.</summary>
	public List<Es1MenuLabel> Labels { get; } = [];

	/// <summary>
	/// Slot <c>+4</c>, <c>MTWindow_AddLabel</c> (<c>00036a93</c>)(window, <paramref name="mode"/>,
	/// <paramref name="width"/>, <paramref name="x"/>, <paramref name="y"/>, size, <paramref name="text"/>): under six
	/// labels, a copy of the text kept with the point, the width and the mode; the rest dropped.
	/// </summary>
	public override void AddLabel(int mode, int width, int x, int y, string text) {
		if (Labels.Count < MaxLabels) {
			Labels.Add(new Es1MenuLabel(text, x, y, (short)width, mode));
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MTWindow_Draw</c> (<c>00036b4c</c>)
	/// (docs/es1/shell/menu-controls.md#the-menu-windows-drawing--mtwindow_draw-00036b4c): the first bitmap at the window's corner,
	/// which <c>DUIWindow_Draw</c> has the context draw untranslated; the translation set; the title's plate and the title,
	/// which the code draws for every menu window, an empty title included (it tests the title buffer's address); then
	/// each label in the label font from (<i>x</i>, <i>y</i> + the font's cell height − 3), its mode as the alignment and
	/// its width as the field width.
	/// </summary>
	public override void DrawBackground(Es1ShellRenderContext context) {
		context.Blit(Bitmaps is { Count: > 0 } bitmaps ? bitmaps[0] : null, Rect.X0, Rect.Y0);
		context.Translate = true;
		DrawTitle(context);
		foreach (var label in Labels) {
			context.Alignment = label.Mode;
			context.FieldWidth = label.Width;
			context.DrawString(LabelFont, label.Text, label.X, (LabelFont?.CellHeight ?? 0) + label.Y - 3);
		}
	}

	/// <summary>
	/// The plate: <i>y0</i> 4 on backdrops 4000 and 2000 and 5 on the others, <i>y1</i> = <i>y0</i> + 17; the margin 20
	/// on backdrop 4000 and 40 on the others; <i>x0</i> = ((<c>int16</c>) width − the title's width − margin) / 2,
	/// truncating, and <i>x1</i> = the title's width + <i>x0</i> + margin. Four lines through <c>0004ba84</c>, ES1GO's
	/// <c>Raster_DrawLineColour</c>: the top and the left edge in <c>0x10</c>, the right in <c>0x1b</c>, the bottom in
	/// <c>0x1c</c>; frames 0 to 3 of <c>res32\corners.dba</c> at (<i>x0</i>, <i>y0</i>), (<i>x1</i> − 4, <i>y0</i>),
	/// (<i>x0</i>, <i>y1</i> − 4) and (<i>x1</i> − 4, <i>y1</i> − 4); the title in the title font, centered between
	/// <i>x0</i> and <i>x1</i>, from (<i>x0</i>, (<i>y1</i> − <i>y0</i>) / 2 + <i>y0</i> − 1).
	/// </summary>
	private void DrawTitle(Es1ShellRenderContext context) {
		short backdrop = _context.Screens.BackdropId;
		int y0 = backdrop is 4000 or 2000 ? 4 : 5;
		int margin = backdrop == 4000 ? 20 : 40;
		int titleWidth = TitleFont?.Measure(Title) ?? 0;
		int x0 = ((short)(Rect.X1 - Rect.X0) - titleWidth - margin) / 2;
		int x1 = titleWidth + x0 + margin;
		int y1 = y0 + 0x11;
		context.Alignment = 2;
		context.FieldWidth = x1 - x0;

		var canvas = context.Canvas;
		canvas.DrawLine(x0, y0, x1, y0, PlateColor);
		canvas.DrawLine(x0, y0, x0, y1, PlateColor);
		canvas.DrawLine(x1, y1, x1, y0, PlateRightColor);
		canvas.DrawLine(x1, y1, x0, y1, PlateBottomColor);

		var corners = _context.Data.Corners;
		canvas.Blit(Es1CornerFrame.Frame(corners, 0), x0, y0);
		canvas.Blit(Es1CornerFrame.Frame(corners, 1), x1 - 4, y0);
		canvas.Blit(Es1CornerFrame.Frame(corners, 2), x0, y1 - 4);
		canvas.Blit(Es1CornerFrame.Frame(corners, 3), x1 - 4, y1 - 4);

		context.DrawString(TitleFont, Title, x0, (y1 - y0) / 2 + y0 - 1);
	}

	/// <summary>
	/// <c>MTWindow_Dtor</c> (<c>00036a09</c>)(window, flags): each label's text freed, the bitmap array destroyed with
	/// <c>GLBitmapArray_Dtor</c>, and <c>DUIWindow_Dtor</c>(window, 0).
	/// </summary>
	public new void Dtor() {
		Labels.Clear();
		base.Dtor();
	}
}
