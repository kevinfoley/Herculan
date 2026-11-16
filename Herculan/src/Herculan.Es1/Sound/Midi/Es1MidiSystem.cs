using Herculan.Engine.Audio;
using Herculan.Engine.Audio.Opl;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Sound.Midi;

/// <summary>
/// The MIDI half of the HMI library (<c>HmiMidi_InitSystem</c>, <c>00070ed8</c>): the open drivers, the song slots and the
/// player over them. One a sound manager, since retail's is the program's one set of globals
/// (docs/es1/sound/song-player.md).
/// </summary>
public sealed class Es1MidiSystem {
	/// <summary>The chip's rate times the interrupts so far, less the frames rendered for them times the interrupt rate.</summary>
	private int _frameCarry;

	/// <summary>Makes the system with no driver open.</summary>
	/// <param name="open">The install's reader, a path to a file's bytes, through which the drivers' images load.</param>
	/// <param name="fmChip">The chip the FM driver writes to; none makes one (<see cref="Es1MidiDrivers.FmChip"/>).</param>
	/// <param name="digital">The digital driver device <c>0xa005</c> plays on; none makes one (<see cref="Es1MidiDrivers.DigiMidi"/>).</param>
	public Es1MidiSystem(Func<string, byte[]?>? open = null, IOplChip? fmChip = null, Es1DigitalDriver? digital = null) {
		Drivers = new Es1MidiDrivers(open, fmChip, digital);
		Songs = new Es1HmiSongs(Drivers);
		Player = new Es1HmiPlayer(Drivers, Songs);
	}

	/// <summary>The open drivers.</summary>
	public Es1MidiDrivers Drivers { get; }

	/// <summary>
	/// <c>HmiMidi_InitSystem</c> (<c>00070ed8</c>)(path): <paramref name="path"/> copied to the drivers'
	/// <see cref="Es1MidiDrivers.DriverPath"/>, an empty string for none. The two devices' function tables it fills are
	/// <see cref="Es1MidiDrivers.DigiMidi"/> and <see cref="Es1MidiDrivers.CallbackMidi"/>, there from the construction;
	/// the absolute address of the data segment and the memory locks have no counterpart, nor
	/// <c>HmiMidi_SystemStarted</c> (<c>000a2570</c>).
	/// </summary>
	public void InitSystem(string? path) => Drivers.DriverPath = path ?? "";

	/// <summary>The song slots.</summary>
	public Es1HmiSongs Songs { get; }

	/// <summary>The player.</summary>
	public Es1HmiPlayer Player { get; }

	/// <summary>
	/// The FM chip's output in the host's mixer, which <see cref="Tick"/> renders into; none until
	/// <see cref="OpenOutput"/>. Not retail's, whose chip makes its own sound.
	/// </summary>
	public OplStream? Output { get; private set; }

	/// <summary>
	/// <see cref="Output"/> opened on <paramref name="backend"/> for <see cref="Es1MidiDrivers.FmChip"/>, in blocks of one
	/// interrupt's share of the chip's rate rounded down, 414 frames at an OPL's 49,716 a second, so the stream's ring holds as few interrupts' writes
	/// ahead of what is heard as it can; any output open before is closed first.
	/// </summary>
	public void OpenOutput(IAudioBackend backend) {
		CloseOutput();
		Output = new OplStream(backend, Drivers.FmChip, Drivers.FmChip.SampleRate / Es1SimClock.InterruptsPerSecond);
	}

	/// <summary><see cref="Output"/> stopped and dropped, the frame count's carry with it.</summary>
	public void CloseOutput() {
		Output?.Dispose();
		Output = null;
		_frameCarry = 0;
	}

	/// <summary>
	/// One interrupt of the game's timer: <see cref="Es1HmiPlayer.Tick"/> on each song slot in turn, then the interrupt's
	/// share of the FM chip's frames rendered into <see cref="Output"/> as one call, so the tick's register writes take
	/// effect at the tick's time in the output. Retail's timer calls each playing song's own event in the order the library
	/// holds them; the slot order stands in for it.
	///
	/// <para>The share is the chip's rate over <see cref="Es1SimClock.InterruptsPerSecond"/>, 49,716 / 120: 414 frames or 415,
	/// the fraction carried to the next interrupt, so that each second of interrupts renders the chip's second. The engine
	/// counts the interrupts at 120 a second of the host's time (<see cref="Es1SimClock"/>), where retail's hardware gives
	/// 120.05, so a song plays 0.04% slower than on that hardware. Every write of a tick takes effect from the tick's first
	/// frame.</para>
	/// </summary>
	public void Tick() {
		for (int slot = 0; slot < Es1HmiSongs.Slots; slot++) {
			if (Songs.Song(slot) is { } song) {
				Player.Tick(song);
			}
		}

		if (Output is not { } output) {
			return;
		}

		_frameCarry += output.Chip.SampleRate;
		int frames = _frameCarry / Es1SimClock.InterruptsPerSecond;
		_frameCarry -= frames * Es1SimClock.InterruptsPerSecond;
		output.Render(frames);
	}
}
