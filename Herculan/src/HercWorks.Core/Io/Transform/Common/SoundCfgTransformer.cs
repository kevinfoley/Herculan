using System.Text;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Read;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>
/// Reads and writes <c>data\sound.cfg</c> (<see cref="SoundCfg"/>), interpreting each key as <c>Sfx_ReadConfig</c>
/// (<c>00463698</c>) does: two case-insensitive word compares, and two numbers read with <c>atol</c>, so
/// <c>Rate = 11 kHz</c> is 11 and <c>Rate = 11025</c> is not.
/// </summary>
public class SoundCfgTransformer : ByteTransformer<SoundCfg> {
	public override SoundCfg? Parse(byte[]? bytes) {
		if (bytes == null) {
			return null;
		}

		var values = IniSection.Read(IniSection.Lines(bytes), SoundCfg.Section);
		string Value(string key) => values.TryGetValue(key, out string? value) ? value : string.Empty;

		int buffers = Atol(Value("Buffers"));
		return new SoundCfg {
			Driver = string.Equals(Value("Driver"), "DirectSound", StringComparison.OrdinalIgnoreCase)
				? SoundCfg.SoundDriver.DirectSound
				: SoundCfg.SoundDriver.Mme,
			Buffers = buffers is >= 1 and <= 64 ? buffers : SoundCfg.DefaultBuffers,
			SampleRate = Atol(Value("Rate")) == 11 ? SoundCfg.LowSampleRate : SoundCfg.StandardSampleRate,
			Mono = string.Equals(Value("Width"), "Mono", StringComparison.OrdinalIgnoreCase),
		};
	}

	/// <summary>
	/// Writes the section in the shipped file's words and spacing. The comments a retail file carries are not
	/// kept.
	/// </summary>
	public override byte[]? Write(SoundCfg source) {
		var text = new StringBuilder()
			.Append('[').Append(SoundCfg.Section).Append("]\r\n\r\n")
			.Append("Driver = ").Append(source.Driver == SoundCfg.SoundDriver.DirectSound ? "DirectSound" : "MME").Append("\r\n")
			.Append("Buffers = ").Append(source.Buffers).Append("\r\n")
			.Append("Rate = ").Append(source.SampleRate == SoundCfg.LowSampleRate ? "11" : "22").Append("\r\n")
			.Append("Width = ").Append(source.Mono ? "Mono" : "Stereo").Append("\r\n");
		return Encoding.ASCII.GetBytes(text.ToString());
	}

	/// <summary>
	/// The C runtime's <c>atol</c>: leading whitespace, an optional sign, then decimal digits up to the first
	/// character that is not one; 0 when there are none.
	/// </summary>
	private static int Atol(string text) {
		int at = 0;
		while (at < text.Length && char.IsWhiteSpace(text[at])) {
			at++;
		}

		bool negative = false;
		if (at < text.Length && text[at] is '+' or '-') {
			negative = text[at] == '-';
			at++;
		}

		long value = 0;
		while (at < text.Length && text[at] is >= '0' and <= '9' && value <= int.MaxValue) {
			value = value * 10 + (text[at] - '0');
			at++;
		}

		value = negative ? -value : value;
		return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
	}
}
