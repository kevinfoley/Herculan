using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's cockpit layout (<see cref="Es1GauFile"/>; docs/es1/formats/gau-cockpit-layout.md), field by field
/// in file order. Writing a parsed file gives back its bytes.
///
/// <para>A file of any length but <see cref="Es1GauFile.Length"/> throws <see cref="InvalidDataException"/>, so a file this
/// model would not give back whole is rejected.</para>
/// </summary>
public class Es1GauFileTransformer : ByteTransformer<Es1GauFile> {
	/// <exception cref="InvalidDataException">The file is not <see cref="Es1GauFile.Length"/> bytes.</exception>
	public override Es1GauFile? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		if (inputArray.Length != Es1GauFile.Length) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {Es1GauFile.Length}.");
		}

		return new Es1GauFile {
			Origin = ReadPoint(),
			Bytes8 = IndexSegment(8),
			WeaponRowCount = IndexIntLE(),
			WeaponRows = ReadRects(Es1GauFile.WeaponRowSlots),
			AmmunitionRow = new Es1GauAmmunitionRowBlock {
				Scratch = ReadRect(),
				SelectButton = ReadRect(),
				RoundCount = ReadRect(),
				Bytes212 = IndexSegment(32),
			},
			EnergyRow = new Es1GauEnergyRowBlock {
				Scratch = ReadRect(),
				SelectButton = ReadRect(),
				ChargeBar = ReadRect(),
				Bytes292 = IndexSegment(12),
				ChargeBarArgument = IndexIntLE(),
				Bytes308 = IndexSegment(32),
			},
			PodRow = new Es1GauPodRowBlock { Scratch = ReadRect(), SelectButton = ReadRect(), Label = ReadRect() },
			Rects388 = ReadRects(2),
			Bytes420 = IndexSegment(32),
			ConsoleButtons = new Es1GauConsoleButtonsBlock { Header = ReadRect(), Buttons = ReadRects(Es1GauFile.ConsoleButtonCount) },
			EnergyMeterBlock = new Es1GauEnergyMeterBlock {
				Origin = ReadPoint(),
				Int2 = IndexIntLE(),
				Int3 = IndexIntLE(),
				Bar = ReadRect(),
				Orientation = IndexIntLE(),
				ClipFile = IndexIntLE(),
				EvenShade = IndexIntLE(),
				OddShade = IndexIntLE(),
				Bytes596 = IndexSegment(20),
			},
			ShieldDisplayBlock = new Es1GauShieldDisplayBlock {
				Rect = ReadRect(),
				FrontButton = ReadRect(),
				RearButton = ReadRect(),
				FrontReadout = ReadRect(),
				RearReadout = ReadRect(),
				Bytes696 = IndexSegment(32),
			},
			MfdPanelBlock = new Es1GauMfdPanelBlock {
				Rect = ReadRect(),
				Buttons = ReadRects(Es1GauMfdPanelBlock.ButtonCount),
				Screen = ReadRect(),
				Bytes968 = IndexSegment(32),
			},
			ThrottleBlock = new Es1GauThrottleBlock {
				Origin = ReadPoint(),
				Int2 = IndexIntLE(),
				Int3 = IndexIntLE(),
				Track = ReadRect(),
				ForwardBar = ReadRect(),
				ReverseBar = ReadRect(),
				Lights = IndexIntLE(),
				KnobFrame = IndexIntLE(),
				Bytes1072 = IndexSegment(32),
			},
			Gunsight = new Es1GauGunsightBlock {
				Origin = ReadPoint(),
				Int2 = IndexIntLE(),
				Int3 = IndexIntLE(),
				HeadingTape = ReadRect(),
				SlideBar = ReadRect(),
				ReticleCentre = ReadPoint(),
				ReticleHalfSize = IndexIntLE(),
				ClickSurface = ReadRect(),
				Bytes1180 = IndexSegment(32),
			},
			HddBlock = new Es1GauHddBlock {
				Rect = ReadRect(),
				Screen = ReadRect(),
				OrderColumn = ReadRect(),
				DamageColumn = ReadRect(),
				TitleIndicator = ReadRect(),
				Buttons = ReadRects(Es1GauHddBlock.ButtonCount),
				MarkerRects = ReadRects(Es1GauHddBlock.CommBoxCount),
				ArrowFrame = IndexIntLE(),
				Int5D = IndexIntLE(),
				HighlightMode = IndexIntLE(),
				Bytes1592 = IndexSegment(12),
			},
			ClockBlock = new Es1GauClockBlock {
				Bytes1604 = IndexSegment(16),
				Rect = ReadRect(),
				Bytes1636 = IndexSegment(32),
			},
			PilotChannelBox = ReadRect(),
			TickerBox = ReadRect(),
		};
	}

	private PixelPoint ReadPoint() => new(IndexIntLE(), IndexIntLE());

	private Es1GauRect ReadRect() => new(IndexIntLE(), IndexIntLE(), IndexIntLE(), IndexIntLE());

	private Es1GauRect[] ReadRects(int count) => Enumerable.Range(0, count).Select(_ => ReadRect()).ToArray();

	public override byte[]? Write(Es1GauFile source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		void Point(PixelPoint p) {
			w.Write(p.X);
			w.Write(p.Y);
		}

		void Rect(Es1GauRect r) {
			w.Write(r.X0);
			w.Write(r.Y0);
			w.Write(r.X1);
			w.Write(r.Y1);
		}

		void Rects(Es1GauRect[] rects) {
			foreach (var r in rects) {
				Rect(r);
			}
		}

		Point(source.Origin);
		w.Write(source.Bytes8);
		w.Write(source.WeaponRowCount);
		Rects(source.WeaponRows);

		Rect(source.AmmunitionRow.Scratch);
		Rect(source.AmmunitionRow.SelectButton);
		Rect(source.AmmunitionRow.RoundCount);
		w.Write(source.AmmunitionRow.Bytes212);

		Rect(source.EnergyRow.Scratch);
		Rect(source.EnergyRow.SelectButton);
		Rect(source.EnergyRow.ChargeBar);
		w.Write(source.EnergyRow.Bytes292);
		w.Write(source.EnergyRow.ChargeBarArgument);
		w.Write(source.EnergyRow.Bytes308);

		Rect(source.PodRow.Scratch);
		Rect(source.PodRow.SelectButton);
		Rect(source.PodRow.Label);

		Rects(source.Rects388);
		w.Write(source.Bytes420);

		Rect(source.ConsoleButtons.Header);
		Rects(source.ConsoleButtons.Buttons);

		var energy = source.EnergyMeterBlock;
		Point(energy.Origin);
		w.Write(energy.Int2);
		w.Write(energy.Int3);
		Rect(energy.Bar);
		w.Write(energy.Orientation);
		w.Write(energy.ClipFile);
		w.Write(energy.EvenShade);
		w.Write(energy.OddShade);
		w.Write(energy.Bytes596);

		var shields = source.ShieldDisplayBlock;
		Rect(shields.Rect);
		Rect(shields.FrontButton);
		Rect(shields.RearButton);
		Rect(shields.FrontReadout);
		Rect(shields.RearReadout);
		w.Write(shields.Bytes696);

		var mfd = source.MfdPanelBlock;
		Rect(mfd.Rect);
		Rects(mfd.Buttons);
		Rect(mfd.Screen);
		w.Write(mfd.Bytes968);

		var throttle = source.ThrottleBlock;
		Point(throttle.Origin);
		w.Write(throttle.Int2);
		w.Write(throttle.Int3);
		Rect(throttle.Track);
		Rect(throttle.ForwardBar);
		Rect(throttle.ReverseBar);
		w.Write(throttle.Lights);
		w.Write(throttle.KnobFrame);
		w.Write(throttle.Bytes1072);

		var gunsight = source.Gunsight;
		Point(gunsight.Origin);
		w.Write(gunsight.Int2);
		w.Write(gunsight.Int3);
		Rect(gunsight.HeadingTape);
		Rect(gunsight.SlideBar);
		Point(gunsight.ReticleCentre);
		w.Write(gunsight.ReticleHalfSize);
		Rect(gunsight.ClickSurface);
		w.Write(gunsight.Bytes1180);

		var hdd = source.HddBlock;
		Rect(hdd.Rect);
		Rect(hdd.Screen);
		Rect(hdd.OrderColumn);
		Rect(hdd.DamageColumn);
		Rect(hdd.TitleIndicator);
		Rects(hdd.Buttons);
		Rects(hdd.MarkerRects);
		w.Write(hdd.ArrowFrame);
		w.Write(hdd.Int5D);
		w.Write(hdd.HighlightMode);
		w.Write(hdd.Bytes1592);

		var clock = source.ClockBlock;
		w.Write(clock.Bytes1604);
		Rect(clock.Rect);
		w.Write(clock.Bytes1636);
		Rect(source.PilotChannelBox);
		Rect(source.TickerBox);

		w.Flush();
		return o.ToArray();
	}
}
