namespace HercWorks.Core.Data.Struct.Dbsim;

/// <summary>
/// One 14-byte record of ROCKETS.DAT, the launcher round's type, indexed by the firing
/// <c>PROJ.DAT</c> record's subtype id. Shares its stride and first two fields with
/// <see cref="BulletType"/> and nothing else: the rocket's readers take the rest at offsets of
/// their own. See docs/retail/formats/rockets-dat.md.
/// </summary>
public class RocketType {
	/// <summary><c>+0x00</c> — which root of <c>ROCKETS.DTS</c> the round is drawn as.</summary>
	public short ModelId { get; set; }

	/// <summary>
	/// <c>+0x02</c> — how long the round flies, in <b>ticks</b>: a plain counter, not the bullet's
	/// <c>0x200</c> age units. It burns out with no impact.
	/// </summary>
	public short Lifetime { get; set; }

	/// <summary><c>+0x04</c> — the acceleration, per 125 ms.</summary>
	public short Acceleration { get; set; }

	/// <summary><c>+0x06</c> — the shot record's hit-test slack, which a bullet keeps at <c>+0x04</c>.</summary>
	public short ClipRadius { get; set; }

	/// <summary><c>+0x08</c> — the animation frame interval; zero means a static shape.</summary>
	public short FrameInterval { get; set; }

	/// <summary><c>+0x0a</c> — which of the shape's sequences that interval steps.</summary>
	public short AnimSequence { get; set; }

	/// <summary><c>+0x0c</c> — the fire sound id, played as <c>id + 10</c>.</summary>
	public short FireSoundId { get; set; }
}
