using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A squad order record, what <see cref="Es1MissionGroup.BroadcastOrder"/> sends and a HERC's slot <c>+0x2c</c> takes
/// (docs/es1/simulation/mission-groups.md#the-squad-order-broadcast, the layout ES2's,
/// docs/retail/simulation/ai-squadmates.md#the-order-record--22-bytes).
/// </summary>
public sealed class Es1SquadOrder {
	/// <summary>The verbs the broadcast and the receiver tell apart: radar on and radar off, the FLASH COMM orders 4 and 7.</summary>
	public const short RadarOnVerb = 4, RadarOffVerb = 7;

	/// <summary>The verbs the receiver takes as <see cref="RadarOnVerb"/> and <see cref="RadarOffVerb"/>.</summary>
	public const short RadarOnVerbAlias = 0x10, RadarOffVerbAlias = 0x11;

	/// <summary><c>+0</c>, the verb: FLASH COMM's stores its row's order as a word.</summary>
	public short Verb { get; set; }

	/// <summary><c>+2</c>, the issuer, which the cockpit's sender stores and the broadcast passes over.</summary>
	public IEs1SimObject? Issuer { get; set; }

	/// <summary>
	/// <c>+6</c>, the point, which verbs <c>0xc</c> to <c>0xe</c> read: the Heads-Down Display's command screen's saved
	/// gridpoint pick (<see cref="Hdd.Es1HddCommandScreen.FillOrderRecord"/>).
	/// </summary>
	public Vec3i Point { get; set; }

	/// <summary>
	/// <c>+0x12</c>, the subject, which verb <c>0xb</c> reads: the command screen's saved unit pick
	/// (<see cref="Hdd.Es1HddCommandScreen.FillOrderRecord"/>).
	/// </summary>
	public IEs1SimObject? Subject { get; set; }
}
