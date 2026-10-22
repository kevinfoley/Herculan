using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es1.Gunsight;
using Herculan.Es1.Panels;

namespace Herculan.Es1.View;

/// <summary>One view's window as <c>CockpitView_ApplyViewState</c> installs it in the render context: its rect, the
/// projection centre the projection reads from it, and its canvas origin.</summary>
/// <param name="X0">The rect's left column, <c>+0x210</c>.</param>
/// <param name="Y0">Its top row.</param>
/// <param name="X1">Its right column.</param>
/// <param name="Y1">Its bottom row.</param>
/// <param name="CentreX">Where the view axis lands on the screen: the rect's top-left less the record's pair with the canvas origin added.</param>
/// <param name="CentreY">The same, in rows.</param>
public readonly record struct Es1ViewWindow(int X0, int Y0, int X1, int Y1, int CentreX, int CentreY) {
	/// <summary>
	/// The window a record's six values install, its pair moved by <paramref name="canvasX"/> and <paramref name="canvasY"/>:
	/// <c>Raster_InstallViewProjection</c>'s centre is the rect's top-left less the context's pair
	/// (docs/retail/simulation/cockpit-views.md#the-projection-centre-is-not-the-middle-of-the-view).
	/// </summary>
	public static Es1ViewWindow Install(Vue.Entry record, int canvasX, int canvasY) => new(
		record.ViewportX0, record.ViewportY0, record.ViewportX1, record.ViewportY1,
		record.ViewportX0 - (record.CenterX + canvasX), record.ViewportY0 - (record.CenterY + canvasY));
}

/// <summary>
/// The files <c>CockpitViewManager_LoadViews</c> (<c>ES1DBSIM 00021fb0</c>) loads for the player chassis' cockpit
/// (docs/es1/simulation/cockpit-views.md#the-view-manager): the <c>.VUE</c> records, each view's clip regions and canopy
/// art, and <c>dpl\cockpit</c>; and what the cockpit object it builds loads: the ten fonts, seven cursors and
/// <c>dba\pbuttons</c> <c>ColorSchemePanels_LoadAll</c> loads, the <c>.GAU</c> layout <c>Cockpit_LoadPanel</c> reads
/// (docs/es1/simulation/cockpit-panel.md#building-it), the gunsight's banks, and the throttle's bar pictures, which
/// its slider's constructor loads (docs/es1/simulation/cockpit-throttle-gauge.md#building-it).
/// <see cref="Es1ViewManager"/> switches between the views.
///
/// <para>Not loaded: the other instruments' files, which plan step 6c ports.</para>
/// </summary>
public sealed class Es1CockpitViews {
	/// <summary>The screen the simulator draws on, 320 by 200 (docs/es1/rendering/polygon-shading.md#the-display-drivers).</summary>
	public const int ScreenWidth = 320;

	/// <inheritdoc cref="ScreenWidth"/>
	public const int ScreenHeight = 200;

	/// <summary>The external view, which has no canopy and loads no art.</summary>
	public const int ExternalView = 4;

	private readonly Vue.Entry[] _records;
	private readonly Es1ClipBlock[] _clips;
	private readonly DynamixBitmap?[] _canopies;

	/// <param name="layout">The <c>.GAU</c> as <c>Gau_Load</c> returns it; an all-zero layout when null.</param>
	/// <param name="cursors">The seven cursors in <see cref="CursorNames"/>' order; none when null.</param>
	/// <param name="fonts">The ten fonts in <see cref="FontNames"/>' order; none when null.</param>
	/// <param name="gunsightBanks">The gunsight's banks; none when null.</param>
	/// <param name="pushButtons">The frames of <c>dba\pbuttons</c>; none when null.</param>
	/// <param name="throttleLights">The frames of the throttle's light file; none when null.</param>
	/// <param name="systemStrings"><c>str\system.str</c>; none when null.</param>
	internal Es1CockpitViews(string name, Vue.Entry[] records, Es1ClipBlock[] clips, DynamixBitmap?[] canopies,
			DynamixPalette? palette, Es1GauFile? layout = null, Es1CursorImage?[]? cursors = null, HudFont?[]? fonts = null,
			Es1GunsightBanks? gunsightBanks = null, IReadOnlyList<DynamixBitmap>? pushButtons = null,
			IReadOnlyList<DynamixBitmap>? throttleLights = null, StringFile? systemStrings = null) {
		Name = name;
		_records = records;
		_clips = clips;
		_canopies = canopies;
		CockpitPalette = palette;
		Layout = layout ?? new Es1GauFile();
		Cursors = cursors ?? new Es1CursorImage?[CursorNames.Length];
		Fonts = fonts ?? new HudFont?[FontNames.Length];
		GunsightBanks = gunsightBanks ?? new Es1GunsightBanks();
		PushButtons = pushButtons;
		ThrottleLights = throttleLights;
		SystemStrings = systemStrings;
	}

	/// <summary>
	/// The seven <c>dci\</c> cursors in the order <c>ColorSchemePanels_LoadAll</c> loads them into
	/// <c>Cockpit_CursorImages</c> (<c>00092d20</c>), ES2's (docs/retail/simulation/cockpit-input.md#9-cursor-rendering):
	/// the arrow, the gunsight's, the map pick's, and the four edges' arrows north, south, east and west.
	/// </summary>
	public static readonly string[] CursorNames = ["CURSOR", "PCURSOR", "MCURSOR", "NCURSOR", "SCURSOR", "ECURSOR", "WCURSOR"];

	/// <summary>
	/// The ten <c>dfn\</c> fonts in the order <c>ColorSchemePanels_LoadAll</c> loads them into <c>ColorSchemePanels</c>
	/// (<c>00092d40</c>) (docs/es1/simulation/cockpit-panel.md#building-it).
	/// </summary>
	public static readonly string[] FontNames =
		["CPBLUE", "CPGREEN", "CPRED", "CPYLW", "CPON", "CPPRESS", "CPOFF", "CPGREY", "CPBLACK", "CPORANGE"];

	/// <summary>The indices of <c>cpgreen</c> (<c>00092d44</c>), <c>cpred</c> (<c>00092d48</c>) and <c>cpblack</c> (<c>00092d60</c>) in <see cref="Fonts"/>.</summary>
	public const int CpGreen = 1, CpRed = 2, CpBlack = 8;

	/// <summary>
	/// <c>str\system.str</c>, the computer's lines, which the first message port built loads into
	/// <c>SystemStringTable</c> (docs/es1/simulation/cockpit-messages.md#the-string-files); null when it is missing or
	/// does not parse.
	/// </summary>
	public StringFile? SystemStrings { get; }

	/// <summary><c>ColorSchemePanels</c>, in <see cref="FontNames"/>' order; null for a font whose file is missing.</summary>
	public IReadOnlyList<HudFont?> Fonts { get; }

	/// <summary>The banks the gunsight's slide bars and heading tape draw from.</summary>
	public Es1GunsightBanks GunsightBanks { get; }

	/// <summary><c>CockpitPButtonsBank</c> (<c>00092d68</c>), the frames of <c>dba\pbuttons</c>; null when the file is missing.</summary>
	public IReadOnlyList<DynamixBitmap>? PushButtons { get; }

	/// <summary>
	/// The frames of <c>dba\&lt;light&gt;</c>, the light name the <c>.GAU</c> throttle block's <c>int32</c> <c>0x10</c>
	/// indexes (<see cref="Throttle.Es1ThrottleSlider.LightName"/>), the throttle's bar pictures; null when the file is
	/// missing.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? ThrottleLights { get; }

	/// <summary><c>CockpitGauRecord</c> (<c>00092d3c</c>): <c>gau\&lt;name&gt;</c> after <c>Gau_Load</c>'s origin add.</summary>
	public Es1GauFile Layout { get; }

	/// <summary><c>Cockpit_CursorImages</c>, in <see cref="CursorNames"/>' order; null for a cursor whose file or bitmap is missing.</summary>
	public IReadOnlyList<Es1CursorImage?> Cursors { get; }

	/// <summary>The chassis name the files are named by, <c>+0x29</c>.</summary>
	public string Name { get; }

	/// <summary><c>CockpitPaletteSingleton</c> (<c>000925f0</c>), <c>dpl\cockpit</c>.</summary>
	public DynamixPalette? CockpitPalette { get; }

	/// <summary>The view count, <c>+0</c>.</summary>
	public int Count => _records.Length;

	/// <summary>View <paramref name="view"/>'s <c>.VUE</c> record, of <c>CockpitVueRecords</c> (<c>000afaa4</c>).</summary>
	public Vue.Entry Record(int view) => _records[view];

	/// <summary>View <paramref name="view"/>'s clip block, of the four at <c>+0x0c</c>; a block of no region when its file is missing.</summary>
	public Es1ClipBlock Clip(int view) => _clips[view];

	/// <summary>Frame 0 of view <paramref name="view"/>'s canopy art, of the four at <c>+0x08</c>; null when its file is missing.</summary>
	public DynamixBitmap? Canopy(int view) => _canopies[view];

	/// <summary>
	/// <c>CockpitViewManager_LoadViews</c> for <paramref name="name"/>: <c>vue\&lt;name&gt;</c>, <c>ed&lt;i&gt;\&lt;name&gt;</c>
	/// for each view and <c>db&lt;n&gt;\&lt;name&gt;</c> for each canopy, view 3 sharing view 2's
	/// (<c>CockpitCanopy_LoadViewBitmap</c>, <c>00022184</c>); <c>dpl\cockpit</c>; the seven cursors; and
	/// <c>gau\&lt;name&gt;</c> (<c>Gau_Load</c>, <c>0002b5c4</c>); the ten fonts; <c>dba\pbuttons</c>; the gunsight's banks;
	/// the throttle's light file; and <c>str\system.str</c> from the untranslated folder, as
	/// <c>ResourcePath_BuildFolderName</c> names it. Null when the
	/// <c>.VUE</c> is missing, or the <c>.GAU</c>,
	/// whose absence <c>Gau_Load</c> asserts on.
	/// </summary>
	public static Es1CockpitViews? Load(GameContent content, string name) {
		if (content.Read("vue", name + ".VUE") is not { } vueBytes || new VueTransformer().Parse(vueBytes) is not { Entries: { } records }) {
			return null;
		}

		var clips = new Es1ClipBlock[records.Length];
		var canopies = new DynamixBitmap?[records.Length];
		for (int view = 0; view < records.Length; view++) {
			string folder = "ed" + view;
			clips[view] = content.Read(folder, name + ".ED" + view) is { } clipBytes
					&& new Es1ClipRegionsTransformer().Parse(clipBytes) is { } regions
				? Es1ClipBlock.FromFile(regions)
				: Es1ClipBlock.Empty;

			int file = view > 2 ? view - 1 : view;
			string dbFolder = "db" + file;
			canopies[view] = content.Read(dbFolder, name + ".DB" + file) is { } artBytes
				? new DynamixBitmapArrayTransformer().Parse(artBytes)?.Images?.FirstOrDefault()
				: null;
		}

		var palette = content.Read("dpl", "COCKPIT.DPL") is { } paletteBytes ? new DynamixPaletteTransformer().Parse(paletteBytes) : null;
		if (content.Read("gau", name + ".GAU") is not { } gauBytes || new Es1GauFileTransformer().Parse(gauBytes) is not { } layout) {
			return null;
		}

		var cursors = CursorNames.Select(cursor => Es1CursorImage.Load(content, cursor)).ToArray();
		var fonts = FontNames.Select(font => HudFont.Load(content, font, "dfn")).ToArray();
		var loaded = layout.Loaded();
		var lights = Throttle.Es1ThrottleSlider.LightName(loaded.ThrottleBlock.Lights) is { Length: > 0 } light
			? Es1WidgetResources.LoadFrames(content, light)
			: null;
		return new Es1CockpitViews(name, records, clips, canopies, palette, loaded, cursors, fonts,
			Es1GunsightBanks.Load(content), Es1WidgetResources.LoadFrames(content, "PBUTTONS"), lights,
			SimStrings.LoadUntranslated(content, "SYSTEM.STR"));
	}
}
