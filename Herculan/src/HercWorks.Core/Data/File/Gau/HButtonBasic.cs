using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// A console button's rect — chain, link or auto-track. See docs/retail/formats/cockpit-hud-widgets.md,
/// "Console buttons".
/// </summary>
public class HButtonBasic : WidgetBase {
	/// <summary>Not in the file; the transformer reads a button as a plain rect and leaves this unset.</summary>
	public PixelPoint LabelOfs { get; set; }

	public HButtonBasic() { }

	public HButtonBasic(PixelPoint origin, PixelPoint labelOfs) {
		Origin = origin;
		LabelOfs = labelOfs;
	}

	public override string ToString() {
		string name = HWidgetId != null ? HWidgetId.Name : GetType().Name;
		return $"{name} [origin=({Origin.X},{Origin.Y}), labelOfs=({LabelOfs.X},{LabelOfs.Y})]";
	}
}
