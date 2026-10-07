using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The scrap dialog's two buttons, in the order its builder constructs them.</summary>
public enum ShellScrapDialogButton {
	Cancel,
	Accept,
}

/// <summary>
/// The two <c>WARNING</c> dialogs: what scrapping something yields, and <c>CANCEL</c> and <c>ACCEPT</c>.
/// <see cref="Herc"/> is the one the build and repair screens' SCRAP opens on the selected bay's machine —
/// built once at startup by <c>ScrapDialog_Build</c> (<c>00447328</c>), put up by <c>ScrapDialog_Show</c>
/// (<c>00447711</c>) and taken down by <c>ScrapDialog_Hide</c> (<c>00447795</c>) — and
/// <see cref="Weapons"/> its twin, the armory's, on the lit weapon's whole stock: <c>WeaponScrapDialog_Build</c>
/// (<c>004477ae</c>), <c>_Show</c> (<c>00447b97</c>) and <c>_Hide</c> (<c>00447c1d</c>). The twin differs
/// only in its caption. See docs/retail/shell/screen-layout.md, "The scrap dialog".
///
/// <para>The rects are the builder's literals. The panel is placed in a window the size of the whole
/// display, so its rect is a canvas rect; everything else is in the panel.</para>
///
/// <para>HERCULAN treats this dialog as modal, a divergence from retail.</para>
/// </summary>
public sealed class ShellScrapDialog {
	/// <summary>The machine scrap dialog, captioned <c>0xcc</c> <c>This herc will yield</c>.</summary>
	public static ShellScrapDialog Herc() => new(HercCaptionText);

	/// <summary>The weapon scrap dialog, captioned <c>0xce</c> <c>These weapons will yield</c>.</summary>
	public static ShellScrapDialog Weapons() => new(WeaponsCaptionText);

	private readonly int _captionText;

	private ShellScrapDialog(int captionText) => _captionText = captionText;

	/// <summary>The panel, an <c>ESAlert</c> (<c>ESAlert_Ctor</c>, <c>0040afe0</c>) — a <c>TitledPanel</c> subclass — in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0xcb, 199, 0x1af, 0x149);

	/// <summary>Two framed panels, the second inside the first's rect, both children of the panel.</summary>
	private static readonly ShellRect OuterFrameRect = new(5, 0x18, 0xdf, 0x60);
	private static readonly ShellRect InnerFrameRect = new(10, 0x20, 0xd8, 0x58);

	/// <summary>The caption over the figure, both centred.</summary>
	private static readonly ShellRect CaptionRect = new(0xc, 0x2e, 0xd6, 0x3b);
	private static readonly ShellRect ValueRect = new(0xc, 0x3c, 0xd6, 0x49);

	private static readonly ShellRect CancelRect = new(10, 0x6a, 0x6c, 0x79);
	private static readonly ShellRect AcceptRect = new(0x76, 0x6a, 0xd8, 0x79);

	/// <summary>Whether the dialog is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// What ACCEPT scraps: the bay the machine dialog was opened on, the one <c>SelectedBaySlot</c> (<c>00482ae5</c>) held, or
	/// the weapon id the weapon dialog was, which <c>WeaponScrapDialog_Show</c> keeps in <c>DAT_0048d970</c>.
	/// </summary>
	public int Subject { get; private set; } = -1;

	/// <summary>
	/// The figure the dialog quotes, in tons: <c>Herc_ScrapValueTons</c> (<c>0041140f</c>) of the machine,
	/// or <c>Armory_ScrapValueTons</c> (<c>0041266a</c>) of the weapon.
	/// </summary>
	public int YieldTons { get; private set; }

	/// <summary>The show: the figure written and the dialog put up.</summary>
	public void Open(int subject, int yieldTons) {
		Subject = subject;
		YieldTons = yieldTons;
		IsOpen = true;
	}

	/// <summary>
	/// The hide. <c>CANCEL</c> does this and nothing else — <c>ScrapDialog_OnCancel</c> (<c>00447c38</c>)
	/// and <c>WeaponScrapDialog_OnCancel</c> (<c>00447d59</c>).
	/// </summary>
	public void Close() => IsOpen = false;

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellScrapDialogButton button) =>
		Inside(PanelRect, button == ShellScrapDialogButton.Cancel ? CancelRect : AcceptRect);

	/// <summary>A button under a canvas point, or null — a click anywhere else is swallowed, as retail's full-display window swallows it (docs/retail/shell/screen-layout.md, "The scrap dialog").</summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellScrapDialogButton>()) {
			if (ButtonRect(button).Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.ScrapDialogButton, (int)button),
					ButtonRect(button), canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the dialog over whatever <paramref name="surface"/> holds. The panel's body is dithered in
	/// <c>0x10</c> rather than filled, so the screen shows through it at half strength.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites) {
		if (!IsOpen) {
			return;
		}

		var font = sprites?.Font(ShellArt.ScreenFont);
		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, PanelBodyDither, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, OuterFrameRect), FrameBorder, FrameFace, fill: true);
		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, InnerFrameRect), FrameBorder, FrameFace, fill: true);

		ShellChrome.PaintText(surface, Inside(PanelRect, CaptionRect), font, text?.Text(_captionText),
			ShellTextAlign.Center, ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(PanelRect, ValueRect), font, $"{YieldTons} {text?.Text(TonsOfSalvageText)}",
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		foreach (var button in Enum.GetValues<ShellScrapDialogButton>()) {
			var rect = ButtonRect(button);
			ShellChrome.PaintButton(surface, rect, ButtonBorder);
			ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 1, rect.Y0, rect.X1, rect.Y1), font,
				text?.Text(button == ShellScrapDialogButton.Cancel ? CancelText : AcceptText), ShellTextAlign.Center,
				ShellChrome.FontInkColor);
		}
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The panel as the constructor and the builder leave it: header 20 tall, face and plate written over the class's.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const byte PanelBodyDither = 0x10;
	private const int TitleHeight = 0x14;
	private const int TitlePlateFirst = 0x3f;
	private const int TitlePlateLast = 0xa6;

	/// <summary><c>ESRegionFill_Ctor</c>'s border argument, and the class's own <c>0x25</c> checkerboard over a filled body.</summary>
	private const byte FrameBorder = 0x15;
	private const byte FrameFace = 0x25;

	private const byte ButtonBorder = 0x22;

	/// <summary><c>estext.bin</c> indices the dialog prints.</summary>
	private const int TitleText = 0xc9;
	private const int CancelText = 0xca;
	private const int AcceptText = 0xcb;
	private const int HercCaptionText = 0xcc;
	private const int WeaponsCaptionText = 0xce;
	private const int TonsOfSalvageText = 0xcd;
}
