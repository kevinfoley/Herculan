using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes ES1's <c>cfg\damage.dat</c> (<see cref="Es1DamageRepairCost"/>;
/// docs/es1/formats/herc-catalogs.md#cfgdamagedat), the five runs <c>LoadDamageDat_Shell</c> (<c>ES1GO 000387a8</c>) reads.
/// Writing a parsed file gives back its bytes.
///
/// <para>A file of any length but 72 bytes throws <see cref="InvalidDataException"/>.</para>
/// </summary>
public class Es1DamageRepairCostTransformer : ByteTransformer<Es1DamageRepairCost> {
	private const int FileLength = 2 * (1 + DamageRepairCost.ExternalGroupCount + Es1DamageRepairCost.InternalCount + 1
		+ Es1DamageRepairCost.WeaponCount);

	/// <exception cref="InvalidDataException">The file is not 72 bytes.</exception>
	public override Es1DamageRepairCost? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		if (inputArray.Length != FileLength) {
			throw new InvalidDataException($"The file is {inputArray.Length} bytes, not {FileLength}.");
		}

		SetBytes(inputArray);
		return new Es1DamageRepairCost {
			ChassisScale = IndexShortLE(),
			ExternalGroupPercent = IndexShortLEArray(DamageRepairCost.ExternalGroupCount),
			InternalPercent = IndexShortLEArray(Es1DamageRepairCost.InternalCount),
			WeaponScale = IndexShortLE(),
			WeaponValue = IndexShortLEArray(Es1DamageRepairCost.WeaponCount),
		};
	}

	public override byte[]? Write(Es1DamageRepairCost source) {
		using var o = new MemoryStream();
		var w = new BinaryWriter(o);
		w.Write(source.ChassisScale);
		foreach (short value in source.ExternalGroupPercent.Concat(source.InternalPercent).Append(source.WeaponScale).Concat(source.WeaponValue)) {
			w.Write(value);
		}

		w.Flush();
		return o.ToArray();
	}
}
