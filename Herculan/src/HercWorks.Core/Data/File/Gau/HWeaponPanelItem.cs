using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// One weapon-row rect of <see cref="GAUFile.Weapons"/>. See docs/retail/simulation/cockpit-hud-widgets.md,
/// "Weapon hardpoint rows".
/// </summary>
public class HWeaponPanelItem : WidgetBase {
	public HWeaponPanelItem() { }

	public HWeaponPanelItem(PixelPoint org, PixelSize size) {
		Origin = org;
		Size = size;
	}

	public override string ToString() {
		string name = HWidgetId != null ? HWidgetId.Name : GetType().Name;
		return $"{name} [origin=({Origin.X},{Origin.Y}), size=({Size.Width},{Size.Height})]";
	}
}
