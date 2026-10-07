using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;

namespace Herculan.Engine.Content;

/// <summary>
/// Builds the palette the cockpit, its HUD sprites and the 3D scene all decode through.
///
/// <para><b>The live palette is the theater palette, in full</b>, with exactly one 24-entry window
/// from <c>COCKPIT.DPL</c> installed over it: live slots
/// <see cref="CockpitSchemeFirstSlot"/>..<see cref="CockpitSchemeFirstSlot"/>+
/// <see cref="CockpitSchemeLength"/>-1 (42..65) take <c>COCKPIT.DPL</c> entries
/// <c>[32 + 24*schemeIndex, +24)</c>. There is no merge of two half-palettes, and every slot outside
/// that window is the theater's. The derivation, the corroborating measurements and the nine-herc
/// scheme table are in docs/retail/formats/cockpit-canopy-palette.md, "Palette".</para>
///
/// <para><c>schemeIndex</c> is the mech type record's <c>+0x52</c>, i.e. offset 80 of
/// <c>dat\&lt;MECH&gt;.DAT</c> —
/// <see cref="HercWorks.Core.Data.File.Dat.Sim.HercSimDat.CockpitColorScheme"/>. Retail values are a 0-8
/// permutation over the nine player hercs, so the nine schemes tile <c>COCKPIT.DPL</c> entries
/// 32..247 exactly.</para>
/// </summary>
public static class CockpitPalette {
	/// <summary>The shared cockpit palette resource — source of the per-herc scheme window only.</summary>
	public const string CockpitPaletteName = "COCKPIT";

	/// <summary>
	/// Its damage-flash counterpart, the <b>same-index</b> scheme table — step 6 of
	/// <c>CockpitViewManager_LoadViews</c> installs this herc's own scheme out of it into the impact
	/// palette, exactly as step 5 does out of <see cref="CockpitPaletteName"/>. Retail's file is the
	/// same 1040 bytes and the same 256 entries with no shade-ramp table, so the two are
	/// interchangeable sources here.
	/// </summary>
	public const string ImpactCockpitPaletteName = "IMPACTCP";

	/// <summary>First live palette slot the cockpit scheme owns (<c>Palette_InstallRange</c>'s base, <c>0x2a</c>).</summary>
	public const int CockpitSchemeFirstSlot = 42;

	/// <summary>How many slots one cockpit scheme covers (<c>Palette_InstallRange</c>'s count, <c>0x18</c>).</summary>
	public const int CockpitSchemeLength = 24;

	/// <summary>
	/// Where scheme 0 starts inside <c>COCKPIT.DPL</c> (the <c>+ 0x20</c> in the source offset). Scheme
	/// <c>n</c> starts at <c>SchemeTableFirstEntry + n*CockpitSchemeLength</c>.
	/// </summary>
	public const int SchemeTableFirstEntry = 32;

	/// <summary>The <c>COCKPIT.DPL</c> entry range scheme <paramref name="schemeIndex"/> reads from.</summary>
	public static int SchemeFirstEntry(int schemeIndex) =>
		SchemeTableFirstEntry + schemeIndex * CockpitSchemeLength;

	/// <summary>
	/// Assembles the live palette: the theater palette as the base, with
	/// <paramref name="cockpitSchemeIndex"/>'s 24-entry cockpit scheme installed over slots 42-65.
	///
	/// <para>Returns null only when neither palette can be read. A missing theater palette falls back
	/// to <c>COCKPIT.DPL</c> as the base so the cockpit still draws, but that is a degraded path — the
	/// theater owns 232 of the 256 slots, including every HUD sprite colour — and callers with a
	/// theater (i.e. every real mission) should always pass its
	/// <c>TheaterDescriptor.PaletteName</c>.</para>
	/// </summary>
	public static DynamixPalette? Load(GameContent content, string? worldPaletteName, int cockpitSchemeIndex) =>
		Load(content, worldPaletteName, cockpitSchemeIndex, CockpitPaletteName);

	/// <summary>
	/// The damage flash's live palette: the theater's <c>IMPACT&lt;n&gt;.DPL</c> as the base with this
	/// herc's scheme out of <see cref="ImpactCockpitPaletteName"/> over the same 24 slots. The
	/// original builds it the same way and from the same pair — see docs/retail/formats/cockpit-canopy-palette.md,
	/// "The damage shake".
	/// </summary>
	public static DynamixPalette? LoadImpact(GameContent content, string? impactPaletteName,
			int cockpitSchemeIndex) =>
		impactPaletteName == null
			? null
			: Load(content, impactPaletteName, cockpitSchemeIndex, ImpactCockpitPaletteName);

	private static DynamixPalette? Load(GameContent content, string? worldPaletteName,
			int cockpitSchemeIndex, string cockpitPaletteName) {
		var cockpit = ReadPalette(content, cockpitPaletteName);
		var palette = ReadPalette(content, worldPaletteName) ?? cockpit;
		if (palette == null) {
			return null;
		}

		if (cockpit != null && cockpitSchemeIndex >= 0) {
			// Snapshot before writing. On the no-theater fallback path `palette` and `cockpit` are the
			// same object and scheme 0's source range (32..55) overlaps the destination (42..65), so
			// copying entry by entry would read back entries it had already overwritten.
			int source = SchemeFirstEntry(cockpitSchemeIndex);
			var scheme = new HercWorks.Core.Data.Struct.ColorBytes?[CockpitSchemeLength];
			for (int i = 0; i < CockpitSchemeLength; i++) {
				scheme[i] = cockpit.Colors.TryGetValue(source + i, out var entry) ? entry : null;
			}

			for (int i = 0; i < CockpitSchemeLength; i++) {
				if (scheme[i] is { } entry) {
					palette.Colors[CockpitSchemeFirstSlot + i] = entry;
				}
			}
		}

		InstallShieldRamp(palette, ShieldFacingNominalCharge, ShieldFacingNominalCharge);

		return palette;
	}


	/// <summary>First live palette slot the shield meter's ring ramp owns (<c>Palette_InstallRange</c>'s base, <c>0x42</c>).</summary>
	public const int ShieldRampFirstSlot = 66;

	/// <summary>How many slots the shield ramp covers — three rings per facing, two facings.</summary>
	public const int ShieldRampLength = 6;

	/// <summary>
	/// A shield facing at its nominal (undamaged, evenly balanced) charge. The facing value runs
	/// 0..<c>0x800</c>, where <c>0x400</c> is the whole shield pool on one side; an even 100/100 split
	/// therefore parks both facings here, which is what the retail screenshots show.
	/// </summary>
	public const int ShieldFacingNominalCharge = 0x200;

	/// <summary>
	/// Installs the shield meter's six-entry ring ramp over slots 66-71, reproducing
	/// <c>ShieldsGauge</c>'s per-frame palette write (<c>ShieldsGauge_UpdateRingPalette</c> (<c>004438f0</c>), called from its paint at
	/// <c>00443730</c>/<c>00443748</c>).
	///
	/// <para><b>The shield meter is not drawn — it is lit.</b> The nested concentric rings live in the
	/// herc's own canopy art in palette indices 66-71; the widget draws no geometry at all, it
	/// recolours those six slots every frame, so rings go dark as charge drops. The ramp formula, the
	/// two colour immediates and the screenshot match are in docs/retail/formats/cockpit-hud-widgets.md, "Ring ramp
	/// — <c>ShieldsGauge_UpdateRingPalette</c> (<c>004438f0</c>)".</para>
	///
	/// <para>Slot order follows the original's stack layout, which the install reads upward from the
	/// last-computed entry: 66-68 are <paramref name="frontCharge"/>'s rings outermost-first, 69-71
	/// <paramref name="rearCharge"/>'s.</para>
	/// </summary>
	public static void InstallShieldRamp(DynamixPalette palette, int frontCharge, int rearCharge) {
		var entries = ShieldRampEntries(frontCharge, rearCharge);
		for (int i = 0; i < ShieldRampLength; i++) {
			palette.Colors[ShieldRampFirstSlot + i] = entries[i];
		}
	}

	/// <summary>
	/// The six ring colours for a given pair of facing charges, in slot order, without installing
	/// them anywhere. <see cref="InstallShieldRamp"/> writes these into a palette at load; the live
	/// cockpit re-derives them every time the charge moves and repaints the canopy's ring pixels
	/// directly, because by then the palette has already been baked into the decoded art.
	/// </summary>
	/// <param name="frontCharge">Front facing charge on the widget's own 0..<c>0x800</c> scale — see <see cref="ShieldFacingCharge"/>.</param>
	/// <param name="rearCharge">Rear facing charge, same scale.</param>
	public static HercWorks.Core.Data.Struct.ColorBytes[] ShieldRampEntries(int frontCharge, int rearCharge) {
		var entries = new HercWorks.Core.Data.Struct.ColorBytes[ShieldRampLength];
		RingsFor(frontCharge, entries, 0);
		RingsFor(rearCharge, entries, 3);
		return entries;
	}

	/// <summary>
	/// One facing's charge on the ramp's 0..<c>0x800</c> scale, from the raw charge the simulation
	/// holds. This is the same Q10 fraction <c>Shield_BalanceInputRead</c> (<c>00413bc8</c>) hands the
	/// gauge — <c>(facing &lt;&lt; 10) / baseMax</c>, divided by the array's <i>base</i> capacity
	/// rather than its pod-boosted one, which is why a Shield Pod drives the rings past
	/// <c>0x400</c> into their overcharged colours instead of renormalising back to nominal.
	/// </summary>
	public static int ShieldFacingCharge(int facingCharge, int baseCapacity) =>
		baseCapacity <= 0 ? 0 : ((int)facingCharge << 10) / baseCapacity;

	/// <summary>
	/// Base and bright ring colours, RGB6 — the exe's own immediates at <c>0049c9cb</c>/<c>0049c9ce</c>.
	/// <c>ShieldsGauge</c> writes them into whichever palette is active every frame, so the rings read the
	/// same through a hit flash; here they are painted into both of the canopy's palette buffers.
	/// </summary>
	private static readonly (int R, int G, int B) ShieldRingBase = (25, 59, 23);
	private static readonly (int R, int G, int B) ShieldRingBright = (59, 59, 23);

	private static void RingsFor(int charge, HercWorks.Core.Data.Struct.ColorBytes[] entries, int firstIndex) {
		bool overcharged = charge > 0x400;
		int value = overcharged ? charge - 0x400 : charge;

		// Written outermost-first to match the install's slot order: ring 3 is the last to light.
		entries[firstIndex] = Ring(Ramp(value, 0x180, 4), overcharged);
		entries[firstIndex + 1] = Ring(Ramp(value, 0x100, 2), overcharged);
		entries[firstIndex + 2] = Ring(value, overcharged);
	}

	/// <summary>
	/// One ring's fill fraction in Q10: nothing below <paramref name="threshold"/>, then
	/// <paramref name="rate"/> times as fast as the base ring, clamped at full.
	/// </summary>
	private static int Ramp(int value, int threshold, int rate) =>
		value < threshold ? 0 : System.Math.Min((value - threshold) * rate, 0x400);

	private static HercWorks.Core.Data.Struct.ColorBytes Ring(int t, bool overcharged) {
		int Channel(int baseValue, int brightValue) {
			int scaled = overcharged
				? baseValue + ((brightValue - baseValue) * t >> 10)
				: baseValue * t >> 10;
			return System.Math.Clamp(scaled, 0, 63);
		}

		int r = Channel(ShieldRingBase.R, ShieldRingBright.R);
		int g = Channel(ShieldRingBase.G, ShieldRingBright.G);
		int b = Channel(ShieldRingBase.B, ShieldRingBright.B);

		// Same 6-bit-to-8-bit expansion DynamixPaletteTransformer applies to a palette read off disk,
		// so a slot written here is indistinguishable from one that came out of a .DPL.
		var color = new HercWorks.Core.Data.Struct.ColorBytes((byte)(r * 4), (byte)(g * 4), (byte)(b * 4), 255);
		color.SetColor(HercWorks.Core.Data.Struct.RgbaColor.FromArgb(255, r * 4, g * 4, b * 4));
		return color;
	}

	private static DynamixPalette? ReadPalette(GameContent content, string? name) =>
		name != null && content.Read("dpl", name + ".DPL") is { } bytes
			? new DynamixPaletteTransformer().Parse(bytes) as DynamixPalette
			: null;
}
