using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The Master Energy Pool meter, content offset 564: a vertical LED bar graph that
/// <c>EnergyPoolGauge_Ctor</c> (<c>00444d5c</c>) builds over this rect. See
/// docs/retail/simulation/cockpit-hud-widgets.md, "LED gauges".
/// </summary>
public class HMeter : WidgetBase {
	public HMeter() { }

	public HMeter(PixelPoint origin, PixelPoint extent) {
		Origin = origin;
		Size = new PixelSize(extent.X - origin.X, extent.Y - origin.Y);
	}
}
