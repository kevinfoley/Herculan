namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// <c>data\sound.cfg</c> — the sound driver settings, section <c>[Sound]</c> of an INI, as both executables
/// interpret it. Read by <see cref="Io.Transform.Common.SoundCfgTransformer"/>. See docs/retail/simulation/audio.md,
/// "DATA\SOUND.CFG".
/// </summary>
public class SoundCfg {
	/// <summary>The file's name in the game's <c>data</c> folder, as the installer writes it.</summary>
	public const string FileName = "SOUND.CFG";

	/// <summary>The section all four keys live in.</summary>
	public const string Section = "Sound";

	/// <summary>What <see cref="Buffers"/> takes when the file's value is missing or outside 1-64.</summary>
	public const int DefaultBuffers = 5;

	/// <summary>The mixing rate <c>Rate = 11</c> selects.</summary>
	public const int LowSampleRate = 11025;

	/// <summary>The mixing rate every other <c>Rate</c> selects, the shipped <c>22</c> included.</summary>
	public const int StandardSampleRate = 22050;

	/// <summary>The output driver <see cref="Driver"/> names.</summary>
	public enum SoundDriver {
		/// <summary><c>MME</c>, the Windows multimedia driver, and anything else the file says.</summary>
		Mme = 1,

		/// <summary><c>DirectSound</c>, compared case-insensitively.</summary>
		DirectSound = 2,
	}

	/// <summary>
	/// <c>Driver</c> — which output driver the simulator opens SOS on. The shell reads the key and sets MME
	/// whatever it says.
	/// </summary>
	public SoundDriver Driver { get; set; } = SoundDriver.Mme;

	/// <summary><c>Buffers</c>, 1-64 — the MME driver's buffer count, per the file's own comments.</summary>
	public int Buffers { get; set; } = DefaultBuffers;

	/// <summary>
	/// <c>Rate</c> — the rate SOS mixes at: <see cref="LowSampleRate"/> when the value reads as the number 11, and
	/// <see cref="StandardSampleRate"/> otherwise.
	/// </summary>
	public int SampleRate { get; set; } = StandardSampleRate;

	/// <summary><c>Width</c> — mono output when the value is <c>Mono</c>, compared case-insensitively; stereo otherwise.</summary>
	public bool Mono { get; set; }

	/// <summary>
	/// Reads the file at <paramref name="path"/>, or the shipped settings when it is absent or unreadable — which
	/// is what both executables get too, <c>GetPrivateProfileStringA</c> handing back its empty default string
	/// for every key of a missing file.
	/// </summary>
	public static SoundCfg Load(string path) {
		try {
			return new Io.Transform.Common.SoundCfgTransformer().Parse(System.IO.File.ReadAllBytes(path)) ?? new SoundCfg();
		} catch (IOException) {
			return new SoundCfg();
		} catch (UnauthorizedAccessException) {
			return new SoundCfg();
		}
	}
}
