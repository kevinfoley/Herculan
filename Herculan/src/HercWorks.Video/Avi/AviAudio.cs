namespace HercWorks.Video.Avi;

/// <summary>
/// A movie's audio packets, widened to signed 16-bit samples one packet at a time.
///
/// <para>Every AVI in the retail corpus carries uncompressed PCM — format tag 1, either 8-bit
/// stereo at 11025 Hz or 16-bit mono at 22050 Hz. There is no ADPCM and nothing else to handle, so
/// an unsupported format tag is refused rather than guessed at, matching how
/// <c>Herculan.Engine.Audio.WaveSample</c> treats the sound banks.</para>
/// </summary>
public static class AviAudio {
	private const ushort PcmFormatTag = 1;

	/// <summary>Whether <paramref name="format"/> is uncompressed 8- or 16-bit PCM, which <see cref="Widen"/> reads.</summary>
	public static bool IsPlayable(AviAudioFormat? format) =>
		format is { FormatTag: PcmFormatTag, BitsPerSample: 8 or 16 };

	/// <summary>
	/// The samples <paramref name="packet"/> holds in <paramref name="format"/>, channels interleaved as the
	/// file has them. A 16-bit packet of odd length drops its last byte.
	/// </summary>
	public static short[] Widen(ReadOnlySpan<byte> packet, AviAudioFormat format) {
		if (format.BitsPerSample == 8) {
			var samples = new short[packet.Length];

			// 8-bit WAVE data is unsigned with 0x80 as silence; widen about that midpoint.
			for (int i = 0; i < packet.Length; i++) {
				samples[i] = (short)((packet[i] - 0x80) << 8);
			}

			return samples;
		}

		var wide = new short[packet.Length / 2];
		for (int i = 0; i < wide.Length; i++) {
			wide[i] = (short)(packet[2 * i] | (packet[2 * i + 1] << 8));
		}

		return wide;
	}
}
