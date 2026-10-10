using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from .GAU HUD-layout files (see <see cref="GAUFile"/> for the
/// format writeup). New: the Java source had a data model with a detailed doc-comment byte layout
/// but no transformer at all — this implements and verifies that layout against real retail data
/// for the portion that held up (offset 0-691 plus the confirmed <see cref="HMfdPanel"/> at 952 and
/// <see cref="HThrottle"/> at 1016), and preserves the rest as raw bytes rather than guessing past
/// where real bytes stopped lining up with a confirmed layout.
///
/// Worth noting for future work: the Java `GAUFile.java` doc comment turned out to already name
/// both `HMfdPanel`'s offset (`"952- PANEL\MFD"`) and `HThrottle`'s (`"1016- SLIDER\THROTTLE\"`)
/// exactly — confirmed independently via user screenshot measurements and
/// `.HB0` cockpit-art overlays before this correlation was checked. The same Java comment also
/// names offset 1088 (`"PANEL\NAVBAR"`), 1104 (`"INDICATOR\TORSO_TWIST"`), and 1136 (`"RETICLE"`)
/// as further unverified leads within <see cref="GAUFile.Remainder"/>.
///
/// <see cref="Write"/> round-trips byte-exact against all 9 real herc `.GAU` files despite
/// Remainder being undecoded — decoding isn't required to round-trip it, since it's captured and
/// written back verbatim.
/// </summary>
public class GauFileTransformer : ByteTransformer<GAUFile> {
	private const int WeaponSlotCount = 10;
	/// <summary>
	/// Bytes between the end of the ShieldsGauge block (offset 696) and the start of MfdPanel's rect
	/// (offset 952) — confirmed all-zero for every real file, kept raw rather than hardcoded zero.
	/// </summary>
	/// </summary>
	private const int RemainderBeforeMfdPanelLength = 256;

	/// <summary>Bytes between the end of Throttle (offset 1064) and the start of HeadingTape (offset 1104) — see the GAUFile class doc comment.</summary>
	private const int RemainderBeforeHeadingTapeLength = 40;

	/// <summary>Bytes between the end of HeadingTape (offset 1120) and the start of Reticle (offset 1136) — the two readout anchors.</summary>
	private const int RemainderBeforeReticleLength = 16;

	/// <summary>Byte offset of <see cref="HGunsightArea"/>'s rect inside <see cref="GAUFile.Remainder"/>, which starts at content offset 1144.</summary>
	private const int GunsightAreaRemainderOffset = 4;

	/// <summary>And <see cref="HAutoTrackLegend"/>'s rect, content offset 1164 inside the same remainder.</summary>
	private const int AutoTrackLegendRemainderOffset = 20;

	/// <summary>And <see cref="HHudScanner"/>'s point, content offset 1196 inside the same remainder.</summary>
	private const int HudScannerRemainderOffset = 52;

	/// <summary>And <see cref="HPilotMessagePort"/>'s rect, content offset 1668.</summary>
	private const int PilotMessagePortRemainderOffset = 524;

	/// <summary>And <see cref="HMessageTicker"/>'s rect, content offset 1684 — the file's last field.</summary>
	private const int MessageTickerRemainderOffset = 540;

	public override GAUFile? Parse(byte[]? inputArray) {
		if (inputArray == null) {
			return null;
		}

		SetBytes(inputArray);

		var gau = new GAUFile {
			HudOrigin = new PixelPoint(IndexIntLE(), IndexIntLE()),
			HudScreenSize = new PixelSize(IndexIntLE(), IndexIntLE()),
			WeaponListTotal = IndexIntLE(),
		};

		var weapons = new HWeaponPanelItem[WeaponSlotCount];
		for (int i = 0; i < WeaponSlotCount; i++) {
			weapons[i] = ReadRect(() => new HWeaponPanelItem());
		}
		gau.Weapons = weapons;

		Skip(288); // offset 180-467, zero in every retail file; holds the weapon gauge factories' runtime scratch slots — see GAUFile's doc comment.

		Skip(16); // confirmed always (0,0,0,0) container rect at offset 468 — see class doc comment.

		gau.ChainButton = ReadRect(() => new HButtonBasic());
		gau.LinkButton = ReadRect(() => new HButtonBasic());
		gau.AutoTrackButton = ReadRect(() => new HButtonBasic());

		Skip(32); // 2 more confirmed always-zero "null widget" slots, offset 532/548.

		gau.EnergyMeter = ReadRect(() => new HMeter());

		Skip(36); // confirmed always-zero, offset 580-615 (two null widget slots plus 4 trailing bytes).

		gau.ShieldDisplay = ReadShieldDisplay();

		gau.RemainderBeforeMfdPanel = IndexSegment(RemainderBeforeMfdPanelLength);

		gau.MfdPanel = ReadRect(() => new HMfdPanel());

		Skip(48); // 3 more confirmed always-zero "null widget" slots, offset 968/984/1000.

		gau.Throttle = ReadThrottle();

		gau.RemainderBeforeHeadingTape = IndexSegment(RemainderBeforeHeadingTapeLength);

		// Two of the remainder's ints belong to the throttle — the gauge constructor reads offsets
		// 1064 and 1072 straight out of the same widget record it reads the track rect from. They are
		// surfaced on the widget but left in the remainder as well, so ObjectToBytes stays the verbatim
		// write-back it was and the round-trip is untouched.
		gau.Throttle.SlideMode = IntLE(gau.RemainderBeforeHeadingTape, 0);
		gau.Throttle.TickOffsetX = IntLE(gau.RemainderBeforeHeadingTape, 8);

		gau.HeadingTape = ReadRect(() => new HHeadingTape());

		gau.RemainderBeforeReticle = IndexSegment(RemainderBeforeReticleLength);

		gau.Reticle = new HReticle { Origin = new PixelPoint(IndexIntLE(), IndexIntLE()) };

		gau.Remainder = IndexSegment(inputArray.Length - Index);

		// Offset 1148, four ints into the remainder past the offset-1144 int the gunsight complex
		// hands its reticle child. Surfaced on its own widget and left in the remainder as well, the
		// same way the throttle's two ints above are, so ObjectToBytes stays a verbatim write-back.
		int areaX0 = IntLE(gau.Remainder, GunsightAreaRemainderOffset);
		int areaY0 = IntLE(gau.Remainder, GunsightAreaRemainderOffset + 4);
		int areaX1 = IntLE(gau.Remainder, GunsightAreaRemainderOffset + 8);
		int areaY1 = IntLE(gau.Remainder, GunsightAreaRemainderOffset + 12);
		gau.GunsightArea = new HGunsightArea {
			Origin = new PixelPoint(areaX0, areaY0),
			Size = new PixelSize(areaX1 - areaX0, areaY1 - areaY0),
		};

		// Offset 1164, straight after it: the ATT legend's box — see HAutoTrackLegend. Surfaced the
		// same way, left in the remainder.
		int legendX0 = IntLE(gau.Remainder, AutoTrackLegendRemainderOffset);
		int legendY0 = IntLE(gau.Remainder, AutoTrackLegendRemainderOffset + 4);
		int legendX1 = IntLE(gau.Remainder, AutoTrackLegendRemainderOffset + 8);
		int legendY1 = IntLE(gau.Remainder, AutoTrackLegendRemainderOffset + 12);
		gau.AutoTrackLegend = new HAutoTrackLegend {
			Origin = new PixelPoint(legendX0, legendY0),
			Size = new PixelSize(legendX1 - legendX0, legendY1 - legendY0),
		};

		// Offset 1196, eight ints further on: the floating scanner repeater's top-left, a bare
		// point with no size — see HHudScanner. Surfaced the same way, left in the remainder.
		gau.HudScanner = new HHudScanner {
			Origin = new PixelPoint(
				IntLE(gau.Remainder, HudScannerRemainderOffset),
				IntLE(gau.Remainder, HudScannerRemainderOffset + 4)),
		};

		// Offset 1668, the four ints before those: the pilot and squad channel's box — see
		// HPilotMessagePort. Surfaced the same way as the two above.
		int pilotX0 = IntLE(gau.Remainder, PilotMessagePortRemainderOffset);
		int pilotY0 = IntLE(gau.Remainder, PilotMessagePortRemainderOffset + 4);
		int pilotX1 = IntLE(gau.Remainder, PilotMessagePortRemainderOffset + 8);
		int pilotY1 = IntLE(gau.Remainder, PilotMessagePortRemainderOffset + 12);
		gau.PilotMessagePort = new HPilotMessagePort {
			Origin = new PixelPoint(pilotX0, pilotY0),
			Size = new PixelSize(pilotX1 - pilotX0, pilotY1 - pilotY0),
			TrainingLift = IntLE(gau.Remainder, PilotMessagePortRemainderOffset - 4),
		};

		// Offset 1684, the file's last four ints: the cockpit message ticker's box — see
		// HMessageTicker. Surfaced the same way as the two above.
		int tickerX0 = IntLE(gau.Remainder, MessageTickerRemainderOffset);
		int tickerY0 = IntLE(gau.Remainder, MessageTickerRemainderOffset + 4);
		int tickerX1 = IntLE(gau.Remainder, MessageTickerRemainderOffset + 8);
		int tickerY1 = IntLE(gau.Remainder, MessageTickerRemainderOffset + 12);
		gau.MessageTicker = new HMessageTicker {
			Origin = new PixelPoint(tickerX0, tickerY0),
			Size = new PixelSize(tickerX1 - tickerX0, tickerY1 - tickerY0),
		};

		return gau;
	}

	/// <summary>Reads one 16-byte (X1,Y1,X2,Y2) rectangle and converts it to the widget's inherited Origin/Size.</summary>
	private T ReadRect<T>(Func<T> factory) where T : WidgetBase {
		int x1 = IndexIntLE();
		int y1 = IndexIntLE();
		int x2 = IndexIntLE();
		int y2 = IndexIntLE();

		var widget = factory();
		widget.Origin = new PixelPoint(x1, y1);
		widget.Size = new PixelSize(x2 - x1, y2 - y1);
		return widget;
	}

	/// <summary>
	/// Reads the ShieldsGauge block at offset 616: a header rect whose first two ints are an origin
	/// offset added to the rest, then the two facing boxes and the two numeric-readout rects — see
	/// <see cref="HShieldDisplay"/> for the constructor this mirrors.
	/// </summary>
	private HShieldDisplay ReadShieldDisplay() {
		var display = new HShieldDisplay {
			HeaderRaw = ReadShieldSlot(),
			FrontBoxRaw = ReadShieldSlot(),
			RearBoxRaw = ReadShieldSlot(),
			FrontLabelRaw = ReadShieldSlot(),
			RearLabelRaw = ReadShieldSlot(),
		};

		// The widget's bounding box is the two facing boxes together.
		int left = Math.Min(display.FrontBox.X, display.RearBox.X);
		int top = Math.Min(display.FrontBox.Y, display.RearBox.Y);
		int right = Math.Max(display.FrontBox.X + display.FrontBoxSize.Width,
			display.RearBox.X + display.RearBoxSize.Width);
		int bottom = Math.Max(display.FrontBox.Y + display.FrontBoxSize.Height,
			display.RearBox.Y + display.RearBoxSize.Height);
		display.Origin = new PixelPoint(left, top);
		display.Size = new PixelSize(right - left, bottom - top);

		return display;
	}

	private int[] ReadShieldSlot() => new[] { IndexIntLE(), IndexIntLE(), IndexIntLE(), IndexIntLE() };

	/// <summary>One little-endian int out of an already-captured raw segment.</summary>
	private static int IntLE(byte[]? segment, int offset) =>
		segment != null && offset + 4 <= segment.Length ? BitConverter.ToInt32(segment, offset) : 0;

	/// <summary>Reads the throttle's track rect, then the corners of its two fill bars.</summary>
	private HThrottle ReadThrottle() {
		var throttle = ReadRect(() => new HThrottle());

		for (int i = 0; i < throttle.BarCorners.Length; i++) {
			throttle.BarCorners[i] = new PixelPoint(IndexIntLE(), IndexIntLE());
		}

		return throttle;
	}

	public override byte[]? Write(GAUFile gau) {
		using var outStream = new MemoryStream();
		void Emit(byte[] bytes) => outStream.Write(bytes, 0, bytes.Length);

		Emit(WriteIntLE(gau.HudOrigin.X));
		Emit(WriteIntLE(gau.HudOrigin.Y));
		Emit(WriteIntLE(gau.HudScreenSize.Width));
		Emit(WriteIntLE(gau.HudScreenSize.Height));
		Emit(WriteIntLE(gau.WeaponListTotal));

		foreach (var weapon in gau.Weapons ?? Array.Empty<HWeaponPanelItem>()) {
			WriteRect(Emit, weapon);
		}

		Emit(new byte[288]); // offset 180-467, zero in every retail file.
		Emit(new byte[16]); // confirmed always (0,0,0,0) container rect at offset 468.

		WriteRect(Emit, gau.ChainButton!);
		WriteRect(Emit, gau.LinkButton!);
		WriteRect(Emit, gau.AutoTrackButton!);

		Emit(new byte[32]); // 2 null widget slots, offset 532/548.

		WriteRect(Emit, gau.EnergyMeter!);

		Emit(new byte[36]); // confirmed always-zero, offset 580-615.

		WriteShieldSlot(Emit, gau.ShieldDisplay!.HeaderRaw);
		WriteShieldSlot(Emit, gau.ShieldDisplay.FrontBoxRaw);
		WriteShieldSlot(Emit, gau.ShieldDisplay.RearBoxRaw);
		WriteShieldSlot(Emit, gau.ShieldDisplay.FrontLabelRaw);
		WriteShieldSlot(Emit, gau.ShieldDisplay.RearLabelRaw);

		if (gau.RemainderBeforeMfdPanel != null) {
			Emit(gau.RemainderBeforeMfdPanel);
		}

		WriteRect(Emit, gau.MfdPanel!);

		Emit(new byte[48]); // 3 null widget slots, offset 968/984/1000.

		WriteRect(Emit, gau.Throttle!);
		foreach (var pt in gau.Throttle!.BarCorners) {
			Emit(WriteIntLE(pt.X));
			Emit(WriteIntLE(pt.Y));
		}

		if (gau.RemainderBeforeHeadingTape != null) {
			Emit(gau.RemainderBeforeHeadingTape);
		}

		WriteRect(Emit, gau.HeadingTape!);

		if (gau.RemainderBeforeReticle != null) {
			Emit(gau.RemainderBeforeReticle);
		}

		Emit(WriteIntLE(gau.Reticle!.Origin.X));
		Emit(WriteIntLE(gau.Reticle.Origin.Y));

		if (gau.Remainder != null) {
			Emit(gau.Remainder);
		}

		return outStream.ToArray();
	}

	/// <summary>Writes a widget's Origin/Size back out as a 16-byte (X1,Y1,X2,Y2) rectangle.</summary>
	private void WriteRect(Action<byte[]> write, WidgetBase widget) {
		write(WriteIntLE(widget.Origin.X));
		write(WriteIntLE(widget.Origin.Y));
		write(WriteIntLE(widget.Origin.X + widget.Size.Width));
		write(WriteIntLE(widget.Origin.Y + widget.Size.Height));
	}

	/// <summary>Writes a shield-display slot's 4 raw ints back out verbatim, in on-disk order.</summary>
	private void WriteShieldSlot(Action<byte[]> write, int[] raw) {
		foreach (int v in raw) {
			write(WriteIntLE(v));
		}
	}
}
