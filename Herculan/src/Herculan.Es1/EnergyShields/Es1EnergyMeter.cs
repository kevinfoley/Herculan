using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.EnergyShields;

/// <summary>
/// The energy meter, the cockpit's instrument <c>+0x1e1</c>, <c>EnergyPoolGauge</c>
/// (docs/es1/simulation/cockpit-energy-shields.md#the-energy-meter): a striped LED bar graph <c>+0x7e</c> that fills with
/// the energy pool <c>+0x7a</c>, across or up the panel by its <c>.GAU</c> block, drawn through the clip shape
/// <c>+0x88</c> where the block names one. Its slots are <c>EnergyPoolGaugeVtable</c> (<c>00093ed6</c>)'s; the child
/// click slot, <c>PanelGauge_OnChildClickNoOpEnergyGaugeCopy</c>, does nothing and nothing calls it, so it is left out.
/// </summary>
public sealed class Es1EnergyMeter {
	/// <summary>The bar's range, the value of a full pool.</summary>
	public const short Full = 0x400;

	/// <summary>The block's <see cref="Es1GauEnergyMeterBlock.Orientation"/> values that build a bar: across and up.</summary>
	public const int Horizontal = 3, Vertical = 4;

	/// <summary>The bar's unlit color.</summary>
	public const byte UnlitColor = 0x3f;

	/// <summary>
	/// <c>EnergyMeter_ClipFileNames</c> (<c>000932c8</c>): the <c>edg\</c> names <see cref="Es1GauEnergyMeterBlock.ClipFile"/>
	/// indexes, the first empty.
	/// </summary>
	public static readonly IReadOnlyList<string> ClipFileNames = ["", "ENG_CLP1", "ENG_CLP2", "ENG_CLP3", "ENG_CLP4"];

	/// <summary>
	/// <c>EnergyPoolGauge_Ctor</c> (<c>ES1DBSIM 00039818</c>)(0, <paramref name="cockpit"/>, <paramref name="block"/>): the
	/// rect the bar's moved by the block's origin; the value 0; with <see cref="Es1GauEnergyMeterBlock.ClipFile"/> 0 no clip
	/// shape, otherwise <c>edg\ENG_CLP</c><i>n</i> loaded (<c>CockpitClipRegions_Load</c>) and shifted by the rect's
	/// top-left (<see cref="Es1ClipBlock.Shifted"/>); and for <see cref="Horizontal"/> an <see cref="Es1LedBarGraph"/>, for
	/// <see cref="Vertical"/> an <see cref="Es1LedBarGraphV"/>, over the rect with the range <see cref="Full"/>, the block's
	/// two shades and <see cref="UnlitColor"/>, and for any other value no bar.
	///
	/// <para>The <c>GLViewport</c> retail makes for the clip shape whether or not it loads one, <c>EnergyMeter_ClipContext</c>
	/// (<c>000b13f0</c>), is made at each draw here from the canvas context it copies (<see cref="Canvas"/>). A clip file
	/// past the table's four, or one missing or not a clip file, gives no clip shape, an engine guard; no retail
	/// <c>.GAU</c> names one.</para>
	/// </summary>
	/// <param name="read">The volumes' reader, (folder, file name) to a file's bytes, through which the clip file is loaded; none when null.</param>
	internal Es1EnergyMeter(Es1Cockpit cockpit, Es1GauEnergyMeterBlock block, Func<string, string, byte[]?>? read) {
		Cockpit = cockpit;
		Rect = Es1Rect.From(block.Bar).Offset(block.Origin.X, block.Origin.Y);
		if (block.ClipFile > 0 && block.ClipFile < ClipFileNames.Count
				&& read?.Invoke("edg", ClipFileNames[block.ClipFile] + ".EDG") is { } bytes
				&& new Es1ClipRegionsTransformer().Parse(bytes) is { } regions) {
			Clip = Es1ClipBlock.FromFile(regions).Shifted(Rect.X0, Rect.Y0);
		}

		byte even = (byte)block.EvenShade, odd = (byte)block.OddShade;
		Bar = block.Orientation switch {
			Horizontal => new Es1LedBarGraph(Full, Rect) { EvenShade = even, OddShade = odd, UnlitColor = UnlitColor },
			Vertical => new Es1LedBarGraphV(Full, Rect) { EvenShade = even, OddShade = odd, UnlitColor = UnlitColor },
			_ => null,
		};
	}

	/// <summary><c>+0x10</c>, the cockpit the meter draws through.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the bar's rect, which is the gauge's own.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x7e</c>, the bar, null when the block names neither orientation.</summary>
	public IEs1LedBarGraph? Bar { get; }

	/// <summary><c>+0x88</c>'s region list, the clip shape the bar is drawn through, or null for none.</summary>
	public Es1ClipBlock? Clip { get; }

	/// <summary><c>+0x7a</c>, the pool as a Q10 fraction, 0 to <see cref="Full"/>.</summary>
	public int Value { get; private set; }

	/// <summary><c>+0x75</c>, the paint count <see cref="SetValue"/> sets to 2.</summary>
	public byte PaintCount { get; private set; }

	/// <summary>
	/// <c>EnergyMeter_SetValue</c> (<c>00039b78</c>)(gauge, &amp;<paramref name="value"/>): the value stored, and the paint
	/// count set to 2 when it differs from the one there.
	/// </summary>
	public void SetValue(int value) {
		int old = Value;
		Value = value;
		if (value != old) {
			PaintCount = 2;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>EnergyPoolGauge_Paint</c> (<c>00039a28</c>), which <c>CockpitView_InvalidateWidgets</c> runs on
	/// a view change: when <c>CockpitView_RectCornerVisible</c> passes the bar's rect (<see cref="IEs1LedBarGraph.GetRect"/>),
	/// the bar's repaint with the value, through the clip shape when there is one; nothing is copied to the screen. With
	/// no bar this and <see cref="Update"/> do nothing, an engine guard where retail reads through the null bar; every
	/// retail <c>.GAU</c> builds one.
	/// </summary>
	public void Paint() {
		if (Bar == null || !Cockpit.RectCornerVisible(Bar.GetRect())) {
			return;
		}

		Bar.Repaint(Canvas(), Value);
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>EnergyPoolGauge_Update</c> (<c>00039abc</c>), which <c>Cockpit_DrawFrame</c> runs once a
	/// frame: when <c>CockpitView_RectCornerVisible</c> passes the bar's rect and the paint count is 2, the bar's paint
	/// to the value, through the clip shape when there is one, the rect copied to the page on the screen
	/// (<see cref="Es1Cockpit.DrawToCockpit"/>), and the count 0.
	/// </summary>
	public void Update() {
		if (Bar == null || !Cockpit.RectCornerVisible(Bar.GetRect()) || PaintCount != 2) {
			return;
		}

		Bar.PaintToValue(Canvas(), (short)Value);
		Cockpit.DrawToCockpit(Rect);
		PaintCount = 0;
	}

	/// <summary>Slot <c>+0x10</c>, <c>PanelGauge_KeyDispatchNoneEnergyGaugeCopy</c> (<c>00039a1d</c>)(gauge, code): 0, taking no key.</summary>
	public static short KeyDispatch(ushort code) {
		_ = code;
		return 0;
	}

	/// <summary>
	/// The context the bar draws through: the canvas context as pushed, or with a clip shape <c>EnergyMeter_ClipContext</c>
	/// given the canvas context's rect and origin, clip mode 2 on <see cref="Clip"/> and the translation set.
	/// </summary>
	private Es1PanelCanvas Canvas() {
		var canvas = Cockpit.PushCanvasContext();
		return Clip == null ? canvas : new Es1PanelCanvas(canvas.Display, canvas.Rect, canvas.Origin) { Regions = Clip };
	}
}
