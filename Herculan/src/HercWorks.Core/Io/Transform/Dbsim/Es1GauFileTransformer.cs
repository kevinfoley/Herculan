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
			PodRow = new Es1GauPodRowBlock { Scratch = ReadRect(), Bytes356 = IndexSegment(16) },
			Rects372 = ReadRects(3),
			Bytes420 = IndexSegment(32),
			ConsoleButtons = new Es1GauConsoleButtonsBlock { Header = ReadRect(), Buttons = ReadRects(Es1GauFile.ConsoleButtonCount) },
			EnergyMeterBlock = ReadInstrumentBlock(68),
			ShieldDisplayBlock = ReadInstrumentBlock(112),
			MfdPanelBlock = new Es1GauMfdPanelBlock { Header = ReadRect(), Rects744 = ReadRects(13), Bytes952 = IndexSegment(48) },
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
			PilotRosterBlock = new Es1GauPilotRosterBlock {
				Header = ReadRect(),
				Bytes1228 = IndexSegment(64),
				Rects1292 = ReadRects(15),
				Rects1532 = ReadRects(3),
				Bytes1580 = IndexSegment(24),
			},
			ClockBlock = IndexSegment(64),
			PilotChannelBox = ReadRect(),
			TickerBox = ReadRect(),
		};
	}

	private PixelPoint ReadPoint() => new(IndexIntLE(), IndexIntLE());

	private Es1GauRect ReadRect() => new(IndexIntLE(), IndexIntLE(), IndexIntLE(), IndexIntLE());

	private Es1GauRect[] ReadRects(int count) => Enumerable.Range(0, count).Select(_ => ReadRect()).ToArray();

	private Es1GauInstrumentBlock ReadInstrumentBlock(int length) => new() { Header = ReadRect(), Rest = IndexSegment(length - 16) };

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
		w.Write(source.PodRow.Bytes356);

		Rects(source.Rects372);
		w.Write(source.Bytes420);

		Rect(source.ConsoleButtons.Header);
		Rects(source.ConsoleButtons.Buttons);

		foreach (var block in new[] { source.EnergyMeterBlock, source.ShieldDisplayBlock }) {
			Rect(block.Header);
			w.Write(block.Rest);
		}

		Rect(source.MfdPanelBlock.Header);
		Rects(source.MfdPanelBlock.Rects744);
		w.Write(source.MfdPanelBlock.Bytes952);

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

		var roster = source.PilotRosterBlock;
		Rect(roster.Header);
		w.Write(roster.Bytes1228);
		Rects(roster.Rects1292);
		Rects(roster.Rects1532);
		w.Write(roster.Bytes1580);

		w.Write(source.ClockBlock);
		Rect(source.PilotChannelBox);
		Rect(source.TickerBox);

		w.Flush();
		return o.ToArray();
	}
}
