using Herculan.Engine.Cockpit;
using System.Numerics;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Content;
using Herculan.Engine.Sim;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Render.Cockpit;

/// <summary>
/// The console instruments under the canopy that sit outside the gunsight: the weapon rows, the shield
/// meter's readouts and the three console buttons.
/// </summary>
internal static class ConsoleGaugePainter {
	/// <summary>
	/// One row per fitted hardpoint, built the way <c>WeaponGauge_Ctor</c> (<c>0044080c</c>) and its
	/// select-gadget child (<c>ChainedWeaponSelectGadget_Ctor</c> (<c>00442488</c>), painted by <c>WeaponSelectGadget_Paint</c> (<c>004426c0</c>)) build it:
	///
	/// <list type="bullet">
	/// <item>the row plate from <c>PWEAPONS</c> — frame 0 selected, frame 1 not — blitted
	/// <see cref="RowPlateBezel"/> device pixels up and left of the <c>.GAU</c> rect;</item>
	/// <item>the hardpoint's state box from <c>PWEAPONS</c> frames 4 and 5 (6x14) at the rect's
	/// <c>+12</c> device offset — the constructor's own <c>+6</c> GAU literal. It is drawn only for a
	/// mount that is armed or in the current fire group, lit (frame 4) when the mount could fire and
	/// dark (frame 5) when it could not, which is why a pod's row never has one;</item>
	/// <item>the slot number at <c>+6</c> device, then the weapon's name in the label rect the
	/// constructor puts at <c>+11..+35</c> GAU. Colour is the font: <c>WHITE</c> for the selected
	/// row, <c>GRAY</c> for the rest.</item>
	/// <item>and, on a pod row whose button is on, that label rect flooded green first — see
	/// <see cref="PodPlateColorId"/>.</item>
	/// <item>the value field past the name, at <c>+0x24..+0x35</c> GAU — a round count for an
	/// ammunition mount (<c>AmmoWeaponGauge_Paint</c> (<c>004411b4</c>) prints <c>itoa(rounds)</c> there) and an LED charge bar
	/// for an energy one (<c>WeaponSliderGadget_Paint</c> (<c>00442b38</c>) paints one across the same span). A pod has neither: its
	/// own constructor widens the name label across both fields instead.</item>
	/// </list>
	///
	/// <para>Under all of it, <c>WPN_DMG</c> frame 0 fills the plate's hole — <c>WeaponSelectGadget_PaintUnderlay</c> (<c>00442394</c>)'s
	/// underlay, a flat plate in the row's own background colour. The bank's other nine frames are the
	/// row's sensor-dropout wipe: while <see cref="WeaponRowState.DropoutHidden"/> every paint but the
	/// plate's bezel holds back, and the wipe's frame stands in the hole instead.</para>
	/// </summary>
	internal static void AddWeaponRows(GAUFile gau, CockpitHudState state,
			(Vector3 FillEven, Vector3 FillOdd)? barColors, Vector3? podPlate,
			Action<string, int, float, float> blit, Func<string, string, float, float, float> drawText,
			Action<float, float, float, float, Vector3> fillRect) {
		if (gau.Weapons is not { } weapons) {
			return;
		}

		const float S = CockpitArt.GauToPixelScale;
		int slots = Math.Min(gau.WeaponListTotal, weapons.Length);
		for (int i = 0; i < slots; i++) {
			var rect = weapons[i];
			var row = i < state.Weapons.Count ? state.Weapons[i] : WeaponRowState.Empty;
			if (!row.Powered) {
				continue;
			}

			// The row's own font, which the slot number always wears: WHITE for the selected row and
			// GRAY for the rest. A pod row's name is the exception — see nameFont.
			string font = row.Selected ? "WHITE" : "GRAY";

			// A pod row's own paint (PodGauge_Paint, 0044171c) re-dresses the name label, and only the name label:
			// it reaches the widget at gauge+0xca and never touches the slot number, which
			// WeaponSelectGadget_Paint draws in the row font above. The label's font comes from the
			// button rather than from the selection, which a pod never has — gray while the button is
			// off, dark while it is on, over the green plate below.
			string nameFont = row.PodButton ? "DARK" : font;
			float left = rect.Origin.X * S;
			float top = rect.Origin.Y * S;

			// WeaponSelectGadget_Paint draws the plate whatever the dropout is doing, and nothing else of
			// the row while it holds the row back; the wipe's last frame fills the hole meanwhile.
			if (row.DropoutHidden) {
				if (row.DropoutFrame is { } wipe) {
					blit(SensorDropout.RowBank, wipe, left, top);
				}

				blit("PWEAPONS", row.Selected ? 0 : 1, left - RowPlateBezel, top - RowPlateBezel);
				continue;
			}

			blit(SensorDropout.RowBank, RowUnderlayFrame, left, top);
			blit("PWEAPONS", row.Selected ? 0 : 1, left - RowPlateBezel, top - RowPlateBezel);
			if (row.Selected || row.InGroup) {
				blit("PWEAPONS", row.Ready ? ReadyStateFrame : UnreadyStateFrame, left + 12, top);
			}

			// The button's other half: the same paint swaps the name label's background from the plate
			// colour it was seeded with to COLORS.DAT id 12, so the whole label rect floods green under
			// the dark ink. The rect is the pod gauge's own, which is why the Turbo Pod's plate is the
			// short one — see PodLabelLeft.
			if (row.PodButton && podPlate is { } plate) {
				bool turbo = row.ChargeBar;
				float x0 = left + (turbo ? TurboPodLabelLeft : PodLabelLeft) * S;
				float x1 = left + (turbo ? TurboPodLabelRight : PodLabelRight) * S;
				float y0 = top + (turbo ? TurboPodLabelTop : PodLabelTop) * S;
				fillRect(x0, y0, x1 + 1, top + PodLabelBottom * S + 1, plate);
			}

			drawText(font, (i + 1).ToString(), left + 6, top);
			if (row.Name is { Length: > 0 } name) {
				drawText(nameFont, name, left + 22, top);
			}

			switch (row.Kind) {
				case WeaponMountKind.Ammunition:
					drawText(font, row.Rounds.ToString(), left + ValueFieldLeft * S, top);
					break;

				// The ELF class keeps the energy class's gauge slot (+0x50), so it prints the same bar,
				// and the Turbo Pod's gauge adds one of its own over the same span — see
				// WeaponRowState.ChargeBar, which is what says whether this row has one.
				case not WeaponMountKind.Ammunition
					when row.ChargeBar && barColors is var (fillEven, fillOdd):
					AddChargeBar(row.ChargeMeter,
						left + ValueFieldLeft * S, top + ChargeBarTop * S,
						(ValueFieldRight - ValueFieldLeft) * S,
						(ChargeBarBottom - ChargeBarTop) * S, fillEven, fillOdd, fillRect);
					break;
			}
		}
	}

	/// <summary>
	/// How far up and left of the <c>.GAU</c> rect the row plate is blitted, in device pixels.
	///
	/// <para>The plate is not a filled plate: <c>PWEAPONS</c> frames 0 and 1 are 116x18 with a 112x14
	/// hole of palette index 0 punched out of the middle, so all the art carries is a two-pixel bezel
	/// and the row's interior is the console bitmap showing through. Offsetting by the bezel width
	/// lands that hole with its top-left corner exactly on the rect, which is where the hardpoint
	/// state box (14 device pixels tall, drawn at the rect's own <c>y</c>) and an engaged pod's green
	/// plate both have to sit for the row to close around them.</para>
	/// </summary>
	private const int RowPlateBezel = 2;

	/// <summary>The <see cref="SensorDropout.RowBank"/> frame every row is underlaid with — 112x14, the plate's hole exactly.</summary>
	private const int RowUnderlayFrame = 0;

	/// <summary><c>PWEAPONS</c> frame for a mount that could fire this instant.</summary>
	private const int ReadyStateFrame = 4;

	/// <summary>And for one that could not — out of ammunition, still charging, or inside its refire delay.</summary>
	private const int UnreadyStateFrame = 5;

	/// <summary>
	/// Where a weapon row's value field starts and ends, in <c>.GAU</c> units from the row's own
	/// left edge — the ammunition gauge's <c>+0x24..+0x35</c> label rect (<c>AmmoWeaponGauge_Ctor</c>, <c>00440f78</c>), which
	/// is also the span the energy gauge hands its LED bar (<c>WeaponSliderGadget_Ctor</c>, <c>00442950</c>).
	/// </summary>
	internal const int ValueFieldLeft = 0x24;

	internal const int ValueFieldRight = 0x35;

	/// <summary>
	/// A pod row's name label, in <c>.GAU</c> units from the row's own left edge and top. It is the
	/// weapon-name label widened over the value field as well, because a pod has nothing to print
	/// there — <c>PodGauge_Ctor</c> (<c>00441524</c>) builds it at <c>x0+11 .. x0+53</c>,
	/// <c>y0 .. y0+5</c>. Both edges are inclusive: the flood covers <c>x1</c> and <c>y1</c> too.
	/// </summary>
	private const int PodLabelLeft = 0xb;

	private const int PodLabelRight = 0x35;

	private const int PodLabelTop = 0;

	private const int PodLabelBottom = 5;

	/// <summary>
	/// And the Turbo Pod's, which is the one pod label that has to share the row with a value field.
	/// <c>TurboPodGauge_Ctor</c> (<c>00441a34</c>) moves the left edge out to <c>x0+6</c>, pulls the
	/// right edge in to two device pixels short of the charge bar, and drops the top edge a unit — so
	/// its plate is shorter and narrower than every other pod's, and sits clear of the bar.
	///
	/// <para>The label's <i>text</i> does not move with it: every row on the panel, this one included,
	/// prints its name at the same <c>+22</c> device pixels.</para>
	/// </summary>
	private const int TurboPodLabelLeft = 6;

	private const int TurboPodLabelRight = 0x22;

	private const int TurboPodLabelTop = 1;

	/// <summary>
	/// What a pod row's name label is flooded with while the pod's button is on — <c>COLORS.DAT</c>
	/// id 12, the green the paper doll and the scanner's hostile structures also wear. Off, the label
	/// keeps the raw palette index <c>0x2e</c> its constructor seeded, which is the row plate's own
	/// background and therefore invisible.
	/// </summary>
	internal const int PodPlateColorId = 12;

	/// <summary>
	/// The charge bar's top and bottom edges, in <c>.GAU</c> units below the row's own top. The
	/// energy gauge builds the bar's rect as the value field at <c>y0..y0+5</c>
	/// (<c>EnergyWeaponGauge_Ctor</c>, <c>00440a68</c>), and <c>WeaponSliderGadget_Ctor</c> (<c>00442950</c>) then drops the top edge by one more unit — so
	/// the bar is a touch shorter than the row and sits clear of the plate's upper bezel.
	/// </summary>
	private const int ChargeBarTop = 1;

	private const int ChargeBarBottom = 5;

	/// <summary>
	/// An energy mount's capacitor bar — the same one-pixel pinstripe of two near-identical shades
	/// <see cref="CanopyPanelPainter.AddGaugeFills"/> paints for the Master Energy Pool, since both are the same
	/// <c>LEDBarGraph</c> class.
	///
	/// <para>Unlike the pool's meter, the unfilled remainder is left alone: the weapon row's bar sits
	/// on the plate art rather than on its own box, and <c>LedBarGraph_PaintToValue</c>'s remainder
	/// colour for this instance is the row background it was built with.</para>
	/// </summary>
	/// <param name="meterValue">The bar's value over its own 0-1024 range.</param>
	private static void AddChargeBar(int meterValue, float left, float top, float width, float height,
			Vector3 fillEven, Vector3 fillOdd, Action<float, float, float, float, Vector3> fillRect) {
		const int Range = 0x400;
		int columns = (int)MathF.Round(width);
		int filled = (int)(Math.Clamp(meterValue, 0, Range) * (long)columns / Range);

		for (int x = 0; x < filled; x++) {
			fillRect(left + x, top, left + x + 1, top + height, (x & 1) == 0 ? fillEven : fillOdd);
		}
	}

	/// <summary>
	/// The shield meter's two numeric readouts, centred in the <c>.GAU</c> label rects at 664 and 680.
	/// <c>ShieldsGauge_Ctor</c> (<c>004434fc</c>) builds them with the <c>WHITE</c> font, and
	/// <c>ShieldsGauge_UpdateReadouts</c> (<c>00444a68</c>) fills them with <c>balance * 200 &gt;&gt; 10</c> and its complement — so an
	/// even fore/aft split reads 100 and 100 out of a 200-point pool.
	///
	/// <para>The meter bodies themselves are not drawn here. They are painted into the canopy art in
	/// palette indices 66-71 and lit by <see cref="CockpitPalette.InstallShieldRamp"/>.</para>
	/// </summary>
	internal static void AddShieldReadouts(GAUFile gau, CockpitHudState state, Vector3? background,
			Action<string, string, int, int, int, int, Vector3?> drawCentered) {
		if (gau.ShieldDisplay is not { } shields) {
			return;
		}

		void Label(string text, PixelPoint origin, PixelSize size) =>
			drawCentered("WHITE", text, origin.X, origin.Y, origin.X + size.Width, origin.Y + size.Height,
				background);

		Label(state.ShieldFront.ToString(), shields.FrontLabel, shields.FrontLabelSize);
		Label(state.ShieldRear.ToString(), shields.RearLabel, shields.RearLabelSize);
	}

	/// <summary>
	/// The three console buttons: a <c>PWEAPONS</c> plate with a caption centred on it.
	///
	/// <para>The plate is not canopy art — <c>ConsoleButton_Paint</c> (<c>00442c88</c>) blits it per
	/// frame from <c>PWEAPONS</c> frames 2 and 3, indexed <c>bank[2 + state]</c>, at the widget's
	/// own rect. Frame 2 is the unlit plate (solid palette index 34, the blue the retail screenshot
	/// shows at RGB (77,77,182)) and frame 3 the lit one (index 14, green). Both are 50x16 against
	/// a 48x14 rect, the same one-pixel overhang the weapon-row plates have, and all three buttons 
	/// are that same 24x7 GAU size in every retail file.</para>
	///
	/// <para>The chain button's caption is its count in Roman numerals, read from DBSIM's own
	/// three-entry table at <c>0049c71c</c> ("I", "II", "III") — a literal table in <c>.rdata</c>,
	/// unrelated to the string file. LINK and TRACK are not fixed: <c>ConsoleButton_Paint</c>
	/// (<c>00442c88</c>) reads them out of <c>ConsoleButtonCaptions</c> (<c>004d13d0</c>), the <c>.bss</c> array
	/// <c>SimStrings_LoadAll</c> fills from <c>STRINGS0.STR</c> group <see cref="CaptionGroup"/>,
	/// indexed by the widget's own kind field (1 = LINK, 2 = TRACK) — see
	/// docs/retail/formats/str-strings.md.</para>
	/// </summary>
	internal static void AddConsoleButtons(GAUFile gau, StringFile? strings, CockpitHudState state,
			Action<string, int, float, float> blit,
			Action<string, string, int, int, int, int, Vector3?> drawCentered) {
		const float S = CockpitArt.GauToPixelScale;

		void Button(string? text, WidgetBase? widget, bool lit = false) {
			if (widget == null) {
				return;
			}

			blit("PWEAPONS", lit ? 3 : 2, widget.Origin.X * S, widget.Origin.Y * S);
			if (text is { Length: > 0 }) {
				drawCentered("WHITE", text, widget.Origin.X, widget.Origin.Y,
					widget.Origin.X + widget.Size.Width, widget.Origin.Y + widget.Size.Height, null);
			}
		}

		// Firing chain and LINK light only while held; TRACK latches. ConsoleButton_Paint
		// (00442c88) takes the first two from the shared press byte and the third from its own flag.
		bool Held(ConsoleButton which) => state.ShowsPressed(CockpitWidgetId.Console(which));

		Button(new string('I', Math.Clamp(state.ChainGroup + 1, 1, 3)), gau.ChainButton,
			Held(ConsoleButton.Chain));
		Button(strings?.Text(CaptionGroup, 1), gau.LinkButton, Held(ConsoleButton.Link));
		Button(strings?.Text(CaptionGroup, 2), gau.AutoTrackButton, state.AutoTrack);
	}

	/// <summary>
	/// <c>STRINGS0.STR</c> group 4: the console button captions — index 0 is <c>"I"</c> (unused; the
	/// chain button gets its numerals from <see cref="AddConsoleButtons"/>'s own table instead), 1 is
	/// <c>"LINK"</c>, 2 is <c>"TRACK"</c>, 3 is empty.
	/// </summary>
	private const int CaptionGroup = 4;
}
