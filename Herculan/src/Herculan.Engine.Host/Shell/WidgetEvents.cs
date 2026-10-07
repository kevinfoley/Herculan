using Herculan.Engine.Shell;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Shell;

/// <summary>
/// The widgets' click handlers and the pointer events that reach them. Each component that puts widgets up
/// registers their handlers here by kind, as VSHELL's builders pass each widget its handler.
/// </summary>
sealed class WidgetEvents {
	private readonly ShellPointer _pointer;
	private readonly Dictionary<ShellWidgetKind, Action<ShellWidget>> _handlers = new();

	public WidgetEvents(ShellPointer pointer) => _pointer = pointer;

	/// <summary>
	/// Which button the event being delivered is, for the handlers that tell them apart: an armory
	/// row's thunk calls one function on the left release and another on the right.
	/// </summary>
	public ShellMouseButton EventButton { get; private set; } = ShellMouseButton.Left;

	/// <summary>Registers the click handler for every widget of a kind.</summary>
	public void Handle(ShellWidgetKind kind, Action<ShellWidget> handler) => _handlers.Add(kind, handler);

	/// <summary>
	/// Runs a widget's click handler — Window_DispatchCallback (0041f5d4) calling what the builder
	/// passed the widget.
	/// </summary>
	public void Fire(ShellWidget widget) {
		if (_handlers.TryGetValue(widget.Kind, out var handler)) {
			handler(widget);
		}
	}

	/// <summary>A button changing state since the last update, delivered as the press or release it is.</summary>
	public void Deliver(MouseButton button, bool held) {
		var shellButton = button == MouseButton.Left ? ShellMouseButton.Left : ShellMouseButton.Right;
		EventButton = shellButton;
		if (held) {
			_pointer.Press(shellButton, Fire);
		} else {
			_pointer.Release(shellButton, Fire);
		}
	}

	/// <summary>The pointer taken onto an edit field, with a left press posted at it, so that keys reach it at once.</summary>
	public void Grab(ShellHit field) => _pointer.Grab(field, Fire);
}
