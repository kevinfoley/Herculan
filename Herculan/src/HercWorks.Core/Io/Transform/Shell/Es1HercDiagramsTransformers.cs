using HercWorks.Core.Data.File.Dat.Shell;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes one of ES1's <c>cfg\&lt;chassis&gt;.DAT</c> (<see cref="Es1HercLayout"/>), as <c>HercLayout_Load</c>
/// (<c>ES1GO 00021d0c</c>) reads each.
/// </summary>
public class Es1HercLayoutTransformer : Es1WalkedFileTransformer<Es1HercLayout> {
	protected override Es1HercLayout ReadFile() {
		var mounts = new Es1HercLayoutMount[Count(ReadInt16())];
		for (int m = 0; m < mounts.Length; m++) {
			var items = new Es1HercLayoutItem[Count(ReadInt16())];
			for (int i = 0; i < items.Length; i++) {
				items[i] = new Es1HercLayoutItem {
					Frame = ReadInt16(), Weapon = ReadInt16(),
					FirstX = ReadInt16(), FirstY = ReadInt16(), SecondX = ReadInt16(), SecondY = ReadInt16(),
				};
			}

			mounts[m] = new Es1HercLayoutMount { Items = items };
		}

		return new Es1HercLayout { Mounts = mounts };
	}

	protected override void WriteFile(BinaryWriter w, Es1HercLayout source) {
		w.Write(checked((short)source.Mounts.Length));
		foreach (var mount in source.Mounts) {
			w.Write(checked((short)mount.Items.Length));
			foreach (var item in mount.Items) {
				WriteInt16s(w, [item.Frame, item.Weapon, item.FirstX, item.FirstY, item.SecondX, item.SecondY]);
			}
		}
	}

	private int Count(short count) =>
		count >= 0 ? count : throw new InvalidDataException($"The count at {Index - 2} is {count}.");
}

/// <summary>
/// Reads and writes one of ES1's <c>cfg\&lt;chassis&gt;DMG1.DAT</c> (<see cref="Es1HercDamageMarks"/>), as
/// <c>HercDamageMarks_Load</c> (<c>ES1GO 00022252</c>) reads each.
/// </summary>
public class Es1HercDamageMarksTransformer : Es1WalkedFileTransformer<Es1HercDamageMarks> {
	protected override Es1HercDamageMarks ReadFile() {
		var groups = new Es1DamageMark[Es1HercDamageMarks.GroupCount][][];
		for (int g = 0; g < groups.Length; g++) {
			groups[g] = new Es1DamageMark[Es1HercDamageMarks.LevelCount][];
			for (int level = 0; level < groups[g].Length; level++) {
				short count = ReadInt16();
				if (count < 0) {
					throw new InvalidDataException($"The count at {Index - 2} is {count}.");
				}

				var marks = new Es1DamageMark[count];
				for (int i = 0; i < count; i++) {
					marks[i] = new Es1DamageMark { Frame = ReadInt16(), X = ReadInt32(), Y = ReadInt32() };
				}

				groups[g][level] = marks;
			}
		}

		return new Es1HercDamageMarks { Groups = groups };
	}

	protected override void WriteFile(BinaryWriter w, Es1HercDamageMarks source) {
		if (source.Groups.Length != Es1HercDamageMarks.GroupCount
			|| source.Groups.Any(g => g.Length != Es1HercDamageMarks.LevelCount)) {
			throw new InvalidDataException("The file holds six groups of three lists.");
		}

		foreach (var group in source.Groups) {
			foreach (var marks in group) {
				w.Write(checked((short)marks.Length));
				foreach (var mark in marks) {
					w.Write(mark.Frame);
					w.Write(mark.X);
					w.Write(mark.Y);
				}
			}
		}
	}
}

/// <summary>
/// Reads and writes ES1's <c>cfg\HOTSPOT.DAT</c> (<see cref="Es1HercHotspots"/>), as <c>HercHotspots_Load</c>
/// (<c>ES1GO 00022404</c>) reads it, counts and coordinates unsigned.
/// </summary>
public class Es1HercHotspotsTransformer : Es1WalkedFileTransformer<Es1HercHotspots> {
	protected override Es1HercHotspots ReadFile() {
		var chassis = new Es1HotspotChassis[(ushort)ReadInt16()];
		for (int c = 0; c < chassis.Length; c++) {
			var areas = new Es1HotspotArea[(ushort)ReadInt16()];
			for (int a = 0; a < areas.Length; a++) {
				areas[a] = new Es1HotspotArea {
					X0 = (ushort)ReadInt16(), Y0 = (ushort)ReadInt16(), X1 = (ushort)ReadInt16(), Y1 = (ushort)ReadInt16(),
				};
			}

			chassis[c] = new Es1HotspotChassis { Areas = areas };
		}

		return new Es1HercHotspots { Chassis = chassis };
	}

	protected override void WriteFile(BinaryWriter w, Es1HercHotspots source) {
		w.Write(checked((ushort)source.Chassis.Length));
		foreach (var chassis in source.Chassis) {
			w.Write(checked((ushort)chassis.Areas.Length));
			foreach (var area in chassis.Areas) {
				w.Write(area.X0);
				w.Write(area.Y0);
				w.Write(area.X1);
				w.Write(area.Y1);
			}
		}
	}
}
