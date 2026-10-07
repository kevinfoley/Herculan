namespace HercWorks.Core.Data.Struct.Dbsim;

/// <summary>
/// One 14-byte record of BULLETS.DAT, the travelling gun round's type, indexed by the firing
/// <c>PROJ.DAT</c> record's subtype id. Shares its stride and first two fields with
/// <see cref="RocketType"/> and nothing else. See docs/retail/simulation/projectiles.md.
/// </summary>
public class BulletType {
	/// <summary><c>+0x00</c> — which root of <c>BULLETS.DTS</c> the round is drawn as.</summary>
	public short ModelId { get; set; }

	/// <summary>
	/// <c>+0x02</c> — how long the round lives, in 125 ms units: it is dropped with no impact once its
	/// age passes <c>Lifetime * 0x200</c>.
	/// </summary>
	public short Lifetime { get; set; }

	/// <summary><c>+0x04</c> — the slack the shot record allows the hit test, in place of a beam's literal 200.</summary>
	public short ClipRadius { get; set; }

	/// <summary>
	/// <c>+0x06</c> — the animation frame interval: the countdown reloaded each time it expires to step
	/// the drawn shape's frame. Zero means a static shape; only the three EMP records set it, all to 256.
	/// </summary>
	public short FrameInterval { get; set; }

	/// <summary><c>+0x08</c> — the fire sound id, played as <c>id + 10</c>.</summary>
	public short FireSoundId { get; set; }

	/// <summary>
	/// <c>+0x0a</c> — the firing scatter in binary-angle units: the spawn displaces two of the round's
	/// three euler angles by <c>(value * 2 &amp; random) - value</c>. 63 on every autocannon, zero on
	/// the EMP and plasma records.
	/// </summary>
	public short Scatter { get; set; }

	/// <summary>
	/// <c>+0x0c</c> — nonzero arms a per-lifetime rate at the object's <c>+0x61</c> whose reader is
	/// open (docs/retail/simulation/projectiles.md#open); 1 on records 0–2 (ATC20/35/50) and zero on the other
	/// nine, <c>ATC75</c>'s and <c>ATC100</c>'s own records 10 and 11 included.
	/// </summary>
	public short LifetimeRateFlag { get; set; }
}
