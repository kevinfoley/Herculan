using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// A shell window's render context, the <c>GLViewport</c> <c>000512a0</c>(0) builds at the window's <c>+0x17</c>
/// (docs/es1/shell/menus.md#the-window): <c>0x239</c> bytes copied from the clip block the shell display's
/// <c>+0x3c</c> points at, the rect <c>+0x210</c> the window's on the screen, the origin <c>+0x220</c> (0, 0), clip mode
/// <c>+0x208</c> 1, and the underline <c>+0x230</c>, alignment <c>+0x231</c> and field width <c>+0x235</c> cleared.
///
/// <para>Here it is an <see cref="Es1PanelCanvas"/> over the shell's display with the rect as its clip and translation,
/// as the simulator's panels draw (an engine shape, retail's fields kept): <see cref="Canvas"/> has the window's corner
/// as its origin while <see cref="Translate"/> (<c>+0x20c</c>) is set, and none with it clear, the rect still clipping.
/// Retail installs a context in <c>000ab612</c> and the draws read it; here the window passes its context to what
/// draws.</para>
/// </summary>
public sealed class Es1ShellRenderContext {
	/// <param name="rect"><c>+0x210</c>–<c>+0x21c</c>, on the screen.</param>
	public Es1ShellRenderContext(Es1VideoMemory display, Es1Rect rect) {
		Display = display;
		Rect = rect;
	}

	public Es1VideoMemory Display { get; }

	/// <summary><c>+0x210</c>–<c>+0x21c</c>: the clip, and the translation while <see cref="Translate"/> is set.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x20c</c>, the translation flag; <c>GLViewport_Ctor</c> leaves the copied block's.</summary>
	public bool Translate { get; set; }

	/// <summary><c>+0x231</c>: 0 draws a string from the pen, 1 ends it at the pen plus <see cref="FieldWidth"/>, 2 centers it in that width.</summary>
	public int Alignment { get; set; }

	/// <summary><c>+0x235</c>, the field width <see cref="Alignment"/> 1 and 2 place a string in.</summary>
	public int FieldWidth { get; set; }

	/// <summary>The canvas the context draws through, its origin the rect's corner while <see cref="Translate"/> is set.</summary>
	public Es1PanelCanvas Canvas => Translate ? new Es1PanelCanvas(Display, Rect) : new Es1PanelCanvas(Display, Rect, (Rect.X0, Rect.Y0));

	/// <summary>
	/// <c>HudFont_DrawString</c> (<c>000520d8</c>)(<paramref name="font"/>, <paramref name="text"/>, pen), the code of
	/// <c>ES1DBSIM</c>'s: with <see cref="Alignment"/> 1 the pen moved right by <see cref="FieldWidth"/> less the text's width
	/// (<c>HudFont_MeasureString</c>), with 2 by half of that, the shift arithmetic; then the glyphs from the pen
	/// (<see cref="Es1PanelCanvas.DrawString"/>). The underline byte <c>+0x230</c> is never set on a shell context.
	/// </summary>
	public void DrawString(HudFont? font, string? text, int x, int y) {
		if (font == null || text == null) {
			return;
		}

		if (Alignment == 1) {
			x += FieldWidth - font.Measure(text);
		} else if (Alignment == 2) {
			x += (FieldWidth - font.Measure(text)) >> 1;
		}

		Canvas.DrawString(font, text, x, y);
	}

	/// <summary><c>Bitmap_Blit</c>(<paramref name="bitmap"/>, 0, point) (<see cref="Es1PanelCanvas.Blit"/>).</summary>
	public void Blit(DynamixBitmap? bitmap, int x, int y) => Canvas.Blit(bitmap, x, y);
}
