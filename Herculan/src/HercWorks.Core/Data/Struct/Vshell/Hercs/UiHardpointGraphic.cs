using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.Core.Data.Struct.Vshell.Hercs;

/// <summary>
/// A screen-layout record with its part id, and — in the <c>gam\arm_*.dat</c> form — a second
/// corner. See <c>docs/retail/formats/herc-catalogs.md#the-screen-layout-families</c>.
/// </summary>
public class UiHardpointGraphic : UiImageDBA {
	/// <summary>
	/// The record's leading <c>int16</c>. In <c>arm_*.dat</c> and <c>rpr_*.dat</c> it is the part id
	/// within its picture — a fitted weapon's part is looked up by mount slot + 2 and slot + 6
	/// respectively; in <c>arm_weap.dat</c> it is the weapon id or guidance kind the panel shows.
	/// </summary>
	public short Id { get; set; }

	/// <summary>Not in the file: a sheet a caller attaches for the frame <see cref="OutlineX"/>/<see cref="OutlineY"/> place.</summary>
	public DynamixBitmapArray? OutlineImg { get; set; }

	/// <summary>
	/// The <c>arm_*.dat</c> record's second x, y — where the arming screen draws a mount's frame from
	/// the chassis's <c>_out</c> bank. Absent from the other layout files.
	/// </summary>
	public int OutlineX { get; set; }
	public int OutlineY { get; set; }

	public override string ToString() {
		return "UiHardpointGraphic [hardpointId=" + Id
			 + ", originX=" + OriginX
			 + ", originY=" + OriginY
			 + ", outlineX=" + OutlineX
			 + ", outlineY=" + OutlineY
			 + ", frameId=" + FrameId + ", blitFlags=" + BlitFlags + "]";
	}
}
