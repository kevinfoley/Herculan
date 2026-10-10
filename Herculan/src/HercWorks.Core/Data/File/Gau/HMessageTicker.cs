namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The cockpit message ticker's box — content offset 1684, the file's last sixteen bytes — read as a
/// rect by the <c>.GAU</c> loader's caller (<c>Gau_BuildCockpitWidgets</c>, <c>00431bf8</c>) and
/// handed to the port's constructor (<c>MessagePort_Ctor</c>, <c>004369a4</c>). This is the
/// scrolling one-line ticker the cockpit computer writes to; the rect before it is the second port
/// of the same class, <see cref="HPilotMessagePort"/>. A 120x9 box horizontally centred on the
/// 320-wide screen in every retail file. See docs/retail/simulation/cockpit-messages.md, "The ticker".
///
/// <para>Read out of <see cref="GAUFile.Remainder"/>, which is still what the write path emits.</para>
/// </summary>
public class HMessageTicker : WidgetBase {
}
