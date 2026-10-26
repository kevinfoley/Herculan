using Herculan.Engine.Numerics;
using Herculan.Es1.EnergyShields;

namespace Herculan.Es1.Sim;

/// <summary>
/// A machine's shield block, the four <c>int16</c> at <c>+0x1b8</c> (docs/es1/simulation/shields-and-behaviour-states.md#the-shields):
/// the front and the rear charge, the balance between them and the capacity that caps the two together.
/// </summary>
public sealed class Es1Shields {
	/// <summary>The balance <c>Shield_Init</c> sets, even, and its top, all to the front.</summary>
	public const short EvenBalance = 0x200, FullBalance = 0x400;

	/// <summary>What <c>Shield_BalanceAdjust</c> moves the balance by.</summary>
	public const short BalanceStep = 0x66;

	/// <summary>The most <c>Shield_RechargeTick</c> grants a tick.</summary>
	public const short MaximumGrant = 4;

	/// <summary>The step by which <c>Shield_RechargeTick</c> moves the front toward its share.</summary>
	public const short FrontSlew = 0x41;

	/// <summary>
	/// <c>Shield_Init</c> (<c>ES1DBSIM 0000fabc</c>)(block, <paramref name="capacity"/>), from <c>Mech_Constructor</c>'s
	/// step 6 with the type record's <c>+0xc0</c>: the capacity, the balance <see cref="EvenBalance"/>, and the front and
	/// the rear each half the capacity.
	/// </summary>
	public Es1Shields(short capacity) {
		Capacity = capacity;
		Balance = EvenBalance;
		Rear = (short)(capacity >> 1);
		Front = (short)(capacity >> 1);
	}

	/// <summary><c>+0</c>, the front charge.</summary>
	public short Front { get; private set; }

	/// <summary><c>+2</c>, the rear charge.</summary>
	public short Rear { get; private set; }

	/// <summary><c>+4</c>, the balance, Q10 over 0 to <see cref="FullBalance"/>: the front's share of the charge.</summary>
	public short Balance { get; private set; }

	/// <summary><c>+6</c>, the capacity, which caps front + rear; nothing found writes it after <c>Shield_Init</c> (docs/es1/simulation/mech.md#open).</summary>
	public short Capacity { get; }

	/// <summary>
	/// <c>Shield_BalanceAdjust</c> (<c>0000fae4</c>)(block, direction): the balance moved <see cref="BalanceStep"/> toward
	/// the front when <paramref name="towardFront"/>, toward the rear otherwise, held to [0, <see cref="FullBalance"/>].
	/// </summary>
	public void BalanceAdjust(bool towardFront) =>
		Balance = (short)Math.Clamp(Balance + (towardFront ? BalanceStep : -BalanceStep), 0, FullBalance);

	/// <summary>
	/// <c>Shield_RechargeTick</c> (<c>0000fb2c</c>)(block, <paramref name="request"/>), the shields' share of the systems
	/// tick's pool: the grant is the request held to at most <see cref="MaximumGrant"/> and to the capacity less the
	/// charge, so a request below 0, a pool under its reserve, takes charge back out of the shields; the front moves by
	/// <see cref="FrontSlew"/> toward its share, the charge plus the grant Q10 times the balance, and the rear holds the
	/// rest. Answers the request less the grant.
	/// </summary>
	public short RechargeTick(short request) {
		short deficit = (short)(Capacity - (Front + Rear));
		short granted = request > MaximumGrant - 1 ? MaximumGrant : request;
		if (deficit < granted) {
			granted = deficit;
		}

		short total = (short)(Front + Rear + granted);
		short front = Front;
		SimMath.RateLimitedMoveToward(ref front, (short)SimMath.Q10MultiplyRounded(total, Balance), FrontSlew);
		Front = front;
		Rear = (short)(total - front);
		return (short)(request - granted);
	}

	/// <summary>
	/// <c>Shield_BalanceInputRead</c> (<c>0000fbb4</c>)(block), from <c>Player_PerFrameCockpitUpdate</c>, on
	/// <paramref name="display"/>, the shield display at <c>CockpitViewInstance</c>'s <c>+0x1e5</c>: a copy of its state
	/// block; a front press moves the balance toward the front (<see cref="BalanceAdjust"/>) and is cleared, or else a rear
	/// press toward the rear; then the readings (front ≪ 10) / capacity and (rear ≪ 10) / capacity and the balance go
	/// into the copy, which is handed back (<see cref="Es1ShieldDisplay.SetStateBlock"/>). A capacity of 0, which retail's
	/// divide would fault on, reads 0, an engine guard.
	/// </summary>
	public void BalanceInputRead(Es1ShieldDisplay display) {
		var block = display.GetStateBlock();
		if (block.FrontPressed) {
			BalanceAdjust(true);
			block.FrontPressed = false;
		} else if (block.RearPressed) {
			BalanceAdjust(false);
			block.RearPressed = false;
		}

		block.FrontReading = Capacity == 0 ? 0 : (Front << 10) / Capacity;
		block.RearReading = Capacity == 0 ? 0 : (Rear << 10) / Capacity;
		block.Balance = Balance;
		display.SetStateBlock(block);
	}
}
