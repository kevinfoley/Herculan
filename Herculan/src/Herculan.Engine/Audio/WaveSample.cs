namespace Herculan.Engine.Audio;

/// <summary>
/// One decoded RIFF/WAVE sample, normalised to signed 16-bit mono.
///
/// <para>The game's own sample banks need nothing more than this: every entry in
/// <c>SIMSOUND.VOL</c>, <c>SHLSOUND.VOL</c> and the three voice archives is uncompressed PCM
/// (format tag 1), single-channel, at 11025 or 22050 Hz, in 8-bit unsigned or 16-bit signed. There
/// is no ADPCM and no stereo anywhere in the shipped data, so nothing here tries to handle
/// either — an unsupported chunk layout returns null rather than being guessed at.</para>
///
/// <para>8-bit input is widened to 16-bit at load rather than at mix time: the backends want one
/// format, and the whole shipped bank is under two megabytes even doubled.</para>
/// </summary>
public sealed class WaveSample {
	private WaveSample(short[] samples, int sampleRate) {
		Samples = samples;
		SampleRate = sampleRate;
	}

	/// <summary>
	/// Wraps samples that were decoded somewhere other than a <c>.WAV</c> file.
	///
	/// <para>Movie soundtracks arrive this way: <c>HercWorks.Video</c> pulls them out of an AVI's
	/// interleaved audio packets, so they never exist as a RIFF file for <see cref="Decode"/> to
	/// parse. The mono contract is the caller's to meet, as it is for every other sample here.</para>
	/// </summary>
	public static WaveSample FromSamples(short[] samples, int sampleRate) {
		ArgumentNullException.ThrowIfNull(samples);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		return new WaveSample(samples, sampleRate);
	}

	/// <summary>Signed 16-bit mono PCM.</summary>
	public short[] Samples { get; }

	/// <summary>Frames per second, as the file declared it, or as <see cref="Downsample"/> made it.</summary>
	public int SampleRate { get; }

	/// <summary>How long the sample runs.</summary>
	public TimeSpan Duration =>
		TimeSpan.FromSeconds(SampleRate > 0 ? (double)Samples.Length / SampleRate : 0);

	/// <summary>
	/// Decodes a RIFF/WAVE file, or returns null when it is not one, is not uncompressed mono PCM,
	/// or is truncated. Chunks are walked rather than assumed to sit at fixed offsets, because a
	/// couple of retail voice files carry trailing data past the end of their RIFF chunk.
	/// </summary>
	public static WaveSample? Decode(byte[] bytes) {
		if (bytes.Length < 12
			|| bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F'
			|| bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') {
			return null;
		}

		int channels = 0, sampleRate = 0, bitsPerSample = 0;
		int dataAt = -1, dataLength = 0;

		int at = 12;
		while (at + 8 <= bytes.Length) {
			uint id = BitConverter.ToUInt32(bytes, at);
			int length = BitConverter.ToInt32(bytes, at + 4);
			if (length < 0 || at + 8 + length > bytes.Length) {
				// A declared length running past the file is a truncated chunk; stop rather than
				// read whatever follows. What has been gathered so far may still be enough.
				break;
			}

			int body = at + 8;
			if (id == FourCc('f', 'm', 't', ' ') && length >= 16) {
				if (BitConverter.ToUInt16(bytes, body) != PcmFormatTag) {
					return null;
				}

				channels = BitConverter.ToUInt16(bytes, body + 2);
				sampleRate = BitConverter.ToInt32(bytes, body + 4);
				bitsPerSample = BitConverter.ToUInt16(bytes, body + 14);
			} else if (id == FourCc('d', 'a', 't', 'a')) {
				dataAt = body;
				dataLength = length;
			}

			// Chunk bodies are word-aligned; an odd length is followed by a pad byte.
			at = body + length + (length & 1);
		}

		if (channels != 1 || sampleRate <= 0 || dataAt < 0) {
			return null;
		}

		return bitsPerSample switch {
			8 => new WaveSample(FromUnsigned8(bytes, dataAt, dataLength), sampleRate),
			16 => new WaveSample(FromSigned16(bytes, dataAt, dataLength), sampleRate),
			_ => null,
		};
	}

	/// <summary>
	/// What the simulator's sample loader, <c>Sos_LoadWaveSample</c> (<c>00474254</c>), makes of a file: a
	/// RIFF/WAVE file through <see cref="Decode"/>, and anything not starting <c>RIFF</c> as raw 8-bit unsigned mono
	/// at <see cref="RawSampleRate"/>. A <c>.hmp</c> song named in <c>SOUNDS.STR</c> ends up here, because the
	/// catalog opens every row as a sample — see docs/retail/formats/audio.md, "Opening a catalog row".
	///
	/// <para>The RIFF half is this engine's chunk walk rather than the original's fixed offsets, so a RIFF file
	/// <see cref="Decode"/> refuses is null here where the original would play it as its header reads.</para>
	/// </summary>
	public static WaveSample? DecodeForSimulator(byte[] bytes) {
		if (bytes.Length >= 4 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F') {
			return Decode(bytes);
		}

		return bytes.Length > 0 ? new WaveSample(FromUnsigned8(bytes, 0, bytes.Length), RawSampleRate) : null;
	}

	/// <summary>The rate <see cref="DecodeForSimulator"/> gives a file that is not RIFF.</summary>
	public const int RawSampleRate = 11025;

	/// <summary>
	/// This sample band-limited and resampled to <paramref name="sampleRate"/>, or this sample itself when it is
	/// not faster than that already.
	///
	/// <para><b>The filter is this engine's.</b> It stands in for SOS mixing a faster sample into a slower output
	/// (docs/retail/formats/audio.md, "DATA\SOUND.CFG"). How SOS converts happens inside <c>sos9503.dll</c> and
	/// has not been read, so this is a clean conversion and not a copy of it: a windowed-sinc low-pass with its
	/// cutoff just under the new Nyquist rate. It removes the top of the old sample's band and adds no
	/// aliasing.</para>
	/// </summary>
	public WaveSample Downsample(int sampleRate) {
		if (sampleRate <= 0 || SampleRate <= sampleRate || Samples.Length == 0) {
			return this;
		}

		double step = (double)SampleRate / sampleRate;
		double cutoff = 0.5 / step * PassbandFraction;
		double half = ZeroCrossings * step;
		var kernel = KernelTable(cutoff, half);

		var output = new short[(int)(Samples.Length / step)];
		for (int n = 0; n < output.Length; n++) {
			double centre = n * step;
			int first = Math.Max(0, (int)Math.Ceiling(centre - half));
			int last = Math.Min(Samples.Length - 1, (int)Math.Floor(centre + half));

			double sum = 0, weights = 0;
			for (int k = first; k <= last; k++) {
				double w = KernelAt(kernel, Math.Abs(k - centre));
				sum += Samples[k] * w;
				weights += w;
			}

			// Dividing by the weights rather than by the kernel's ideal area keeps the gain at 1 at the ends,
			// where the kernel runs off the sample.
			output[n] = (short)Math.Clamp(Math.Round(weights != 0 ? sum / weights : 0), short.MinValue, short.MaxValue);
		}

		return new WaveSample(output, sampleRate);
	}

	/// <summary>Where <see cref="Downsample"/>'s cutoff sits, as a fraction of the new Nyquist rate.</summary>
	private const double PassbandFraction = 0.9;

	/// <summary><see cref="Downsample"/>'s kernel half-width, in periods of the new rate.</summary>
	private const int ZeroCrossings = 16;

	/// <summary>Kernel samples per input frame in <see cref="KernelTable"/>, interpolated between.</summary>
	private const int KernelResolution = 256;

	/// <summary>
	/// A Blackman-windowed sinc low-pass, cutoff in cycles per input frame, tabulated from 0 to
	/// <paramref name="half"/> input frames and zero beyond.
	/// </summary>
	private static double[] KernelTable(double cutoff, double half) {
		var table = new double[(int)Math.Ceiling(half * KernelResolution) + 2];
		for (int i = 0; i < table.Length; i++) {
			double x = (double)i / KernelResolution;
			if (x >= half) {
				break;
			}

			double phase = 2 * Math.PI * cutoff * x;
			double sinc = x == 0 ? 1 : Math.Sin(phase) / phase;
			double window = 0.42 + 0.5 * Math.Cos(Math.PI * x / half) + 0.08 * Math.Cos(2 * Math.PI * x / half);
			table[i] = sinc * window;
		}

		return table;
	}

	private static double KernelAt(double[] table, double distance) {
		double position = distance * KernelResolution;
		int index = (int)position;
		if (index + 1 >= table.Length) {
			return 0;
		}

		double fraction = position - index;
		return table[index] + (table[index + 1] - table[index]) * fraction;
	}

	private const int PcmFormatTag = 1;

	private static uint FourCc(char a, char b, char c, char d) =>
		(uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

	/// <summary>8-bit WAVE data is unsigned with 0x80 as silence; widen it about that midpoint.</summary>
	private static short[] FromUnsigned8(byte[] bytes, int at, int length) {
		var samples = new short[length];
		for (int i = 0; i < length; i++) {
			samples[i] = (short)((bytes[at + i] - 0x80) << 8);
		}

		return samples;
	}

	private static short[] FromSigned16(byte[] bytes, int at, int length) {
		var samples = new short[length / 2];
		for (int i = 0; i < samples.Length; i++) {
			samples[i] = BitConverter.ToInt16(bytes, at + i * 2);
		}

		return samples;
	}
}
