using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes an <see cref="Es1HmiBank"/>, which <c>SoundManager_LoadBanks</c> (<c>ES1DBSIM 0004f6d4</c>,
/// <c>ES1GO 00042bf9</c>) reads whole (docs/es1/formats/sound-samples.md#the-banks). Writing a parsed bank gives back its bytes.
/// </summary>
public class Es1HmiBankTransformer : ByteTransformer<Es1HmiBank> {
	public override Es1HmiBank? Parse(byte[]? inputArray) =>
		inputArray == null || inputArray.Length <= 0 ? null : new Es1HmiBank { Bytes = (byte[])inputArray.Clone() };

	public override byte[]? Write(Es1HmiBank source) => (byte[])source.Bytes.Clone();
}
