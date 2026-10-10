namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The front-window HUD's **target-indicator area** — content offset 1148, the last rect the
/// "roving gunsight" complex's constructor (`Gau_RovingGunsightWidget`, `0043c7d8`) reads, and the
/// only one three of its children share: the clickable gunsight surface (child 0), the target-box
/// child (child 5) and its never-painted sibling (child 6).
///
/// What makes it load-bearing is child 5's paint (`Gunsight_TargetIndicatorPaint (0043b950)`): when the selected target does
/// **not** project inside this rect, the box is replaced by an arrow pointing at it, and the arrow
/// is placed where the line from the reticle to the target crosses this rect's border. So the rect
/// is what keeps the off-screen arrow clear of the canopy — every retail file sets it well inside
/// the cockpit's window opening (APOCA `66,0 - 253,146`, RAZOR `55,68 - 264,186`, in the file's own
/// 320-wide space).
///
/// Read out of <see cref="GAUFile.Remainder"/>, which is still what the write path emits.
/// See `Herculan.Engine.Content.TargetBox` and docs/retail/simulation/cockpit-gunsight-hud.md.
/// </summary>
public class HGunsightArea : WidgetBase {
}
