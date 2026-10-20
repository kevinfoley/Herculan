using Herculan.Es1.Input;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The chain of views and the cameras it moves between (docs/es1/simulation/main-loop.md#the-views--viewchain_apply-0005894c,
/// ES2's docs/retail/simulation/external-views.md#the-chain-of-views): the globals <c>ViewChain_View</c>,
/// <c>ViewChain_PendingView</c>, <c>ViewChain_Viewed</c>, <c>ViewChain_Chosen</c>,
/// <c>ViewChain_CameraControlPreferred</c> and <c>ViewChain_CaptionFrames</c>; the current camera
/// <c>ViewObjectPtr</c> (<c>ES1DBSIM 000b5b8a</c>), the squad cameras (<c>000bda9e</c>) and the spare
/// (<c>ViewChain_SpareCamera</c>, <c>000b5d38</c>); and <c>ViewChain_Apply</c>, <c>ViewChain_ViewObject</c>,
/// <c>ViewChain_ToggleCameraControl</c> and <c>ViewChain_DrawCaption</c>. <c>InputDrivesCamera</c> is the controls'
/// (<see cref="Es1ControlsState.InputDrivesCamera"/>), which the input build also reads.
///
/// <para>ES1's chain has views 1 (the free camera, camera mode 0), 2 (the cockpit, mode 2 on the player) and 3 (the
/// outside view, mode 1 on <see cref="Chosen"/>); no chase view, no lock.</para>
/// </summary>
public sealed class Es1ViewChain {
	/// <summary>The views <c>ViewChain_View</c> holds.</summary>
	public const int FreeView = 1, CockpitView = 2, OutsideView = 3;

	/// <summary>The frames the caption is drawn for after a change that sets it.</summary>
	public const short CaptionFrameCount = 2;

	private readonly Es1Camera[] _squadCameras;
	private Es1Camera? _spare;

	/// <summary>
	/// <c>Sim_StartMission</c>'s steps 9, 10 and 12 (docs/es1/simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc):
	/// <see cref="Viewed"/> and <see cref="Chosen"/> the player's machine; one camera per member of its group (the count
	/// <c>+0x12</c>), each with no object, mode 0 and no added rotation; and the main camera attached to the player and
	/// put in mode 2. <c>Main_StaticInit</c> leaves both views at 2 and the preference at 1.
	/// </summary>
	/// <param name="views">The view manager, <c>CockpitViewManagerInstance</c>; null with no cockpit loaded, when no key queues a view command.</param>
	/// <param name="caption">The caption's font and strings; null draws no caption.</param>
	public Es1ViewChain(Es1Mech localPlayer, Es1ViewManager? views, Es1ViewCaption? caption = null) {
		LocalPlayer = localPlayer;
		Manager = views;
		Caption = caption;
		Viewed = localPlayer;
		Chosen = localPlayer;
		int count = localPlayer.Group?.Members.Count ?? 0;
		_squadCameras = Enumerable.Range(0, count).Select(_ => new Es1Camera()).ToArray();

		// TSCamera_Ctor's arguments land in the fields this port does not keep (Es1Camera); Cam_AttachTo acts on
		// ViewObjectPtr, which this camera already is.
		Camera = new Es1Camera();
		Camera.AttachTo(localPlayer);
		Camera.Mode = Es1Camera.Attached;
	}

	/// <summary><c>LocalPlayerMech</c> (<c>000b5b86</c>).</summary>
	public Es1Mech LocalPlayer { get; }

	/// <summary><c>CockpitViewManagerInstance</c> (<c>000b5b60</c>), whose step flag <see cref="Apply"/> runs on.</summary>
	public Es1ViewManager? Manager { get; }

	/// <summary>The caption's font and strings, or null.</summary>
	public Es1ViewCaption? Caption { get; }

	/// <summary><c>ViewObjectPtr</c> (<c>000b5b8a</c>), the camera the frame places and draws from.</summary>
	public Es1Camera Camera { get; private set; }

	/// <summary>The squad cameras, one per member of the player's group.</summary>
	public IReadOnlyList<Es1Camera> SquadCameras => _squadCameras;

	/// <summary><c>ViewChain_View</c> (<c>000b5b8e</c>).</summary>
	public int View { get; set; } = CockpitView;

	/// <summary><c>ViewChain_PendingView</c> (<c>000b5bba</c>), the view a queued change goes to.</summary>
	public int PendingView { get; set; } = CockpitView;

	/// <summary><c>ViewChain_Viewed</c> (<c>000bda9a</c>), the object the controls and the views follow.</summary>
	public Es1Mech Viewed { get; set; }

	/// <summary><c>ViewChain_Chosen</c> (<c>000b5bbc</c>), the object the outside view looks at.</summary>
	public Es1Mech Chosen { get; set; }

	/// <summary><c>ViewChain_CameraControlPreferred</c> (<c>000b5bb8</c>): the outside view on the player gives the camera the controls.</summary>
	public bool CameraControlPreferred { get; set; } = true;

	/// <summary><c>ViewChain_CaptionFrames</c> (<c>000b5bc0</c>), the ticks the caption is still drawn on.</summary>
	public short CaptionFrames { get; set; }

	/// <summary>
	/// <c>ViewChain_Apply</c> (<c>0005894c</c>), at the tick's step 5. While the manager has a change staged and the
	/// pending view differs, it becomes the view: 1 puts the camera in mode 0 and gives it the controls as preferred; 2
	/// views the player in mode 2 with the controls on the machine; 3 views <see cref="Chosen"/> in mode 1 and sets the
	/// caption's frames. With nothing staged, the outside view on the player takes the preference into
	/// <c>InputDrivesCamera</c>. Then the caption is drawn while its frames count down.
	/// </summary>
	public void Apply(Es1ControlsState controls) {
		if (Manager?.Staged != true) {
			if (View == OutsideView && ReferenceEquals(Viewed, LocalPlayer)
					&& controls.InputDrivesCamera != CameraControlPreferred) {
				controls.InputDrivesCamera = CameraControlPreferred;
			}
		} else if (PendingView != View) {
			View = PendingView;
			switch (PendingView) {
				case FreeView:
					Camera.Mode = Es1Camera.Free;
					controls.InputDrivesCamera = CameraControlPreferred;
					break;
				case CockpitView:
					ViewObject(LocalPlayer, controls);
					Camera.Mode = Es1Camera.Attached;
					controls.InputDrivesCamera = false;
					break;
				case OutsideView:
					ViewObject(Chosen, controls);
					Camera.Mode = Es1Camera.Orbit;
					CaptionFrames = CaptionFrameCount;
					break;
			}
		}

		if (CaptionFrames != 0) {
			DrawCaption(controls);
			CaptionFrames--;
		}
	}

	/// <summary>
	/// <c>ViewChain_ViewObject</c> (<c>00058a44</c>): the first squad camera already on <paramref name="target"/> becomes the
	/// camera, in mode 1; or the first with no object is copied from the camera, becomes it and is attached to the
	/// target; with neither, the spare, built on the first call, the same way. Then <paramref name="target"/> is
	/// <see cref="Viewed"/>, and the camera has the controls for any object but the player, and as preferred for the
	/// player.
	/// </summary>
	public void ViewObject(Es1Mech target, Es1ControlsState controls) {
		bool placed = false;
		foreach (var camera in _squadCameras) {
			if (ReferenceEquals(camera.Object, target)) {
				Camera = camera;
				camera.Mode = Es1Camera.Orbit;
				placed = true;
				break;
			}

			if (camera.Object == null) {
				camera.CopyFrom(Camera);
				Camera = camera;
				camera.AttachTo(target);
				placed = true;
				break;
			}
		}

		if (!placed) {
			_spare ??= new Es1Camera();
			_spare.CopyFrom(Camera);
			Camera = _spare;
			_spare.AttachTo(target);
		}

		Viewed = target;
		controls.InputDrivesCamera = !ReferenceEquals(target, LocalPlayer) || CameraControlPreferred;
	}

	/// <summary>
	/// <c>ViewChain_ToggleCameraControl</c> (<c>00059dbc</c>): flips <see cref="CameraControlPreferred"/>, and sets the
	/// caption's frames when the camera's object is the player.
	/// </summary>
	public void ToggleCameraControl() {
		CameraControlPreferred = !CameraControlPreferred;
		if (ReferenceEquals(Camera.Object, LocalPlayer)) {
			CaptionFrames = CaptionFrameCount;
		}
	}

	/// <summary>
	/// <c>ViewChain_DrawCaption</c> (<c>00058b68</c>) on the draw page below the external view's rect: the name is
	/// <see cref="Es1ViewCaption.YouText"/> for the player. <see cref="Apply"/> calls it while the caption's frames count
	/// down, and <c>PreferencesPanel_Raise</c> on its return to view 4.
	/// </summary>
	internal void DrawCaption(Es1ControlsState controls) {
		if (Manager == null || Caption == null) {
			return;
		}

		// PLACEHOLDER: a squadmate's name, Squad_PilotName (0002e2f4) of its Squad_IndexOf slot, is the pilot roster's
		// (the instrument +0x1f5, HddGauge_Name), which plan step 6c ports; until then it is drawn empty, as retail draws
		// the name of an object outside the squad (the empty string 00096d19).
		string name = ReferenceEquals(Camera.Object, LocalPlayer) ? Caption.YouText : string.Empty;
		Caption.Draw(Manager.Display, Manager.ExternalViewRect.ViewportY1, name, controls.InputDrivesCamera);
	}
}
