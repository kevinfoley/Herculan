namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - one of ES1's digital sounds, a <c>.SFX</c> effect or voice line or a <c>.RAW</c> shell sound or talking-head line
/// (docs/es1/formats/sound-samples.md). The file has no header: its whole content is the sample the sound manager hands
/// the driver. Read by <see cref="Io.Transform.Common.Es1SoundSampleTransformer"/>.
/// </summary>
public class Es1SoundSample {
	/// <summary>
	/// The rate, in Hz, of the digital-driver block the sound manager builds (docs/es1/formats/sound-samples.md#the-driver);
	/// the sample carries no rate of its own.
	/// </summary>
	public const int DriverRate = 11025;

	/// <summary>The file's content, every byte of it the sample's data.</summary>
	public byte[] Data { get; set; } = [];
}
