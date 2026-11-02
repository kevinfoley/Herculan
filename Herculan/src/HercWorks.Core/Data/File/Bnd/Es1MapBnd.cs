using System.Buffers.Binary;

namespace HercWorks.Core.Data.File.Bnd;

/// <summary>
/// The fields of a shell tuning file (<see cref="Es1ShellTuningFile"/>) at their offsets, over the bytes its loader
/// read whole into a global. A file shorter than the global, or none, leaves the rest 0, the global's value in the image.
/// </summary>
public abstract class Es1TuningView {
	private readonly byte[] _bytes;

	protected Es1TuningView(byte[]? bytes, int length) {
		_bytes = new byte[length];
		if (bytes != null) {
			Array.Copy(bytes, _bytes, Math.Min(bytes.Length, length));
		}
	}

	protected int Int32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));

	protected short Int16(int offset) => BinaryPrimitives.ReadInt16LittleEndian(_bytes.AsSpan(offset));

	protected byte Byte(int offset) => _bytes[offset];
}

/// <summary>
/// ES1's shell <c>BND\MAP.BND</c>, which <c>Map_LoadBnd</c> (<c>ES1GO 00008cd4</c>) reads into <c>0009a420</c>: the war
/// map's base class's constants. Layout and values: docs/es1/formats/bnd-notes.md#bndmapbnd-and-bndshellmapbnd.
/// </summary>
public sealed class Es1MapBnd : Es1TuningView {
	/// <summary>The global's length, the retail file's.</summary>
	public const int Length = 32;

	public Es1MapBnd(byte[]? bytes) : base(bytes, Length) {
	}

	/// <summary>Offset 4, the pan step's top.</summary>
	public int PanStepHigh => Int32(4);

	/// <summary>Offset 8, the pan step's bottom.</summary>
	public int PanStepLow => Int32(8);

	/// <summary>Offset 12, <c>MapBnd_PanLimit</c> (<c>0009a42c</c>).</summary>
	public int PanLimit => Int32(12);

	/// <summary>Offset 16, <c>MapBnd_ZoomStep</c> (<c>0009a430</c>).</summary>
	public int ZoomStep => Int32(16);

	/// <summary>Offset 20, the altitude's bottom.</summary>
	public int AltitudeLow => Int32(20);

	/// <summary>Offset 24, the altitude's top.</summary>
	public int AltitudeHigh => Int32(24);

	/// <summary>Offset 28, <c>MapBnd_StartAltitude</c> (<c>0009a43c</c>).</summary>
	public int StartAltitude => Int32(28);
}

/// <summary>
/// ES1's shell <c>BND\SHELLMAP.BND</c>, which <c>ShellMap_LoadBnd</c> (<c>ES1GO 00009210</c>) reads into <c>0009a44c</c>:
/// the war map's map's constants, each named after its <c>ShellMapBnd_*</c> global. Layout and values:
/// docs/es1/formats/bnd-notes.md#bndmapbnd-and-bndshellmapbnd.
/// </summary>
public sealed class Es1ShellMapBnd : Es1TuningView {
	/// <summary>The global's length, the retail file's.</summary>
	public const int Length = 142;

	public Es1ShellMapBnd(byte[]? bytes) : base(bytes, Length) {
	}

	/// <summary>Offset 4, <c>ShellMapBnd_GridSpacing</c> (<c>0009a450</c>).</summary>
	public int GridSpacing => Int32(4);

	/// <summary>Offset <c>0xc</c>, <c>ShellMapBnd_FirstMarkerFrame</c> (<c>0009a458</c>).</summary>
	public short FirstMarkerFrame => Int16(0xc);

	/// <summary>Offset <c>0xe</c>, <c>ShellMapBnd_LastMarkerFrame</c> (<c>0009a45a</c>).</summary>
	public short LastMarkerFrame => Int16(0xe);

	/// <summary>Offset <c>0x12</c>, <c>ShellMapBnd_PlayerFrame</c> (<c>0009a45e</c>).</summary>
	public short PlayerFrame => Int16(0x12);

	/// <summary>Offset <c>0x14</c>, <c>ShellMapBnd_SquadmateFrame</c> (<c>0009a460</c>).</summary>
	public short SquadmateFrame => Int16(0x14);

	/// <summary>Offset <c>0x1c</c>, <c>ShellMapBnd_Side1BaseFrame</c> (<c>0009a468</c>).</summary>
	public short Side1BaseFrame => Int16(0x1c);

	/// <summary>Offset <c>0x1e</c>, <c>ShellMapBnd_Side1SmallBaseFrame</c> (<c>0009a46a</c>).</summary>
	public short Side1SmallBaseFrame => Int16(0x1e);

	/// <summary>Offset <c>0x20</c>, <c>ShellMapBnd_BaseFrame</c> (<c>0009a46c</c>).</summary>
	public short BaseFrame => Int16(0x20);

	/// <summary>Offset <c>0x22</c>, <c>ShellMapBnd_SmallBaseFrame</c> (<c>0009a46e</c>).</summary>
	public short SmallBaseFrame => Int16(0x22);

	/// <summary>Offset <c>0x24</c>, <c>ShellMapBnd_SquadGadgetSize</c> (<c>0009a470</c>).</summary>
	public short SquadGadgetSize => Int16(0x24);

	/// <summary>Offset <c>0x28</c>, <c>ShellMapBnd_MarkerSize</c> (<c>0009a474</c>).</summary>
	public short MarkerSize => Int16(0x28);

	/// <summary>Offset <c>0x2a</c>, <c>ShellMapBnd_BaseGadgetSize</c> (<c>0009a476</c>).</summary>
	public short BaseGadgetSize => Int16(0x2a);

	/// <summary>Offset <c>0x2c</c>, <c>ShellMapBnd_SmallBaseGadgetSize</c> (<c>0009a478</c>).</summary>
	public short SmallBaseGadgetSize => Int16(0x2c);

	/// <summary>Offset <c>0x30</c>, <c>ShellMapBnd_ShapeRamp</c> (<c>0009a47c</c>).</summary>
	public short ShapeRamp => Int16(0x30);

	/// <summary>Offset <c>0x32</c>, the byte <c>ShellMapBnd_DrawShapes</c> (<c>0009a47e</c>).</summary>
	public byte DrawShapes => Byte(0x32);

	/// <summary>Offset <c>0x34</c>, <c>ShellMapBnd_LineSpacing</c> (<c>0009a480</c>).</summary>
	public short LineSpacing => Int16(0x34);

	/// <summary>Offset <c>0x36</c>, <c>ShellMapBnd_FullViewMargin</c> (<c>0009a482</c>).</summary>
	public int FullViewMargin => Int32(0x36);

	/// <summary>Offset <c>0x3a</c>, <c>ShellMapBnd_SquadViewMargin</c> (<c>0009a486</c>).</summary>
	public int SquadViewMargin => Int32(0x3a);

	/// <summary>Offset <c>0x3e</c>, <c>ShellMapBnd_PenRadius</c> (<c>0009a48a</c>).</summary>
	public short PenRadius => Int16(0x3e);

	/// <summary>Offset <c>0x40</c>, <c>ShellMapBnd_SquadWorldSize</c> (<c>0009a48c</c>).</summary>
	public int SquadWorldSize => Int32(0x40);

	/// <summary>Offset <c>0x44</c>, <c>ShellMapBnd_SquadPixelBelow</c> (<c>0009a490</c>).</summary>
	public short SquadPixelBelow => Int16(0x44);

	/// <summary>Offset <c>0x46</c>, <c>ShellMapBnd_BaseWorldSize</c> (<c>0009a492</c>).</summary>
	public int BaseWorldSize => Int32(0x46);

	/// <summary>Offset <c>0x4a</c>, <c>ShellMapBnd_SmallBaseWorldSize</c> (<c>0009a496</c>).</summary>
	public int SmallBaseWorldSize => Int32(0x4a);

	/// <summary>Offset <c>0x4e</c>, <c>ShellMapBnd_BasePixelBelow</c> (<c>0009a49a</c>).</summary>
	public short BasePixelBelow => Int16(0x4e);

	/// <summary>Offset <c>0x50</c>, <c>ShellMapBnd_SmallBasePixelBelow</c> (<c>0009a49c</c>).</summary>
	public short SmallBasePixelBelow => Int16(0x50);

	/// <summary>Offset <c>0x52</c>, <c>ShellMapBnd_ClearWait</c> (<c>0009a49e</c>).</summary>
	public int ClearWait => Int32(0x52);

	/// <summary>Offset <c>0x56</c>, <c>ShellMapBnd_ShapesWait</c> (<c>0009a4a2</c>).</summary>
	public int ShapesWait => Int32(0x56);

	/// <summary>Offset <c>0x5a</c>, <c>ShellMapBnd_GridLineWait</c> (<c>0009a4a6</c>).</summary>
	public int GridLineWait => Int32(0x5a);

	/// <summary>Offset <c>0x5e</c>, <c>ShellMapBnd_FullViewHold</c> (<c>0009a4aa</c>).</summary>
	public int FullViewHold => Int32(0x5e);

	/// <summary>Offset <c>0x62</c>, <c>ShellMapBnd_ZoomTicks</c> (<c>0009a4ae</c>).</summary>
	public int ZoomTicks => Int32(0x62);

	/// <summary>Offset <c>0x66</c>, <c>ShellMapBnd_MemberTicks</c> (<c>0009a4b2</c>).</summary>
	public int MemberTicks => Int32(0x66);

	/// <summary>Offset <c>0x6a</c>, <c>ShellMapBnd_LegDistanceUnit</c> (<c>0009a4b6</c>).</summary>
	public int LegDistanceUnit => Int32(0x6a);

	/// <summary>Offset <c>0x6e</c>, <c>ShellMapBnd_LegTicksPerUnit</c> (<c>0009a4ba</c>).</summary>
	public int LegTicksPerUnit => Int32(0x6e);

	/// <summary>Offset <c>0x72</c>, <c>ShellMapBnd_SectionPause</c> (<c>0009a4be</c>).</summary>
	public int SectionPause => Int32(0x72);

	/// <summary>Offset <c>0x76</c>, <c>ShellMapBnd_StepPause</c> (<c>0009a4c2</c>).</summary>
	public int StepPause => Int32(0x76);

	/// <summary>Offset <c>0x7a</c>, <c>ShellMapBnd_TicksPerChar</c> (<c>0009a4c6</c>).</summary>
	public int TicksPerChar => Int32(0x7a);

	/// <summary>Offset <c>0x7e</c>, <c>ShellMapBnd_GadgetTextMargin</c> (<c>0009a4ca</c>).</summary>
	public short GadgetTextMargin => Int16(0x7e);

	/// <summary>Offset <c>0x80</c>, <c>ShellMapBnd_CalloutHeight</c> (<c>0009a4cc</c>).</summary>
	public short CalloutHeight => Int16(0x80);

	/// <summary>Offset <c>0x82</c>, <c>ShellMapBnd_CalloutGap</c> (<c>0009a4ce</c>).</summary>
	public short CalloutGap => Int16(0x82);

	/// <summary>Offset <c>0x84</c>, <c>ShellMapBnd_PointerWidth</c> (<c>0009a4d0</c>).</summary>
	public short PointerWidth => Int16(0x84);

	/// <summary>Offset <c>0x86</c>, <c>ShellMapBnd_BoundsMargin</c> (<c>0009a4d2</c>).</summary>
	public int BoundsMargin => Int32(0x86);

	/// <summary>Offset <c>0x8a</c>, <c>ShellMapBnd_ShapeArenaSize</c> (<c>0009a4d6</c>).</summary>
	public int ShapeArenaSize => Int32(0x8a);
}
