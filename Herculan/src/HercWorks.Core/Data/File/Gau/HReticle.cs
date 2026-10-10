namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The aiming reticle's position, content offset 1136: a bare (X,Y) point, not a rect, so
/// <see cref="WidgetBase.Size"/> is unused. It is also the <c>.VUE</c> projection centre. See
/// docs/retail/simulation/hud-target-indicator.md, "Child 4 — the reticle".
/// </summary>
public class HReticle : WidgetBase {
}
