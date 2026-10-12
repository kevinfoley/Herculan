namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - ES1's <c>STYLES.DAT</c> (<c>dat\</c> in <c>SIMVOL0.VOL</c> and <c>ZONES.VOL</c>, <c>cfg\</c> in
/// <c>SHELL0.VOL</c>): an <c>int16</c> count, then that many 64-byte widget style records, which both programs' loaders
/// read whole. Layout and loads: docs/es1/formats/styles-dat.md. Read by
/// <see cref="Io.Transform.Dbsim.Es1WidgetStylesTransformer"/>.
/// </summary>
public class Es1WidgetStyles {
	public Es1WidgetStyle[] Styles { get; set; } = [];
}

/// <summary>
/// OPAQUE - one style record, kept as its bytes: no reader of its fields has been read
/// (docs/es1/formats/styles-dat.md#open).
/// </summary>
public class Es1WidgetStyle {
	/// <summary>The record's length in the file.</summary>
	public const int Length = 0x40;

	public byte[] Bytes { get; set; } = new byte[Length];
}
