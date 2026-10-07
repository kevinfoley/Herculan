using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// Where a shell movie plays, in canvas pixels: the rect <c>Avi_Play</c> (<c>0041e01c</c>) hands
/// <c>MoveWindow</c> for the movie's window, as its left, top, width and height. The movie is drawn
/// stretched to fill it, which the rects' sizes indicate and retail has not been checked for
/// (docs/retail/shell/screen-layout.md#open).
/// </summary>
public readonly record struct ShellMovieRect(int X, int Y, int Width, int Height);

/// <summary>
/// One entry of the ring: the movie's id, where it plays, the palette installed before it (null for
/// none), and whether the campaign map's location picture follows it. The callback the original's
/// entry also carries is left out: every caller passes none.
/// </summary>
public sealed record ShellMovieEntry(int Id, ShellMovieRect Rect, int? Palette, bool ShowsLocation);

/// <summary>
/// VSHELL's movie queue, <c>avi.cpp</c>'s ten-entry ring at <c>00485668</c> — which movie each id is,
/// the rects its callers pass, and <c>Movie_Enqueue</c> (<c>0041e29c</c>). <see cref="ShellMovieRun"/>
/// plays it out. See docs/retail/shell/screen-layout.md#the-shells-movies.
/// </summary>
public sealed class ShellMovieQueue {
	/// <summary>How many entries the ring holds.</summary>
	public const int Capacity = 10;

	/// <summary>The intro's two parts, which the startup queues.</summary>
	public const int IntroPart1 = 0x44;
	public const int IntroPart2 = 0x45;

	/// <summary>The ending, queued with <see cref="Credits"/> when the campaign is won.</summary>
	public const int Victory = 0x53;

	/// <summary>The credits, which the main menu's CREDITS plays.</summary>
	public const int Credits = 0x54;

	/// <summary>The lunar drop, which stands in for the location picture at stage 5.</summary>
	public const int Dropship = 0x55;

	/// <summary>The campaign map's first movie is <c>stage + 0x45</c>, <c>c1</c> to <c>c5</c>.</summary>
	public const int StageMovieBase = 0x45;

	/// <summary>The campaign map's second movie is <c>stage + 0x4a</c>, the theater's thumbnail.</summary>
	public const int StageThumbnailBase = 0x4a;

	/// <summary>The rect every full-window movie is queued with, centred on the canvas.</summary>
	public static readonly ShellMovieRect FullRect = new(0x20, 0x3c, 0x240, 0x168);

	/// <summary>The mission screen's Telecomm picture: the map view's first movie and the briefing's.</summary>
	public static readonly ShellMovieRect TelecommRect = new(0x15, 0x56, 0xef, 0xb3);

	/// <summary>The mission screen's map panel: the map view's second movie.</summary>
	public static readonly ShellMovieRect MapPanelRect = new(0x122, 0x43, 0x127, 0xe2);

	/// <summary>The disc folder the table's paths name.</summary>
	public const string MovieFolder = "AVI";

	/// <summary>
	/// The table at <c>00470e74</c>: 86 <c>avi\</c> paths indexed by movie id, with the folder left off.
	/// An id is used as an index unchecked.
	/// </summary>
	public static readonly string[] FileNames = {
		"pt1.avi", "pt2.avi", "pt3.avi", "pt4.avi", "pt5.avi", "pt6.avi",
		"rc1.avi", "rc2.avi", "rc3.avi", "rc4.avi", "rc5.avi",
		"as1.avi", "as2.avi", "as3.avi", "as4.avi", "as5.avi", "as6.avi", "as7.avi",
		"es1.avi", "es2.avi", "es3.avi", "es4.avi",
		"rs1.avi", "rs2.avi", "rs3.avi", "rs4.avi",
		"sc1.avi", "sc2.avi", "sc3.avi", "sc4.avi", "sc5.avi",
		"rd1.avi", "rd2.avi", "rd3.avi", "rd4.avi",
		"sp1.avi",
		"gd1.avi", "gd2.avi", "gd3.avi", "gd4.avi",
		"ex1.avi", "ex2.avi", "ex3.avi", "ex4.avi",
		"rf1.avi", "rf2.avi", "rf3.avi", "rf4.avi",
		"co1.avi", "co2.avi", "co3.avi", "co4.avi",
		"fl1.avi", "fl2.avi", "fl3.avi", "fl4.avi",
		"sk1.avi", "sk2.avi", "sk3.avi", "sk4.avi",
		"sv1.avi", "sv2.avi", "sv3.avi", "sv4.avi",
		"hc1.avi", "hc2.avi", "hc3.avi", "hc4.avi",
		"intr_pt1.avi", "intr_pt2.avi",
		"c1.avi", "c2.avi", "c3.avi", "c4.avi", "c5.avi",
		"alph_th.avi", "delt_th.avi", "omic_th.avi", "brav_th.avi", "luna.avi",
		"transm3.avi", "end1a.avi", "death.avi", "victory.avi", "credits.avi", "dropship.avi",
	};

	private readonly ShellMovieEntry?[] _ring = new ShellMovieEntry?[Capacity];
	private int _writeIndex;
	private int _readIndex;

	/// <param name="enabled">Whether movies are on, <c>Shell_MoviesEnabled</c> (<c>00482275</c>), which <c>-a</c> clears.</param>
	/// <param name="introLanguage">The language whose folder the intro is read from; see <see cref="IntroLanguage"/>.</param>
	public ShellMovieQueue(bool enabled = true, GameLanguage introLanguage = GameLanguage.English) {
		Enabled = enabled;
		IntroLanguage = introLanguage;
	}

	/// <summary><c>Shell_MoviesEnabled</c> (<c>00482275</c>): while it is clear nothing is queued and nothing plays.</summary>
	public bool Enabled { get; }

	/// <summary>
	/// The language whose folder the intro's two parts are read from: the shell's language in v1.10, whose
	/// <c>Movie_PlayQueue</c> rewrites their <c>avi\</c> to <c>avf\</c> or <c>avg\</c>, and English in v1.0
	/// (docs/retail/retail-builds.md#v110s-shell-reads-the-language-twice-more).
	/// </summary>
	public GameLanguage IntroLanguage { get; }

	/// <summary>
	/// <c>MovieQueue_Running</c> (<c>00470e70</c>): set while <see cref="ShellMovieRun"/> plays the
	/// ring out. <c>WinButton_HandleEvent</c> and <c>ESButtonBitmap_HandleEvent</c> ignore mouse events
	/// while it is set.
	/// </summary>
	public bool Running { get; set; }

	/// <summary>The entry at the read index, or null when the ring is empty there.</summary>
	public ShellMovieEntry? Next => _ring[_readIndex];

	/// <summary>The movie file an id names, or null for an id past the table.</summary>
	public static string? FileName(int id) => id >= 0 && id < FileNames.Length ? FileNames[id] : null;

	/// <summary>The disc folder <paramref name="language"/>'s intro is read from.</summary>
	public static string IntroFolder(GameLanguage language) => language switch {
		GameLanguage.French => "AVF",
		GameLanguage.German => "AVG",
		_ => MovieFolder,
	};

	/// <summary>The movie an id names as a path under the disc, or null for an id past the table.</summary>
	public string? MoviePath(int id) => FileName(id) is { } name
		? Path.Combine(id is IntroPart1 or IntroPart2 ? IntroFolder(IntroLanguage) : MovieFolder, name)
		: null;

	/// <summary>
	/// <c>Movie_Enqueue</c> (<c>0041e29c</c>): writes an entry at the write index and moves it on,
	/// modulo the ring, unless movies are off or an entry anywhere in the ring already carries the id.
	/// Nothing stops the write landing on an entry not yet played.
	/// </summary>
	public void Enqueue(int id, ShellMovieRect rect, int? palette = null, bool showsLocation = false) {
		if (!Enabled || _ring.Any(entry => entry?.Id == id)) {
			return;
		}

		_ring[_writeIndex] = new ShellMovieEntry(id, rect, palette, showsLocation);
		_writeIndex = (_writeIndex + 1) % Capacity;
	}

	/// <summary>Frees the entry at the read index and moves the read index on — the end of one entry's turn.</summary>
	public void Remove() {
		_ring[_readIndex] = null;
		_readIndex = (_readIndex + 1) % Capacity;
	}
}
