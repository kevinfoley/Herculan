using System.Security.Cryptography;
using System.Text;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.World;
using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// A scripted half-minute in the cockpit, pinned to a digest of everything the frame's order can change, taken
/// every frame (<see cref="SimulatorRig.Snapshot"/>). It presses most of the cockpit's keys, clicks its buttons,
/// opens and answers each modal panel, works the stick and the developer keys, and ends on [Ctrl+Q]'s QUIT.
///
/// <para>The digest is a characterization, not a statement of retail behaviour: it holds the cockpit's
/// behaviour still across a refactor that is not meant to change it. A change that is meant to change what
/// the cockpit does changes the digest too; set <c>HERCULAN_RIG_LOG</c> to a file path to have the run's
/// per-frame snapshots written there, compare them against the same file from before the change, and
/// record the new digest only once every difference is one the change intends.</para>
/// </summary>
[Collection(SimTimestepCollection.Name)]
public class CockpitSessionTests {
	private const string Digest = "B97742EA30CF34466D9F162A4FDE4334496115E9F50847772F9A76ACFCA1C16A";

	[Fact]
	public void TheScriptedSessionMatchesItsDigest() {
		// Pointed at the nearest Cybrid, which starts just outside radar range, so the target keys have
		// something to select once the machine has walked a little way.
		if (SimulatorRig.Load(developer: true, stage: FaceTheNearestCybrid) is not { } rig) {
			return;
		}

		var log = new StringBuilder();
		void Run(int frames) {
			for (int i = 0; i < frames; i++) {
				rig.Frame();
				log.AppendLine(rig.Snapshot());
			}
		}

		void Hold(int frames, params Key[] keys) {
			rig.Keys.Hold(keys);
			Run(frames);
			rig.Keys.Release(keys);
		}

		void Tap(params Key[] keys) {
			Hold(1, keys);
			Run(1);
		}

		void Click(Func<CockpitWidgetId, bool> match, CockpitMouseButtons button = CockpitMouseButtons.Left) {
			log.AppendLine($"click {rig.Click(match, button)}");
			log.AppendLine(rig.Snapshot());
		}

		// The power-up, then the throttle open and a turn.
		Run(40);
		Hold(60, Key.Up);
		Hold(30, Key.Right);

		// Radar, target selection and the shield balance.
		Tap(Key.R);
		Run(30);
		for (int i = 0; i < 3; i++) {
			Tap(Key.Enter);
			Run(5);
		}

		Tap(Key.Apostrophe);
		Tap(Key.LeftBracket);
		Tap(Key.RightBracket);
		Tap(Key.RightBracket);

		// The weapon panel's keys, and the trigger held.
		Tap(Key.Number1);
		Tap(Key.Number2);
		Tap(Key.AltLeft, Key.Number2);
		Tap(Key.W);
		Tap(Key.AltLeft, Key.W);
		Tap(Key.L);
		Tap(Key.GraveAccent);
		Tap(Key.Equal);
		Tap(Key.Minus);
		Hold(40, Key.Space);

		// ATT on and off, the turret keys and the two centring keys.
		Tap(Key.T);
		Run(50);
		Tap(Key.T);
		Hold(10, Key.I);
		Hold(10, Key.J);
		Tap(Key.Backspace);
		Tap(Key.BackSlash);
		Run(20);

		// The MFD's six screens and its button keys.
		foreach (var key in new[] { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6 }) {
			Tap(key);
			Run(5);
		}

		Tap(Key.AltLeft, Key.R);
		Tap(Key.AltLeft, Key.T);
		Tap(Key.F1);
		Tap(Key.D);

		// FLASH COMM's letters, its cursor, XMIT, an [Alt] order and a nav marker.
		Tap(Key.F2);
		Tap(Key.A);
		Tap(Key.G);
		Tap(Key.Period);
		Tap(Key.Comma);
		Tap(Key.X);
		Tap(Key.AltLeft, Key.H);
		Tap(Key.AltLeft, Key.D);
		Run(40);

		// The command display: a pilot, an order, a unit, a pick and XMIT, the map's zoom and scroll.
		Tap(Key.F7);
		Run(40);
		Tap(Key.Number1);
		Tap(Key.A);
		Tap(Key.Tab);
		Tap(Key.Enter);
		Tap(Key.X);
		Tap(Key.Equal);
		Tap(Key.Minus);
		Hold(10, Key.Up);
		Hold(10, Key.Left);
		Tap(Key.Keypad5);
		Tap(Key.Backspace);
		Tap(Key.Comma);
		Tap(Key.Period);
		Run(20);

		// The damage detail, its categories and its subjects; then back up.
		Tap(Key.F8);
		Tap(Key.S);
		Tap(Key.I);
		Tap(Key.W);
		Tap(Key.Up);
		Tap(Key.Right);
		Tap(Key.Left);
		Tap(Key.F1);
		Run(40);

		// The two glances.
		Tap(Key.F9);
		Run(30);
		Tap(Key.F10);
		Run(30);

		// Each modal panel up and down: pause, objectives, preferences with a setting stepped, and [Q].
		Tap(Key.P);
		Run(30);
		Tap(Key.Escape);
		Run(10);
		Tap(Key.F11);
		Run(20);
		Tap(Key.Enter);
		Run(10);
		Tap(Key.F12);
		Run(20);
		Tap(Key.Tab);
		Tap(Key.Tab);
		Tap(Key.Enter);
		Run(5);
		Tap(Key.Escape);
		Run(10);
		Tap(Key.Q);
		Run(20);
		Tap(Key.Escape);
		Run(10);

		// The external view: the controls on the camera, the next squadmate, and back.
		Tap(Key.V);
		Run(30);
		Tap(Key.Enter);
		Run(10);
		Tap(Key.N);
		Run(10);
		Tap(Key.Enter);
		Tap(Key.V);
		Run(20);

		// The developer keys: the freeze, a single step, a move and a turn, and a hit.
		Tap(Key.AltLeft, Key.S);
		Run(10);
		Tap(Key.AltLeft, Key.KeypadAdd);
		Run(10);
		Tap(Key.AltLeft, Key.S);
		Tap(Key.ControlLeft, Key.AltLeft, Key.Number5);
		Tap(Key.AltLeft, Key.Up);
		Tap(Key.ControlLeft, Key.Left);
		Tap(Key.ControlLeft, Key.AltLeft, Key.D);
		Run(10);

		// The mouse: an MFD screen, the console's three buttons, a weapon row, a shield facing, the
		// system buttons.
		Click(id => id.Kind == CockpitWidgetKind.MfdButton && id.Index == 3);
		Click(id => id.Kind == CockpitWidgetKind.ConsoleButton && id.AsConsoleButton == ConsoleButton.Link);
		Click(id => id.Kind == CockpitWidgetKind.ConsoleButton && id.AsConsoleButton == ConsoleButton.Chain);
		Click(id => id.Kind == CockpitWidgetKind.ConsoleButton && id.AsConsoleButton == ConsoleButton.Track);
		Click(id => id.Kind == CockpitWidgetKind.WeaponRow && id.Index == 0);
		Click(id => id.Kind == CockpitWidgetKind.ShieldFacing);
		Click(id => id.Kind == CockpitWidgetKind.MfdButton && id.Index == 0, CockpitMouseButtons.Right);
		rig.ClickSystemButton(SystemButton.Manual);
		rig.ClickSystemButton(SystemButton.FullScreen);
		log.AppendLine(rig.Snapshot());
		Run(10);

		// The stick: the axes, each of its eight buttons in turn, and the hat's four ways.
		rig.Stick.Capabilities = new JoystickCapabilities(Present: true, ButtonCount: JoystickCapabilities.MaxButtons,
			HasThrottle: true, HasRudder: true, HasHat: true);
		rig.Stick.Reading = new JoystickReading(StickY: -MechControls.AxisFull);
		Run(30);
		rig.Stick.Reading = new JoystickReading(StickX: MechControls.AxisFull, Throttle: MechControls.AxisFull / 2);
		Run(10);
		for (int button = 0; button < JoystickCapabilities.MaxButtons; button++) {
			rig.Stick.Reading = new JoystickReading(Buttons: (byte)(1 << button));
			Run(2);
			rig.Stick.Reading = JoystickReading.Neutral;
			Run(4);
		}

		foreach (var hat in new[] { JoystickHat.North, JoystickHat.South, JoystickHat.West, JoystickHat.East, JoystickHat.North }) {
			rig.Stick.Reading = new JoystickReading(Hat: hat);
			Run(3);
			rig.Stick.Reading = JoystickReading.Neutral;
			Run(20);
		}

		Run(60);

		// Target selection again, the Cybrid now in range: cycle, nearest, the Targeting Pod's component
		// step, the TARGET STATUS screen's own [Enter], clear, and nearest again with the trigger held.
		Tap(Key.Enter);
		Run(5);
		Tap(Key.Apostrophe);
		Run(5);
		Tap(Key.Tab);
		Tap(Key.F5);
		Tap(Key.Enter);
		Run(20);
		Tap(Key.Semicolon);
		Tap(Key.Apostrophe);
		Hold(30, Key.Space);
		Run(30);

		// [Ctrl+Q], focus on QUIT and press it: the mission ends, and the game with it.
		Tap(Key.ControlLeft, Key.Q);
		Run(10);
		Tap(Key.Tab);
		Tap(Key.Enter);
		Run(10);

		string text = log.ToString();
		if (Environment.GetEnvironmentVariable("HERCULAN_RIG_LOG") is { Length: > 0 } logPath) {
			File.WriteAllText(logPath, text);
		}

		Assert.True(rig.MissionOver && rig.QuitGame, "the scripted QUIT did not end the game");
		Assert.Equal(Digest, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
	}

	private static void FaceTheNearestCybrid(MissionScene scene, StagedStart staged) {
		if (scene.PlayerMech is not { } player) {
			return;
		}

		var nearest = scene.World.Objects
			.Where(o => o.Side == MissionSide.Cybrid && !o.AwaitingDeployment)
			.OrderBy(o => player.Position.ApproxDistanceTo(o.Position))
			.FirstOrDefault();
		if (nearest != null) {
			staged.Heading = (ushort)Detection.HeadingToward(nearest.Position, player.Position);
		}
	}
}
