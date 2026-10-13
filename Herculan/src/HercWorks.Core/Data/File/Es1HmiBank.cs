namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of the HMI sound library's banks in ES1's SHELL0.VOL that the sound manager loads: <c>sos\MELODIC.BNK</c> and
/// <c>sos\DRUM.BNK</c>, the MIDI driver's instruments, and <c>sos\DRUM32.DIG</c>, which it loads into the second MIDI
/// driver (docs/es1/formats/sound-samples.md#the-banks). The program reads each whole and hands it to the library's driver,
/// which owns the layout. Read by <see cref="Io.Transform.Common.Es1HmiBankTransformer"/>.
/// </summary>
public class Es1HmiBank {
	/// <summary>The file's content, as the sound manager reads it.</summary>
	public byte[] Bytes { get; set; } = [];
}
