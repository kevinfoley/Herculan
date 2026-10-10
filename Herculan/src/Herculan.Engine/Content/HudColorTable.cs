using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Cockpit;
using System.Buffers.Binary;

namespace Herculan.Engine.Content;

/// <summary>
/// <c>dat\COLORS.DAT</c> — the indirection between the logical colour ids stored in the game's data
/// files and actual palette indices.
///
/// <para>HUD data files never name a palette index directly. A <c>.PDG</c> damage region, a gauge
/// constructor, a text label: each carries a small id, and DBSIM resolves it once at load time
/// through this table, in place (<c>arr[i] = table[arr[i]]</c>). The table itself lives at
/// <c>HudColorTable</c> (<c>004d3c00</c>) in DBSIM's .bss, which <c>HudColor_LoadResources</c>
/// (<c>00467a60</c>) fills straight from the file — the 27 entries this loads.</para>
///
/// <para>Independently verified: the heads-down display's damage screen resolves ids 19, 9, 15, 12,
/// which this table maps to palette 16, 10, 13, 14 — black, red, yellow and green in the assembled
/// palette (see <see cref="CockpitPalette"/>). That the four land on exactly the colours a damage
/// readout wears is what corroborates the table; it is <b>not</b> where those readouts get their
/// colour, which is a font — see docs/retail/simulation/heads-down-display.md, Rejected readings.</para>
///
/// <para>The indirection is what makes the ids theater-independent: entries point into both the
/// fixed 0-15 system colours and the theater-owned 16-31 ramp, so the same data file yields
/// appropriate colours whichever <c>WORLD&lt;n&gt;.DPL</c> is loaded.</para>
/// </summary>
public sealed class HudColorTable {
	/// <summary>Resource folder and file name.</summary>
	public const string ResourceFolder = "dat";
	public const string ResourceName = "COLORS.DAT";

	/// <summary>Entry count in the retail file (54 payload bytes / 2).</summary>
	public const int EntryCount = 27;

	/// <summary>
	/// Logical ids an LED gauge bar draws with, all from <c>EnergyPoolGauge_Ctor</c>
	/// (<c>EnergyPoolGauge_Ctor</c>, <c>00444d5c</c>) and confirmed against the bar's own fill routine (<c>LedBarGraph_FillPinstripe</c>, <c>00439758</c>).
	///
	/// <para>The filled span is not a solid colour: the routine walks the bar's x range twice, once
	/// over even columns and once over odd, drawing a full-height line each step with a different
	/// colour — a one-pixel vertical pinstripe. Ids 6 and 5 resolve to palette 98 and 97, two shades
	/// of blue — <c>(0,116,204)</c> and <c>(0,40,160)</c> — which interleaved read as a single shaded
	/// fill rather than two colours. Field <c>0x2c</c> is the even columns, <c>0x30</c> the odd. See
	/// docs/retail/simulation/cockpit-hud-widgets.md, "LED gauges".</para>
	/// </summary>
	public const int GaugeFillEvenId = 6;

	/// <summary>Odd-column fill colour — see <see cref="GaugeFillEvenId"/>.</summary>
	public const int GaugeFillOddId = 5;

	/// <summary>
	/// The unfilled remainder past the fill point: black. Written to the bar's field <c>0x24</c>,
	/// which the paint method (<c>LedBarGraph_PaintToValue</c>, <c>004395e8</c>) installs as the draw colour for that span.
	/// </summary>
	public const int GaugeRemainderId = 19;

    /// <summary>
    /// The value range an LED bar's fill fraction is measured against — <c>EnergyPoolGauge_Ctor</c>
    /// passes this to the bar constructor, which precomputes <c>span = (end - start) * 0x10000 / range</c>
    /// so painting is <c>start + (value * span &gt;&gt; 16)</c> in 16.16 fixed point.
    /// </summary>
    public const int GaugeValueRange = 0x400;

	/// <summary>
	/// What the Heads-Down Display floods a screen or a label background with — palette 16, black.
	/// Both its pages use it: the command display's paint (<c>HddCommandScreen_Repaint</c>, <c>0044c894</c>) reads it as id 19 and
	/// the damage detail's (<c>HddDamageScreen_Update</c>, <c>00450c54</c>) as id 3, and this table sends both to the same index.
	/// </summary>
	public const int HeadsDownBackgroundId = 19;

	/// <summary>
	/// The small block beside that display's title — palette 13, yellow. Its paint
	/// (<c>HddDisplay_Repaint</c>, <c>00449a50</c>) fills the rect with this while the <c>+0x51f</c> flag is up, which is
	/// while the damage detail's subject is the player — <see cref="HddDamageSubject.IndicatorLit"/>.
	/// </summary>
	public const int HeadsDownIndicatorId = 15;

	/// <summary>The same block with the flag down — palette 102.</summary>
	public const int HeadsDownIndicatorUnlitId = 13;

	/// <summary>
	/// The plate the damage screen's subject caption sits on while the subject is the player — palette
	/// 98, the same blue an LED bar's even columns use. See <see cref="HddDamageSubject.CaptionColorId"/>.
	/// </summary>
	public const int HeadsDownSubjectPlateId = 6;

	/// <summary>
	/// The colour a squad comm box paints its pilot's name on, and the same colour that pilot's own
	/// map marker and order link take: <c>HudColorTable_Get</c> (<c>00434280</c>) reads the slot's id
	/// from its own array at <c>0049b040</c>. See "The gauge" in docs/retail/simulation/heads-down-display.md.
	/// A slot outside the squad answers -1, which resolves to no colour.
	/// </summary>
	public static int PilotColorId(int slot) =>
		slot >= 0 && slot < PilotColorIds.Length ? PilotColorIds[slot] : -1;

	private static readonly int[] PilotColorIds = [12, 15, 26];

	private readonly int[] _entries;

	private HudColorTable(int[] entries) {
		_entries = entries;
	}

	/// <summary>Palette index for a logical colour id, or null when the id is out of range.</summary>
	public int? PaletteIndex(int logicalId) =>
		logicalId >= 0 && logicalId < _entries.Length ? _entries[logicalId] : null;

	/// <summary>
	/// Resolves a logical colour id all the way to RGB through <paramref name="palette"/>. Null when
	/// the id is out of range or the palette has no such entry — callers draw nothing rather than
	/// substituting a colour of their own.
	/// </summary>
	public RgbaColor? Resolve(int logicalId, DynamixPalette palette) =>
		PaletteIndex(logicalId) is { } index && palette.Colors.TryGetValue(index, out var entry)
			? entry.GetColor()
			: null;

	/// <summary>
	/// Loads and parses the table. Returns null when the resource is missing or too short to hold a
	/// full set of entries.
	/// </summary>
	public static HudColorTable? Load(GameContent content) {
		if (content.Read(ResourceFolder, ResourceName) is not { } bytes
			|| bytes.Length < EntryCount * 2) {
			return null;
		}

		var entries = new int[EntryCount];
		for (int i = 0; i < EntryCount; i++) {
			entries[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2));
		}

		return new HudColorTable(entries);
	}
}
