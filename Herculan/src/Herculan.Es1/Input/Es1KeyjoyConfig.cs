using System.Text;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Input;

/// <summary>
/// <c>Keyjoy_LoadConfig</c> (<c>ES1DBSIM 0004d918</c>): the three switches of <c>data\keyjoy.cfg</c>, each set by its
/// key reading <c>REVERSE</c> (docs/es1/simulation/input.md#keyjoycfg--keyjoy_loadconfig-0004d918; what each inverts,
/// ES2's docs/retail/simulation/joystick-input.md#the-keyjoy-switches).
/// </summary>
public sealed class Es1KeyjoyConfig {
	/// <summary>The file, which <c>Config_BuildPath</c> (<c>00059300</c>) puts in <c>data\</c> outside a replay.</summary>
	public const string FileName = "keyjoy.cfg";

	/// <summary>The words of the table at <c>00095948</c>, in its order; a word matching none is index 7, which does nothing.</summary>
	private static readonly string[] Words = ["=", "#", "TILT", "BACKTURN", "DEFAULT", "REVERSE", "MISSILE"];

	private const int ArmWord = 0, CommentWord = 1, TiltWord = 2, BackturnWord = 3, DefaultWord = 4, ReverseWord = 5, MissileWord = 6;

	/// <summary><c>Str_CompareNoCaseN</c>'s length, 255.</summary>
	private const int CompareLength = 0xff;

	/// <summary><c>Keyjoy_TiltReversed</c> (<c>0009580c</c>): the keyboard's pitch is negated.</summary>
	public bool TiltReversed { get; private set; }

	/// <summary><c>Keyjoy_BackturnReversed</c> (<c>00095810</c>): the steering is negated while the throttle axis is positive.</summary>
	public bool BackturnReversed { get; private set; }

	/// <summary><c>Keyjoy_MissileReversed</c> (<c>00095814</c>): a flown round's pitch is not negated.</summary>
	public bool MissileReversed { get; private set; }

	/// <summary>
	/// <c>data\keyjoy.cfg</c> of the install, opened in mode <c>rt</c>; all three switches clear when it does not open.
	/// The replay's <c>tapes\keyjoy.cfg</c> is not read: playback is not ported.
	/// </summary>
	public static Es1KeyjoyConfig Load(string installRoot) {
		string path = Es1MissionFiles.DataPath(installRoot, FileName);
		return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.Latin1)) : new Es1KeyjoyConfig();
	}

	/// <summary>
	/// The read: a word at a time (<c>fscanf " %s"</c>), each compared ignoring case with the seven words. <c>=</c>
	/// arms a value; <c>#</c> reads the rest of the line (<c>" %[^\n]"</c>, which first skips the white space, the line
	/// end among it, so a <c>#</c> that ends its line takes the next one); <c>TILT</c>, <c>BACKTURN</c> and
	/// <c>MISSILE</c> choose the key; <c>DEFAULT</c> with a value armed disarms it, and <c>REVERSE</c> with a value armed
	/// sets the key's switch and disarms it. <c>DEFAULT</c> or <c>REVERSE</c> with no value armed, and <c>REVERSE</c>
	/// with no key chosen, stop the read.
	/// </summary>
	public static Es1KeyjoyConfig Parse(string text) {
		var config = new Es1KeyjoyConfig();
		int position = 0, key = 0;
		bool armed = false;
		while (ReadWord(text, ref position) is { } word) {
			switch (Array.FindIndex(Words, candidate => SameIgnoringCase(word, candidate))) {
				case ArmWord:
					armed = true;
					break;
				case CommentWord:
					SkipWhiteSpace(text, ref position);
					while (position < text.Length && text[position] != '\n') {
						position++;
					}

					break;
				case TiltWord:
					key = 1;
					break;
				case BackturnWord:
					key = 2;
					break;
				case MissileWord:
					key = 3;
					break;
				case DefaultWord:
					if (!armed) {
						return config;
					}

					armed = false;
					break;
				case ReverseWord:
					if (!armed) {
						return config;
					}

					switch (key) {
						case 1:
							config.TiltReversed = true;
							break;
						case 2:
							config.BackturnReversed = true;
							break;
						case 3:
							config.MissileReversed = true;
							break;
						default:
							return config;
					}

					armed = false;
					break;
			}
		}

		return config;
	}

	/// <summary><c>" %s"</c>: skips white space and takes the run of characters up to the next, or null at the end.</summary>
	private static string? ReadWord(string text, ref int position) {
		SkipWhiteSpace(text, ref position);
		int start = position;
		while (position < text.Length && !IsSpace(text[position])) {
			position++;
		}

		return position > start ? text[start..position] : null;
	}

	private static void SkipWhiteSpace(string text, ref int position) {
		while (position < text.Length && IsSpace(text[position])) {
			position++;
		}
	}

	/// <summary>The C library's white space: space, tab, line feed, vertical tab, form feed and carriage return.</summary>
	private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

	/// <summary><c>Str_CompareNoCaseN</c>(a, b, 255) answering 0: the first 255 characters equal with ASCII letters folded.</summary>
	private static bool SameIgnoringCase(string word, string candidate) {
		string a = word.Length > CompareLength ? word[..CompareLength] : word;
		if (a.Length != candidate.Length) {
			return false;
		}

		for (int i = 0; i < a.Length; i++) {
			if (char.ToUpperInvariant(a[i]) != candidate[i] || a[i] > 0x7f) {
				return false;
			}
		}

		return true;
	}
}
