using Silk.NET.Input;

namespace Herculan.Engine.Host;

/// <summary>
/// The mouse's two buttons and a front end's skip keys, polled once an update: what each was the last time, so
/// that a change is delivered as the press or release it is, taken without delivering it, or read as a skip.
/// Polling rather than queueing the device's own events means two changes inside one update arrive together,
/// and a press and release both inside one update are lost.
/// </summary>
sealed class PolledButtons {
	private readonly Key[] _skipKeys;
	private bool _leftHeld;
	private bool _rightHeld;
	private bool _skipKeyHeld;

	public PolledButtons(params Key[] skipKeys) => _skipKeys = skipKeys;

	/// <summary>Takes the buttons' state without delivering it, so an edge made meanwhile is spent.</summary>
	public void SpendButtons(IMouse mouse) {
		_leftHeld = mouse.IsButtonPressed(MouseButton.Left);
		_rightHeld = mouse.IsButtonPressed(MouseButton.Right);
	}

	/// <summary>As <see cref="SpendButtons"/>, and the skip keys' state too.</summary>
	public void SpendAll(IMouse mouse, IKeyboard? keyboard) {
		SpendButtons(mouse);
		_skipKeyHeld = SkipKeyDown(keyboard);
	}

	/// <summary>Whether a button or a skip key has gone down since the last update. Every state is taken either way.</summary>
	public bool SkipPressed(IMouse? mouse, IKeyboard? keyboard) {
		bool left = mouse?.IsButtonPressed(MouseButton.Left) == true;
		bool right = mouse?.IsButtonPressed(MouseButton.Right) == true;
		bool key = SkipKeyDown(keyboard);
		bool pressed = (left && !_leftHeld) || (right && !_rightHeld) || (key && !_skipKeyHeld);
		_leftHeld = left;
		_rightHeld = right;
		_skipKeyHeld = key;
		return pressed;
	}

	/// <summary>Each button that has changed state since the last update, left first, handed to <paramref name="changed"/> as the press or release it is.</summary>
	public void Deliver(IMouse mouse, Action<MouseButton, bool> changed) {
		Edge(mouse.IsButtonPressed(MouseButton.Left), ref _leftHeld, MouseButton.Left, changed);
		Edge(mouse.IsButtonPressed(MouseButton.Right), ref _rightHeld, MouseButton.Right, changed);
	}

	private static void Edge(bool held, ref bool wasHeld, MouseButton button, Action<MouseButton, bool> changed) {
		if (held == wasHeld) {
			return;
		}

		wasHeld = held;
		changed(button, held);
	}

	private bool SkipKeyDown(IKeyboard? keyboard) => keyboard != null && _skipKeys.Any(keyboard.IsKeyPressed);
}
