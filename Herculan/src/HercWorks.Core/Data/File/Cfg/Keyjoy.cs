namespace HercWorks.Core.Data.File.Cfg;

/// <summary>
/// <c>data\keyjoy.cfg</c> — the four axis-sense switches, the only part of the retail input
/// configuration outside <c>prefs.cfg</c>. Read by <see cref="Io.Transform.Common.KeyjoyTransformer"/>.
/// See docs/retail/simulation/joystick-input.md.
///
/// <para><c>Keyjoy_LoadConfig</c> (<c>0045b78c</c>) reads it once during input init with four
/// <c>GetPrivateProfileStringA</c> calls against section <c>[Keyjoy]</c>, each a case-insensitive
/// compare against the word <c>Reverse</c> — anything else, the shipped <c>Default</c> included, leaves
/// the flag clear. The game has no writer: retail ships the file with its own explanatory comments and
/// expects the player to edit it by hand.</para>
/// </summary>
public class Keyjoy {
	/// <summary>Where the simulator keeps the file, relative to the game's <c>data</c> folder.</summary>
	public const string FileName = "keyjoy.cfg";

	/// <summary>The section all four keys live in.</summary>
	public const string Section = "Keyjoy";

	/// <summary>The value that sets a switch; every other value clears it.</summary>
	public const string ReverseWord = "Reverse";

	/// <summary>
	/// <c>Tilt</c> (<c>DAT_0049eab8</c>) — inverts the keyboard's turret-pitch axis, the second axis
	/// pair's y. The file's own comment puts it as which way keypad 8 tilts the turret. It is applied
	/// unconditionally, so it reaches a keyboard pilot whether or not a stick is attached.
	/// </summary>
	public bool ReverseTilt { get; set; }

	/// <summary>
	/// <c>Backturn</c> (<c>DAT_0049eabc</c>) — inverts the steering axis while the throttle axis is
	/// positive, which is to say while backing up. The file's comment describes it as which way
	/// keypad 3 turns while reversing. Applied last of all, to the already-combined axes.
	/// </summary>
	public bool ReverseBackturn { get; set; }

	/// <summary><c>Missile</c> (<c>DAT_0049eac0</c>) — inverts the pitch axis inside the missile camera (<c>DAT_004d25aa</c>).</summary>
	public bool ReverseMissile { get; set; }

	/// <summary>
	/// <c>Rudder</c> (<c>DAT_0049eac4</c>) — inverts the joystick's rudder axis, and only when the
	/// device reports having one. The file's comment puts it as which way the left pedal turns.
	/// </summary>
	public bool ReverseRudder { get; set; }

	/// <summary>
	/// Reads the file at <paramref name="path"/>, or every switch at its shipped setting when it is
	/// absent or unreadable — which is what the original does too, <c>GetPrivateProfileStringA</c>
	/// falling back to the default string it is handed rather than failing.
	/// </summary>
	public static Keyjoy Load(string path) {
		try {
			return new Io.Transform.Common.KeyjoyTransformer().Parse(System.IO.File.ReadAllBytes(path)) ?? new Keyjoy();
		} catch (IOException) {
			return new Keyjoy();
		} catch (UnauthorizedAccessException) {
			return new Keyjoy();
		}
	}
}
