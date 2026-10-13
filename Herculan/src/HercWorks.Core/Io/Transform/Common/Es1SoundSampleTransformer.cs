using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes ES1's <c>.SFX</c> and <c>.RAW</c> sounds (<see cref="Es1SoundSample"/>), which the sound manager loads
/// whole as the sample (<c>SoundObject_LoadSample</c>, <c>ES1DBSIM 000501ec</c>; docs/es1/formats/sound-samples.md#naming-and-loading).
/// Writing a parsed sample gives back its bytes.
/// </summary>
public class Es1SoundSampleTransformer : ByteTransformer<Es1SoundSample> {
	public override Es1SoundSample? Parse(byte[]? inputArray) =>
		inputArray == null || inputArray.Length <= 0 ? null : new Es1SoundSample { Data = (byte[])inputArray.Clone() };

	public override byte[]? Write(Es1SoundSample source) => (byte[])source.Data.Clone();
}
