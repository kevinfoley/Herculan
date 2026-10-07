namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The front-window HUD's heading tape rect, content offset 1104 — the first rect the gunsight
/// complex's constructor (<c>Gau_RovingGunsightWidget</c>, <c>0043c7d8</c>) reads, handed to
/// <c>HudHeadingTape_Ctor</c> (<c>0043b57c</c>). The rotation indicator and both waypoint indicators
/// have no rect of their own and are derived from this one. 120x17, horizontally centred, in every
/// retail file. See docs/retail/formats/cockpit-gunsight-hud.md, "Heading tape".
/// </summary>
public class HHeadingTape : WidgetBase {
}
