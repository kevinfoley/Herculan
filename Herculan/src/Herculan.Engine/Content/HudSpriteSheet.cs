using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Render;

namespace Herculan.Engine.Content;

/// <summary>
/// One HUD sprite: where it sits in the shared atlas, and how big it is in cockpit pixels.
/// </summary>
/// <param name="Scale">
/// How many cockpit pixels one of the sprite's own pixels covers - 1 for a bank that ships in
/// <c>hba</c> and 2 for one that only ships in <c>dba</c>. DBSIM does the same doubling, guarded
/// on <c>VideoMode_PanelMode == 3 && VideoMode_UseHiResBanks == 0</c>, wherever it blits a
/// bank whose hi-res half does not exist.
/// </param>
public readonly record struct HudSprite(AtlasRect Rect, int Width, int Height, int Scale = 1);

/// <summary>
/// Every HUD sprite the cockpit draws, from several <c>.HBA</c> banks, packed into one atlas so the
/// whole HUD costs a single texture bind.
///
/// <para><b>Why <c>hba</c> and not <c>dba</c>.</b> The game ships each HUD sprite bank twice under the
/// same name — <c>dba\NAME.DBA</c> for its 320-wide mode and <c>hba\NAME.HBA</c> for its 640-wide one,
/// the latter being exactly twice the former on both axes, frame for frame. DBSIM picks between them
/// at load time off a video-mode global, with the literal folder names sitting adjacent to each bank
/// name in its .rdata (<c>"NAME\0hba\0dba\0"</c>). The same <c>d</c>/<c>h</c> split runs through the
/// whole resource set: <c>db0</c>/<c>hb0</c> canopy art, <c>dfn</c>/<c>hfn</c> fonts, <c>edg</c>/
/// <c>hdg</c> clip regions. This engine renders the 640x480 <c>.HB0</c> canopy, so it takes the <c>hba</c> half
/// throughout — mixing the two would put half-scale sprites on full-scale art.</para>
///
/// <para>That same video mode is what <see cref="CockpitArt.GauToPixelScale"/> is: the original's
/// hardcoded widget coordinates appear in the disassembly as <c>value &lt;&lt; (shift &amp; 0x1f)</c>
/// against per-axis resolution-shift globals, so <c>.GAU</c> coordinates are authored in the 320-wide
/// space and doubled for the 640-wide one.</para>
///
/// <para>Index 0 decodes to alpha 0 here, meaning "leave the console art showing through". The canopy
/// frames in <see cref="CockpitArt"/> no longer use index 0 for anything: their cutout comes from the
/// herc's own <see cref="CockpitClipRegions"/> data, so black console detail stays opaque there.</para>
///
/// <para><b>Some banks do sit inside the per-herc cockpit scheme window.</b> Most author in the
/// 12-35 band, which the theater palette owns outright, but <c>WPN_DMG</c> is 1552/1568 pixels of
/// index 46 in frame 0 and <c>PWEAPONS</c> frame 1 is entirely index 42 — both inside slots 42-65,
/// which <see cref="CockpitPalette"/> replaces per herc. That is deliberate: those plates are meant
/// to match the console they sit on.</para>
/// </summary>
public sealed class HudSpriteSheet {
	/// <summary>Resource folder for the 640-wide sprite banks, and their extension.</summary>
	public const string ResourceFolder = "hba";

	/// <summary>
	/// Resource folder for the 320-wide banks, used only for the handful that have no <c>hba</c> half
	/// at all - <c>FLYERS</c> is one. A sprite from one of these reports <see cref="HudSprite.Scale"/>
	/// 2 so it still covers the cockpit pixels it was authored to cover.
	/// </summary>
	public const string LoResResourceFolder = "dba";

	private readonly Dictionary<string, Bank> _banks;
	private readonly Dictionary<string, HudFont> _fonts;
	private readonly Dictionary<string, DynamixBitmap[]> _indexed;

	private HudSpriteSheet(TextureAtlas atlas, Dictionary<string, Bank> banks, Dictionary<string, HudFont> fonts,
			Dictionary<string, DynamixBitmap[]> indexed) {
		Atlas = atlas;
		_banks = banks;
		_fonts = fonts;
		_indexed = indexed;
	}

	/// <summary>Every loaded bank's frames, packed together. Upload once, bind once.</summary>
	public TextureAtlas Atlas { get; }

	/// <summary>Names of the banks that actually loaded, in request order.</summary>
	public IEnumerable<string> BankNames => _banks.Keys;

	/// <summary>
	/// Sprite <paramref name="frame"/> of <paramref name="bank"/>, or null when that bank never
	/// loaded or the frame index is out of its range. Callers draw nothing on null rather than
	/// falling back to another frame — a missing sprite should look missing, not wrong.
	/// </summary>
	public HudSprite? Sprite(string bank, int frame) {
		if (!_banks.TryGetValue(bank, out var entry)
			|| frame < 0 || frame >= entry.FrameSizes.Length
			|| Atlas.Frame(entry.FirstAtlasFrame + frame) is not { } rect) {
			return null;
		}

		var (width, height) = entry.FrameSizes[frame];
		return new HudSprite(rect, width, height, entry.Scale);
	}

	/// <summary>
	/// How many frames <paramref name="bank"/> holds, or zero when it never loaded. The heading tape
	/// needs this: its scroll maps a whole turn onto the whole bank, so the bank's own length is the
	/// scale — the original reads it out of the loaded resource the same way.
	/// </summary>
	public int FrameCount(string bank) =>
		_banks.TryGetValue(bank, out var entry) ? entry.FrameSizes.Length : 0;

	/// <summary>
	/// One frame of a bank loaded with <c>keepIndexed</c>, still as the palette indices the file
	/// holds. The atlas has already resolved those to RGBA, which loses the identity a paper-doll
	/// tint needs: it repaints only the pixels holding one particular index (see
	/// <see cref="PaperDollDamage"/>), a question RGB cannot answer once two palette slots share a
	/// colour.
	/// </summary>
	public readonly record struct IndexedFrame(int Width, int Height, byte[] Pixels);

	/// <inheritdoc cref="IndexedFrame"/>
	public IndexedFrame? Indexed(string bank, int frame) {
		if (!_indexed.TryGetValue(bank, out var frames) || frame < 0 || frame >= frames.Length
			|| frames[frame] is not { ImageData: { } pixels } image
			|| image.Cols <= 0 || image.Rows <= 0
			|| pixels.Length < image.Cols * image.Rows) {
			return null;
		}

		return new IndexedFrame(image.Cols, image.Rows, pixels);
	}

	/// <summary>
	/// A loaded HUD font's metrics, or null when that font was not requested or could not be read.
	/// Its glyphs are packed into <see cref="Atlas"/> alongside the sprite banks and are addressed
	/// through <see cref="Sprite"/> under the font's own name, so text costs no extra texture bind.
	/// </summary>
	public HudFont? Font(string name) => _fonts.TryGetValue(name, out var font) ? font : null;

	/// <summary>
	/// Loads and packs the named sprite banks out of <c>hba\</c> and the named fonts out of
	/// <c>hfn\</c>. Banks and fonts that are missing from the mounted archives are skipped, so a
	/// partial set still draws what it has; returns null only when nothing loaded at all.
	///
	/// <para>Fonts share the sprite atlas rather than getting one of their own: a glyph is an indexed
	/// bitmap of exactly the same shape as a sprite frame, so one bank entry per font — its glyphs in
	/// character order — makes <see cref="Sprite"/> address glyphs as well.</para>
	/// </summary>
	/// <param name="resourceFolder">
	/// Folder and extension the banks are read from. Defaults to <see cref="ResourceFolder"/>; the
	/// shell keeps its own banks in <c>dba\</c> at full canvas scale rather than as a 320-wide half,
	/// so it passes that and takes no doubling with it (see <see cref="Shell.ShellArt"/>).
	/// </param>
	/// <param name="fontFolder">Folder the fonts are read from — see <see cref="HudFont.Load"/>.</param>
	public static HudSpriteSheet? Load(GameContent content, DynamixPalette? palette,
			IEnumerable<string> bankNames, IEnumerable<string>? fontNames = null,
			IEnumerable<string>? loResBankNames = null, IEnumerable<string>? indexedBankNames = null,
			string? resourceFolder = null, string? fontFolder = null) {
		resourceFolder ??= ResourceFolder;
		var frames = new List<DynamixBitmap>();
		var banks = new Dictionary<string, Bank>(StringComparer.OrdinalIgnoreCase);
		var fonts = new Dictionary<string, HudFont>(StringComparer.OrdinalIgnoreCase);
		var indexed = new Dictionary<string, DynamixBitmap[]>(StringComparer.OrdinalIgnoreCase);
		var keepIndexed = new HashSet<string>(indexedBankNames ?? Array.Empty<string>(),
			StringComparer.OrdinalIgnoreCase);

		foreach (string name in bankNames) {
			if (banks.ContainsKey(name)) {
				continue;
			}

			if (content.Read(resourceFolder, name + "." + resourceFolder.ToUpperInvariant()) is not { } bytes
				|| new DynamixBitmapArrayTransformer().Parse(bytes) is not DynamixBitmapArray bank
				|| bank.Images is not { Length: > 0 } images) {
				continue;
			}

			banks[name] = new Bank(frames.Count, SizesOf(images));
			if (keepIndexed.Contains(name)) {
				indexed[name] = images;
			}

			frames.AddRange(images);
		}

		// Banks the game only ships at 320-wide. Loaded at their own size and marked to draw doubled,
		// which is the original's own path for the same files.
		foreach (string name in loResBankNames ?? Array.Empty<string>()) {
			if (banks.ContainsKey(name)) {
				continue;
			}

			if (content.Read(LoResResourceFolder, name + "." + LoResResourceFolder.ToUpperInvariant()) is not { } loResBytes
				|| new DynamixBitmapArrayTransformer().Parse(loResBytes) is not DynamixBitmapArray loResBank
				|| loResBank.Images is not { Length: > 0 } loResImages) {
				continue;
			}

			banks[name] = new Bank(frames.Count, SizesOf(loResImages), Scale: 2);
			frames.AddRange(loResImages);
		}

		foreach (string name in fontNames ?? Array.Empty<string>()) {
			if (banks.ContainsKey(name) || HudFont.Load(content, name, fontFolder) is not { } font) {
				continue;
			}

			var glyphs = font.Glyphs.ToArray();
			banks[name] = new Bank(frames.Count, SizesOf(glyphs));
			fonts[name] = font;
			frames.AddRange(glyphs);
		}

		if (frames.Count == 0) {
			return null;
		}

		// One combined bank so the existing shelf packer does the work — the atlas has no notion of
		// which source bank a frame came from, which is exactly what the FirstAtlasFrame offsets are for.
		var combined = new DynamixBitmapArray { Images = frames.ToArray() };
		return TextureAtlas.Build(combined, palette, transparentIndex0: true) is { } atlas
			? new HudSpriteSheet(atlas, banks, fonts, indexed)
			: null;
	}

	private static (int Width, int Height)[] SizesOf(IReadOnlyList<DynamixBitmap> images) {
		var sizes = new (int Width, int Height)[images.Count];
		for (int i = 0; i < images.Count; i++) {
			sizes[i] = (images[i]?.Cols ?? 0, images[i]?.Rows ?? 0);
		}

		return sizes;
	}

	private readonly record struct Bank(int FirstAtlasFrame, (int Width, int Height)[] FrameSizes, int Scale = 1);
}
