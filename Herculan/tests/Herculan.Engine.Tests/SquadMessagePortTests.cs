using System.Text;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Ai;
using Herculan.Engine.Terrain;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="SquadMessagePort.Withdraw"/>, and the [F7] XMIT's use of it through
/// <see cref="SquadOrders.SendToSlot"/>. The catalog is built in memory, so no install is needed.
/// </summary>
public class SquadMessagePortTests {
	private const int OtherMessage = 3;

	private static readonly object SpeakerA = new();
	private static readonly object SpeakerB = new();

	[Fact]
	public void WithdrawRemovesOnlyTheQueuedMatchForThatSpeaker() {
		var port = Port();
		port.Post(SquadOrders.StandingByMessage, 0, SpeakerA);
		port.Post(SquadOrders.StandingByMessage, 0, SpeakerB);
		port.Post(OtherMessage, 0, SpeakerA);

		Assert.True(port.Withdraw(SquadOrders.StandingByMessage, SpeakerA));
		Assert.Equal(2, port.QueueLength);
		Assert.False(port.Withdraw(SquadOrders.StandingByMessage, SpeakerA));
	}

	[Fact]
	public void WithdrawLeavesALineThatIsDueAndWaitingForItsCommBox() {
		var port = Port();
		port.Post(SquadOrders.StandingByMessage, 0, SpeakerA);
		port.Update(1);

		Assert.False(port.Withdraw(SquadOrders.StandingByMessage, SpeakerA));
		Assert.Equal(1, port.QueueLength);
	}

	[Fact]
	public void WithdrawTakesDownALineAlreadyOnScreen() {
		var port = Port();
		SquadMessagePort.Queued? begun = null;
		SquadMessagePort.Queued? ended = null;
		port.Begin += message => begun = message;
		port.End += message => ended = message;

		port.Post(SquadOrders.StandingByMessage, 0, SpeakerA);
		port.Update(1);
		port.MarkReady(begun);
		port.Update(2);
		Assert.NotNull(port.Current);

		Assert.True(port.Withdraw(SquadOrders.StandingByMessage, SpeakerA));
		port.Update(3);

		Assert.Null(port.Current);
		Assert.Same(begun, ended);
		Assert.Equal(0, port.QueueLength);
	}

	[Fact]
	public void SendToSlotWithdrawsStandingByEvenWhenNobodyTakesTheOrder() {
		var sink = new UnsayLog();
		var world = new SimWorld(FlatTerrain()) { Sounds = sink };

		bool delivered = SquadOrders.SendToSlot(world, Array.Empty<SimObject>(), 0,
			new SquadOrderMessage(SquadCommand.JoinOnMeCommand));

		Assert.False(delivered);
		Assert.Equal(new[] { (SquadOrders.StandingByMessage, (object?)null) }, sink.Withdrawn);
	}

	/// <summary>A port whose one slot speaks the two lines above, each up for one to five timing units.</summary>
	private static SquadMessagePort Port() {
		var catalog = Catalog(
			("STANDING BY...", SquadOrders.StandingByMessage),
			("TAKING FIRE!", OtherMessage));
		return new SquadMessagePort(slot => slot == 0 ? catalog : null);
	}

	private static SquadMessages Catalog(params (string Text, int Id)[] entries) {
		using var body = new MemoryStream();
		using var writer = new BinaryWriter(body);
		writer.Write((short)entries.Length);

		foreach (var (text, id) in entries) {
			// Id, variant, priority, then the four timings: up for 1 to 5 units, due at once, dropped
			// after 10.
			byte[] attributes = { (byte)id, 0, 0, 1, 5, 0, 10 };
			writer.Write((short)(text.Length + 1));
			writer.Write(Encoding.ASCII.GetBytes(text));
			writer.Write((byte)0);
			writer.Write((byte)attributes.Length);
			writer.Write(attributes);
		}

		writer.Flush();
		var content = body.ToArray();
		var bytes = new byte[4 + content.Length];
		BitConverter.GetBytes(content.Length).CopyTo(bytes, 0);
		content.CopyTo(bytes, 4);
		return SquadMessages.FromTable(SimStrings.Parse(bytes)!);
	}

	private static HeightGrid FlatTerrain() {
		const int widthShift = 4;
		const int cellCount = 1 << (widthShift * 2);
		return new HeightGrid(widthShift, widthShift, 12, 16, 10, new byte[cellCount], new byte[cellCount]);
	}

	/// <summary>Records squad withdrawals and ignores every other noise.</summary>
	private sealed class UnsayLog : ISoundSink {
		public List<(int, object?)> Withdrawn { get; } = new();

		public void SquadUnsay(int messageId, object? speaker) => Withdrawn.Add((messageId, speaker));

		public void Play(int id) { }

		public void PlayAt(int id, Vec3i position, SoundReach? reach, object? source) { }

		public void Stop(int id) { }

		public void MoveTo(int id, Vec3i position, SoundReach? reach, object? source) { }

		public void SetPitch(int id, int rate) { }

		public void Say(int messageId) { }

		public void SquadSay(int messageId, object speaker) { }

		public void CommandSay(int messageId) { }

		public void Unsay(int messageId) { }
	}
}
