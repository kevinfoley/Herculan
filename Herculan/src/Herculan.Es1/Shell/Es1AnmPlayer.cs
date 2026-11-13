using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Es1.Sound;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell;

/// <summary>
/// The shell's <c>.ANM</c> player, <c>Anm_Play</c> (<c>000132d8</c>) and the player object it runs
/// (docs/es1/formats/anm-animation.md#the-player): a full-screen animation drawn frame by frame straight onto the
/// visible page, over a copy of the draw page that shows through color 0, with a sound.
/// </summary>
public sealed class Es1AnmPlayer {
	/// <summary>The ticks of <c>Shell_WaitTicks</c> in a second, which <see cref="Play"/>'s pacing counts in.</summary>
	private const int TicksPerSecond = 60;

	private readonly Es1VideoMemory _display;
	private readonly GameContent _content;
	private readonly Es1ShellSound _sound;
	private readonly Func<short, IEnumerable<Es1ShellWait>> _waitTicks;

	public Es1AnmPlayer(Es1VideoMemory display, GameContent content, Es1ShellSound sound,
			Func<short, IEnumerable<Es1ShellWait>> waitTicks) {
		_display = display;
		_content = content;
		_sound = sound;
		_waitTicks = waitTicks;
	}

	/// <summary>The sound object <c>0007e9a4</c>, the last animation's sound; none until one is made.</summary>
	public Es1SoundObject? Sound { get; private set; }

	/// <summary>
	/// <c>Anm_Play</c> (<c>000132d8</c>)(<paramref name="mode"/>, <paramref name="soundIndex"/>, <paramref name="path"/>)
	/// (docs/es1/formats/anm-animation.md#playing): the source page set to the draw page and the draw page to the visible
	/// one; in a mode other than 0 the source page's screen grabbed as the background; with a sound index other than −1
	/// and a sound manager, <see cref="Sound"/> destroyed and made anew on the index, and, while
	/// <paramref name="soundOption"/> (<c>Shell_SoundOption</c>) is set and it was made, the song faded
	/// (<see cref="Es1ShellSound.FadeOut"/>) and the sound played; the file's frames 0 to its frame count − 2 drawn; the
	/// source page set to the visible page and the draw page put back, the whole screen copied between them, and the
	/// source page put back.
	///
	/// <para>Retail's shell passes mode 1 alone, which draws the frames with no wait between them, each showing for as
	/// long as the PC took to write it through the VGA's ports. Here a frame's drawing takes no time, so each frame is
	/// held for the time the file's frames a second give it, mode 0's time: this engine's pacing, not retail's.</para>
	/// </summary>
	public IEnumerable<Es1ShellWait> Play(byte mode, short soundIndex, string path, byte soundOption) {
		var display = _display;
		int keptSource = display.SourcePage, keptDraw = display.DrawPage;
		display.SourcePage = keptDraw;
		display.DrawPage = display.VisiblePage;
		ushort[]? background = mode != 0
			? display.ReadRect(0, 0, Es1VideoMemory.PageWidth - 1, Es1VideoMemory.PageHeight - 1)
			: null;

		if (soundIndex != -1 && _sound.Manager is { } manager) {
			Sound?.Destroy();
			Sound = manager.CreateObject(2, 0, soundIndex);
			if (soundOption != 0 && Sound is { } sound) {
				if (_sound.MusicObject is { } music) {
					foreach (var wait in _sound.FadeOut(music)) {
						yield return wait;
					}
				}

				sound.Play();
			}
		}

		if (Load(path) is { } file) {
			short hold = (short)(file.FramesPerSecond != 0 ? TicksPerSecond / file.FramesPerSecond : 0);
			// Anm_SetFrame takes a frame below the frame count − 1.
			for (int frame = 0; frame == 0 || frame < file.FrameCount - 1; frame++) {
				DrawFrame(file, frame, background);
				foreach (var wait in _waitTicks(hold)) {
					yield return wait;
				}
			}
		}

		display.SourcePage = display.VisiblePage;
		display.DrawPage = keptDraw;
		display.CopyRect(0, 0, Es1VideoMemory.PageWidth - 1, Es1VideoMemory.PageHeight - 1, 0, 0);
		display.SourcePage = keptSource;
	}

	/// <summary>
	/// <c>Anm_Load</c> (<c>00013aa8</c>): the file at <paramref name="path"/>, a folder and a name, when it opens and
	/// holds the two ids; none otherwise.
	/// </summary>
	private Es1DeluxePaintAnimation? Load(string path) {
		int cut = path.LastIndexOf('\\');
		string folder = cut < 0 ? "" : path[..cut], name = path[(cut + 1)..].ToUpperInvariant();
		if (_content.Read(folder, name) is not { } bytes || new Es1DeluxePaintAnimationTransformer().Parse(bytes) is not { } file) {
			Console.Error.WriteLine($"{path} is missing or unreadable: the animation is not played.");
			return null;
		}

		return file.Id == Es1DeluxePaintAnimation.LpfId && file.ContentType == Es1DeluxePaintAnimation.AnimId ? file : null;
	}

	/// <summary>
	/// <c>Anm_FindFrameRecord</c> (<c>00013d9e</c>): the operations of <paramref name="frame"/>'s record, in the last
	/// page whose table entry holds the frame; none when no page does. A record past the page's own is none, an engine
	/// guard.
	/// </summary>
	private static byte[]? FindFrameRecord(Es1DeluxePaintAnimation file, int frame) {
		Es1AnimationPage? found = null;
		foreach (var page in file.Pages) {
			if (page.TableFirstRecord <= frame && frame < page.TableFirstRecord + page.TableRecordCount) {
				found = page;
			}
		}

		int index = found == null ? -1 : frame - found.FirstRecord;
		return found != null && index >= 0 && index < found.Records.Length ? found.Records[index].Ops : null;
	}

	/// <summary>
	/// <c>Anm_DrawFrame</c> (<c>00013c46</c>): the frame's operations run from pixel offset 0 of the draw page
	/// (docs/es1/formats/anm-animation.md#the-draw-operations), a 0 pixel taking <paramref name="background"/>'s at its
	/// offset when there is one. The operations stop at the record's end and a pixel past the page is not written,
	/// engine guards.
	/// </summary>
	private void DrawFrame(Es1DeluxePaintAnimation file, int frame, ushort[]? background) {
		if (FindFrameRecord(file, frame) is not { } ops) {
			return;
		}

		int at = 0, offset = 0;
		while (at < ops.Length) {
			byte b = ops[at++];
			if (b == 0) {
				// A fill: a count and a color.
				if (at + 1 >= ops.Length) {
					return;
				}

				int count = ops[at++];
				byte color = ops[at++];
				Fill(ref offset, count, color, background);
			} else if (b < 0x80) {
				Copy(ops, ref at, ref offset, b, background);
			} else if (b > 0x80) {
				offset += b & 0x7f;
			} else {
				if (at + 1 >= ops.Length) {
					return;
				}

				short word = (short)(ops[at] | ops[at + 1] << 8);
				at += 2;
				if (word == 0) {
					return;
				}

				if (word > 0) {
					offset += word;
				} else if ((word & 0x4000) == 0) {
					Copy(ops, ref at, ref offset, word & 0x3fff, background);
				} else if (at < ops.Length) {
					Fill(ref offset, word & 0x3fff, ops[at++], background);
				}
			}
		}
	}

	/// <summary><c>VgaPlanar_FillRun</c> (<c>0001320f</c>): <paramref name="count"/> pixels of <paramref name="color"/> from <paramref name="offset"/>.</summary>
	private void Fill(ref int offset, int count, byte color, ushort[]? background) {
		for (int i = 0; i < count; i++) {
			Put(offset++, color, background);
		}
	}

	/// <summary><c>VgaPlanar_CopyRun</c> (<c>00013270</c>): the next <paramref name="count"/> bytes of <paramref name="ops"/> to the pixels from <paramref name="offset"/>.</summary>
	private void Copy(byte[] ops, ref int at, ref int offset, int count, ushort[]? background) {
		for (int i = 0; i < count && at < ops.Length; i++) {
			Put(offset++, ops[at++], background);
		}
	}

	/// <summary>The pixel at <paramref name="offset"/> of the draw page set to <paramref name="color"/>, or for color 0 with a background to the background's cell there.</summary>
	private void Put(int offset, byte color, ushort[]? background) {
		if (offset < 0 || offset >= Es1VideoMemory.PageWidth * Es1VideoMemory.PageHeight) {
			return;
		}

		ushort value = color == 0 && background != null ? background[offset] : color;
		_display.Plot(offset % Es1VideoMemory.PageWidth, offset / Es1VideoMemory.PageWidth, value);
	}
}
