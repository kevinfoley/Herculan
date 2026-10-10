namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The front-window HUD's <b>ATT legend</b> box — content offset 1164, the rect the gunsight
/// complex's constructor (<c>Gau_RovingGunsightWidget</c>, <c>0043c7d8</c>) gives the first of its
/// two text labels. While Automatic Turret Tracking is on, the complex's paint puts a plate and the
/// word <c>ATT</c> in it. See docs/retail/simulation/cockpit-gunsight-hud.md, "The ATT legend".
///
/// <para>Read out of <see cref="GAUFile.Remainder"/>, which is still what the write path emits.</para>
/// </summary>
public class HAutoTrackLegend : WidgetBase {
}
