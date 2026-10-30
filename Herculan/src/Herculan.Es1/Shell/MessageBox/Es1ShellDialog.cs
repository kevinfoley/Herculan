using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.MessageBox;

/// <summary>
/// The shell's dialog record <c>Shell_Dialog</c> (<c>0009afc8</c>), which holds the one text box the shell shows at a time
/// (docs/es1/shell/message-box.md#the-dialog-record): the file's version word <c>+4</c> and the box <c>+6</c>, both 0 from
/// the static initializer <c>00020b68</c>. Its stream <c>+0</c>, which a loader keeps open only while the box is built, is
/// not kept here. The record also holds what every box shares: the frame bitmaps <c>DialogBox_FrameBitmaps</c>
/// (<c>0007ee64</c>), and the shell's globals a box reads, each handed in as its reader. The war map's briefing and the
/// debriefing load their <c>.ENG</c> blocks into it with <c>Dialog_LoadConditional</c>.
/// </summary>
public sealed class Es1ShellDialog {
	/// <summary>The version word of a message's file, which makes a <see cref="Es1DigitalDialogBox"/>.</summary>
	public const short MessageVersion = 2;

	/// <summary>The version word that makes a <see cref="Es1DefaultDialogBox"/>.</summary>
	private const short DefaultVersion = 0;

	private readonly Func<string, string, byte[]?>? _read;

	/// <param name="read">The shell's resource reader, (folder, name) to the file's bytes or null.</param>
	/// <param name="font">The font slot <c>000aacda</c>, the <c>nm_green</c> font.</param>
	/// <param name="radioOption"><c>Shell_RadioOption</c> (<c>0007fcbc</c>), the shell's copy of the RADIO preference.</param>
	/// <param name="backdropId"><c>Shell_BackdropId</c> (<c>000817c4</c>).</param>
	/// <param name="line"><c>MessageBox_Line</c> (<c>000aad5c</c>), the line a message's box shows.</param>
	public Es1ShellDialog(Es1VideoMemory display, Es1DirtyList dirty, Func<string, string, byte[]?>? read,
			Es1LipSyncPlayer player, Func<HudFont?> font, Func<short> radioOption, Func<short> backdropId, Func<short> line) {
		Display = display;
		Dirty = dirty;
		_read = read;
		Player = player;
		Font = font;
		RadioOption = radioOption;
		BackdropId = backdropId;
		Line = line;
	}

	/// <summary>The shell's display, on whose draw page a box draws.</summary>
	public Es1VideoMemory Display { get; }

	/// <summary>The shell's dirty list.</summary>
	public Es1DirtyList Dirty { get; }

	/// <summary><c>Shell_LipSyncPlayer</c> (<c>0009afd4</c>), a message's talking head.</summary>
	public Es1LipSyncPlayer Player { get; }

	/// <summary>The font slot <c>000aacda</c>, which every box takes.</summary>
	public Func<HudFont?> Font { get; }

	/// <summary><c>Shell_RadioOption</c> (<c>0007fcbc</c>).</summary>
	public Func<short> RadioOption { get; }

	/// <summary><c>Shell_BackdropId</c> (<c>000817c4</c>), which places a message's box.</summary>
	public Func<short> BackdropId { get; }

	/// <summary><c>MessageBox_Line</c> (<c>000aad5c</c>).</summary>
	public Func<short> Line { get; }

	/// <summary><c>+4</c>, the version word of the file the box was loaded from.</summary>
	public short Version { get; private set; }

	/// <summary><c>+6</c>, the box; none in the image.</summary>
	public Es1DialogBox? Box { get; private set; }

	/// <summary><c>DialogBox_FrameBitmaps</c> (<c>0007ee64</c>), <c>res32\textbox.dba</c>; none until a box in the top strip draws.</summary>
	private IReadOnlyList<DynamixBitmap>? _frameBitmaps;

	/// <summary>
	/// <see cref="_frameBitmaps"/>, loaded with <c>Resource_LoadBitmapArray</c> while there are none. A file that does not
	/// read gives none, and the frame is not drawn, where retail draws from a null array, an engine guard.
	/// </summary>
	public IReadOnlyList<DynamixBitmap>? FrameBitmaps() =>
		_frameBitmaps ??= _read?.Invoke("res32", "TEXTBOX.DBA") is { } bytes ? new DynamixBitmapArrayTransformer().Parse(bytes)?.Images : null;

	/// <summary><c>DialogBox_Dtor</c>'s release of <see cref="_frameBitmaps"/> (<c>GLBitmapArray_Dtor</c>), so the next frame drawn loads them again.</summary>
	public void ReleaseFrameBitmaps() => _frameBitmaps = null;

	/// <summary>
	/// <c>Dialog_LoadFromFile</c> (<c>00018c36</c>)(dialog, <paramref name="a"/>, <paramref name="b"/>, <paramref name="path"/>)
	/// (docs/es1/formats/bin-string-tables.md#the-load): the box deleted (<see cref="DeleteBox"/>), the file opened and its
	/// version word read; 0 makes an <see cref="Es1DefaultDialogBox"/>, 2 an <see cref="Es1DigitalDialogBox"/> on the file's
	/// lines with its talking head (<paramref name="a"/>, <paramref name="b"/>), any other no box. A file that does not open
	/// or read leaves no box and the version as it was, reported, where retail reads from the failed stream, an engine
	/// guard.
	/// </summary>
	/// <param name="path">The path, its folder before the backslash, as <c>DigitalDialog_ShowMessage</c> formats it.</param>
	public IEnumerable<Es1ShellWait> LoadFromFile(short a, byte b, string path) {
		foreach (var wait in DeleteBox()) {
			yield return wait;
		}

		int slash = path.IndexOf('\\');
		byte[]? bytes = _read?.Invoke(slash < 0 ? "" : path[..slash], path[(slash + 1)..]);
		if (bytes is not { Length: >= 2 }) {
			Console.Error.WriteLine($"{path} is missing or unreadable: the message box shows no text.");
			yield break;
		}

		Version = (short)(bytes[0] | bytes[1] << 8);
		if (Version == DefaultVersion) {
			Box = new Es1DefaultDialogBox(this);
		} else if (Version == MessageVersion) {
			if (new Es1DialogTextTransformer().Parse(bytes) is not { } text) {
				Console.Error.WriteLine($"{path} does not read: the message box shows no text.");
				yield break;
			}

			var box = new Es1DigitalDialogBox(this, text.Lines, Line());
			foreach (var wait in box.LoadTalkingHead(a, b)) {
				yield return wait;
			}

			Box = box;
		}
	}

	/// <summary>
	/// <c>Dialog_DeleteBox</c> (<c>00018d93</c>): with a box, its destructor run (the vtable's slot <c>+4</c> with 3) and the
	/// box cleared.
	/// </summary>
	public IEnumerable<Es1ShellWait> DeleteBox() {
		if (Box is not { } box) {
			yield break;
		}

		foreach (var wait in box.Dtor()) {
			yield return wait;
		}

		Box = null;
	}

	/// <summary><c>Dialog_Draw</c> (<c>00018dcb</c>): with a box, <see cref="Es1DialogBox.Draw"/>(box, <see cref="Version"/>).</summary>
	public void Draw() => Box?.Draw(Version);

	/// <summary><c>Dialog_MarkDirty</c> (<c>00018d76</c>): with a box, <see cref="Es1DialogBox.MarkDirty"/>.</summary>
	public void MarkDirty() => Box?.MarkDirty();

	/// <summary><c>Dialog_PrevPage</c> (<c>00018df0</c>): with a box, a page back (<see cref="Es1DialogBox.PrevPage"/>) and the box drawn.</summary>
	public void PrevPage() {
		if (Box is { } box) {
			box.PrevPage();
			Draw();
		}
	}

	/// <summary><c>Dialog_NextPage</c> (<c>00018e18</c>): with a box, a page on (<see cref="Es1DialogBox.NextPage"/>) and the box drawn.</summary>
	public void NextPage() {
		if (Box is { } box) {
			box.NextPage();
			Draw();
		}
	}

	/// <summary>
	/// <c>Dialog_LoadConditional</c> (<c>00018cdb</c>)(dialog, <paramref name="ordinal"/>, <paramref name="tag"/>,
	/// <paramref name="path"/>) (docs/es1/formats/eng-mission-text.md#reading-a-block), the loader of a mission's
	/// <c>.ENG</c> blocks: the box deleted (<see cref="DeleteBox"/>), the file opened and its version word read; 1 makes an
	/// <see cref="Es1ConditionalDialogBox"/> from the block of <paramref name="tag"/> of that ordinal, any other no box.
	/// The file is read through <see cref="Es1MissionTextTransformer"/>, which finds a block by the byte counts where retail
	/// searches for the tag's bytes; in every retail file the two find the same blocks. A file that does not open or read
	/// leaves no box and the version as it was, reported, where retail reads from the failed stream, an engine guard.
	/// </summary>
	/// <param name="path">The path, its folder before the backslash, as <c>Mission_SetTextPath</c> builds it.</param>
	/// <param name="flag"><c>MissionVar_ReadFlag</c> (<c>0003628d</c>), which a choice's text is picked by.</param>
	public IEnumerable<Es1ShellWait> LoadConditional(short ordinal, string tag, string path, Func<short, short> flag) {
		foreach (var wait in DeleteBox()) {
			yield return wait;
		}

		int slash = path.IndexOf('\\');
		byte[]? bytes = _read?.Invoke(slash < 0 ? "" : path[..slash], path[(slash + 1)..]);
		if (bytes is not { Length: >= 2 }) {
			Console.Error.WriteLine($"{path} is missing or unreadable: its {tag} box shows no text.");
			yield break;
		}

		Version = (short)(bytes[0] | bytes[1] << 8);
		if (Version != ConditionalVersion) {
			yield break;
		}

		Es1MissionText? text;
		try {
			text = new Es1MissionTextTransformer().Parse(bytes);
		} catch (InvalidDataException ex) {
			Console.Error.WriteLine($"{path} does not read ({ex.Message}): its {tag} box shows no text.");
			yield break;
		}

		if (text != null) {
			Box = new Es1ConditionalDialogBox(this, text, ordinal, tag, flag);
		}
	}

	/// <summary>The version word of an <c>.ENG</c> file, which makes an <see cref="Es1ConditionalDialogBox"/>.</summary>
	public const short ConditionalVersion = 1;
}
