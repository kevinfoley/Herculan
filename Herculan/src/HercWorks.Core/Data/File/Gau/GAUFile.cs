using HercWorks.Core.Data.Struct;
using HercWorks.Vol;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// <c>SIMVOL0\GAU\&lt;herc&gt;.GAU</c> — one herc's cockpit widget layout, a fixed 1700-byte
/// (<c>0x6a4</c>) record of rects in the 320-wide HUD space. A rect is four <c>int32</c>s
/// <c>X1,Y1,X2,Y2</c> (top-left, bottom-right), which
/// <see cref="Io.Transform.Dbsim.GauFileTransformer"/> converts to each widget's Origin/Size. Who
/// reads which offset is in docs/retail/formats/cockpit-hud-widgets.md, ".GAU widget tree", and the docs it
/// links. Offsets are content offsets:
///   0 - <see cref="HudOrigin"/>, an origin offset added to every widget rect; (0,0) in retail.
///   8 - <see cref="HudScreenSize"/>, e.g. (320,400).
///   16 - <see cref="WeaponListTotal"/>, how many of the 10 slots below are in use.
///   20 - <see cref="Weapons"/>, 10 weapon-row rects (5 left column, 5 right). Unused slots hold the
///     sentinel rect (100,140,155,146) rather than zeros.
///   180-467 - zero in every retail file.
///   468 - the console-button record, the offset its constructor is handed; its first 16 bytes are
///     zero in every retail file.
///   484, 500, 516 - <see cref="ChainButton"/>, <see cref="LinkButton"/>, <see cref="AutoTrackButton"/>.
///   532 - a fourth console button's rect, zero in every retail file.
///   548 - the energy meter's record; 16 bytes, zero in every retail file.
///   564 - <see cref="EnergyMeter"/>.
///   580-615 - zero in every retail file.
///   616 - <see cref="ShieldDisplay"/>, 80 bytes. RAZOR's bytes parse through the same struct but its
///     cockpit has an altimeter here instead.
///   696 - <see cref="RemainderBeforeMfdPanel"/>, 256 bytes, zero in every retail file. 728 starts
///     the MFD block: an origin offset, then 13 rect-shaped slots no constructor reads
///     (docs/retail/formats/mfd.md, "Geometry").
///   952 - <see cref="MfdPanel"/>.
///   968-1015 - zero in every retail file; 1000 starts the throttle gauge's record, whose first two
///     ints are an origin offset.
///   1016 - <see cref="Throttle"/>, 48 bytes: the track rect, then the forward and reverse fill-bar
///     rects.
///   1064 - <see cref="RemainderBeforeHeadingTape"/>, 40 bytes:
///     - 1064: <see cref="HThrottle.SlideMode"/>.
///     - 1068: not read by the throttle's constructor.
///     - 1072: <see cref="HThrottle.TickOffsetX"/>.
///     - 1076-1087: zero in every retail file.
///     - 1088-1103: the head of the gunsight complex's record — an origin offset, then the complex's
///       own bottom-right (320, 117 or 157). See docs/retail/formats/cockpit-gunsight-hud.md.
///   1104 - <see cref="HeadingTape"/>.
///   1120 - <see cref="RemainderBeforeReticle"/>, 16 bytes: the time readout's anchor at 1120 and the
///     speed caption's at 1128 (docs/retail/formats/cockpit-gunsight-hud.md, "Speed and time readouts").
///   1136 - <see cref="Reticle"/>, a single (X,Y) point.
///   1144 - <see cref="Remainder"/>, 556 bytes to the end of the file:
///     - 1144: the half-extent of the reticle child's rect about <see cref="Reticle"/>; zero in
///       every retail file, and unread by that child's paint.
///     - 1148-1163: <see cref="GunsightArea"/>.
///     - 1164-1179: <see cref="AutoTrackLegend"/>.
///     - 1180-1195: the gunsight complex's second label, which neither of its paints writes to.
///     - 1196-1203: <see cref="HudScanner"/>.
///     - 1204-1211: no widget constructor reads this span.
///     - 1212-~1589: the Heads-Down Display's block (<c>HddDisplay_Ctor</c>, <c>00448cc8</c>); see
///       docs/retail/formats/heads-down-display.md, ".GAU block at 1212".
///     - 1664: <see cref="HPilotMessagePort.TrainingLift"/>.
///     - 1668-1683: <see cref="PilotMessagePort"/>.
///     - 1684-1699: <see cref="MessageTicker"/>.
///
/// <para>There is no navigation-bar or compass widget in the file: the heading display is the
/// gunsight complex's heading tape at 1104.</para>
///
/// <para>The three raw remainders are written back verbatim, so the transformer round-trips all nine
/// retail files byte-exact. The widgets surfaced from inside them are copies; editing one does not
/// change what is written.</para>
/// </summary>
public class GAUFile {
	public PixelPoint HudOrigin { get; set; }
	public PixelSize HudScreenSize { get; set; }

	public int WeaponListTotal { get; set; }
	public HWeaponPanelItem[]? Weapons { get; set; }

	public HButtonBasic? ChainButton { get; set; }
	public HButtonBasic? LinkButton { get; set; }
	public HButtonBasic? AutoTrackButton { get; set; }
	public HMeter? EnergyMeter { get; set; }
	public HShieldDisplay? ShieldDisplay { get; set; }

	/// <summary>Content offsets 696-951, kept raw — see the class doc comment.</summary>
	public byte[]? RemainderBeforeMfdPanel { get; set; }

	public HMfdPanel? MfdPanel { get; set; }

	public HThrottle? Throttle { get; set; }

	/// <summary>Content offsets 1064-1103, kept raw — see the class doc comment.</summary>
	public byte[]? RemainderBeforeHeadingTape { get; set; }

	public HHeadingTape? HeadingTape { get; set; }

	/// <summary>Content offsets 1120-1135, kept raw — see the class doc comment.</summary>
	public byte[]? RemainderBeforeReticle { get; set; }

	public HReticle? Reticle { get; set; }

	/// <summary>Content offset 1144 to the end of the file, kept raw — see the class doc comment.</summary>
	public byte[]? Remainder { get; set; }

	/// <summary>
	/// The gunsight complex's target-indicator area at content offset 1148 - see
	/// <see cref="HGunsightArea"/>. Surfaced from <see cref="Remainder"/>, which still carries the
	/// same bytes and is what the write path emits.
	/// </summary>
	public HGunsightArea? GunsightArea { get; set; }

	/// <summary>
	/// The ATT legend's box at content offset 1164 - see <see cref="HAutoTrackLegend"/>. Surfaced from
	/// <see cref="Remainder"/> the same way <see cref="GunsightArea"/> is.
	/// </summary>
	public HAutoTrackLegend? AutoTrackLegend { get; set; }

	/// <summary>
	/// The floating scanner repeater's top-left at content offset 1196 - see
	/// <see cref="HHudScanner"/>. Surfaced from <see cref="Remainder"/> the same way
	/// <see cref="GunsightArea"/> is.
	/// </summary>
	public HHudScanner? HudScanner { get; set; }

	/// <summary>
	/// The pilot and squad channel's message box at content offset 1668 - see
	/// <see cref="HPilotMessagePort"/>. Surfaced from <see cref="Remainder"/> the same way
	/// <see cref="GunsightArea"/> is.
	/// </summary>
	public HPilotMessagePort? PilotMessagePort { get; set; }

	/// <summary>
	/// The cockpit message ticker's box at content offset 1684, the file's last field - see
	/// <see cref="HMessageTicker"/>. Surfaced from <see cref="Remainder"/> the same way
	/// <see cref="GunsightArea"/> is.
	/// </summary>
	public HMessageTicker? MessageTicker { get; set; }
}
