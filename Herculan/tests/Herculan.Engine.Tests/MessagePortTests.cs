using System.Text;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="MessagePort"/>'s show against the view: a line that goes up in the external view is spoken
/// but neither drawn nor toned. The catalog is built in memory, so no install is needed.
/// </summary>
public class MessagePortTests {
	private const int Message = 0;

	[Fact]
	public void ALineShownInTheCockpitIsDrawnTonedAndSpoken() {
		var (port, spoken, tones) = Port();

		ShowOneLine(port);

		Assert.True(port.Ticker.HasText);
		Assert.Equal(new[] { Message }, spoken);
		Assert.Single(tones);
	}

	[Fact]
	public void ALineShownInTheExternalViewIsSpokenWithoutTextOrTone() {
		var (port, spoken, tones) = Port();
		port.ExternalView = true;

		ShowOneLine(port);

		Assert.False(port.Ticker.HasText);
		Assert.Equal(new[] { Message }, spoken);
		Assert.Empty(tones);
	}

	[Fact]
	public void ALineShownInTheExternalViewStaysHiddenBackInTheCockpit() {
		var (port, _, _) = Port();
		port.ExternalView = true;
		ShowOneLine(port);

		port.ExternalView = false;
		port.Update(3);

		Assert.Equal(1, port.QueueLength);
		Assert.False(port.Ticker.HasText);
	}

	// Posted at tick 0, due on the first update after it and shown on the next.
	private static void ShowOneLine(MessagePort port) {
		port.Post(Message);
		port.Update(1);
		port.Update(2);
	}

	private static (MessagePort Port, List<int> Spoken, List<int> Tones) Port() {
		var port = new MessagePort(Catalog("INTERNAL DAMAGE"));
		var spoken = new List<int>();
		var tones = new List<int>();
		port.Speak += spoken.Add;
		port.AlertTone += tones.Add;
		return (port, spoken, tones);
	}

	/// <summary>One group of lines, each with the retail file's timings: up for 3 to 6 units, due at once, dropped after 20.</summary>
	private static SystemMessages Catalog(params string[] lines) {
		using var body = new MemoryStream();
		using var writer = new BinaryWriter(body);
		writer.Write((short)lines.Length);

		for (int id = 0; id < lines.Length; id++) {
			byte[] attributes = { (byte)id, 0, 0, 3, 6, 0, 0x14, (byte)(id + 1) };
			writer.Write((short)(lines[id].Length + 1));
			writer.Write(Encoding.ASCII.GetBytes(lines[id]));
			writer.Write((byte)0);
			writer.Write((byte)attributes.Length);
			writer.Write(attributes);
		}

		writer.Flush();
		var content = body.ToArray();
		var bytes = new byte[4 + content.Length];
		BitConverter.GetBytes(content.Length).CopyTo(bytes, 0);
		content.CopyTo(bytes, 4);
		return SystemMessages.FromTable(SimStrings.Parse(bytes)!);
	}
}
