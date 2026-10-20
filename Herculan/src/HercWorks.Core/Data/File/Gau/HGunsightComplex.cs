namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The front-window HUD's own rect — content offset 1088, the head of the record the "roving gunsight" complex's
/// constructor (<c>Gau_RovingGunsightWidget</c>, <c>0043c7d8</c>) reads: an origin, then the complex's bottom-right.
/// Every child's rect is measured from the origin, and the gunsight drag arms inside the whole rect. See
/// docs/retail/formats/gau-cockpit-layout.md#gau-block-at-1088 and docs/retail/simulation/gunsight-drag.md.
///
/// <para>Read out of <see cref="GAUFile.RemainderBeforeHeadingTape"/>, which is still what the write path emits.</para>
/// </summary>
public class HGunsightComplex : WidgetBase {
}
