using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from BASES.DAT (see <see cref="BasesDat"/>). The file has no stride:
/// each record's length depends on its component count, so it is walked record by record, and a walk
/// that does not end exactly on the last byte means the record shape is wrong.
/// </summary>
public class BasesDatTransformer : ByteTransformer<BasesDat> {
	/// <exception cref="InvalidDataException">The walk does not consume the file exactly.</exception>
	public override BasesDat? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		var types = new BaseTypeRecord[IndexShortLE()];
		for (int i = 0; i < types.Length; i++) {
			var type = new BaseTypeRecord {
				Unk00 = IndexShortLE(),
				ShapeIndex = IndexShortLE(),
				HulkTypeIndex = IndexShortLE(),
				AnimThreadCount = IndexShortLE(),
				FireShapeIndex = IndexShortLE(),
				FirePoint = IndexShortLEArray(3),
				DestroyedEffect = IndexShortLE(),
			};

			short componentCount = IndexShortLE();
			type.Components = new BaseComponentRecord[Math.Max((int)componentCount, 0)];
			for (int c = 0; c < type.Components.Length; c++) {
				type.Components[c] = new BaseComponentRecord {
					MaxDamage = IndexShortLE(),
					DestroyedSubShape = IndexShortLE(),
					DestroyedEffect = IndexShortLE(),
					FireShapeIndex = IndexShortLE(),
					DebrisGroup = IndexShortLE(),
					EmitPoint = IndexShortLEArray(3),
					Position = IndexShortLEArray(3),
					SmokeSpread = IndexShortLEArray(3),
					ParentComponent = IndexShortLE(),
				};
			}

			type.Unk18 = IndexShortLEArray(3);
			type.Invulnerable = IndexShortLE();
			type.AnimThreadRates = IndexShortLEArray(2);
			type.AnimCellSequence = IndexShortLE();
			type.AnimCellInterval = IndexShortLE();
			type.SilhouetteIndex = IndexShortLE();
			type.HitRadius = IndexShortLE();
			type.AimPointHeight = IndexShortLE();
			type.Armament = IndexShortLE();
			type.CollisionModel = IndexShortLE();
			type.TextureSelector = IndexShortLE();
			types[i] = type;
		}

		if (Index != inputArray.Length) {
			throw new InvalidDataException(
				$"BASES.DAT: walked {Index} of {inputArray.Length} bytes across {types.Length} records — " +
				"the record shape does not match this file.");
		}

		return new BasesDat { Types = types };
	}

	public override byte[]? Write(BasesDat source) {
		using var outStream = new MemoryStream();
		Emit(outStream, WriteShortLE((short)source.Types.Length));

		foreach (var type in source.Types) {
			Emit(outStream, WriteShortLE(type.Unk00));
			Emit(outStream, WriteShortLE(type.ShapeIndex));
			Emit(outStream, WriteShortLE(type.HulkTypeIndex));
			Emit(outStream, WriteShortLE(type.AnimThreadCount));
			Emit(outStream, WriteShortLE(type.FireShapeIndex));
			Emit(outStream, WriteShortLESegment(type.FirePoint));
			Emit(outStream, WriteShortLE(type.DestroyedEffect));
			Emit(outStream, WriteShortLE((short)type.Components.Length));

			foreach (var component in type.Components) {
				Emit(outStream, WriteShortLE(component.MaxDamage));
				Emit(outStream, WriteShortLE(component.DestroyedSubShape));
				Emit(outStream, WriteShortLE(component.DestroyedEffect));
				Emit(outStream, WriteShortLE(component.FireShapeIndex));
				Emit(outStream, WriteShortLE(component.DebrisGroup));
				Emit(outStream, WriteShortLESegment(component.EmitPoint));
				Emit(outStream, WriteShortLESegment(component.Position));
				Emit(outStream, WriteShortLESegment(component.SmokeSpread));
				Emit(outStream, WriteShortLE(component.ParentComponent));
			}

			Emit(outStream, WriteShortLESegment(type.Unk18));
			Emit(outStream, WriteShortLE(type.Invulnerable));
			Emit(outStream, WriteShortLESegment(type.AnimThreadRates));
			Emit(outStream, WriteShortLE(type.AnimCellSequence));
			Emit(outStream, WriteShortLE(type.AnimCellInterval));
			Emit(outStream, WriteShortLE(type.SilhouetteIndex));
			Emit(outStream, WriteShortLE(type.HitRadius));
			Emit(outStream, WriteShortLE(type.AimPointHeight));
			Emit(outStream, WriteShortLE(type.Armament));
			Emit(outStream, WriteShortLE(type.CollisionModel));
			Emit(outStream, WriteShortLE(type.TextureSelector));
		}

		return outStream.ToArray();
	}

	private static void Emit(MemoryStream outArr, byte[] data) => outArr.Write(data, 0, data.Length);
}
