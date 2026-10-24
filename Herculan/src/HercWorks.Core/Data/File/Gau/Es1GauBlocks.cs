using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// The ammunition row's block at 164 of <see cref="Es1GauFile"/> (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2).
/// Its parts' rects are relative to the row's top-left.
/// </summary>
public class Es1GauAmmunitionRowBlock {
	/// <summary>Offset 164: the scratch rect the factory copies a row's rect to before building the row's gauge.</summary>
	public Es1GauRect Scratch { get; set; }

	/// <summary>Offset 180: the select button's rect.</summary>
	public Es1GauRect SelectButton { get; set; }

	/// <summary>Offset 196: the round count's rect.</summary>
	public Es1GauRect RoundCount { get; set; }

	/// <summary>Offsets 212-243, which the doc does not name.</summary>
	public byte[] Bytes212 { get; set; } = new byte[32];
}

/// <summary>
/// The energy row's block at 244 of <see cref="Es1GauFile"/> (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2).
/// Its parts' rects are relative to the row's top-left.
/// </summary>
public class Es1GauEnergyRowBlock {
	/// <summary>Offset 244: the scratch rect the factory copies a row's rect to before building the row's gauge.</summary>
	public Es1GauRect Scratch { get; set; }

	/// <summary>Offset 260: the select button's rect.</summary>
	public Es1GauRect SelectButton { get; set; }

	/// <summary>Offset 276: the charge bar's rect.</summary>
	public Es1GauRect ChargeBar { get; set; }

	/// <summary>Offsets 292-303, which the doc does not name.</summary>
	public byte[] Bytes292 { get; set; } = new byte[12];

	/// <summary>
	/// Offset 304: the <c>int32</c> the gauge hands the charge bar's constructor (docs/es1/simulation/cockpit-weapon-gauges.md#the-energy-row).
	/// </summary>
	public int ChargeBarArgument { get; set; }

	/// <summary>Offsets 308-339, which the doc does not name.</summary>
	public byte[] Bytes308 { get; set; } = new byte[32];
}

/// <summary>The pod row's block at 340 of <see cref="Es1GauFile"/> (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2).</summary>
public class Es1GauPodRowBlock {
	/// <summary>Offset 340: the scratch rect the factory copies a row's rect to before building the row's gauge.</summary>
	public Es1GauRect Scratch { get; set; }

	/// <summary>Offset 356: the select button's rect.</summary>
	public Es1GauRect SelectButton { get; set; }

	/// <summary>Offset 372: the label's rect, the first of the three <c>Gau_Load</c> constructs from 372.</summary>
	public Es1GauRect Label { get; set; }
}

/// <summary>The console buttons' block at 452 of <see cref="Es1GauFile"/> (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2).</summary>
public class Es1GauConsoleButtonsBlock {
	/// <summary>Offset 452: the 16-byte header, which <see cref="Es1GauFile.Loaded"/> offsets as a rect.</summary>
	public Es1GauRect Header { get; set; }

	/// <summary>Offset 468: the five button rects.</summary>
	public Es1GauRect[] Buttons { get; set; } = new Es1GauRect[Es1GauFile.ConsoleButtonCount];
}

/// <summary>
/// An instrument's block of <see cref="Es1GauFile"/> whose bytes the doc names only as a block
/// (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2): its first 16 bytes, which <see cref="Es1GauFile.Loaded"/>
/// offsets as a rect, and the rest kept whole.
/// </summary>
public class Es1GauInstrumentBlock {
	/// <summary>The block's first 16 bytes.</summary>
	public Es1GauRect Header { get; set; }

	/// <summary>The block's bytes past <see cref="Header"/>.</summary>
	public byte[] Rest { get; set; } = [];
}

/// <summary>
/// The block at 728 of <see cref="Es1GauFile"/>, which <c>Gau_MfdPanelWidget</c> (<c>0002cdac</c>) hands its instrument
/// (docs/es1/simulation/cockpit-panel.md#building-it), with the 13 rects <c>Gau_Load</c> constructs at 744.
/// </summary>
public class Es1GauMfdPanelBlock {
	/// <summary>Offset 728: the block's first 16 bytes, which <see cref="Es1GauFile.Loaded"/> offsets as a rect.</summary>
	public Es1GauRect Header { get; set; }

	/// <summary>Offset 744: thirteen rects.</summary>
	public Es1GauRect[] Rects744 { get; set; } = new Es1GauRect[13];

	/// <summary>Offsets 952-999, which the doc does not name.</summary>
	public byte[] Bytes952 { get; set; } = new byte[48];
}

/// <summary>
/// The block at 1212 of <see cref="Es1GauFile"/>, which <c>Gau_PilotRosterWidget</c> (<c>0002ce60</c>) hands the instrument at
/// the cockpit's <c>+0x1f5</c> (docs/es1/formats/gau-cockpit-layout.md#differences-from-es2), with the rect arrays
/// <c>Gau_Load</c> constructs at 1292 and 1532.
/// </summary>
public class Es1GauPilotRosterBlock {
	/// <summary>Offset 1212: the block's first 16 bytes, which <see cref="Es1GauFile.Loaded"/> offsets as a rect.</summary>
	public Es1GauRect Header { get; set; }

	/// <summary>Offsets 1228-1291, which the doc does not name.</summary>
	public byte[] Bytes1228 { get; set; } = new byte[64];

	/// <summary>Offset 1292: fifteen rects.</summary>
	public Es1GauRect[] Rects1292 { get; set; } = new Es1GauRect[15];

	/// <summary>Offset 1532: three rects.</summary>
	public Es1GauRect[] Rects1532 { get; set; } = new Es1GauRect[3];

	/// <summary>Offsets 1580-1603, which the doc does not name.</summary>
	public byte[] Bytes1580 { get; set; } = new byte[24];
}

/// <summary>
/// The cockpit clock's block at 1604 of <see cref="Es1GauFile"/>, which <c>CockpitClock_Create</c> (<c>0002ce98</c>) hands
/// <c>CockpitClock_Ctor</c> (<c>000433cc</c>) (docs/es1/formats/ttm-scripts.md#the-cockpit-clock): the constructor reads
/// the rect at 1620 alone, as the gauge base's rect and its three labels'.
/// </summary>
public class Es1GauClockBlock {
	/// <summary>Offsets 1604-1619, which the clock does not read.</summary>
	public byte[] Bytes1604 { get; set; } = new byte[16];

	/// <summary>Offset 1620: the clock's rect, the block + <c>0x10</c>.</summary>
	public Es1GauRect Rect { get; set; }

	/// <summary>Offsets 1636-1667, which the clock does not read.</summary>
	public byte[] Bytes1636 { get; set; } = new byte[32];
}

/// <summary>
/// The throttle's block at 1000 of <see cref="Es1GauFile"/>, the <c>int32</c> from 1000 to 1071 that
/// <c>ThrottleGauge_Ctor</c> (<c>0003b0d4</c>) and the two slider constructors read
/// (docs/es1/formats/gau-cockpit-layout.md#the-throttle-block-at-1000), then 32 bytes they do not.
/// </summary>
public class Es1GauThrottleBlock {
	/// <summary><c>int32</c> 0 and 1, offset 1000: the origin, added to every rect below.</summary>
	public PixelPoint Origin { get; set; }

	/// <summary><c>int32</c> 2, offset 1008: unread by the constructors.</summary>
	public int Int2 { get; set; }

	/// <summary><c>int32</c> 3, offset 1012: unread by the constructors.</summary>
	public int Int3 { get; set; }

	/// <summary><c>int32</c> 4 to 7, offset 1016: the track's rect, which is the slider's own.</summary>
	public Es1GauRect Track { get; set; }

	/// <summary><c>int32</c> 8 to 0xb, offset 1032: the forward bar's rect.</summary>
	public Es1GauRect ForwardBar { get; set; }

	/// <summary><c>int32</c> 0xc to 0xf, offset 1048: the reverse bar's rect.</summary>
	public Es1GauRect ReverseBar { get; set; }

	/// <summary>
	/// <c>int32</c> 0x10, offset 1064: 1 builds the vertical slider and any other value the horizontal one; also the index of
	/// the bars' picture among the light names at <c>0009328c</c>.
	/// </summary>
	public int Lights { get; set; }

	/// <summary><c>int32</c> 0x11, offset 1068: the knob's frame in <c>dba\pbuttons</c>.</summary>
	public int KnobFrame { get; set; }

	/// <summary>Offsets 1072-1103, which the constructors do not read.</summary>
	public byte[] Bytes1072 { get; set; } = new byte[32];
}

/// <summary>
/// The gunsight's block at 1104 of <see cref="Es1GauFile"/>, the <c>int32</c> from 1104 to 1179 that
/// <c>Gau_RovingGunsightWidget</c> (<c>00034aec</c>) reads (docs/es1/formats/gau-cockpit-layout.md#the-gunsight-block-at-1104),
/// then 32 bytes it does not.
/// </summary>
public class Es1GauGunsightBlock {
	/// <summary><c>int32</c> 0 and 1, offset 1104: the origin, added to every child's rect but the reticle's centre.</summary>
	public PixelPoint Origin { get; set; }

	/// <summary><c>int32</c> 2, offset 1112: unread by the builder.</summary>
	public int Int2 { get; set; }

	/// <summary><c>int32</c> 3, offset 1116: unread by the builder.</summary>
	public int Int3 { get; set; }

	/// <summary><c>int32</c> 4 to 7, offset 1120: the heading tape's rect, which the rotation indicator's and the waypoint indicator's derive from.</summary>
	public Es1GauRect HeadingTape { get; set; }

	/// <summary><c>int32</c> 8 to 0xb, offset 1136: the vertical slide bar's rect.</summary>
	public Es1GauRect SlideBar { get; set; }

	/// <summary><c>int32</c> 0xc and 0xd, offset 1152: the reticle's centre, where the cursor is put.</summary>
	public PixelPoint ReticleCentre { get; set; }

	/// <summary>
	/// <c>int32</c> 0xe, offset 1160: the half-size of the square about <see cref="ReticleCentre"/> that the reticle, the
	/// target box and child 6 take as their rect.
	/// </summary>
	public int ReticleHalfSize { get; set; }

	/// <summary><c>int32</c> 0xf to 0x12, offset 1164: the click surface's rect.</summary>
	public Es1GauRect ClickSurface { get; set; }

	/// <summary>Offsets 1180-1211, which the builder does not read.</summary>
	public byte[] Bytes1180 { get; set; } = new byte[32];
}
