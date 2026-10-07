using System.Numerics;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Install;
using Herculan.Engine.Platform;
using Herculan.Engine.Video;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host;

/// <summary>
/// Plays one <c>.AVI</c> cutscene in a window instead of running a mission or the front end —
/// <c>--movie</c>. The same thin-host arrangement as <see cref="Shell.ShellHost"/>: everything here is
/// wiring, and every rule about how a movie decodes lives in <c>HercWorks.Video</c>.
///
/// <para>It exists so a decoder can be looked at. A codec that is subtly wrong still returns frames
/// and still passes a unit test that only checks it did not throw; the failure mode is a picture
/// that is skewed, mirrored or mis-coloured, and the only reliable way to catch that is to put it on
/// the screen. See docs/retail/formats/avi-video.md.</para>
///
/// <para>The frame is letterboxed at its own aspect ratio rather than stretched, and sampled
/// nearest-neighbour, for the reason <see cref="GpuTexture"/> gives: these are 240x180-ish pictures
/// that the original scaled up with a point-sampling blitter.</para>
/// </summary>
static class MovieHost {
	/// <summary>
	/// Frames to let pass before <c>--screenshot</c> fires, matching <see cref="FrontEndWindow"/>: the
	/// window manager can hand back a stale or part-sized framebuffer for the first frame or two.
	/// </summary>
	private const int ScreenshotFrame = 5;

	/// <summary>
	/// The folder cutscenes sit in, on the disc beside the archive directory rather than inside it;
	/// paths under it go through <see cref="GameInstall.OpenDiscFile"/>.
	/// </summary>
	public const string MovieFolderName = Herculan.Engine.Shell.ShellMovieQueue.MovieFolder;

	public static int Run(string installRoot, GameDisc? disc, string movieName, string? screenshotPath = null,
			bool silentAudio = false) {
		using var stream = Open(installRoot, disc, movieName, out string path);
		if (stream == null) {
			Console.Error.WriteLine(
				$"No such movie: {movieName}\n"
				+ $"Pass a path, or a file name to be looked up in the disc's or the install's {MovieFolderName} folder.");
			return 1;
		}

		if (ReadMovie(stream) is not { } bytes) {
			Console.Error.WriteLine(
				$"{Path.GetFileName(path)} is larger than the {HercWorks.Video.VideoLimits.Default.MaxFileBytes:N0} bytes a movie may be.");
			return 1;
		}

		// Report what the container says even when the codec is one that cannot be decoded, because
		// that is the answer to "why will this file not play".
		if (HercWorks.Video.Avi.AviFile.Open(bytes) is not { VideoFormat: { } format } container) {
			Console.Error.WriteLine($"{Path.GetFileName(path)} is not an AVI this reader understands.");
			return 1;
		}

		string fourCc = HercWorks.Video.Riff.RiffReader.FourCcText(format.Compression);
		Console.WriteLine($"{Path.GetFileName(path)}: {format.Width}x{format.Height}, "
			+ $"{format.BitCount}bpp, compression {fourCc} (0x{format.Compression:X8}), "
			+ $"{container.VideoPackets.Count} video packets, {container.FramesPerSecond:0.##} fps"
			+ (container.AudioFormat is { } audio
				? $", audio {audio.Channels}ch {audio.SampleRate}Hz {audio.BitsPerSample}-bit"
				: ", no audio"));

		if (MoviePlayer.Open(bytes) is not { } player) {
			Console.Error.WriteLine(
				$"No decoder for compression {fourCc}, or a frame size it cannot code.\n"
				+ "See docs/retail/formats/avi-video.md for what the corpus uses.");
			return 1;
		}

		using (player) {
			Console.WriteLine($"Playing {player.Width}x{player.Height}, {player.Duration.TotalSeconds:0.0}s. "
				+ "The movie loops; close the window to stop.");

			return Present(player, screenshotPath, silentAudio);
		}
	}

	/// <summary>
	/// Reads a movie whole, or returns null when it is larger than
	/// <see cref="HercWorks.Video.VideoLimits.MaxFileBytes"/>. The length is checked on the open
	/// stream before anything is allocated, so an oversized file is refused rather than read into
	/// memory first (or, past 2 GB, made to throw) and only then rejected by the parser.
	/// </summary>
	public static byte[]? ReadMovie(Stream stream) {
		if (stream.Length > HercWorks.Video.VideoLimits.Default.MaxFileBytes) {
			return null;
		}

		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		return bytes;
	}

	/// <summary>
	/// Takes the argument as a path when it names a file, and otherwise as a name to look up in the
	/// movie folder, so <c>--movie ALPH_TH.AVI</c> works without a full path. <paramref name="path"/> is
	/// what was opened, for messages.
	/// </summary>
	private static Stream? Open(string installRoot, GameDisc? disc, string movieName, out string path) {
		path = movieName;
		if (File.Exists(movieName)) {
			return File.OpenRead(movieName);
		}

		// Let the caller name a movie without its extension.
		foreach (string name in new[] { movieName, movieName + ".AVI" }) {
			path = Path.Combine(MovieFolderName, name);
			if (GameInstall.OpenDiscFile(installRoot, disc, path) is { } stream) {
				return stream;
			}
		}

		return null;
	}

	private static int Present(MoviePlayer player, string? screenshotPath, bool silentAudio) {
		using var window = new EngineWindow($"HERCULAN Engine — movie");

		ShaderProgram? shader = null;
		GpuOverlayMesh? mesh = null;
		IAudioBackend? audio = null;
		int framesRendered = 0;

		window.Load += (loadedGl, _) => {
			shader = ShaderProgram.Load(loadedGl, "Overlay2D.glsl");
			mesh = new GpuOverlayMesh(loadedGl);

			if (silentAudio) {
				return;
			}

			// A machine with no working OpenAL plays silent rather than failing the run, the same way
			// the mission host treats it.
			audio = OpenAlBackend.TryCreate(out string? failure) as IAudioBackend ?? new NullAudioBackend();
			if (failure != null) {
				Console.WriteLine($"Audio unavailable ({failure}) — playing silent.");
			}

			player.StartAudio(audio);
		};

		window.Render += (delta, frameGl) => {
			// Looping keeps a short cutscene on screen long enough to look at; the thumbnails are
			// eleven frames and would otherwise be over before the window settled.
			if (player.IsFinished) {
				player.Restart();
			}

			player.Update(frameGl, TimeSpan.FromSeconds(delta));

			frameGl.ClearColor(0f, 0f, 0f, 1f);
			frameGl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

			var framebuffer = window.FramebufferSize;
			if (shader != null && mesh != null && player.Texture is { } texture) {
				frameGl.Disable(EnableCap.DepthTest);
				shader.Use();
				shader.SetVector2("uViewportSize", new Vector2(framebuffer.X, framebuffer.Y));
				shader.SetSamplerTexture("uTexture", texture.Handle, 0);
				mesh.SubmitAndDraw(Quad(framebuffer.X, framebuffer.Y, player.Width, player.Height));
			}

			framesRendered++;
			if (screenshotPath != null && framesRendered == ScreenshotFrame) {
				Screenshot.Capture(frameGl, framebuffer.X, framebuffer.Y, screenshotPath);
				window.Close();
			}
		};

		window.Closing += () => {
			player.Stop(audio);
			audio?.Dispose();
			audio = null;

			// The player owns a GL texture, so it has to be released here while the context is still
			// current. Disposing it with the rest of the host's objects after Run returns would call
			// glDeleteTextures against a context GLFW has already torn down.
			player.Dispose();
			mesh?.Dispose();
			mesh = null;
			shader?.Dispose();
			shader = null;
		};

		window.Run();
		return 0;
	}

	/// <summary>
	/// Builds the two triangles the frame is drawn on, centred and scaled to fit the window at the
	/// movie's own aspect ratio. Positions are in pixel space with the origin top-left, which is what
	/// <c>Overlay2D.glsl</c> expects.
	/// </summary>
	private static Overlay2DVertex[] Quad(int viewWidth, int viewHeight, int movieWidth, int movieHeight) {
		float scale = Math.Min((float)viewWidth / movieWidth, (float)viewHeight / movieHeight);
		float drawWidth = movieWidth * scale;
		float drawHeight = movieHeight * scale;
		float left = (viewWidth - drawWidth) * 0.5f;
		float top = (viewHeight - drawHeight) * 0.5f;
		float right = left + drawWidth;
		float bottom = top + drawHeight;

		// VideoFrame is top row first, so V runs with Y and needs no flip.
		var topLeft = new Overlay2DVertex(new Vector2(left, top), new Vector2(0f, 0f));
		var topRight = new Overlay2DVertex(new Vector2(right, top), new Vector2(1f, 0f));
		var bottomRight = new Overlay2DVertex(new Vector2(right, bottom), new Vector2(1f, 1f));
		var bottomLeft = new Overlay2DVertex(new Vector2(left, bottom), new Vector2(0f, 1f));

		return [topLeft, topRight, bottomRight, topLeft, bottomRight, bottomLeft];
	}
}
