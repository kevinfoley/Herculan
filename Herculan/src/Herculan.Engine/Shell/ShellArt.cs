using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>One decoded shell bitmap, RGBA8, top row first — CPU-side, no GL.</summary>
public sealed record ShellImage(byte[] Pixels, int Width, int Height);

/// <summary>
/// Everything the shell draws with: its palette, the backdrop its screens sit on, its button art and
/// its fonts. The counterpart to <see cref="CockpitArt"/> on the other side of the two-process split
/// (see docs/retail/shell/campaign-loop.md) — VSHELL and DBSIM share the resource container formats and
/// share almost nothing else.
///
/// <para><b>The shell's <c>dba\</c> is not DBSIM's <c>dba\</c>.</b> In the simulator archives that
/// folder holds the 320-wide half of each sprite bank, with <c>hba\</c> carrying the 640-wide half
/// (<see cref="HudSpriteSheet"/>). <c>SHELL0.VOL</c> ships one set only, and its screens are authored
/// at 640x480 — the full-screen panel rect is <c>{0, 0, 0x27f, 0x1df}</c> — so shell art is drawn at
/// its own size with no doubling. The same goes for <c>dfn\</c>: the shell has no <c>hfn\</c> half,
/// and its fonts are the text at canvas scale rather than a half-scale set to double.</para>
///
/// <para><b>The palette comes from the table, by index.</b> VSHELL selects one with
/// <c>Shell_InstallPalette(index)</c> (<c>004075b2</c>) into the pointer table at <c>0046dcdc</c>, and that table is decoded —
/// see <see cref="ShellPalette"/>, which also carries which screen picks which. Index 1,
/// <c>dpl\palette.dpl</c>, is what the shell installs on entry (<c>EsGlobal_Init</c>,
/// <c>004073bc</c>) and what the service bay re-installs whenever it is shown (<c>0043b23d</c>),
/// hence <see cref="DefaultPaletteName"/>. Pass another name to <see cref="Load"/> to draw the frame
/// through one of the other nineteen.</para>
/// </summary>
public sealed class ShellArt {
	/// <summary>
	/// Entry <see cref="ShellPalette.ServiceBay"/> of the palette table: the one the shell installs on
	/// entry and the one the bay, the main menu and the save screen are drawn through.
	/// </summary>
	public static readonly string DefaultPaletteName = ShellPalette.Names[ShellPalette.ServiceBay];

	/// <summary>
	/// The backdrop every tab screen's root widget is textured with, loaded once by the shell's global
	/// init into <c>ShellBackdropBitmap</c> (<c>0046dcd4</c>) and handed to each screen builder as its root's image.
	/// </summary>
	public const string BackdropName = "BAY2A_84";

	/// <summary>Resource folder for the shell's sprite banks, and their extension.</summary>
	public const string BankFolder = "dba";

	/// <summary>Resource folder for the shell's fonts, and their extension.</summary>
	public const string FontFolder = "dfn";

	/// <summary>Resource folder for the shell's single-frame bitmaps.</summary>
	public const string BitmapFolder = "dbm";

	/// <summary>
	/// The bank every framed button on the tab strip draws: frame 1 unlit, frame 2 lit, frame 3 the
	/// disabled plate every button on the strip shares.
	/// </summary>
	public const string ButtonBank = "MNU_BTTN";

	/// <summary>The two-frame bank the strip's leftmost square button draws instead.</summary>
	public const string MenuButtonBank = "ONLINE";

	/// <summary>
	/// Banks loaded for the shell frame. Screen-specific banks are loaded by their screens. The archive's
	/// <c>CURSOR</c> bank is not among them: the shell's pointer is the Windows arrow — see
	/// docs/retail/shell/screen-layout.md#the-pointer.
	/// </summary>
	public static readonly string[] BankNames = { ButtonBank, MenuButtonBank };

	/// <summary>
	/// The three fonts the shell's global init loads and keeps for the whole session — <c>FONT2</c>
	/// twice into two separate handles (<c>ShellFont2HandleA</c> and <c>ShellFont2HandleB</c>) and <c>BLACK</c> into a
	/// third (<c>0046dccc</c>), which is the one every tab caption is drawn in. <c>FONT</c> is in the
	/// archive and is loaded here alongside them, though the init does not ask for it.
	/// </summary>
	public static readonly string[] FontNames = { "FONT", "FONT2", "BLACK" };

	/// <summary>The font tab captions are drawn in — <c>ShellButtonFontHandle</c> (<c>0046dccc</c>), the handle each strip button is given.</summary>
	public const string ButtonFont = "BLACK";

	/// <summary>
	/// The font every tab screen's own content is drawn in. The shell's init loads <c>font2.dfn</c>
	/// twice, into <c>ShellFont2HandleA</c> (<c>0046dcc4</c>) and <c>ShellFont2HandleB</c> (<c>0046dcc8</c>), and a screen builder hands out both — the save
	/// screen gives its buttons the first and its title, slot rows and readouts the second — so the two
	/// handles are one typeface and there is nothing to choose between them.
	/// </summary>
	public const string ScreenFont = "FONT2";

	private ShellArt(string paletteName, DynamixPalette palette, ShellImage backdrop,
			HudSpriteSheet? sprites, ShellText? text) {
		PaletteName = paletteName;
		Palette = palette;
		Backdrop = backdrop;
		Sprites = sprites;
		Text = text;
	}

	/// <summary>
	/// Which <c>dpl\</c> entry everything here was decoded through. The art is decoded once per palette
	/// rather than re-mapped per frame, so switching palettes means loading a second
	/// <see cref="ShellArt"/> — this is what says whether that is needed.
	/// </summary>
	public string PaletteName { get; }

	/// <summary>The palette every image and glyph here was decoded through.</summary>
	public DynamixPalette Palette { get; }

	/// <summary>
	/// The backdrop, at its own size — 640x480 in the retail archive, exactly the canvas, so it lands
	/// as one quad covering the screen.
	/// </summary>
	public ShellImage Backdrop { get; }

	/// <summary>
	/// The shell's sprite banks and fonts, packed into one atlas so the whole frame costs a single
	/// texture bind. Null when none of them could be loaded, in which case the backdrop draws alone.
	///
	/// <para>Palette index 0 decodes to alpha 0 in everything packed here, the sentinel role it plays
	/// in the simulator's banks too. It is the reading the shell's own art is assumed to share; a plate
	/// that spent index 0 as ink rather than as a cutout would show the backdrop through itself.</para>
	/// </summary>
	public HudSpriteSheet? Sprites { get; }

	/// <summary>The shell's UI text, or null when <c>LANG0.VOL</c> is not mounted.</summary>
	public ShellText? Text { get; }

	/// <summary>
	/// Loads the shell's shared art. Returns null when the palette or the backdrop is missing, since
	/// neither has a sensible substitute; a missing bank, font or string table is survivable and comes
	/// back as a null property instead.
	/// </summary>
	public static ShellArt? Load(GameContent content, string? paletteName = null) {
		string name = paletteName ?? DefaultPaletteName;
		if (ReadPalette(content, name) is not { } palette
			|| LoadImage(content, BitmapFolder, BackdropName + ".DBM", palette) is not { } backdrop) {
			return null;
		}

		return new ShellArt(name, palette, backdrop,
			HudSpriteSheet.Load(content, palette, BankNames, FontNames,
				resourceFolder: BankFolder, fontFolder: FontFolder),
			ShellText.Load(content));
	}

	/// <summary>
	/// Every frame of one <c>dba\</c> bank as indexed bitmaps, for the widgets that paint into a
	/// <see cref="ShellSurface"/> rather than through the atlas. Null when the bank is missing.
	/// </summary>
	public static DynamixBitmap[]? ReadBankFrames(GameContent content, string name) =>
		content.Read(BankFolder, name + ".DBA") is { } bytes
			&& new DynamixBitmapArrayTransformer().Parse(bytes) is DynamixBitmapArray { Images: { } images }
			? images : null;

	/// <summary>
	/// One <c>dbm\</c> bitmap decoded through this art's palette, as the backdrop is, or null when it is
	/// missing — the startup sequence's frames (<see cref="ShellStartupSequence.FrameNames"/>).
	/// </summary>
	public ShellImage? LoadBitmap(GameContent content, string name) =>
		LoadImage(content, BitmapFolder, name + ".DBM", Palette);

	/// <summary>
	/// One frame of a <c>dba\</c> bank decoded through this art's palette, or null when the bank or the
	/// frame is missing — the mission screen's location picture.
	/// </summary>
	public ShellImage? LoadBankFrame(GameContent content, string name, int frame) =>
		ReadBankFrames(content, name) is { } frames && frame >= 0 && frame < frames.Length
			? Decode(frames[frame], Palette)
			: null;

	private static DynamixPalette? ReadPalette(GameContent content, string name) =>
		content.Read("dpl", name + ".DPL") is { } bytes
			? new DynamixPaletteTransformer().Parse(bytes) as DynamixPalette
			: null;

	/// <summary>
	/// Reads and decodes one <c>.DBM</c> through <paramref name="palette"/>. Index 0 stays opaque:
	/// a shell bitmap is a background plate, not a sprite with a cutout, so there is nothing behind it
	/// for a transparent index to reveal.
	/// </summary>
	private static ShellImage? LoadImage(GameContent content, string folder, string name, DynamixPalette palette) {
		return content.Read(folder, name) is { } bytes && new DynamixBitmapTransformer().Parse(bytes) is DynamixBitmap image
			? Decode(image, palette)
			: null;
	}

	private static ShellImage? Decode(DynamixBitmap image, DynamixPalette palette) {
		if (image.Cols <= 0 || image.Rows <= 0) {
			return null;
		}

		int width = image.Cols;
		int height = image.Rows;
		var pixels = new byte[width * height * 4];
		byte[] indices = image.ImageData ?? Array.Empty<byte>();
		int count = Math.Min(indices.Length, width * height);

		for (int i = 0; i < count; i++) {
			int index = indices[i];
			var color = palette.Colors.TryGetValue(index, out var entry)
				? entry.GetColor()
				: new RgbaColor(255, (byte)index, (byte)index, (byte)index);

			pixels[i * 4] = color.R;
			pixels[i * 4 + 1] = color.G;
			pixels[i * 4 + 2] = color.B;
			pixels[i * 4 + 3] = 255;
		}

		return new ShellImage(pixels, width, height);
	}
}
