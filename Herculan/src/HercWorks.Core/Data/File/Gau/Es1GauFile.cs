using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Dbsim;

namespace HercWorks.Core.Data.File.Gau;

/// <summary>
/// FILE - ES1's <c>gau\&lt;chassis&gt;.GAU</c>, in SIMVOL0.VOL: the <c>0x6a4</c>-byte cockpit layout <c>Gau_Load</c>
/// (<c>ES1DBSIM 0002b5c4</c>) reads (docs/es1/formats/gau-cockpit-layout.md). Members are in file order, each block the doc
/// names as a typed member, and the bytes it does not name kept whole so a write gives the file back. Read by
/// <see cref="Es1GauFileTransformer"/>. This is the file as read; <see cref="Loaded"/> gives the layout after
/// <c>Gau_Load</c>'s origin add.
/// </summary>
public class Es1GauFile {
	/// <summary>The file's length, the bytes <c>Gau_Load</c> allocates and reads.</summary>
	public const int Length = 0x6a4;

	/// <summary>The weapon-row rects the file has room for.</summary>
	public const int WeaponRowSlots = 9;

	/// <summary>The console buttons' rects in <see cref="ConsoleButtons"/>.</summary>
	public const int ConsoleButtonCount = 5;

	/// <summary>Offset of <see cref="WeaponRows"/>.</summary>
	public const int WeaponRowsOffset = 20;

	/// <summary>
	/// The blocks whose first 16 bytes <see cref="Loaded"/> offsets: the console buttons', the four instruments' from 548 to
	/// 1000, the gunsight's and the one at 1212.
	/// </summary>
	public static readonly IReadOnlyList<int> OffsetBlocks = [452, 548, 616, 728, 1000, 1104, 1212];

	/// <summary>Offset 0: the origin pair <see cref="Loaded"/> adds.</summary>
	public PixelPoint Origin { get; set; }

	/// <summary>Offsets 8-15, which the doc does not name.</summary>
	public byte[] Bytes8 { get; set; } = new byte[8];

	/// <summary>Offset 16: the number of weapon rows in use.</summary>
	public int WeaponRowCount { get; set; }

	/// <summary>Offset 20: nine weapon-row rects, indexed by the row the mount's factory is given.</summary>
	public Es1GauRect[] WeaponRows { get; set; } = new Es1GauRect[WeaponRowSlots];

	/// <summary>Offset 164.</summary>
	public Es1GauAmmunitionRowBlock AmmunitionRow { get; set; } = new();

	/// <summary>Offset 244.</summary>
	public Es1GauEnergyRowBlock EnergyRow { get; set; } = new();

	/// <summary>Offset 340.</summary>
	public Es1GauPodRowBlock PodRow { get; set; } = new();

	/// <summary>Offset 372: three rects.</summary>
	public Es1GauRect[] Rects372 { get; set; } = new Es1GauRect[3];

	/// <summary>Offsets 420-451, which the doc does not name.</summary>
	public byte[] Bytes420 { get; set; } = new byte[32];

	/// <summary>Offset 452.</summary>
	public Es1GauConsoleButtonsBlock ConsoleButtons { get; set; } = new();

	/// <summary>
	/// Offset 548, 68 bytes: the block <c>Gau_EnergyMeterWidget</c> (<c>0002cd3c</c>) hands the instrument at the cockpit's
	/// <c>+0x1e1</c> (docs/es1/simulation/cockpit-panel.md#building-it).
	/// </summary>
	public Es1GauInstrumentBlock EnergyMeterBlock { get; set; } = new() { Rest = new byte[52] };

	/// <summary>
	/// Offset 616, 112 bytes: the block <c>Gau_ShieldDisplayWidget</c> (<c>0002cd74</c>) hands the instrument at the cockpit's
	/// <c>+0x1e5</c>.
	/// </summary>
	public Es1GauInstrumentBlock ShieldDisplayBlock { get; set; } = new() { Rest = new byte[96] };

	/// <summary>Offset 728, 272 bytes.</summary>
	public Es1GauMfdPanelBlock MfdPanelBlock { get; set; } = new();

	/// <summary>
	/// Offset 1000, 104 bytes: the block <c>Gau_ThrottleWidget</c> (<c>0002cdf0</c>) hands the instrument at the cockpit's
	/// <c>+0x1ed</c>.
	/// </summary>
	public Es1GauInstrumentBlock ThrottleBlock { get; set; } = new() { Rest = new byte[88] };

	/// <summary>Offset 1104, 108 bytes.</summary>
	public Es1GauGunsightBlock Gunsight { get; set; } = new();

	/// <summary>Offset 1212, 392 bytes.</summary>
	public Es1GauPilotRosterBlock PilotRosterBlock { get; set; } = new();

	/// <summary>Offsets 1604-1667: the cockpit clock's block (docs/es1/formats/ttm-scripts.md#the-cockpit-clock), kept whole.</summary>
	public byte[] ClockBlock { get; set; } = new byte[64];

	/// <summary>Offset 1668: the pilot channel's box.</summary>
	public Es1GauRect PilotChannelBox { get; set; }

	/// <summary>Offset 1684: the ticker's box.</summary>
	public Es1GauRect TickerBox { get; set; }

	/// <summary>
	/// The layout as <c>Gau_Load</c> (<c>0002b5c4</c>) returns it (docs/es1/formats/gau-cockpit-layout.md#the-load): a copy of
	/// this file with, when <see cref="Origin"/> is not (0, 0), the origin added to both corners of the first
	/// <see cref="WeaponRowCount"/> rects from <see cref="WeaponRowsOffset"/> and to the first 16 bytes of each of
	/// <see cref="OffsetBlocks"/>. This file is left as read.
	///
	/// <para>The add works on the file's bytes. A count past <see cref="WeaponRowSlots"/>, which the doc does not cover, is this
	/// reader's choice: the add goes on in 16-byte steps into the blocks after the rows, and throws past the file's end.</para>
	/// </summary>
	/// <exception cref="InvalidDataException">The count reaches past the end of the file, where the load would write past its struct.</exception>
	public Es1GauFile Loaded() {
		byte[] bytes = new Es1GauFileTransformer().Write(this)!;
		if (Origin != default) {
			for (int row = 0; row < WeaponRowCount; row++) {
				AddOrigin(bytes, WeaponRowsOffset + row * 16);
			}

			foreach (int block in OffsetBlocks) {
				AddOrigin(bytes, block);
			}
		}

		return new Es1GauFileTransformer().Parse(bytes)!;
	}

	/// <summary>Adds <see cref="Origin"/> to the two corners of the rect at <paramref name="at"/>.</summary>
	private void AddOrigin(byte[] bytes, int at) {
		if (at + 16 > bytes.Length) {
			throw new InvalidDataException($"The weapon row count {WeaponRowCount} reaches past the file's {bytes.Length} bytes.");
		}

		var span = bytes.AsSpan(at, 16);
		for (int corner = 0; corner < 2; corner++) {
			Add(span.Slice(corner * 8, 4), Origin.X);
			Add(span.Slice(corner * 8 + 4, 4), Origin.Y);
		}

		static void Add(Span<byte> value, int delta) =>
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(value,
				System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(value) + delta);
	}
}
