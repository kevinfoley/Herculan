using HercWorks.Core.Data.File;

namespace Herculan.Es1.Flic;

/// <summary>
/// <c>PLAYFLIC.EXE</c>'s screen and palette as a flic's frames change them (docs/es1/formats/dfl-flic.md): the mode-<c>0x13</c>
/// screen buffer <c>Screen_OpenMode13</c> (<c>ES1PLAYFLIC 00001844</c>) opens, the palette copy at <c>00025056</c>, the VGA
/// DAC, and the running fade (<c>00025662</c>, <c>0002565e</c>, <c>00025666</c>, <c>00025668</c>). <see cref="DecodeFrame"/>
/// is <c>Flic_DecodeFrame</c> (<c>000031d0</c>) over one frame's subchunks.
///
/// <para>The screen starts at index 0 everywhere, as setting mode <c>0x13</c> clears video memory. The DAC starts at 0
/// everywhere, which is the BIOS's mode-<c>0x13</c> palette only at index 0, the one entry the cleared screen shows; both
/// files send their palette before their first picture, so no other entry of the BIOS's is ever shown.</para>
/// </summary>
public sealed class Es1FlicDecoder {
	/// <summary>The mode-<c>0x13</c> screen's width, its stride.</summary>
	public const int ScreenWidth = 320;

	/// <summary>The mode-<c>0x13</c> screen's height.</summary>
	public const int ScreenHeight = 200;

	/// <summary>The subchunk types <see cref="DecodeFrame"/> handles (docs/es1/formats/dfl-flic.md#the-frames).</summary>
	public const ushort Color256 = 0x04, Color64 = 0x0b, Black = 0x0d, Picture = 0x14, Sound = 0x15, Fade = 0x16, Sync = 0x18;

	// Flic_DecodeFade's directions and kinds (docs/es1/formats/dfl-flic.md#the-palette-fades).
	private const int FromColor = 6, ToColor = 7, AtOnce = 8, PerFrame = 9;

	private readonly int _width, _height;

	// The palette copy, 8-bit channels as the flic's palette chunks give them.
	private readonly byte[] _palette = new byte[256 * 3];

	// The running fade: the remaining count (00025662), the steps (0002565e), the direction (00025666) and the color
	// (00025668).
	private int _remaining, _steps, _direction;
	private readonly byte[] _color = new byte[3];

	/// <summary>A decoder for a flic of <paramref name="width"/> × <paramref name="height"/>, centred on the screen as <c>Flic_CenterOnScreen</c> (<c>00000510</c>) centres it.</summary>
	public Es1FlicDecoder(int width, int height) {
		_width = width;
		_height = height;
		OriginX = (ScreenWidth - width) / 2;
		OriginY = (ScreenHeight - height) / 2;
	}

	/// <summary>The screen, <see cref="ScreenWidth"/> × <see cref="ScreenHeight"/> palette indices.</summary>
	public byte[] Screen { get; } = new byte[ScreenWidth * ScreenHeight];

	/// <summary>The DAC's 256 entries, three 6-bit channels each.</summary>
	public byte[] Dac { get; } = new byte[256 * 3];

	/// <summary>How many times the DAC has been written: what changes when it does.</summary>
	public int DacVersion { get; private set; }

	/// <summary>How many times the screen has been written: what changes when it does.</summary>
	public int ScreenVersion { get; private set; }

	/// <summary>The frame's origin on the screen.</summary>
	public int OriginX { get; }

	/// <inheritdoc cref="OriginX"/>
	public int OriginY { get; }

	/// <summary>Whether a fade a step a frame is running, which <c>Flic_Play</c> steps before each frame (<see cref="FadeStep"/>).</summary>
	public bool Fading => _remaining != 0;

	/// <summary>
	/// <c>Flic_DecodeFrame</c> (<c>000031d0</c>): <paramref name="frame"/>'s subchunks dispatched by type. Each fade that
	/// writes its palettes at once is yielded before it is written, for the caller to time its steps
	/// (<see cref="Es1FlicFadeAtOnce"/>); the frame's decoding goes on when the enumeration is resumed, which writes the
	/// fade's last palettes. The sound and the sync chunks, whose handlers act on the sound and the clock, are yielded
	/// for the caller to run (<see cref="Es1FlicMachineChunk"/>).
	/// </summary>
	public IEnumerable<Es1FlicStep> DecodeFrame(Es1FlicFrame frame) {
		foreach (var chunk in frame.Chunks) {
			switch (chunk.Type) {
				case Color256:
					DecodeColorPackets(chunk.Data, sixBit: false);
					break;
				case Color64:
					DecodeColorPackets(chunk.Data, sixBit: true);
					break;
				case Black:
					DecodeBlack();
					break;
				case Picture:
					DecodeCopyDelta(chunk.Data);
					break;
				case Fade:
					if (DecodeFade(chunk.Data) is { } atOnce) {
						yield return atOnce;
						atOnce.Finish();
					}

					break;
				case Sound or Sync:
					yield return new Es1FlicMachineChunk(chunk.Type, chunk.Data);
					break;
				// Animator Pro's 7, 0xc, 0xf and 0x10 and Dynamix's 0x17 are in neither file (dfl-flic.md#the-frames), so their
				// handlers are not ported; any other type is passed over, as retail passes it.
			}
		}
	}

	/// <summary>
	/// <c>Flic_FadeStep</c> (<c>000027a8</c>): one step of the running fade, the remaining count down by one
	/// (docs/es1/formats/dfl-flic.md#the-palette-fades).
	/// </summary>
	public void FadeStep() {
		_remaining--;
		int k = _direction == ToColor ? _steps - _remaining : _remaining;
		SetFadePalette(k, _steps);
	}

	/// <summary>
	/// <c>Flic_DecodeColorPackets</c> (<c>00002c54</c>): each packet's entries into the palette copy, and to the DAC
	/// while no fade runs, <c>COLOR_256</c>'s 8-bit channels shifted right by 2 and <c>COLOR_64</c>'s as they are.
	/// </summary>
	private void DecodeColorPackets(byte[] data, bool sixBit) {
		int packets = (short)(data[0] | data[1] << 8), at = 2, entry = 0;
		while (--packets >= 0) {
			entry += data[at];
			int count = data[at + 1] == 0 ? 256 : data[at + 1];
			at += 2;
			int bytes = Math.Min(count * 3, Math.Min(data.Length - at, _palette.Length - entry * 3));
			if (bytes <= 0) {
				// Engine guard: retail reads and writes past the chunk and the palette; here the packet ends there.
				return;
			}

			if (_remaining == 0) {
				for (int i = 0; i < bytes; i++) {
					Dac[entry * 3 + i] = sixBit ? data[at + i] : (byte)(data[at + i] >> 2);
				}

				DacVersion++;
			}

			Array.Copy(data, at, _palette, entry * 3, bytes);
			at += count * 3;
			entry += count;
		}
	}

	/// <summary><c>Flic_DecodeBlack</c> (<c>00002ff8</c>): index 0 over the flic's rect at its origin.</summary>
	private void DecodeBlack() {
		for (int y = 0; y < _height; y++) {
			for (int x = 0; x < _width; x++) {
				Put((OriginY + y) * ScreenWidth + OriginX + x, 0);
			}
		}

		ScreenVersion++;
	}

	/// <summary>
	/// <c>Flic_DecodeChunk14</c> (<c>00002c1c</c>) and <c>Flic_DecodeCopyDelta</c> (<c>00003e19</c>): the commands from
	/// the frame's origin (docs/es1/formats/dfl-flic.md#the-picture-chunk).
	/// </summary>
	private void DecodeCopyDelta(byte[] data) {
		int count = data[0] | data[1] << 8, at = 4;
		int cursor = OriginY * ScreenWidth + OriginX;
		for (int i = 0; i < count && at + 2 <= data.Length; i++) {
			byte b0 = data[at], b1 = data[at + 1];
			ushort w0 = (ushort)(b0 | b1 << 8);
			int op = b0 & 7, n, offset;
			if (op is 6 or 7) {
				if (at + 4 > data.Length) {
					break;
				}

				ushort w1 = (ushort)(data[at + 2] | data[at + 3] << 8);
				at += 4;
				(offset, n) = op == 6 ? ((short)w0 >> 3, (int)w1) : ((int)(short)w1, w0 >> 3);
			} else {
				at += 2;
				switch (op) {
					case 0:
						cursor += w0 >> 3;
						continue;
					case 1:
						n = w0 >> 3;
						for (int k = 0; k < n && at + k < data.Length; k++) {
							Put(cursor + k, data[at + k]);
						}

						at += n;
						cursor += n;
						continue;
					case 2:
						(offset, n) = ((sbyte)b0 >> 3, b1);
						break;
					case 3:
						(offset, n) = ((sbyte)b1, b0 >> 3);
						break;
					case 4:
						(offset, n) = (((sbyte)b0 >> 3) - ScreenWidth, b1);
						break;
					default:
						(offset, n) = (((sbyte)b0 >> 3) + ScreenWidth, b1);
						break;
				}
			}

			// REP MOVSW: each two bytes read and then written, then one MOVSB for an odd count, from the screen as it stands.
			int source = cursor + offset;
			for (int k = 0; k + 1 < n; k += 2) {
				byte first = Get(source + k), second = Get(source + k + 1);
				Put(cursor + k, first);
				Put(cursor + k + 1, second);
			}

			if ((n & 1) != 0) {
				Put(cursor + n - 1, Get(source + n - 1));
			}

			cursor += n;
		}

		ScreenVersion++;
	}

	/// <summary>
	/// <c>Flic_DecodeFade</c> (<c>0000286c</c>): a fade a step a frame is started and its first step written; a fade at once
	/// is answered for <see cref="DecodeFrame"/> to yield. Another direction or kind does nothing.
	/// </summary>
	private Es1FlicFadeAtOnce? DecodeFade(byte[] data) {
		if (data.Length < 16) {
			return null;
		}

		int direction = BitConverter.ToInt32(data, 0), kind = BitConverter.ToInt32(data, 4);
		int steps = BitConverter.ToInt32(data, 8), color = BitConverter.ToInt32(data, 12);
		if (direction is not (FromColor or ToColor) || kind is not (AtOnce or PerFrame)) {
			return null;
		}

		_color[0] = (byte)((color & 0xf00) >> 4);
		_color[1] = (byte)(color & 0xf0);
		_color[2] = (byte)(color << 4);
		if (steps == 0) {
			steps = 1;
		}

		if (kind == AtOnce) {
			return new Es1FlicFadeAtOnce(this, direction == ToColor, steps);
		}

		_direction = direction;
		_steps = steps;
		_remaining = steps;
		FadeStep();
		_remaining++;
		return null;
	}

	/// <summary>
	/// The DAC written whole with every entry <i>P</i> + (<i>C</i> − <i>P</i>) × <paramref name="k"/> / <paramref name="n"/>
	/// in each channel, signed and truncated, through <c>Vga_SetDac8</c> (<c>00001af0</c>).
	/// </summary>
	internal void SetFadePalette(int k, int n) {
		for (int i = 0; i < _palette.Length; i++) {
			int p = _palette[i];
			Dac[i] = (byte)((byte)((_color[i % 3] - p) * k / n + p) >> 2);
		}

		DacVersion++;
	}

	/// <summary>The palette copy itself to the DAC, the end of a fade at once from the color.</summary>
	internal void SetPalette() {
		for (int i = 0; i < _palette.Length; i++) {
			Dac[i] = (byte)(_palette[i] >> 2);
		}

		DacVersion++;
	}

	/// <summary>A fade at once ends any running fade.</summary>
	internal void EndFade() {
		_steps = 0;
		_remaining = 0;
	}

	// Engine guard: retail writes and reads video memory past the screen's 64,000 bytes; here those are dropped and read as 0.
	private void Put(int at, byte value) {
		if ((uint)at < (uint)Screen.Length) {
			Screen[at] = value;
		}
	}

	private byte Get(int at) => (uint)at < (uint)Screen.Length ? Screen[at] : (byte)0;
}

/// <summary>A step of a frame's decoding that <see cref="Es1FlicDecoder.DecodeFrame"/> hands its caller.</summary>
public abstract class Es1FlicStep;

/// <summary>
/// A <c>0x15</c> sound or <c>0x18</c> sync chunk, whose handlers, <c>Flic_DecodeSound</c> (<c>000023ec</c>) and
/// <c>Flic_DecodeSync</c> (<c>0000213c</c>), act on the machine's sound and clock rather than the screen
/// (docs/es1/formats/dfl-flic.md#the-sync-chunk).
/// </summary>
public sealed class Es1FlicMachineChunk(ushort type, byte[] data) : Es1FlicStep {
	/// <summary>The subchunk's type, <see cref="Es1FlicDecoder.Sound"/> or <see cref="Es1FlicDecoder.Sync"/>.</summary>
	public ushort Type { get; } = type;

	/// <summary>The subchunk's data.</summary>
	public byte[] Data { get; } = data;
}

/// <summary>
/// A <c>0x16</c> fade of kind 8, which <c>Flic_DecodeFade</c> (<c>0000286c</c>) writes as <see cref="Steps"/> palettes back
/// to back (docs/es1/formats/dfl-flic.md#the-palette-fades). The caller shows the step its time has reached
/// (<see cref="ShowStep"/>); resuming the frame writes the rest (<see cref="Finish"/>).
/// </summary>
public sealed class Es1FlicFadeAtOnce : Es1FlicStep {
	private readonly Es1FlicDecoder _decoder;
	private readonly bool _toColor;

	internal Es1FlicFadeAtOnce(Es1FlicDecoder decoder, bool toColor, int steps) {
		_decoder = decoder;
		_toColor = toColor;
		Steps = steps;
	}

	/// <summary>The palettes the fade writes.</summary>
	public int Steps { get; }

	/// <summary>The fade's palette number <paramref name="step"/>, from 0, to the DAC: <i>k</i> = <see cref="Steps"/> − step from the color, step + 1 to it.</summary>
	public void ShowStep(int step) => _decoder.SetFadePalette(_toColor ? step + 1 : Steps - step, Steps);

	/// <summary>The fade's last palette, then the palette itself after a fade from the color; the running fade ended.</summary>
	internal void Finish() {
		ShowStep(Steps - 1);
		if (!_toColor) {
			_decoder.SetPalette();
		}

		_decoder.EndFade();
	}
}
