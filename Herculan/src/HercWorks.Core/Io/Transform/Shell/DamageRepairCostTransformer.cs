using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// <c>gam\damage.dat</c> — the 102-byte unit-value table. Fixed-length apart from its one counted
/// weapon column, so the whole file is a straight walk; a retail file consumes to the last byte.
///
/// <para>The values are left exactly as they are on disk. Expanding them against a chassis price is
/// the loader's job and belongs with the cost model, not here — see
/// <c>Herculan.Engine.Shell.ShellRepairCosts</c>. See <c>docs/retail/formats/herc-catalogs.md</c>.</para>
/// </summary>
public class DamageRepairCostTransformer : ByteTransformer<DamageRepairCost> {
	public override DamageRepairCost? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);
		var data = new DamageRepairCost {
			ChassisScale = IndexShortLE(),
			ExternalGroupPercent = IndexShortLEArray(DamageRepairCost.ExternalGroupCount),
			InternalPercent = IndexShortLEArray(DamageRepairCost.InternalCount),
			WeaponScale = IndexShortLE(),
		};

		short count = IndexShortLE();
		data.WeaponValue = count > 0 ? IndexShortLEArray(count) : Array.Empty<short>();
		return data;
	}

	public override byte[]? Write(DamageRepairCost data) {
		using var bytes = new MemoryStream();

		void Emit(short value) {
			byte[] pair = WriteShortLE(value);
			bytes.Write(pair, 0, pair.Length);
		}

		Emit(data.ChassisScale);
		foreach (short percent in data.ExternalGroupPercent) {
			Emit(percent);
		}

		foreach (short percent in data.InternalPercent) {
			Emit(percent);
		}

		Emit(data.WeaponScale);
		Emit((short)data.WeaponValue.Length);
		foreach (short value in data.WeaponValue) {
			Emit(value);
		}

		return bytes.ToArray();
	}
}
