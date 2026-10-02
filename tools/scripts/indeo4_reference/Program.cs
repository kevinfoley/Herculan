using System.Runtime.InteropServices;
using HercWorks.Video;
using HercWorks.Video.Avi;
using HercWorks.Video.Codecs;
using HercWorks.Video.Codecs.Indeo4;

// Drives the retail IR41_32.DLL as a reference decoder and compares its output with
// HercWorks.Video's Indeo 4 decoder, frame by frame and sample by sample.
//
// The DLL is called through its Video for Windows DriverProc; nothing is registered with the system.
// It refuses YVU9 output and dithers its RGB24, so the comparison reads its decoded planes out of
// its own memory after each frame instead. See Herculan/docs/formats/indeo4.md#evidence.
//
//   dotnet run --project tools/scripts/indeo4_reference -- <movie.avi> [--dll <IR41_32.DLL>] [--quiet]
//
// The DLL defaults to INDEO\IR41_32.DLL beside the movie's AVI folder, which is where the game's
// installer puts it. Exit code 0 when every frame matches, 1 on any mismatch, 2 on a usage error.
internal static unsafe class Program {
	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int DriverProcFn(int driverId, int driver, uint message, nint param1, nint param2);

	[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
	private static extern nint LoadLibraryA(string path);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern nint GetProcAddress(nint module, string name);

	[StructLayout(LayoutKind.Sequential)]
	private struct BitmapInfoHeader {
		public int Size, Width, Height;
		public short Planes, BitCount;
		public uint Compression;
		public int SizeImage, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant;
	}

	private const uint DrvLoad = 1;
	private const uint DrvEnable = 2;
	private const uint DrvOpen = 3;
	private const uint IcmDecompressQuery = 0x400B;
	private const uint IcmDecompressBegin = 0x400C;
	private const uint IcmDecompress = 0x400D;
	private const uint IcmDecompressEnd = 0x400E;
	private const int IcDecompressNotKeyFrame = 0x4000_0000;
	private const int IcModeDecompress = 2;
	private const int MaxPacketBytes = 1 << 20;

	// Where IR41_32.DLL keeps its decoded planes: the driver instance's codec at +0x10, its state at
	// +0x5c, the decoder at +0x40, and each plane's pointer and pitch at these offsets in turn.
	private static readonly int[] s_planeOffsets = [0x104, 0x138, 0x16C];

	private static int Main(string[] args) {
		string? moviePath = null;
		string? dllPath = null;
		bool quiet = false;
		for (int i = 0; i < args.Length; i++) {
			switch (args[i]) {
				case "--dll" when i + 1 < args.Length:
					dllPath = args[++i];
					break;
				case "--quiet":
					quiet = true;
					break;
				default:
					moviePath ??= args[i];
					break;
			}
		}

		if (moviePath == null) {
			Console.Error.WriteLine("usage: indeo4_reference <movie.avi> [--dll <IR41_32.DLL>] [--quiet]");
			return 2;
		}

		dllPath ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(moviePath))!, "..", "INDEO", "IR41_32.DLL");
		if (!File.Exists(dllPath)) {
			Console.Error.WriteLine($"no codec DLL at {dllPath}; pass --dll");
			return 2;
		}

		AviFile? avi = AviFile.Open(File.ReadAllBytes(moviePath));
		AviVideoFormat? format = avi?.VideoFormat;
		if (avi == null || format == null || format.Compression != CodecRegistry.Indeo4) {
			Console.Error.WriteLine($"{moviePath} is not an IV41 movie");
			return 2;
		}

		int width = format.Width;
		int height = format.Height;
		int chromaWidth = (width + 3) / 4;
		int chromaHeight = (height + 3) / 4;
		int[] planeWidths = [width, chromaWidth, chromaWidth];
		int[] planeHeights = [height, chromaHeight, chromaHeight];

		var ours = (Indeo4Decoder?)CodecRegistry.Create(format, VideoLimits.Default);
		if (ours == null) {
			Console.Error.WriteLine("HercWorks.Video refuses this movie's dimensions");
			return 2;
		}

		nint module = LoadLibraryA(dllPath);
		if (module == 0) {
			Console.Error.WriteLine($"could not load {dllPath} (error {Marshal.GetLastWin32Error()}); is this process 32-bit?");
			return 2;
		}

		var driverProc = Marshal.GetDelegateForFunctionPointer<DriverProcFn>(GetProcAddress(module, "DriverProc"));
		driverProc(0, 1, DrvLoad, 0, 0);
		driverProc(0, 1, DrvEnable, 0, 0);

		// Everything the DLL is handed lives in unmanaged memory: it keeps pointers between calls,
		// and a managed buffer that moved under it crashed the second frame.
		int* open = (int*)Marshal.AllocHGlobal(36);
		new Span<int>(open, 9).Clear();
		open[0] = 36;
		open[1] = FourCc("vidc");
		open[2] = FourCc("iv41");
		open[4] = IcModeDecompress;
		int driverId = driverProc(0, 1, DrvOpen, 0, (nint)open);
		if (driverId == 0) {
			Console.Error.WriteLine("the DLL refused to open a decompressor");
			return 2;
		}

		var input = (BitmapInfoHeader*)Marshal.AllocHGlobal(sizeof(BitmapInfoHeader));
		var output = (BitmapInfoHeader*)Marshal.AllocHGlobal(sizeof(BitmapInfoHeader));
		*input = new BitmapInfoHeader {
			Size = 40, Width = width, Height = height, Planes = 1,
			BitCount = (short)format.BitCount, Compression = format.Compression,
		};
		*output = new BitmapInfoHeader {
			Size = 40, Width = width, Height = height, Planes = 1, BitCount = 24,
			SizeImage = ((width * 3) + 3 & ~3) * height,
		};

		if (driverProc(driverId, 1, IcmDecompressQuery, (nint)input, (nint)output) != 0
			|| driverProc(driverId, 1, IcmDecompressBegin, (nint)input, (nint)output) != 0) {
			Console.Error.WriteLine("the DLL refused RGB24 output");
			return 2;
		}

		byte* packetBuffer = (byte*)Marshal.AllocHGlobal(MaxPacketBytes);
		byte* rgbBuffer = (byte*)Marshal.AllocHGlobal(output->SizeImage + 65536);
		int* decompress = (int*)Marshal.AllocHGlobal(24);
		var frame = new VideoFrame(width, height);

		int exact = 0;
		int frames = avi.VideoPackets.Count;
		for (int i = 0; i < frames; i++) {
			AviPacket packet = avi.VideoPackets[i];
			ReadOnlySpan<byte> data = avi.PacketData(packet);
			if (data.Length > MaxPacketBytes) {
				Console.Error.WriteLine($"frame {i}: packet too large");
				return 2;
			}

			data.CopyTo(new Span<byte>(packetBuffer, MaxPacketBytes));
			input->SizeImage = data.Length;
			decompress[0] = packet.IsKeyFrame ? 0 : IcDecompressNotKeyFrame;
			decompress[1] = (int)input;
			decompress[2] = (int)packetBuffer;
			decompress[3] = (int)output;
			decompress[4] = (int)rgbBuffer;
			decompress[5] = 0;
			int result = driverProc(driverId, 1, IcmDecompress, (nint)decompress, 24);

			if (!ours.DecodeFrame(data, frame)) {
				Console.WriteLine($"frame {i}: HercWorks.Video refused the packet (DLL returned {result})");
				return 1;
			}

			byte[][] planes = ours.Planes;
			if (planes[0].Length == 0) {
				// Nothing decoded yet: an empty packet before the first frame.
				exact++;
				continue;
			}

			int codec = *(int*)(driverId + 0x10);
			int state = *(int*)(codec + 0x5C);
			int decoder = *(int*)(state + 0x40);

			var mismatches = new int[3];
			for (int p = 0; p < 3; p++) {
				byte* reference = *(byte**)(decoder + s_planeOffsets[p]);
				int pitch = *(int*)(decoder + s_planeOffsets[p] + 4);
				for (int y = 0; y < planeHeights[p]; y++) {
					for (int x = 0; x < planeWidths[p]; x++) {
						if (reference[(y * pitch) + x] != planes[p][(y * planeWidths[p]) + x]) {
							mismatches[p]++;
						}
					}
				}
			}

			if (mismatches[0] + mismatches[1] + mismatches[2] == 0) {
				exact++;
			} else {
				Console.WriteLine($"frame {i}: samples differing: luma {mismatches[0]}, chroma {mismatches[1]} and {mismatches[2]}");
			}

			if (!quiet && i % 50 == 0) {
				Console.WriteLine($"frame {i} of {frames}");
			}
		}

		driverProc(driverId, 1, IcmDecompressEnd, 0, 0);

		Console.WriteLine($"{exact} of {frames} frames identical to IR41_32.DLL");
		return exact == frames ? 0 : 1;
	}

	private static int FourCc(string code) => code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24);
}
