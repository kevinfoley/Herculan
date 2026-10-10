namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// Top-left of the front window's floating scanner repeater — content offset 1196, two more ints of
/// the "roving gunsight" block that <c>Gau_RovingGunsightWidget</c> (<c>0043c7d8</c>) reads straight
/// into the widget at <c>+0x10b</c>/<c>+0x10f</c>, right after the four rects
/// <see cref="HGunsightArea"/> covers.
///
/// Like <see cref="HReticle"/> this is a bare (X,Y) point rather than a corner rect —
/// <see cref="WidgetBase.Size"/> is unused. The repeater's extent is not in the file: its paint
/// (<c>HudScanner_Paint</c>, <c>0043f2b0</c>) squares off <c>0x2e</c> units from this point on both axes.
///
/// Position varies per herc; see docs/retail/simulation/mfd-scanner.md, "Geometry".
///
/// Read out of <see cref="GAUFile.Remainder"/>, which is still what the write path emits.
/// </summary>
public class HHudScanner : WidgetBase {
}
