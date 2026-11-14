using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Read;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Disc;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Es1.Input;
using Herculan.Es1.Shell;
using Herculan.Es1.View;
using Silk.NET.Input;

namespace Herculan.Es1.Flic;

/// <summary>
/// ES1's flic player, <c>PLAYFLIC.EXE</c>'s <c>Flic_Main</c> (<c>ES1PLAYFLIC 00000310</c>), as a turn in the launcher's
/// window (docs/es1/formats/dfl-flic.md#playing-a-flic): the flic opened, played (<see cref="Es1FlicPlayer"/>) into the
/// mode-<c>0x13</c> screen with its sound (<see cref="Es1FlicSound"/>), shown at 4:3 as the shell's screen
/// is (<see cref="Es1ScreenPresenter"/>), and closed.
///
/// <para>Retail sets mode <c>0x13</c> before it opens the flic and the saved mode after; between turns the launcher's
/// window shows black (<see cref="HostWindow.Hold"/>), which stands in for both.</para>
///
/// <para><b>Engine guard:</b> closing the window ends the flic, as a key does.</para>
/// </summary>
public static class Es1FlicHost {
	/// <summary>The default <c>Flic_DecodeSound</c> gives <c>pathFlic</c>.</summary>
	public const string DefaultPathFlic = @".\";

	private const string FlicPlayerSection = "FlicPlayer", PathFlicKey = "pathFlic";

	/// <summary>The most a flic or a sound read from a disc may hold; the retail flics are under 6 MB.</summary>
	public const long MaxFileBytes = 64L << 20;

	/// <summary>
	/// Plays the flic at <paramref name="path"/> with <paramref name="switches"/>, <c>PLAYFLIC.EXE</c>'s arguments after the
	/// flic's name, in <paramref name="window"/>. Returns <c>Flic_Play</c>'s answer (<see cref="Es1FlicPlayer.Result"/>), or
	/// the open's error. The install folder stands in for DOS's current folder, where <c>RESOURCE.CFG</c> is read
	/// (<see cref="ReadPathFlic"/>); <paramref name="altDrive"/> is <c>ALT_DRIVE</c>, under whose path a sound's path on a
	/// drive is taken (<see cref="ReadSound"/>).
	/// </summary>
	/// <param name="silent">The host's <c>--no-sound</c>: no audio device is opened, and the sounds are not heard.</param>
	public static int Run(string path, IReadOnlyList<string> switches, HostWindow window, string installRoot, string? altDrive,
			bool silent) =>
		Run(path, () => File.ReadAllBytes(path), switches, window, installRoot,
			string.IsNullOrEmpty(altDrive) ? null : relative => ReadFile(CaseInsensitivePath.Combine(altDrive, relative)), silent);

	/// <summary>
	/// Plays the flic at <paramref name="relativePath"/> on <paramref name="disc"/>, the CD the Settings window names
	/// (<see cref="Install.Es1Disc"/>), as <see cref="Run(string, IReadOnlyList{string}, HostWindow, string, string?, bool)"/>
	/// plays one on <c>ALT_DRIVE</c>, a sound's path on a drive taken on the disc.
	/// </summary>
	public static int Run(GameDisc disc, string relativePath, IReadOnlyList<string> switches, HostWindow window,
			string installRoot, bool silent) =>
		Run(disc.Describe(relativePath), () => disc.ReadAllBytes(relativePath, MaxFileBytes)
				?? throw new FileNotFoundException($"{disc.Describe(relativePath)} is missing or larger than {MaxFileBytes} bytes."),
			switches, window, installRoot, relative => ReadDiscFile(disc, relative), silent);

	// The flic, named for messages by path and read by readFlic; readOnCd reads a path relative to the CD's root, or is
	// null when there is no CD.
	private static int Run(string path, Func<byte[]> readFlic, IReadOnlyList<string> switches, HostWindow window,
			string installRoot, Func<string, byte[]?>? readOnCd, bool silent) {
		var parsed = ParseSwitches(switches);

		// Machine_Open: the audio device for this turn, as the shell's and the simulator's turns open their own, closed with
		// Machine_Close; with none the sounds play unheard (Es1FlicSound).
		string? audioFailure = null;
		var device = silent ? null : OpenAlBackend.TryCreate(out audioFailure);
		using IAudioBackend audio = device as IAudioBackend ?? new NullAudioBackend();
		if (audioFailure != null) {
			Console.Error.WriteLine(device != null
				? $"Audio: {audioFailure}."
				: $"Audio unavailable ({audioFailure}) — the flic plays silent.");
		}

		// Flic_Open (000030f0). A flic that does not open prints Flic_Main's message; its second line, for a file that does
		// not open or read, is the C library's text, which the host's stands in for.
		Es1Flic? flic;
		try {
			flic = new Es1FlicTransformer().Parse(readFlic());
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
				or DiscFormatException) {
			Console.Error.WriteLine($"READFLIC had troubles with {path}.{Environment.NewLine}{ex.Message}.");
			return -5;
		} catch (InvalidDataException ex) {
			// Engine guard: retail plays the frames up to the one it cannot read and then reports Bad frame in flic; this
			// reader refuses the whole file.
			Console.Error.WriteLine($"READFLIC had troubles with {path}.{Environment.NewLine}Bad frame in flic. ({ex.Message})");
			return Es1FlicPlayer.BadFrame;
		}

		if (flic == null || flic.Magic is not (Es1Flic.FlcMagic or Es1Flic.FliMagic)) {
			Console.Error.WriteLine($"READFLIC had troubles with {path}.{Environment.NewLine}Not a flic file.");
			return -3;
		}

		using var sound = new Es1FlicSound(audio, ReadPathFlic(installRoot), dosPath => ReadSound(dosPath, installRoot, readOnCd));
		var player = new Es1FlicPlayer(flic, parsed, sound);
		Play(player, window);
		window.Hold(null);
		if (player.Result is < 0 and not Es1FlicPlayer.KeyEnded) {
			Console.Error.WriteLine($"READFLIC had troubles with {path}.{Environment.NewLine}Bad frame in flic.");
		}

		return player.Result;
	}

	/// <summary>
	/// <c>Flic_Main</c>'s switches, each looked up by the character after its <c>-</c> or <c>/</c>, either case, the last
	/// given winning: <c>-S</c> and <c>-Z</c>, <c>atoi</c> of the rest, and <c>-Q</c>. <c>-B</c> and <c>-C</c> size the
	/// preload, which has no counterpart here; <c>-D</c>'s debug screen has none either.
	/// </summary>
	private static Es1FlicSwitches ParseSwitches(IReadOnlyList<string> switches) {
		var parsed = new Es1FlicSwitches(0, 0, false);
		foreach (string argument in switches) {
			if (argument.Length < 2 || argument[0] is not ('-' or '/')) {
				continue;
			}

			switch (char.ToUpperInvariant(argument[1])) {
				case 'S':
					parsed = parsed with { Speed = Atoi(argument.AsSpan(2)) };
					break;
				case 'Z':
					parsed = parsed with { ZeroAdjust = Atoi(argument.AsSpan(2)) };
					break;
				case 'Q':
					parsed = parsed with { Quiet = true };
					break;
			}
		}

		return parsed;
	}

	/// <summary>
	/// <c>Flic_DecodeSound</c>'s read of <c>RESOURCE.CFG</c>'s <c>[FlicPlayer]</c> <c>pathFlic</c> through
	/// <c>Profile_GetString</c> (<c>00000e58</c>): <see cref="DefaultPathFlic"/> when the file, the section or the key is
	/// missing. An empty value, which retail passes over for a later line of the same key, is taken as missing. The
	/// value is taken as written: retail's <c>Str_SkipControlChars</c> (<c>000017d4</c>) leaves every value as it is
	/// (docs/es1/formats/dfl-flic.md#the-sound). Retail reads the file again for each load; it is read once here, as
	/// nothing writes it while a flic plays.
	/// </summary>
	private static string ReadPathFlic(string installRoot) {
		byte[]? bytes = null;
		try {
			string path = CaseInsensitivePath.Combine(installRoot, Es1ResourceCfg.FileName);
			bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// The file does not open, and the default stands.
		}

		return Es1Profile.Parse(bytes).GetString(FlicPlayerSection, PathFlicKey) is { Length: > 0 } value ? value : DefaultPathFlic;
	}

	/// <summary>
	/// The file a sound's DOS path names. A path on a drive, as <c>RESOURCE.CFG</c>'s <c>pathFlic</c> names the CD's, is
	/// taken on the CD by <paramref name="readOnCd"/>, which stands for the CD as <c>ES.BAT</c>'s <c>ALT_DRIVE=D:</c> does,
	/// and failing that in the install folder, the CD's copy; a path with no drive is taken in the install folder, DOS's
	/// current folder here. This mapping is the user's choice: retail opens the path as DOS resolves it. Null when no
	/// candidate opens.
	/// </summary>
	private static byte[]? ReadSound(string dosPath, string installRoot, Func<string, byte[]?>? readOnCd) {
		bool onDrive = dosPath.Length > 1 && dosPath[1] == ':';
		string[] names = (onDrive ? dosPath[2..] : dosPath).Split('\\', '/').Where(n => n is not ("" or ".")).ToArray();
		if (names.Length == 0) {
			return null;
		}

		string relative = string.Join('\\', names);
		return (onDrive ? readOnCd?.Invoke(relative) : null) ?? ReadFile(CaseInsensitivePath.Combine(installRoot, relative));
	}

	/// <summary>The file at <paramref name="path"/>, or null when it is not there or does not open.</summary>
	private static byte[]? ReadFile(string path) {
		try {
			return File.Exists(path) ? File.ReadAllBytes(path) : null;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary><paramref name="relative"/> on <paramref name="disc"/>, or null when it is not there, too long or does not read.</summary>
	private static byte[]? ReadDiscFile(GameDisc disc, string relative) {
		try {
			return disc.ReadAllBytes(relative, MaxFileBytes);
		} catch (Exception ex) when (ex is IOException or DiscFormatException) {
			return null;
		}
	}

	/// <summary>The C library's <c>atoi</c>: leading white space, a sign, then digits up to the first that is not one.</summary>
	private static int Atoi(ReadOnlySpan<char> text) {
		int at = 0, sign = 1, value = 0;
		while (at < text.Length && char.IsWhiteSpace(text[at])) {
			at++;
		}

		if (at < text.Length && text[at] is '+' or '-') {
			sign = text[at] == '-' ? -1 : 1;
			at++;
		}

		while (at < text.Length && text[at] is >= '0' and <= '9') {
			value = unchecked(value * 10 + (text[at] - '0'));
			at++;
		}

		return sign * value;
	}

	private static void Play(Es1FlicPlayer player, HostWindow host) {
		using var window = new EngineWindow(host, Es1ShellHost.Title);
		var display = new Es1VideoMemory(Es1VideoMemory.ShellWidth);
		var cells = new ushort[Es1FlicDecoder.ScreenWidth * Es1FlicDecoder.ScreenHeight];
		Es1ScreenPresenter? presenter = null;
		int screenShown = -1, dacShown = -1;

		window.Load += (gl, input) => {
			presenter = new Es1ScreenPresenter(gl, display);
			// The window as ES1 last ran; its [Alt+Enter] is not a key that skips the movie.
			var fullScreenKey = Es1FullScreen.Start(window, () => input.Mice.Count > 0 ? input.Mice[0] : null);
			foreach (var keyboard in input.Keyboards) {
				keyboard.KeyDown += (device, key, _) => {
					if (!fullScreenKey.KeyDown(key, device.IsKeyPressed) && BiosBuffers(key)) {
						player.KeyPressed();
					}
				};
				keyboard.KeyUp += (_, key, _) => fullScreenKey.KeyUp(key);
			}

			// Mode 0x13 shows no pointer.
			foreach (var mouse in input.Mice) {
				mouse.Cursor.CursorMode = CursorMode.Hidden;
			}
		};
		window.Update += delta => {
			player.Advance(delta);
			if (player.Ended) {
				window.Close();
			}
		};
		window.Render += (_, _) => {
			if (presenter == null) {
				return;
			}

			var decoder = player.Decoder;
			if (decoder.ScreenVersion != screenShown) {
				for (int i = 0; i < cells.Length; i++) {
					cells[i] = decoder.Screen[i];
				}

				display.WriteRect(0, 0, Es1FlicDecoder.ScreenWidth - 1, Es1FlicDecoder.ScreenHeight - 1, cells);
				screenShown = decoder.ScreenVersion;
			}

			if (decoder.DacVersion != dacShown) {
				presenter.SetPalette(Palette(decoder.Dac));
				dacShown = decoder.DacVersion;
			}

			presenter.Present(window.FramebufferSize);
		};
		window.Closing += () => {
			presenter?.Dispose();
			presenter = null;
		};
		window.Run();
	}

	/// <summary>
	/// Whether the BIOS puts a key in its buffer, where <c>Key_Poll</c> (<c>00001ca4</c>) finds it: every key but the
	/// shift and lock keys, which change only its flags, and <c>Pause</c> and <c>Print Screen</c>, which it handles
	/// itself. The Windows and menu keys, which a keyboard of the time lacks, count as none.
	/// </summary>
	private static bool BiosBuffers(Key key) => key is not (Key.Unknown or Key.ShiftLeft or Key.ShiftRight
		or Key.ControlLeft or Key.ControlRight or Key.AltLeft or Key.AltRight or Key.CapsLock or Key.NumLock
		or Key.ScrollLock or Key.Pause or Key.PrintScreen or Key.SuperLeft or Key.SuperRight or Key.Menu);

	/// <summary>The DAC's 6-bit channels as a palette, each times 4, as the ES1 palettes this engine shows are scaled.</summary>
	private static DynamixPalette Palette(byte[] dac) {
		const int scale = 4;
		var palette = new DynamixPalette { ColorCount = 256, Scalar = scale };
		for (int index = 0; index < 256; index++) {
			byte r = dac[index * 3], g = dac[index * 3 + 1], b = dac[index * 3 + 2];
			var entry = new ColorBytes(r, g, b, 255);
			entry.SetColor(RgbaColor.FromArgb(255, r * scale, g * scale, b * scale));
			palette.Colors[index] = entry;
		}

		return palette;
	}
}
