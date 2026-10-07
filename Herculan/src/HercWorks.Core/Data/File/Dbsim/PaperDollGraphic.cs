using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/PDG/{herc}.PDG — the paper-doll damage diagram: three views, each an origin/size
/// pair and a list of damage regions over that view's frame of <c>{herc}.HBA</c>/<c>.DBA</c>, then
/// the weapon-icon anchors. Coordinates are in the 320-wide space. Read by <c>PaperDoll_Load</c>
/// (<c>004379cc</c>); see docs/retail/formats/cockpit-hud-widgets.md#pdg--paper-doll-damage-diagram.
/// </summary>
public class PaperDollGraphic {
	public int TotalViews { get; set; }
	public ViewEntry[]? Entries { get; set; }
	public HardpointEntry[]? Hardpoints { get; set; }

	public ViewEntry NewViewEntry() => new();
	public ViewRegion NewViewRegion() => new();
	public HardpointEntry NewHardpointEntry() => new();

	public class ViewEntry {
		public PixelPoint Origin { get; set; }
		public PixelPoint Size { get; set; }
		public ViewRegion[]? Regions { get; set; }
	}

	/// <summary>One damage region: a rect whose pixels of <see cref="ColorId"/> are recoloured by condition.</summary>
	public class ViewRegion {
		public int Index { get; set; }
		public PixelPoint TopLeft { get; set; }
		public PixelPoint BottomRight { get; set; }

		/// <summary>The <c>COLORS.DAT</c> id the art drew this body part in — the pixels the tint replaces.</summary>
		public int ColorId { get; set; }

		/// <summary>Recolour mode, 0-3. 0 in every retail region. See docs/retail/formats/cockpit-hud-widgets.md#tinting.</summary>
		public int RecolorMode { get; set; }
	}

	/// <summary>
	/// Where one weapon icon sits around the doll — entry <c>n</c> places the icon of the hardpoint
	/// whose <c>.GL</c> slot byte (<see cref="GunLayout.HardpointEntry.LoadoutSlot"/>) is <c>n</c>.
	/// See docs/retail/formats/cockpit-hud-widgets.md#weapon-icons.
	/// </summary>
	public class HardpointEntry {
		/// <summary>The anchor point, relative to the view's origin, in the 320-wide space.</summary>
		public PixelPoint Origin { get; set; }

		/// <summary>Added to the weapon's own icon index to pick the <c>WEAPONS</c> frame.</summary>
		public int FrameOffset { get; set; }

		/// <summary>
		/// Which point of the icon lands on <see cref="Origin"/>: the low three bits choose the
		/// horizontal edge (1 left, 2 right, 4 centre) and the rest the vertical (8 top, 0x10 bottom,
		/// 0x20 centre).
		/// </summary>
		public int Alignment { get; set; }

		/// <summary>The icon's blit flags. 0 in every retail file.</summary>
		public int BlitFlags { get; set; }
	}
}
