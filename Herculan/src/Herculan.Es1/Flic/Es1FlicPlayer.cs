using HercWorks.Core.Data.File;

namespace Herculan.Es1.Flic;

/// <summary>
/// <c>Flic_Play</c> (<c>ES1PLAYFLIC 00003530</c>) (docs/es1/formats/dfl-flic.md#playing-a-flic): each frame decoded
/// (<see cref="Es1FlicDecoder"/>) when its due time comes, on the 8253's clock or, after a sync chunk moves it there, on a
/// sound's position (<see cref="Es1FlicSound"/>); a key waiting ends the flic at a frame's wait, and the flic ends after its
/// last frame's wait.
///
/// <para>Retail loops until the flic ends; here the loop is run as far as the clock and the sound have reached each time
/// the host's frame hands it time (<see cref="Advance"/>), so a late frame is decoded at once, as retail decodes it. The
/// clock counts the host's time in the 8253's ticks from the start of the flic.</para>
/// </summary>
public sealed class Es1FlicPlayer {
	/// <summary>The 8253 timer's ticks a second, <c>Clock_Open</c> (<c>00001c1c</c>)'s rate.</summary>
	public const int ClockRate = 1_193_180;

	/// <summary><c>Flic_WaitFrame</c>'s answer when a key ends the flic, which <c>Flic_Main</c> reports nothing for.</summary>
	public const int KeyEnded = -10;

	/// <summary><c>Flic_NextFrame</c>'s answer for a top-level chunk that is not a frame: Bad frame in flic.</summary>
	public const int BadFrame = -4;

	/// <summary>The sync on the clock, <c>Flic_Play</c>'s at the start; the sync chunk's <c>0xe</c> is a sound's, as is any other value.</summary>
	public const uint ClockSync = 0xd, SoundSync = 0xe;

	/// <summary>
	/// PLACEHOLDER: how long one palette of a fade at once takes on screen, in seconds. Retail writes the palettes with no
	/// wait, so the DAC's port writes alone set it (dfl-flic.md#open); this is 768 writes at about 1 µs each, an estimate
	/// the user chose until it is measured.
	/// </summary>
	public const double FadeStepSeconds = 768e-6;

	private readonly Es1Flic _flic;
	private readonly Es1FlicSound _sound;
	private readonly Es1FlicSwitches _switches;
	private readonly uint _speed;

	private enum Phase { Next, Decoding, SyncWait, Waiting }

	private Phase _phase = Phase.Next;
	private int _frame;
	private uint _due;
	private double _seconds;

	// The sync at 0002566c: the sync (+0), the frame count (+4), the start (+8) and the sound's number (+0xc).
	private uint _sync = ClockSync, _count = 1, _start;
	private int _soundNumber;

	// Where the loop stands in time: the time the wait it last passed was reached, or the end of a fade at once it ran
	// since. A frame decoded late, in a host frame after its due time, is decoded at this time as retail decodes it.
	private double _cursor;
	private IEnumerator<Es1FlicStep>? _decoding;
	private (Es1FlicFadeAtOnce Fade, double Start)? _fade;
	private bool _keyWaiting;

	/// <summary>
	/// A player for <paramref name="flic"/> with <paramref name="switches"/>, its sounds in <paramref name="sound"/>: at
	/// <see cref="Es1FlicSwitches.Speed"/> milliseconds a frame when it is not 0, and otherwise at the header's, which a
	/// <c>.FLI</c> gives in 70ths of a second (<c>Flic_Open</c>, <c>000030f0</c>). The clock starts at 0, <c>Flic_Play</c>'s
	/// start, with the sync on it and the frame count at 1.
	/// </summary>
	public Es1FlicPlayer(Es1Flic flic, Es1FlicSwitches switches, Es1FlicSound sound) {
		_flic = flic;
		_switches = switches;
		_sound = sound;
		_speed = flic.Magic == Es1Flic.FliMagic ? flic.Speed * 1000 / 70 : flic.Speed;
		if (switches.Speed != 0) {
			_speed = (uint)switches.Speed;
		}

		Decoder = new Es1FlicDecoder(flic.Width, flic.Height);
	}

	/// <summary>The screen and the DAC the frames are decoded into.</summary>
	public Es1FlicDecoder Decoder { get; }

	/// <summary>Whether the flic has ended.</summary>
	public bool Ended { get; private set; }

	/// <summary><c>Flic_Play</c>'s answer once <see cref="Ended"/>: 0, <see cref="KeyEnded"/> or <see cref="BadFrame"/>.</summary>
	public int Result { get; private set; }

	/// <summary>
	/// A key the BIOS buffers was pressed: <c>Key_Poll</c> (<c>00001ca4</c>) finds it at the next wait, which reads it and
	/// ends the flic, or ends only the sync chunk's wait.
	/// </summary>
	public void KeyPressed() => _keyWaiting = true;

	/// <summary>The clock moved on by <paramref name="seconds"/>, and the loop run as far as it and the sound have reached.</summary>
	public void Advance(double seconds) {
		_seconds += seconds;
		_sound.Update(_seconds);
		while (!Ended) {
			switch (_phase) {
				case Phase.Next:
					// 1. With a fade running, a step of it; 2. Flic_NextFrame.
					if (Decoder.Fading) {
						Decoder.FadeStep();
					}

					var frame = _flic.Frames[_frame];
					if (frame.Type != Es1Flic.FrameType) {
						End(BadFrame);
						break;
					}

					_decoding = Decoder.DecodeFrame(frame).GetEnumerator();
					_phase = Phase.Decoding;
					break;
				case Phase.Decoding:
					if (_fade is { } fade) {
						int step = (int)((_seconds - fade.Start) / FadeStepSeconds);
						if (step < fade.Fade.Steps) {
							fade.Fade.ShowStep(step);
							return;
						}

						_cursor = fade.Start + fade.Fade.Steps * FadeStepSeconds;
						_fade = null;
					}

					if (_decoding!.MoveNext()) {
						switch (_decoding.Current) {
							case Es1FlicFadeAtOnce atOnce:
								_fade = (atOnce, _cursor);
								break;
							case Es1FlicMachineChunk { Type: Es1FlicDecoder.Sound } chunk:
								// Flic_DecodeSound (000023ec) does nothing under -Q.
								if (!_switches.Quiet) {
									_sound.Decode(chunk.Data);
								}

								break;
							case Es1FlicMachineChunk chunk:
								if (DecodeSync(chunk.Data)) {
									_phase = Phase.SyncWait;
								}

								break;
						}

						break;
					}

					// 3. Flic_FrameDue (000033a8), and the frame count up by one.
					_due = FrameDue();
					_count++;
					_phase = Phase.Waiting;
					break;
				case Phase.SyncWait:
					// Flic_DecodeSync's wait for the new start: a key is read and ends this wait alone.
					if (_keyWaiting) {
						_keyWaiting = false;
						_phase = Phase.Decoding;
						break;
					}

					if (!Reached(_start)) {
						return;
					}

					_phase = Phase.Decoding;
					break;
				case Phase.Waiting:
					// 4. Flic_WaitFrame (00003440): a key first, then the clock or the sound against the due time, unsigned. On a
					// sound of no number in the list, no wait and no key read.
					if (_sync != ClockSync && _sound.Position(_soundNumber) == null) {
						Passed();
						break;
					}

					if (_keyWaiting) {
						_keyWaiting = false;
						End(KeyEnded);
						break;
					}

					if (!Reached(_due)) {
						return;
					}

					Passed();
					break;
			}
		}
	}

	/// <summary>The clock now, in the 8253's ticks.</summary>
	private uint Clock(double seconds) => unchecked((uint)(long)(seconds * ClockRate));

	/// <summary>
	/// Whether the clock, or the sound the sync is on, has reached <paramref name="time"/>, unsigned; with it reached, the
	/// loop's time moved on to the moment it was, as near as the host frame tells.
	/// </summary>
	private bool Reached(uint time) {
		if (_sync == ClockSync) {
			if (Clock(_seconds) < time) {
				return false;
			}

			_cursor = Math.Max(_cursor, (double)time / ClockRate);
			return true;
		}

		uint position = _sound.Position(_soundNumber) ?? time;
		if (position < time) {
			return false;
		}

		_cursor = Math.Max(_cursor, _seconds - (double)(position - time) / Es1FlicSound.Rate);
		return true;
	}

	/// <summary>A frame's wait passed: the next frame, or the flic's end after the last.</summary>
	private void Passed() {
		if (++_frame >= _flic.Frames.Length) {
			End(0);
			return;
		}

		_phase = Phase.Next;
	}

	/// <summary>
	/// <c>Flic_FrameDue</c> (<c>000033a8</c>): (speed × rate) / 1000, unsigned, times the frame count, plus the start; the
	/// rate is the clock's on the clock and <see cref="Es1FlicSound.Rate"/> on a sound. Its first call on a sound also keeps
	/// values at <c>00015c34</c>, <c>000248f8</c> and <c>000248fc</c> that no reader is found for (dfl-flic.md#open), which
	/// are not kept here.
	/// </summary>
	private uint FrameDue() {
		uint rate = _sync == ClockSync ? (uint)ClockRate : Es1FlicSound.Rate;
		return unchecked(_speed * rate / 1000 * _count + _start);
	}

	/// <summary>
	/// <c>Flic_DecodeSync</c> (<c>0000213c</c>), (docs/es1/formats/dfl-flic.md#the-sync-chunk): the new start and sync, and
	/// whether its wait for the start follows, as it does with the flag set unless the sync is on a sound of no number in
	/// the list.
	/// </summary>
	private bool DecodeSync(byte[] data) {
		if (data.Length < 16) {
			return false;
		}

		uint sync = BitConverter.ToUInt32(data, 0);
		int flag = BitConverter.ToInt32(data, 4), number = BitConverter.ToInt32(data, 8), adjust = BitConverter.ToInt32(data, 12);
		if (sync == ClockSync) {
			// The clock as retail reads it here: at the time the loop stands at.
			_start = Clock(_cursor);
		} else if (sync == SoundSync) {
			if (_switches.Quiet) {
				return false;
			}

			_soundNumber = number;
			_start = _sound.Position(number) ?? 0;
		}

		// The zero adjust: an IMUL and an unsigned DIV; -Z's: the same on the clock, a signed IDIV on a sound.
		unchecked {
			if (flag != 0) {
				_start += sync == ClockSync ? (uint)(adjust * ClockRate) / 1000 : (uint)(adjust * Es1FlicSound.Rate) / 1000;
			}

			int extra = _switches.ZeroAdjust;
			if (extra != 0) {
				_start += sync == ClockSync ? (uint)(extra * ClockRate) / 1000 : (uint)(extra * Es1FlicSound.Rate / 1000);
			}
		}

		_sync = sync;
		_count = 1;
		return flag != 0 && (sync == ClockSync || _sound.Position(_soundNumber) != null);
	}

	private void End(int result) {
		Result = result;
		Ended = true;
		_decoding?.Dispose();
		_decoding = null;
	}
}

/// <summary>
/// <c>Flic_Main</c>'s switches (docs/es1/formats/dfl-flic.md#the-player) that act on the playing: <c>-S</c>'s milliseconds
/// a frame, 0 for the header's; <c>-Z</c>'s milliseconds added to a sync's start (<c>000155f8</c>); and <c>-Q</c>, quiet,
/// which clears <c>000155f4</c>.
/// </summary>
public readonly record struct Es1FlicSwitches(int Speed, int ZeroAdjust, bool Quiet);
