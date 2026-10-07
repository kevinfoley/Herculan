namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// <c>data\prefs.cfg</c> — the simulator's option array, read by
/// <see cref="Io.Transform.Common.PrefsTransformer"/>. See docs/retail/simulation/preferences.md.
///
/// <para><b>The file is the array.</b> <c>Prefs_LoadOptions</c> (<c>00459754</c>) memsets
/// <c>SimOptions</c> (<c>004d1fbc</c>) to zero for <c>0x36</c> bytes and reads the file straight over it, with no
/// parse at all, so an option's index is its byte offset and a retail <c>prefs.cfg</c> is 54 bytes.
/// Only the options the two [F12] panels and VSHELL's preferences screen put on screen are named
/// here; the remaining bytes are carried, not interpreted.</para>
/// </summary>
public class Prefs {
	/// <summary>Where the simulator keeps the file, relative to the game's <c>data</c> folder.</summary>
	public const string FileName = "prefs.cfg";

	/// <summary>How many bytes the simulator reads, and so how long a usable file is.</summary>
	public const int Length = 0x36;

	/// <summary>MUSIC — CD audio on or off.</summary>
	public const int MusicOption = 0;

	/// <summary>SOUNDS — the effect mixer on or off.</summary>
	public const int SoundsOption = 1;

	/// <summary>PILOT MESSAGE, <c>PilotMessageMode</c> (<c>004d1fbe</c>) — the pilot and squad channel's two halves.</summary>
	public const int PilotMessageOption = 2;

	/// <summary>COMPUTER MESSAGE, <c>ComputerMessageMode</c> (<c>004d1fbf</c>) — the computer ticker's two halves.</summary>
	public const int ComputerMessageOption = 3;

	/// <summary>
	/// VSHELL's <c>Game Resolution</c>, and the byte <c>VideoMode_Configure</c> reads: 0 for 640x480,
	/// 1 for 320x240.
	/// </summary>
	public const int VideoModeOption = 4;

	/// <summary>
	/// <c>Display Mode</c>: 0 windowed, 1 full screen. Both programs go full screen from it at startup, and
	/// both write it back when the player has changed the state.
	/// </summary>
	public const int DisplayModeOption = 6;

	/// <summary>TERRAIN DISTANCE, <c>DAT_004d1fc3</c> — the terrain draw radius.</summary>
	public const int TerrainDistanceOption = 7;

	/// <summary>
	/// TERRAIN TEXTURE, <c>DAT_004d1fc4</c>, which the original tests per triangle to pick textured or
	/// flat span writers (docs/retail/formats/terrain-texturing.md, "The terrain-texture switch").
	/// </summary>
	public const int TerrainTextureOption = 8;

	/// <summary>HERC DETAIL, <c>DAT_004d1fc5</c>.</summary>
	public const int HercDetailOption = 9;

	/// <summary>STRUCTURE DETAIL, <c>DAT_004d1fc6</c>.</summary>
	public const int StructureDetailOption = 10;

	/// <summary>EFFECTS DETAIL, <c>Sound_DetailSetting</c> (<c>004d1fc7</c>).</summary>
	public const int EffectsDetailOption = 11;

	/// <summary>
	/// Where the [F12] → CONTROLS panel's twelve options start for a walking HERC — <c>ControlsOptionBase</c> (<c>004d25fb</c>)
	/// as <c>Sim_InitMissionSession</c> (<c>004614fc</c>) sets it, and the value
	/// <c>Main_StaticInit</c> (<c>0045cad8</c>) starts it on.
	/// </summary>
	public const int HercControlsBase = 0x0d;

	/// <summary>
	/// And where they start for the RAZOR, which has a second twelve-byte block of its own. The two
	/// sets are independent: rebinding in one machine does not disturb the other's.
	/// </summary>
	public const int RazorControlsBase = 0x19;

	/// <summary>How many options a controls block holds: the four axis assignments, then one action code per joystick button.</summary>
	public const int ControlsOptionCount = 12;

	/// <summary>How many of those are the axis rows, which come first.</summary>
	public const int ControlsAxisCount = 4;

	/// <summary>Which block the machine being flown reads — the whole of what <c>PilotingRazor</c> (<c>004d25f5</c>) selects.</summary>
	public static int ControlsBase(bool razor) => razor ? RazorControlsBase : HercControlsBase;

	/// <summary>
	/// The file's bytes: at least <see cref="Length"/>, and a longer file's tail is kept. All zero for a
	/// fresh array, which is what the simulator's memset leaves where no file was read.
	/// </summary>
	public byte[] Options { get; set; } = new byte[Length];

	/// <summary>Option <paramref name="index"/>'s byte, or 0 when it is outside the array.</summary>
	public byte this[int index] => index >= 0 && index < Options.Length ? Options[index] : (byte)0;
}
