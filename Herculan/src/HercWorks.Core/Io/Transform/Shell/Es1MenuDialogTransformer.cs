using HercWorks.Core.Data.File;

namespace HercWorks.Core.Io.Transform.Shell;

/// <summary>
/// Reads and writes one of ES1's <c>dlg\MENU&lt;n&gt;.DLG</c> (<see cref="Es1MenuDialog"/>) as the shell's dialog builder,
/// <c>ES1GO 0003a1f7</c>, reads it: an <c>int16</c> count, then that many 13-byte records
/// (docs/es1/formats/dlg-menu-dialogs.md#layout).
/// </summary>
public class Es1MenuDialogTransformer : Es1WalkedFileTransformer<Es1MenuDialog> {
	protected override Es1MenuDialog ReadFile() {
		short count = ReadInt16();
		if (count < 0) {
			throw new InvalidDataException($"The count is {count}.");
		}

		var controls = new Es1MenuControl[count];
		for (int i = 0; i < count; i++) {
			controls[i] = new Es1MenuControl {
				Left = ReadInt16(), Top = ReadInt16(), Right = ReadInt16(), Bottom = ReadInt16(),
				Kind = ReadByte(), Style = ReadByte(), Key = ReadByte(),
				Id = ReadInt16(),
			};
		}

		return new Es1MenuDialog { Controls = controls };
	}

	protected override void WriteFile(BinaryWriter w, Es1MenuDialog source) {
		w.Write(checked((short)source.Controls.Length));
		foreach (var control in source.Controls) {
			WriteInt16s(w, [control.Left, control.Top, control.Right, control.Bottom]);
			w.Write(control.Kind);
			w.Write(control.Style);
			w.Write(control.Key);
			w.Write(control.Id);
		}
	}
}
