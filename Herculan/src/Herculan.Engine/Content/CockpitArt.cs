using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Content;

/// <summary>One decoded cockpit-art frame, RGBA8, top row first — CPU-side, no GL.</summary>
public sealed record CockpitFrame(byte[] Pixels, int Width, int Height) {
	/// <summary>
	/// Where this frame's shield-meter ring pixels ended up, one list of <see cref="Pixels"/> byte
	/// offsets per ramp slot (<see cref="CockpitPalette.ShieldRampFirstSlot"/> + index).
	///
	/// <para>The meter's rings are part of the canopy art, not geometry the HUD draws, and the widget
	/// animates them by rewriting six palette slots every frame. Decoding indexed art to RGBA at load
	/// bakes the palette in and loses that, so the positions are kept here and
	/// <see cref="CockpitArt.UpdateShieldRings"/> repaints them in place. Retail frames put a few
	/// hundred pixels in these six indices, so a repaint is cheap and only happens when the charge
	/// actually moves.</para>
	/// </summary>
	public int[][] ShieldRingPixels { get; init; } =
		Enumerable.Range(0, CockpitPalette.ShieldRampLength).Select(_ => Array.Empty<int>()).ToArray();

	/// <summary>
	/// The same frame expanded through the damage-flash palette instead, or null when that palette
	/// was not available. Built at load rather than on each toggle: the flash alternates several times
	/// a second, and re-expanding a 640x480 frame at that rate would be visible as a hitch.
	/// </summary>
	public byte[]? ImpactPixels { get; init; }

	/// <summary>Which buffer to upload this frame from — see <see cref="ImpactPixels"/>.</summary>
	public byte[] PixelsFor(bool flashActive) =>
		flashActive && ImpactPixels is { } impact ? impact : Pixels;
}

/// <summary>
/// A herc's cockpit canopy art and HUD widget layout — see docs/formats/cockpit-canopy-palette.md and
/// docs/formats/cockpit-hud-widgets.md (Milestone 8
/// Phase 0) for the RE and the real-data verification behind every constant here.
///
/// <para><see cref="Front"/> is <c>(herc).HB0</c> (the center/front view) and <see cref="Side"/> is
/// <c>(herc).HB2</c> (a real, distinct side view — not a duplicate of the front). There is no
/// separate mirrored asset: the left panel reuses <see cref="Side"/> with its UVs flipped
/// horizontally at draw time. <see cref="HeadsDown"/> is <c>(herc).HB1</c>, DBSIM's view 1 — the
/// Heads-Down Display below the dashboard that <c>[F7]</c>/<c>[F8]</c> pan down to.</para>
///
/// <para>Both frames decode through the live palette (<see cref="CockpitPalette"/>): the theater
/// palette in full, with this herc's own 24-entry cockpit colour scheme installed over slots 42-65.
/// Indices are used <b>as authored</b> — no shift, no offset. The per-herc canopy colour difference
/// is entirely a property of which scheme window gets installed, resolved from the herc's own data
/// file (see <see cref="ColorSchemeIndex"/>).</para>
///
/// <para><b>The 3D-viewport cutout is data, not a colour key.</b> Each herc ships a per-view region
/// file — <c>hd0</c> for the forward view, <c>hd2</c> for the side view — that states per scanline
/// exactly which columns the live 3D scene shows through; DBSIM's rasterizer is physically span-clipped
/// to it (see <see cref="CockpitClipRegions"/>). <see cref="Load"/> punches those spans into each
/// frame's alpha channel (0 = viewport hole, 255 = opaque art). The previous border-flood-fill over
/// pure-black pixels survives only as a fallback for when the region file can't be read: it inferred
/// the hole from art that happens to be black there, which is close but not the same thing, and it had
/// no way to know about the rows the original leaves opaque despite their colour.</para>
/// </summary>
public sealed class CockpitArt {
	/// <summary>
	/// GAU widget coordinates address a 320x400 logical space; cockpit art is 640x480 pixels. A plain
	/// uniform 2x scale on both axes maps one onto the other — confirmed by overlaying five
	/// independently-positioned real widgets (MFD panel, shield display, throttle, chain button,
	/// energy meter) on real <c>APOCA.HB0</c> art and finding every one lands exactly on its physical
	/// console graphic at this scale, and no other tried combination (1x, 2x/1x, 2x/1.2x) fits more
	/// than one widget at once. The original's mechanism is <c>VideoMode_XCoordShift</c>
	/// (<c>004d25be</c>) and its Y twin, a shift of 1 in the 640-wide mode. See
	/// docs/formats/cockpit-hud-widgets.md, "<c>.GAU</c> widget tree".
	/// </summary>
	public const float GauToPixelScale = 2f;

	/// <summary>DBSIM's own view numbering: the forward view the console instruments belong to.</summary>
	public const int ForwardViewIndex = 0;

	/// <summary>
	/// DBSIM's own view numbering for the sideways glance whose canopy bitmap is authored (view 3
	/// reuses this same bitmap mirrored, which is what the renderer's mirror flag does).
	/// </summary>
	public const int SideViewIndex = 2;

	/// <summary>
	/// The sprite banks this milestone draws, all from <c>hba\</c> — see <see cref="HudSpriteSheet"/>
	/// for why the <c>hba</c> half and not <c>dba</c>, and <see cref="Sprites"/> for which widget each
	/// one serves.
	///
	/// <para><c>ICONS</c> is loaded here rather than where it is used, unlike the original: its own
	/// gadget constructor (<c>HddMarker_Ctor</c>) loads it lazily the first time a map marker
	/// is built. Ninety frames of nine-frame rotation groups, and the only consumer is the command
	/// display's map — see <see cref="HddMap"/>.</para>
	/// </summary>
	/// <para><c>WEAPONS</c> is the damage detail's weapon icons, which <c>PaperDoll_InitTables</c>
	/// loads once with the rest of the paper-doll data; it is kept indexed as well, because the icons
	/// are recoloured by palette index the way a doll region is (<see cref="PaperDollDamage"/>).</para>
	/// <para><c>OBJ_ALRT</c> and <c>GNL_ALRT</c> are two of the modal alert panels' plates and
	/// <c>ALERT</c> the button plate they share (<see cref="ObjectivesPanel"/>,
	/// <see cref="StatusAlertPanel"/>); <c>PRF_ALRT</c> and <c>CTL_ALRT</c> are the preferences and
	/// controls panels', which carry their own button art rather than sharing that one
	/// (<see cref="PreferencesPanel"/>, <see cref="ControlsPanel"/>).
	/// They come from SIMALERT.VOL rather than the main archive, and
	/// the original loads each when its panel is constructed rather than up front; packing them with
	/// the rest costs one atlas entry each and keeps a panel to a single texture bind.</para>
	public static readonly string[] HudBankNames = {
		"HUD", "HUDHTICK", "MFD", SensorDropout.MfdBank, "RADAR", "THROTTLE", SensorDropout.RowBank, "PWEAPONS",
		"HDD", "BASES", "VEHICLES", PaperDollDamage.WeaponIconBank,
		"ICONS", ObjectivesPanel.BackgroundBank, StatusAlertPanelLayout.PlateBank,
		AlertPanelLayout.ButtonBank, PreferencesPanelLayout.PlateBank, ControlsPanelLayout.PlateBank,
	};

	/// <summary>
	/// Banks the game ships only at 320-wide, loaded through
	/// <see cref="HudSpriteSheet.LoResResourceFolder"/> and drawn doubled.
	/// <c>MfdStatusScreen_Ctor</c> asks for <c>bases</c>, <c>vehicles</c> and <c>flyers</c> together as
	/// its target silhouettes, but only the first two exist in <c>hba</c>.
	/// </summary>
	public static readonly string[] LoResHudBankNames = { "FLYERS", SquadCommChannel.StaticBank };

	/// <summary>
	/// The <c>.HFN</c> fonts the cockpit draws text with, out of the 18 <c>ColorSchemePanels</c>
	/// (<c>0049b0ac</c>) DBSIM loads. In this format the font is the colour — each file is the same
	/// typeface stencilled in one palette index — so a widget picks its text colour by picking a font.
	/// These are the ones the widgets reached so far name: <c>WHITE</c> (shield readouts, the selected
	/// weapon's name, an idle hardpoint digit), <c>GRAY</c> (an unselected weapon's name and its round
	/// count), <c>GREEN</c> (the selected weapon's round count), <c>DARK</c> (a lit hardpoint digit),
	/// and <c>HUD1</c>/<c>HUD2</c>/<c>HUD3</c>, the three theater-coloured fonts the gunsight
	/// complex's readouts use.
	/// </summary>
	/// <para><c>CPGREEN</c> is <c>ColorSchemePanels[1]</c>, the font the MFD's FLASH COMM screen lists
	/// its squadmate orders in, with <c>CPRED</c> (<c>[2]</c>) as each row's alternate for its hotkey
	/// character and <c>CPOFF</c> (<c>[6]</c>) for a row the squad cannot take — see
	/// <see cref="MfdFlashCommScreen"/>, whose remarks say why no retail row ever reads the last.</para>
	/// <para><c>CPON</c> and <c>CPPRESS</c> are <c>[4]</c> and <c>[5]</c>, which the Heads-Down
	/// Display's XMIT and CANCEL buttons caption themselves in unlit and lit
	/// (<see cref="HddLayout.TransmitButtonFont"/>); <c>CPYLW</c> is <c>[3]</c>, that display's
	/// subject caption.</para>
	/// <para><c>CPGREY</c> and <c>CPORANGE</c> are <c>[7]</c> and <c>[9]</c>, two of the five a damage
	/// detail row cycles through as its component is worn down — see
	/// <see cref="PaperDollDamage.RowFont"/>.</para>
	/// <para><c>TITLE</c>, <c>ACTIVE</c>, <c>PUSHED</c>, <c>INACTIVE</c> and <c>GREEN6X8</c> are five
	/// of the seven <c>CockpitFontsAndCorners_Init</c> (<c>004544a4</c>) loads outside the
	/// <c>ColorSchemePanels</c> set, and they are the alert family's. A panel titles itself in the
	/// first and captions its buttons in the next three — at rest, held and disabled, which is
	/// <c>PanelButton_Ctor</c>'s own four-entry table. The last is both the status alert's body and
	/// the preferences and controls panels' value readouts
	/// (<see cref="PreferencesPanelLayout.ValueFont"/>).</para>
	public static readonly string[] HudFontNames = {
		"WHITE", "GRAY", "GREEN", "DARK", "RED", "HUD1", "HUD2", "HUD3",
		"CPGREEN", "CPRED", "CPON", "CPPRESS", "CPYLW", "CPBLUE", "CPORANGE", "CPGREY", "CPOFF",
		AlertPanelLayout.TitleFont, AlertPanelLayout.ButtonFont, AlertPanelLayout.ButtonPressedFont,
		AlertPanelLayout.DisabledButtonFont, StatusAlertPanelLayout.BodyFont,
	};

	/// <summary>
	/// Everything this class resolves out of <i>one</i> palette: the pre-resolved tuples a widget that
	/// names a fixed id or index wants, and the two tables a widget that picks at draw time reads.
	///
	/// <para>Two of these are built — one against the live palette, one against the damage-flash
	/// palette — and <see cref="FlashActive"/> picks between them. Retail's flash swaps the whole
	/// palette, and these colours are its HUD half: in eight of the ten theaters most of
	/// <c>COLORS.DAT</c>'s twenty-seven entries move, most of them a long way, so a HUD that kept its
	/// own colours through a flash would be the one part of the screen visibly refusing to. The
	/// per-theater counts are in docs/formats/cockpit-canopy-palette.md, "The damage shake".</para>
	/// </summary>
	private sealed record PaletteColors(
		(Vector3, Vector3, Vector3)? Gauge,
		(Vector3, Vector3)? HeadsDown,
		(Vector3, Vector3)? TargetArrow,
		(Vector3, Vector3) WeaponBar,
		Vector3?[]? Logical,
		Vector3?[] Entries);

	private readonly PaletteColors _colors;
	private readonly PaletteColors? _flashColors;

	/// <summary>
	/// Whether the HUD draws through the damage-flash palette. The host sets it from
	/// <c>CockpitHitShake.FlashActive</c>; it does nothing when the theater supplied no impact
	/// palette. See docs/formats/cockpit-canopy-palette.md, "The damage shake".
	/// </summary>
	public bool FlashActive { get; set; }

	private PaletteColors Live => FlashActive && _flashColors is { } flash ? flash : _colors;

	private CockpitArt(CockpitFrame front, CockpitFrame side, CockpitFrame? headsDown, GAUFile gau, HudSpriteSheet? sprites,
			HudColorTable? colors, PaletteColors resolved, PaletteColors? flashResolved,
			int colorSchemeIndex, bool clipRegionsLoaded, string hercName, StringFile? strings) {
		Front = front;
		Side = side;
		HeadsDown = headsDown;
		Gau = gau;
		Sprites = sprites;
		Colors = colors;
		_colors = resolved;
		_flashColors = flashResolved;
		ColorSchemeIndex = colorSchemeIndex;
		ClipRegionsLoaded = clipRegionsLoaded;
		HercName = hercName;
		Strings = strings;
	}

	/// <summary>
	/// The herc this cockpit belongs to, which is also the name of its own sprite bank — the paper-doll
	/// wireframe the MFD's status screen draws comes from <c>hba\&lt;HERC&gt;.HBA</c>.
	/// </summary>
	public string HercName { get; }

	/// <summary>
	/// <c>str\STRINGS0.STR</c>, the simulator's UI text — every caption and readout label the cockpit
	/// prints, including the MFD's screen titles and button captions. Null when the resource is
	/// missing, in which case text-bearing widgets draw their art and no words.
	/// </summary>
	public StringFile? Strings { get; }

	/// <summary>
	/// The herc's <c>pdg\&lt;HERC&gt;.PDG</c> damage diagram — three views, each an origin/size pair
	/// and a list of body regions. The MFD's status screen positions its wireframe by the view's
	/// origin; the regions are what the original tints per body part as damage lands. Null when the
	/// file is missing or does not parse, in which case no wireframe is drawn.
	/// </summary>
	public PaperDollGraphic? PaperDoll { get; private init; }

	/// <summary>
	/// The same diagram for every machine in the mission, keyed by upper-case herc name - what F5
	/// needs, since the status screen draws the <i>subject's</i> doll and the subject there is the
	/// current target. The player's own entry is in here too and <see cref="PaperDoll"/> is its alias.
	/// </summary>
	public IReadOnlyDictionary<string, PaperDollGraphic> PaperDolls { get; private init; }
		= new Dictionary<string, PaperDollGraphic>();

	/// <summary>
	/// <c>pdg\WEAPONS.PDG</c>, the sizes of the <see cref="PaperDollDamage.WeaponIconBank"/> frames
	/// the damage detail anchors its weapon icons by. Null when it is missing, in which case no icon
	/// is drawn.
	/// </summary>
	public WeaponPaperDiagram? WeaponIconSizes { get; private init; }

	/// <summary>This herc's diagram, or another machine's by name. Null when that file was missing.</summary>
	public PaperDollGraphic? PaperDollFor(string? hercName) =>
		hercName != null && PaperDolls.TryGetValue(hercName.ToUpperInvariant(), out var doll) ? doll : null;

	/// <summary>
	/// Where this herc's cockpit views sit in the cockpit canvas — <c>vue\&lt;HERC&gt;.VUE</c>. Null
	/// when the file is missing, in which case callers fall back to
	/// <see cref="CockpitViewGeometry.DefaultHeadsDownOriginY"/>. Loaded here because
	/// <see cref="HeadsDown"/>'s own widget layout is expressed against view 1's canvas origin.
	/// </summary>
	public CockpitViewGeometry? ViewGeometry { get; private init; }

	/// <summary>
	/// The Heads-Down Display's widget layout, from the herc's own <c>.GAU</c> — see
	/// <see cref="HddLayout"/>. Null when the block could not be read, in which case the pan still
	/// reaches <see cref="HeadsDown"/> but nothing is drawn over its art.
	/// </summary>
	public HddLayout? HeadsDownLayout { get; private init; }

	/// <summary>
	/// The two flat colours the Heads-Down Display fills with: colour id 19 for every screen and label
	/// background, and id 15 for the small indicator block beside the title. Null when
	/// <c>COLORS.DAT</c> is missing, in which case the display draws its sprites and text and floods
	/// nothing — better than flooding a colour of the engine's own choosing over the art.
	/// </summary>
	public (Vector3 Background, Vector3 Indicator)? HeadsDownColors => Live.HeadsDown;

	/// <summary>
	/// The two colours the front-window HUD's off-screen target arrow is filled with, unlocked then
	/// locked - <see cref="TargetBox.ArrowColorId"/> and <see cref="TargetBox.ArrowLockedColorId"/>
	/// through <c>COLORS.DAT</c>, green and red. The arrow is a flat polygon rather than a sprite,
	/// which is why it needs a resolved colour at all.
	/// </summary>
	public (Vector3 Unlocked, Vector3 Locked)? TargetArrowColors => Live.TargetArrow;


	/// <summary>
	/// Which of <c>COCKPIT.DPL</c>'s nine 24-entry cockpit colour schemes this herc renders through —
	/// mech type record <c>+0x52</c>, i.e. offset 80 of <c>dat\&lt;MECH&gt;.DAT</c>. See
	/// <see cref="CockpitPalette"/>. -1 when the herc's data file could not be read, in which case no
	/// scheme is installed and slots 42-65 keep the theater's own filler green.
	/// </summary>
	public int ColorSchemeIndex { get; }

	/// <summary>
	/// True when both views' cutouts came from the herc's own <c>.HD0</c>/<c>.HD2</c> region files.
	/// False means at least one fell back to inferring the hole from black pixels — worth surfacing,
	/// since the fallback is an approximation.
	/// </summary>
	public bool ClipRegionsLoaded { get; }

	/// <summary>The center/front cockpit view — console, HUD instruments, and the 3D viewport cutout.</summary>
	public CockpitFrame Front { get; }

	/// <summary>A side cockpit view — mirror horizontally at draw time for the opposite panel.</summary>
	public CockpitFrame Side { get; }

	/// <summary>
	/// The Heads-Down Display's background — <c>(herc).HB1</c>, DBSIM's view 1. Null when the file is
	/// missing, in which case the pan has nothing to pan to and the caller should stay forward.
	///
	/// <para>No 3D-viewport hole is punched into it, unlike <see cref="Front"/> and
	/// <see cref="Side"/>. That is what the data says rather than a simplification: every herc's
	/// <c>.HD1</c> region file is 16 bytes of zeroes and its <c>.VUE</c> view-1 rect is zero-size, so
	/// the heads-down view shows no live world. RAZOR is the sole exception — a 2368-byte <c>.HD1</c>
	/// and a real <c>0,0-320,181</c> viewport rect — and rendering that is left for the pass that
	/// gives the HDD live content, since a hole cut now would only expose cleared background.</para>
	/// </summary>
	public CockpitFrame? HeadsDown { get; }

	/// <summary>The HUD widget layout to overlay on <see cref="Front"/> — center panel only (see Overlay2DRenderer).</summary>
	public GAUFile Gau { get; }

	/// <summary>
	/// The HUD's own sprite art, keyed by bank name, or null when none of the banks could be loaded
	/// (in which case the renderer draws the canopy alone).
	///
	/// <para>Bank-to-widget mapping, from the loader functions the bank-name string literals xref to
	/// in DBSIM: <c>HUD</c> is the gunsight/reticle set (<c>Gau_RovingGunsightWidget</c>, 0043c7d8),
	/// <c>HUDHTICK</c> the heading tick tape (HudHeadingTape_Ctor, 0043b57c), <c>MFD</c> the multi-function display
	/// screen (MfdDisplay_Ctor (00445218), which also owns <c>MFD_DMG</c>, its sensor-dropout wipe, and <c>RADAR</c>),
	/// <c>THROTTLE</c> the slider knob (ThrottleGauge_Ctor, 00447b84), <c>PWEAPONS</c> the weapon row plates and console
	/// buttons and <c>WPN_DMG</c> the weapon rows' underlay and dropout wipe (both WeaponGauge_Ctor (0044080c)).</para>
	/// </summary>
	public HudSpriteSheet? Sprites { get; }

	/// <summary>
	/// The logical-colour-id lookup every HUD data file's colour fields go through — see
	/// <see cref="HudColorTable"/>. Null when <c>dat\COLORS.DAT</c> is missing.
	///
	/// </summary>
	public HudColorTable? Colors { get; }

	/// <summary>
	/// An LED gauge bar's three resolved colours — even-column fill, odd-column fill, and unfilled
	/// remainder (see <see cref="HudColorTable.GaugeFillEvenId"/>). Null when <c>COLORS.DAT</c> is
	/// missing, in which case gauges draw nothing rather than a colour of the engine's own choosing.
	/// </summary>
	public (Vector3 FillEven, Vector3 FillOdd, Vector3 Remainder)? GaugeColors => Live.Gauge;

	/// <summary>
	/// Every <c>COLORS.DAT</c> id resolved against the live palette, or null when the table or the
	/// palette is missing. The pre-resolved tuples above cover the widgets that name a fixed pair or
	/// triple; this is for the ones that pick an id at draw time from what they are drawing — the
	/// scanner's contacts, whose colour is a function of the contact's class and side.
	/// </summary>
	private Vector3?[]? LogicalColors => Live.Logical;

	/// <summary>
	/// One <c>COLORS.DAT</c> id as a colour, or null when the id is out of range, the table is
	/// missing, or the palette has no such slot — callers draw nothing rather than substituting.
	/// </summary>
	public Vector3? LogicalColor(int id) =>
		LogicalColors is { } table && id >= 0 && id < table.Length ? table[id] : null;

	/// <summary>Every live-palette slot as a colour — see <see cref="PaletteEntry"/>.</summary>
	private Vector3?[]? PaletteEntries => Live.Entries;

	/// <summary>
	/// <see cref="Sprites"/>' atlas expanded through the damage-flash palette, or null when the
	/// theater supplied none. The host uploads it as a second texture and binds whichever the flash
	/// is showing — see <see cref="CockpitFrame.ImpactPixels"/>, which is the canopy's half of the
	/// same arrangement.
	/// </summary>
	public byte[]? ImpactSpritePixels { get; private init; }

	/// <summary>
	/// One raw palette index as the damage flash would colour it, or null when the theater supplied no
	/// impact palette. <see cref="PaletteEntry"/> already follows <see cref="FlashActive"/>; this is
	/// for the one consumer that has to resolve <i>both</i> up front rather than per draw — the
	/// heads-down map's relief raster, which is rasterized once at load.
	/// </summary>
	public Vector3? FlashPaletteEntry(int index) =>
		_flashColors is { } flash && index >= 0 && index < flash.Entries.Length
			? flash.Entries[index]
			: null;

	/// <summary>
	/// One <b>raw</b> palette index as a colour, or null when the palette has no such slot.
	///
	/// <para>Which of this and <see cref="LogicalColor"/> a widget wants is decided by where its
	/// number came from, and the original is consistent about it: a colour that arrives in a
	/// <i>data file</i> is a logical id and is resolved through <c>COLORS.DAT</c> once at load, while
	/// a colour a <i>constructor states as an immediate</i> is already a palette index. The scanner
	/// uses both — its contact colours are read out of the resolved table at paint time, its screen
	/// background is the literal <c>0x11</c> its constructor writes — and so does the weapon panel,
	/// whose bar colours are the raw 32/34/46 (see <see cref="WeaponBarColors"/>).</para>
	/// </summary>
	public Vector3? PaletteEntry(int index) =>
		PaletteEntries is { } table && index >= 0 && index < table.Length ? table[index] : null;

	/// <summary>
	/// A weapon row's charge bar, which does <b>not</b> take its colours from <c>COLORS.DAT</c> the
	/// way <see cref="GaugeColors"/> does: <c>WeaponSliderGadget_Ctor</c> (<c>00442950</c>) overwrites the bar's three colour
	/// fields with the raw palette indices 32, 34 and 46 immediately after constructing it. So a
	/// capacitor bar is pinned to fixed palette slots, where the Master Energy Pool's meter follows
	/// whatever <c>COLORS.DAT</c> ids 6/5/19 resolve to (<see cref="HudColorTable.GaugeFillEvenId"/>).
	///
	/// <para>The override is conditional on one byte of the widget geometry the constructor is handed,
	/// which lands in the <c>.GAU</c>'s confirmed-zero padding in every retail file — so the override
	/// always applies.</para>
	/// </summary>
	public (Vector3 FillEven, Vector3 FillOdd)? WeaponBarColors => Live.WeaponBar;

	/// <summary>Palette index for the even columns of a weapon row's charge bar.</summary>
	public const int WeaponBarFillEvenIndex = 0x20;

	/// <summary>And the odd ones — one shade apart, so the pair reads as a single shaded fill.</summary>
	public const int WeaponBarFillOddIndex = 0x22;

	/// <summary>
	/// Loads and decodes one herc's cockpit art and HUD layout. Returns null when any required
	/// resource is missing from the mounted archives, in which case the caller should fall back to
	/// drawing no cockpit overlay rather than a partially-wrong one.
	/// </summary>
	/// <param name="extraLoResBankNames">
	/// Further <c>dba</c>-only banks to pack alongside <see cref="LoResHudBankNames"/>. The squad's
	/// <c>PILOT&lt;n&gt;</c> portraits go in this way: which three they are is a per-mission fact, and
	/// loading all twelve to avoid asking would cost about a megapixel of atlas for nine banks nothing
	/// will draw.
	/// </param>
	/// <param name="impactPaletteName">
	/// The theater's <c>IMPACT&lt;n&gt;.DPL</c> (<c>TheaterDescriptor.ImpactPaletteName</c>). Supplying
	/// it decodes the canopy frames a second time through the damage-flash palette so the panel can
	/// flash with the world; leaving it out costs the panel's half of the flash and nothing else. See
	/// <see cref="CockpitFrame.ImpactPixels"/>.
	/// </param>
	public static CockpitArt? Load(GameContent content, string hercName, string? worldPaletteName = null,
			IEnumerable<string>? targetHercNames = null, IEnumerable<string>? extraLoResBankNames = null,
			string? impactPaletteName = null) {
		int schemeIndex = ReadColorSchemeIndex(content, hercName);
		if (CockpitPalette.Load(content, worldPaletteName, schemeIndex) is not { } palette) {
			return null;
		}

		var impactPalette = CockpitPalette.LoadImpact(content, impactPaletteName, schemeIndex);

		byte[]? gauBytes = content.Read("gau", hercName + ".GAU");
		if (gauBytes == null || new GauFileTransformer().Parse(gauBytes) is not { } gau) {
			return null;
		}

		var front = LoadFrame(content, "hb0", hercName + ".HB0", palette, impactPalette);
		var side = LoadFrame(content, "hb2", hercName + ".HB2", palette, impactPalette);
		if (front == null || side == null) {
			return null;
		}

		// Not required: a missing HB1 costs the heads-down view, not the cockpit.
		var headsDown = LoadFrame(content, "hb1", hercName + ".HB1", palette, impactPalette);

		bool clipped = CutViewportHole(content, hercName, ForwardViewIndex, front)
			& CutViewportHole(content, hercName, SideViewIndex, side);

		// The hole is punched into the alpha channel after the expansion, and both cutout paths write
		// only the ordinary buffer — one of them by reading the art's colour, which is the ordinary
		// palette's. Carrying the result across is what keeps the flash from filling the viewport in.
		CopyAlphaToImpact(front);
		CopyAlphaToImpact(side);
		if (headsDown != null) {
			CopyAlphaToImpact(headsDown);
		}

		var colors = HudColorTable.Load(content);
		var viewGeometry = CockpitViewGeometry.Load(content, hercName);

		// The herc's own bank goes in alongside the shared ones: it holds the paper-doll wireframe
		// frames the MFD status screen draws, and its name is the herc's.
		// The herc's own bank and every other machine's go in alongside the shared ones: each holds the
		// paper-doll wireframe frames the MFD status screen draws for that machine, and the bank's name
		// is the machine's. F5 shows the target's doll, so the mission's other types are needed too.
		var hercBanks = new List<string> { hercName.ToUpperInvariant() };
		foreach (string target in targetHercNames ?? Array.Empty<string>()) {
			string upper = target.ToUpperInvariant();
			if (!hercBanks.Contains(upper)) {
				hercBanks.Add(upper);
			}
		}

		var banks = HudBankNames.Concat(hercBanks).ToArray();

		var sprites = HudSpriteSheet.Load(content, palette, banks, HudFontNames,
			LoResHudBankNames.Concat(extraLoResBankNames ?? Array.Empty<string>()),
			hercBanks.Append(PaperDollDamage.WeaponIconBank));

		return new CockpitArt(front, side, headsDown, gau,
			sprites,
			colors,
			Resolve(colors, palette),
			impactPalette != null ? Resolve(colors, impactPalette) : null,
			schemeIndex,
			clipped,
			hercName.ToUpperInvariant(),
			SimStrings.Load(content)) {
			ImpactSpritePixels = impactPalette != null && sprites != null
				? sprites.Atlas.ExpandThrough(impactPalette)
				: null,
			PaperDoll = LoadPaperDoll(content, hercName),
			PaperDolls = hercBanks
				.Select(name => (Name: name, Doll: LoadPaperDoll(content, name)))
				.Where(entry => entry.Doll != null)
				.ToDictionary(entry => entry.Name, entry => entry.Doll!, StringComparer.OrdinalIgnoreCase),
			WeaponIconSizes = content.Read("pdg", PaperDollDamage.WeaponIconBank + ".PDG") is { } iconSizes
				? new WeaponPDGTransformer().Parse(iconSizes)
				: null,
			ViewGeometry = viewGeometry,
			HeadsDownLayout = HddLayout.Load(gau,
				viewGeometry?.CanvasOriginY(CockpitViewGeometry.HeadsDownViewIndex)
					?? CockpitViewGeometry.DefaultHeadsDownOriginY),
		};
	}

	/// <summary>Every colour one palette yields — see <see cref="PaletteColors"/>.</summary>
	private static PaletteColors Resolve(HudColorTable? colors, DynamixPalette palette) =>
		new(ResolveGaugeColors(colors, palette),
			ResolveHeadsDownColors(colors, palette),
			ResolveArrowColors(colors, palette),
			(PaletteColor(palette, WeaponBarFillEvenIndex), PaletteColor(palette, WeaponBarFillOddIndex)),
			ResolveLogicalColors(colors, palette),
			ResolvePaletteEntries(palette));

	/// <summary>One machine's <c>pdg&lt;HERC&gt;.PDG</c>, or null when it is missing or unparseable.</summary>
	private static PaperDollGraphic? LoadPaperDoll(GameContent content, string hercName) =>
		content.Read("pdg", hercName + ".PDG") is { } bytes
			&& new PaperDiagramGraphTransformer().Parse(bytes) is PaperDollGraphic doll
				? doll
				: null;

	/// <summary>
	/// Reads the herc's cockpit colour-scheme index out of its own <c>dat\&lt;MECH&gt;.DAT</c>, or -1
	/// when that file is missing or unparseable. The field is
	/// <see cref="HercWorks.Core.Data.File.Dat.Sim.HercSimDat.CockpitColorScheme"/> — record offset 80,
	/// which is the mech type struct's <c>+0x52</c> that
	/// <c>CockpitViewManager_LoadViews</c> indexes <c>COCKPIT.DPL</c> with.
	/// </summary>
	private static int ReadColorSchemeIndex(GameContent content, string hercName) =>
		content.Read("dat", hercName + ".DAT") is { } bytes
			&& new HercSimDataTransformer().Parse(bytes) is HercSimDat data
			? data.CockpitColorScheme
			: -1;

	/// <summary>All three gauge colours or none — a bar drawn with only some of them resolved would
	/// be worse than one not drawn at all.</summary>
	private static (Vector3, Vector3, Vector3)? ResolveGaugeColors(HudColorTable? colors, DynamixPalette palette) {
		if (colors?.Resolve(HudColorTable.GaugeFillEvenId, palette) is not { } even
			|| colors.Resolve(HudColorTable.GaugeFillOddId, palette) is not { } odd
			|| colors.Resolve(HudColorTable.GaugeRemainderId, palette) is not { } remainder) {
			return null;
		}

		return (ToVector(even), ToVector(odd), ToVector(remainder));
	}

	/// <summary>Both Heads-Down Display fill colours or neither, for the reason in
	/// <see cref="ResolveGaugeColors"/>.</summary>
	private static (Vector3, Vector3)? ResolveHeadsDownColors(HudColorTable? colors, DynamixPalette palette) {
		if (colors?.Resolve(HudColorTable.HeadsDownBackgroundId, palette) is not { } background
			|| colors.Resolve(HudColorTable.HeadsDownIndicatorId, palette) is not { } indicator) {
			return null;
		}

		return (ToVector(background), ToVector(indicator));
	}

	/// <summary>Both arrow colours or neither, for the reason in <see cref="ResolveGaugeColors"/>.</summary>
	private static (Vector3, Vector3)? ResolveArrowColors(HudColorTable? colors, DynamixPalette palette) {
		if (colors?.Resolve(TargetBox.ArrowColorId, palette) is not { } unlocked
			|| colors.Resolve(TargetBox.ArrowLockedColorId, palette) is not { } locked) {
			return null;
		}

		return (ToVector(unlocked), ToVector(locked));
	}

	/// <summary>Every id in the table, resolved once — see <see cref="LogicalColor"/>.</summary>
	private static Vector3?[]? ResolveLogicalColors(HudColorTable? colors, DynamixPalette palette) {
		if (colors == null) {
			return null;
		}

		var resolved = new Vector3?[HudColorTable.EntryCount];
		for (int id = 0; id < resolved.Length; id++) {
			resolved[id] = colors.Resolve(id, palette) is { } color ? ToVector(color) : null;
		}

		return resolved;
	}

	/// <summary>The whole live palette, resolved once — see <see cref="PaletteEntry"/>.</summary>
	private static Vector3?[] ResolvePaletteEntries(DynamixPalette palette) {
		var resolved = new Vector3?[256];
		for (int index = 0; index < resolved.Length; index++) {
			resolved[index] = palette.Colors.TryGetValue(index, out var entry) ? ToVector(entry.GetColor()) : null;
		}

		return resolved;
	}

	/// <summary>One live-palette slot as a colour, for the widgets that name a raw index rather than
	/// a <c>COLORS.DAT</c> id. Black when the palette has no such slot.</summary>
	private static Vector3 PaletteColor(DynamixPalette palette, int index) =>
		palette.Colors.TryGetValue(index, out var entry) ? ToVector(entry.GetColor()) : Vector3.Zero;

	private static Vector3 ToVector(HercWorks.Core.Data.Struct.RgbaColor c) =>
		new(c.R / 255f, c.G / 255f, c.B / 255f);

	// The ring colours currently painted into the frames, so a repaint is skipped on the many frames
	// where the charge did not move far enough to change any of the six.
	private HercWorks.Core.Data.Struct.ColorBytes[]? _shieldRingColors;

	/// <summary>
	/// Repaints the shield meter's rings for the charge the machine is actually holding —
	/// <c>ShieldsGauge</c>'s per-frame palette write, applied to already-decoded art.
	///
	/// <para>Charges are on the widget's own 0..<c>0x800</c> scale; use
	/// <see cref="CockpitPalette.ShieldFacingCharge"/> to get there from raw simulation charge. The
	/// rings genuinely go dark as a facing empties, which is the whole point of the meter and is what
	/// the palette baked at load could never show.</para>
	/// </summary>
	/// <returns>
	/// True when pixels changed and the caller must re-upload the affected textures. False means the
	/// six colours resolved identically to last time and nothing was touched.
	/// </returns>
	public bool UpdateShieldRings(int frontCharge, int rearCharge) {
		var colors = CockpitPalette.ShieldRampEntries(frontCharge, rearCharge);

		if (_shieldRingColors is { } previous) {
			bool same = true;
			for (int i = 0; i < colors.Length && same; i++) {
				var a = previous[i].GetColor();
				var b = colors[i].GetColor();
				same = a.R == b.R && a.G == b.G && a.B == b.B;
			}

			if (same) {
				return false;
			}
		}

		_shieldRingColors = colors;

		// Every frame gets the pass, not just the front one: a herc whose side or heads-down art
		// happens to use these indices would otherwise drift out of step with the front panel.
		Paint(Front);
		Paint(Side);
		if (HeadsDown is { } headsDown) {
			Paint(headsDown);
		}

		return true;

		void Paint(CockpitFrame frame) {
			for (int slot = 0; slot < colors.Length; slot++) {
				var color = colors[slot].GetColor();
				foreach (int offset in frame.ShieldRingPixels[slot]) {
					frame.Pixels[offset] = color.R;
					frame.Pixels[offset + 1] = color.G;
					frame.Pixels[offset + 2] = color.B;

					// The same six colours into the flash buffer, not the impact palette's entries for
					// those slots: the gauge writes these literals into whichever palette is active, so
					// the rings keep their own colour through a flash in the original too.
					if (frame.ImpactPixels is { } impact) {
						impact[offset] = color.R;
						impact[offset + 1] = color.G;
						impact[offset + 2] = color.B;
					}
				}
			}
		}
	}

	/// <summary>
	/// Reads and decodes one <c>.HBx</c> file's single frame through <paramref name="palette"/>.
	/// Mirrors <c>TextureAtlas.DecodeFrame</c>'s indexed-colour expansion exactly (including no
	/// special-casing of index 0 here — the viewport cutout is a separate, deliberate pass, not a
	/// side effect of decoding).
	/// </summary>
	private static CockpitFrame? LoadFrame(GameContent content, string folder, string name,
			DynamixPalette palette, DynamixPalette? impactPalette) {
		byte[]? bytes = content.Read(folder, name);
		if (bytes == null
			|| new DynamixBitmapArrayTransformer().Parse(bytes) is not DynamixBitmapArray array
			|| array.Images is not { Length: > 0 } images
			|| images[0] is not { } frame
			|| frame.Cols <= 0 || frame.Rows <= 0) {
			return null;
		}

		int width = frame.Cols;
		int height = frame.Rows;
		var pixels = new byte[width * height * 4];
		var impactPixels = impactPalette != null ? new byte[width * height * 4] : null;
		byte[] indices = frame.ImageData ?? Array.Empty<byte>();
		int count = Math.Min(indices.Length, width * height);

		var ringPixels = new List<int>[CockpitPalette.ShieldRampLength];
		for (int i = 0; i < ringPixels.Length; i++) {
			ringPixels[i] = new List<int>();
		}

		for (int i = 0; i < count; i++) {
			int index = indices[i];
			Expand(palette, index, pixels, i * 4);
			if (impactPixels != null) {
				Expand(impactPalette!, index, impactPixels, i * 4);
			}

			int slot = index - CockpitPalette.ShieldRampFirstSlot;
			if (slot >= 0 && slot < CockpitPalette.ShieldRampLength) {
				ringPixels[slot].Add(i * 4);
			}
		}

		return new CockpitFrame(pixels, width, height) {
			ShieldRingPixels = ringPixels.Select(list => list.ToArray()).ToArray(),
			ImpactPixels = impactPixels,
		};
	}

	/// <summary>
	/// Carries a frame's alpha channel into its flash buffer, so both agree about which pixels are
	/// the 3D viewport's hole.
	/// </summary>
	private static void CopyAlphaToImpact(CockpitFrame frame) {
		if (frame.ImpactPixels is not { } impact) {
			return;
		}

		for (int at = 3; at < frame.Pixels.Length; at += 4) {
			impact[at] = frame.Pixels[at];
		}
	}

	/// <summary>
	/// One index to one RGBA pixel. An index the palette does not carry is written as grey of its own
	/// value rather than black, so a frame decoded against the wrong palette is visibly odd instead of
	/// silently holed.
	/// </summary>
	private static void Expand(DynamixPalette palette, int index, byte[] pixels, int at) {
		var color = palette.Colors.TryGetValue(index, out var entry)
			? entry.GetColor()
			: new HercWorks.Core.Data.Struct.RgbaColor(255, (byte)index, (byte)index, (byte)index);

		pixels[at] = color.R;
		pixels[at + 1] = color.G;
		pixels[at + 2] = color.B;
		pixels[at + 3] = 255;
	}

	/// <summary>
	/// Punches the view's 3D-viewport hole into <paramref name="frame"/>'s alpha channel using the
	/// herc's own <c>.HD&lt;view&gt;</c> region file — the same data DBSIM's rasterizer is span-clipped
	/// to. Returns true when that file supplied the mask, false when it was missing and
	/// <see cref="CutViewportHoleByColor"/> had to infer one instead.
	///
	/// <para>A file that parses to zero spans is honoured as zero spans, not treated as a failure: that
	/// is a legitimate "this view shows no 3D" (the heads-down view's file is 16 bytes of zeroes). The
	/// forward and side views this class loads both have real spans, so a zero-span result here would
	/// mean something else is wrong — but silently substituting a guessed mask would hide it.</para>
	/// </summary>
	private static bool CutViewportHole(GameContent content, string hercName, int viewIndex, CockpitFrame frame) {
		if (CockpitClipRegions.Load(content, hercName, viewIndex) is not { } regions) {
			CutViewportHoleByColor(frame);
			return false;
		}

		byte[] pixels = frame.Pixels;
		int rows = Math.Min(regions.RowCount, frame.Height);
		for (int y = 0; y < rows; y++) {
			foreach (var span in regions.Row(y)) {
				int from = Math.Max(span.Start, 0);
				int to = Math.Min(span.Start + span.Length, frame.Width);
				for (int x = from; x < to; x++) {
					pixels[(y * frame.Width + x) * 4 + 3] = 0;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Fallback cutout for when the region file is unavailable: flood-fills every connected region of
	/// pure-black (0,0,0) pixels that touches the image's outer edge, setting each one's alpha to 0.
	/// Multi-source (every border pixel is a candidate seed) rather than a single interior point,
	/// because the canopy strut splits the hole into 2-3 disconnected islands and a single seed only
	/// catches the one it sits in.
	///
	/// <para>Kept only as a fallback. It infers the hole from art that happens to be black there, which
	/// is not the same statement as the region file's, and it cannot know about rows the original leaves
	/// opaque regardless of their colour.</para>
	/// </summary>
	private static void CutViewportHoleByColor(CockpitFrame frame) {
		byte[] pixels = frame.Pixels;
		bool IsBlackOpaque(int x, int y) {
			int i = (y * frame.Width + x) * 4;
			return pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] != 0;
		}

		var queue = new Queue<(int X, int Y)>();
		for (int x = 0; x < frame.Width; x++) {
			queue.Enqueue((x, 0));
			queue.Enqueue((x, frame.Height - 1));
		}
		for (int y = 0; y < frame.Height; y++) {
			queue.Enqueue((0, y));
			queue.Enqueue((frame.Width - 1, y));
		}

		while (queue.Count > 0) {
			var (x, y) = queue.Dequeue();
			if (x < 0 || x >= frame.Width || y < 0 || y >= frame.Height || !IsBlackOpaque(x, y)) {
				continue;
			}

			pixels[(y * frame.Width + x) * 4 + 3] = 0;

			queue.Enqueue((x + 1, y));
			queue.Enqueue((x - 1, y));
			queue.Enqueue((x, y + 1));
			queue.Enqueue((x, y - 1));
		}
	}
}
