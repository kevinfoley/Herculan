using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace Herculan.Engine.Audio;

/// <summary>
/// Keeps an <see cref="OpenAlBackend"/>'s device on the system's default output: brings it back when
/// its output goes, and moves it when the default changes. This engine's own; retail's
/// sound driver is not the model, and what retail does when its output goes has not been looked into.
///
/// <para><b>Moving is <c>alcReopenDeviceSOFT</c></b> (<c>ALC_SOFT_reopen_device</c>) with no name,
/// which reopens the same device on the default output. The context, every source and every buffer
/// survive it, so sample ids and play handles stay valid.</para>
///
/// <para><b>What triggers a move is an event, never a timer, where the runtime has one.</b> Every
/// extension is looked up by name when the device opens, and a missing one only takes away what it
/// provides:</para>
/// <list type="bullet">
/// <item><c>AL_SOFT_events</c>' disconnect event reports the output going. Without it a drop is
/// never noticed.</item>
/// <item><c>AL_SOFTX_hold_on_disconnect</c> keeps sources playing through a drop, so they resume
/// where they were once the device is back. Without it OpenAL stops every source as its device
/// disconnects, and <see cref="OpenAlBackend.Update"/> restarts the loops from their top and the
/// streams from the next block they are fed; a one-shot under way is lost. It is an in-progress
/// extension (the <c>SOFTX</c>), which a later runtime may rename or drop.</item>
/// <item><c>ALC_SOFT_system_events</c> (OpenAL Soft 1.24 on) reports the default output changing,
/// which moves the device to it, and an output being added, which retries a disconnected device at
/// once. Without it a device plugged in while another output is playing is not followed.</item>
/// </list>
///
/// <para>While disconnected the device is also retried every <see cref="ReopenInterval"/>, for an
/// output that comes back without an event saying so.</para>
///
/// <para>The events arrive on OpenAL's own threads, where no AL call is allowed, so the callbacks
/// only raise flags, and <see cref="Update"/> acts on them on the thread that owns the device.</para>
/// </summary>
internal sealed unsafe class OpenAlDeviceWatch : IDisposable {
	/// <summary>
	/// How often <see cref="Update"/> retries a disconnected device. A try still goes through the
	/// system's device setup on the frame that makes it, so it is not every frame. This engine's
	/// number.
	/// </summary>
	public static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(1);

	// ALC_CONNECTED, ALC_EXT_disconnect's query; Silk.NET's GetContextInteger has no member for it.
	private const GetContextInteger AlcConnected = (GetContextInteger)0x313;

	// ALC_SOFT_system_events is process-wide: one callback for the whole library, however many devices
	// are open. The callback fans each event out to every live watch through this snapshot, which is
	// replaced whole on the device-owning thread and read without a lock on the event thread.
	private static volatile OpenAlDeviceWatch[] s_systemWatchers = Array.Empty<OpenAlDeviceWatch>();
	private static delegate* unmanaged[Cdecl]<
		delegate* unmanaged[Cdecl]<int, int, Device*, int, byte*, void*, void>, void*, void> s_setSystemCallback;
	private static int s_defaultChangedEvent;
	private static int s_deviceAddedEvent;
	private static int s_playbackDevice;

	private readonly ALContext _alc;
	private readonly Device* _device;
	private readonly delegate* unmanaged[Cdecl]<Device*, byte*, int*, byte> _reopenDevice;

	// What OnContextEvent's user parameter names, so the per-context callback can find this watch.
	private GCHandle _self;
	private int _disconnectEvent;

	private volatile bool _disconnectSignalled;
	private volatile bool _defaultChangedSignalled;
	private volatile bool _deviceAddedSignalled;
	private long _nextReopen;

	/// <summary>
	/// Subscribes to whatever the runtime offers. <paramref name="al"/>'s context must be current, as
	/// it is while <see cref="OpenAlBackend.TryCreate"/> builds the backend.
	/// </summary>
	public OpenAlDeviceWatch(AL al, ALContext alc, Device* device) {
		_alc = alc;
		_device = device;

		if (!alc.IsExtensionPresent(device, "ALC_SOFT_reopen_device")) {
			// Nothing below could be acted on.
			return;
		}

		_reopenDevice = (delegate* unmanaged[Cdecl]<Device*, byte*, int*, byte>)
			alc.GetProcAddress(device, "alcReopenDeviceSOFT");
		HoldsSources = HoldSourcesOnDisconnect(al);
		WatchForDisconnect(al);
		WatchSystem();
	}

	/// <summary>
	/// Whether the device has an output. False from a reported disconnect until a reopen succeeds;
	/// sound started meanwhile goes nowhere.
	/// </summary>
	public bool Connected { get; private set; } = true;

	/// <summary>Whether sources hold their place through a disconnect (<c>AL_SOFTX_hold_on_disconnect</c>).</summary>
	public bool HoldsSources { get; }

	/// <summary>
	/// Acts on what the events have reported since the last call. Call once a frame.
	/// </summary>
	/// <returns>
	/// True when the device was reopened this call, after a disconnect or onto a new default; the
	/// caller then puts back whatever the runtime stopped.
	/// </returns>
	public bool Update() {
		if (_reopenDevice == null) {
			return false;
		}

		if (Connected) {
			if (_disconnectSignalled) {
				_disconnectSignalled = false;

				// The event thread runs behind the device, so an event raised before the last reopen
				// can arrive after it; the device's own flag says whether this one is stale.
				int connected = 1;
				_alc.GetContextProperty(_device, AlcConnected, 1, &connected);
				if (connected == 0) {
					Connected = false;
					_nextReopen = 0;
					Console.Error.WriteLine("Audio output lost; waiting for a device to reopen on.");
				}
			}

			if (Connected) {
				if (!_defaultChangedSignalled) {
					return false;
				}

				_defaultChangedSignalled = false;

				// A failed move leaves the device where it was or, if that output is gone too,
				// disconnected, which the disconnect event then reports.
				if (!Reopen()) {
					return false;
				}

				Console.Error.WriteLine("Audio output moved to the new default device.");
				return true;
			}
		}

		if (_deviceAddedSignalled || _defaultChangedSignalled) {
			_deviceAddedSignalled = false;
			_defaultChangedSignalled = false;
			_nextReopen = 0;
		}

		long now = Environment.TickCount64;
		if (now < _nextReopen) {
			return false;
		}

		_nextReopen = now + (long)ReopenInterval.TotalMilliseconds;
		if (!Reopen()) {
			return false;
		}

		Connected = true;
		Console.Error.WriteLine("Audio output reopened.");
		return true;
	}

	// A null name is the default output, which is where TryCreate opened it.
	private bool Reopen() => _reopenDevice(_device, null, null) != 0;

	/// <summary>Turns off <c>AL_STOP_SOURCES_ON_DISCONNECT_SOFT</c> on the current context.</summary>
	private static bool HoldSourcesOnDisconnect(AL al) {
		if (!al.IsExtensionPresent("AL_SOFTX_hold_on_disconnect")) {
			return false;
		}

		int capability = al.GetEnumValue("AL_STOP_SOURCES_ON_DISCONNECT_SOFT");
		if (capability == 0) {
			return false;
		}

		al.Disable((Capability)capability);
		return !al.IsEnabled((Capability)capability);
	}

	/// <summary>
	/// Subscribes the current context to <c>AL_SOFT_events</c>' disconnect event, and to nothing
	/// else. The event type's value is asked of the runtime by name rather than written in here.
	/// </summary>
	private void WatchForDisconnect(AL al) {
		if (!al.IsExtensionPresent("AL_SOFT_events")) {
			return;
		}

		int disconnected = al.GetEnumValue("AL_EVENT_TYPE_DISCONNECTED_SOFT");
		var control = (delegate* unmanaged[Cdecl]<int, int*, byte, void>)(void*)
			al.GetProcAddress("alEventControlSOFT");
		var setCallback = (delegate* unmanaged[Cdecl]<
				delegate* unmanaged[Cdecl]<int, uint, uint, int, byte*, void*, void>, void*, void>)(void*)
			al.GetProcAddress("alEventCallbackSOFT");
		if (disconnected == 0 || control == null || setCallback == null) {
			return;
		}

		_disconnectEvent = disconnected;
		_self = GCHandle.Alloc(this);
		setCallback(&OnContextEvent, (void*)GCHandle.ToIntPtr(_self));
		control(1, &disconnected, 1);
	}

	/// <summary><c>AL_SOFT_events</c>' callback, on OpenAL's event thread for this context.</summary>
	[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
	private static void OnContextEvent(int type, uint item, uint param, int length, byte* message, void* user) {
		if (GCHandle.FromIntPtr((nint)user).Target is OpenAlDeviceWatch watch && type == watch._disconnectEvent) {
			watch._disconnectSignalled = true;
		}
	}

	/// <summary>
	/// Joins the process-wide <c>ALC_SOFT_system_events</c> subscription, setting it up first if this
	/// is the only device open. Each event type is enabled on its own, because a backend that cannot
	/// report one of them refuses only that one.
	/// </summary>
	private void WatchSystem() {
		const string extension = "ALC_SOFT_system_events";
		if (!_alc.IsExtensionPresent(null, extension) && !_alc.IsExtensionPresent(_device, extension)) {
			return;
		}

		if (s_setSystemCallback == null) {
			int changed = _alc.GetEnumValue(_device, "ALC_EVENT_TYPE_DEFAULT_DEVICE_CHANGED_SOFT");
			int added = _alc.GetEnumValue(_device, "ALC_EVENT_TYPE_DEVICE_ADDED_SOFT");
			int playback = _alc.GetEnumValue(_device, "ALC_PLAYBACK_DEVICE_SOFT");
			var control = (delegate* unmanaged[Cdecl]<int, int*, byte, byte>)(void*)
				_alc.GetProcAddress(_device, "alcEventControlSOFT");
			var setCallback = (delegate* unmanaged[Cdecl]<
					delegate* unmanaged[Cdecl]<int, int, Device*, int, byte*, void*, void>, void*, void>)(void*)
				_alc.GetProcAddress(_device, "alcEventCallbackSOFT");
			if (changed == 0 || added == 0 || playback == 0 || control == null || setCallback == null) {
				return;
			}

			s_defaultChangedEvent = changed;
			s_deviceAddedEvent = added;
			s_playbackDevice = playback;
			setCallback(&OnSystemEvent, null);

			bool any = control(1, &changed, 1) != 0;
			any |= control(1, &added, 1) != 0;
			if (!any) {
				setCallback(null, null);
				return;
			}

			s_setSystemCallback = setCallback;
		}

		s_systemWatchers = [.. s_systemWatchers, this];
	}

	/// <summary><c>ALC_SOFT_system_events</c>' callback, on OpenAL's event thread.</summary>
	[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
	private static void OnSystemEvent(int type, int deviceType, Device* device, int length, byte* message, void* user) {
		if (deviceType != s_playbackDevice) {
			return;
		}

		foreach (var watch in s_systemWatchers) {
			if (type == s_defaultChangedEvent) {
				watch._defaultChangedSignalled = true;
			} else if (type == s_deviceAddedEvent) {
				watch._deviceAddedSignalled = true;
			}
		}
	}

	/// <summary>
	/// Leaves the subscriptions. Call after the context is destroyed, which stops its event thread,
	/// and before the library is released: the last watch out takes the process-wide callback down
	/// with it, so a library loaded afresh later is subscribed afresh.
	/// </summary>
	public void Dispose() {
		if (Array.IndexOf(s_systemWatchers, this) >= 0) {
			s_systemWatchers = s_systemWatchers.Where(watch => watch != this).ToArray();
			if (s_systemWatchers.Length == 0 && s_setSystemCallback != null) {
				s_setSystemCallback(null, null);
				s_setSystemCallback = null;
			}
		}

		if (_self.IsAllocated) {
			_self.Free();
		}
	}
}
