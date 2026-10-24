using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// ES1's camera object, the <c>0x4a</c>-byte <c>TSCamera</c> the camera module <c>000003ac</c>–<c>00000ac0</c> places
/// (docs/es1/simulation/external-views.md#the-camera): a free camera (mode 0), an orbit around an object (mode 1) and a
/// camera riding an object's eye (mode 2), with no chase mode and no lock. <c>ViewObjectPtr</c> (<c>000b5b8a</c>), the
/// squad cameras and the spare are each one of these (<see cref="Es1ViewChain"/>).
///
/// <para>Not kept: <c>+0x12</c>–<c>+0x21</c>, the <c>TSContext</c> part and the constructor's arguments
/// (external-views.md's Open), which every camera of the slice holds alike: the projection is the main camera's
/// near plane and focal shift (<see cref="NearPlane"/>, <see cref="FocalPixels"/>).</para>
/// </summary>
public sealed class Es1Camera {
	/// <summary>The modes <c>+0x36</c> holds.</summary>
	public const short Free = 0, Orbit = 1, Attached = 2;

	/// <summary>The height <c>Cam_Update</c> keeps the free camera and the orbit's eye at or above: ES1's ground is flat at 0.</summary>
	public const int Floor = 100;

	/// <summary>The orbit's distance limits, <c>0x9c4</c> and 30000.</summary>
	public const short MinDistance = 0x9c4, MaxDistance = 30000;

	/// <summary>The orbit's and the free camera's pitch limit, ±<c>0x3000</c>.</summary>
	public const short PitchLimit = 0x3000;

	/// <summary>The main camera's near plane, <c>0x100</c>, its focal shift 8 (docs/es1/rendering/view-transform-and-projection.md#differences-from-es2).</summary>
	public const int NearPlane = 0x100;

	/// <summary><c>2^shift</c>, the focal length in the 320 × 200 screen's pixels.</summary>
	public const int FocalPixels = 1 << 8;

	private short _speed, _pitchRate, _rollRate, _headingRate;
	private short _orbitPitch, _orbitRoll, _orbitHeading;

	/// <summary><c>+0x00</c>, the position.</summary>
	public Vec3i Position { get; set; }

	/// <summary><c>+0x0c</c>, the view's euler triple: pitch, roll, heading.</summary>
	public (short X, short Y, short Z) View { get; set; }

	/// <summary><c>+0x22</c>, the orbit's distance.</summary>
	public short Distance { get; set; }

	/// <summary><c>+0x24</c>: the zoom rate in the orbit, the speed in the free camera.</summary>
	public short Speed { get => _speed; set => _speed = value; }

	/// <summary><c>+0x26</c>, <c>+0x28</c>, <c>+0x2a</c>: the orbit's pitch, roll and heading, the heading relative to the object's.</summary>
	public (short Pitch, short Roll, short Heading) OrbitAngles {
		get => (_orbitPitch, _orbitRoll, _orbitHeading);
		set => (_orbitPitch, _orbitRoll, _orbitHeading) = value;
	}

	/// <summary><c>+0x2c</c>, <c>+0x2e</c>, <c>+0x30</c>: their rates, added once a <see cref="Update"/>.</summary>
	public (short Pitch, short Roll, short Heading) Rates {
		get => (_pitchRate, _rollRate, _headingRate);
		set => (_pitchRate, _rollRate, _headingRate) = value;
	}

	/// <summary><c>+0x32</c>, the object viewed; null in a camera nothing has attached.</summary>
	public Es1Mech? Object { get; private set; }

	/// <summary><c>+0x36</c>, the mode: <see cref="Free"/>, <see cref="Orbit"/> or <see cref="Attached"/>.</summary>
	public short Mode { get; set; }

	/// <summary><c>+0x38</c>, the orbit's centre in the object's frame.</summary>
	public (short X, short Y, short Z) OrbitCentre { get; private set; }

	/// <summary><c>+0x3e</c>, the attached eye in the object's camera node's frame.</summary>
	public (short X, short Y, short Z) Eye { get; private set; }

	/// <summary><c>+0x44</c>–<c>+0x48</c>, the rotation added to the attached view; zeroed when a camera is built.</summary>
	public (short X, short Y, short Z) AddedRotation { get; set; }

	/// <summary>
	/// The object a draw leaves out because the camera rides in it: <c>Camera_IsMountedOn</c> (<c>000003ac</c>) answers 1
	/// for the object in mode 2 alone.
	/// </summary>
	public Es1Mech? MountedObject => Mode == Attached ? Object : null;

	/// <summary><c>Camera_IsMountedOn</c> (<c>000003ac</c>): in mode 2 on <paramref name="target"/>.</summary>
	public bool IsMountedOn(Es1Mech target) => Mode == Attached && ReferenceEquals(Object, target);

	/// <summary>
	/// <c>Cam_AttachTo</c> (<c>0005923c</c>), which acts on <c>ViewObjectPtr</c>; its callers make this camera that first.
	/// Slot <c>+0x34</c> of the object, <c>Mech_GetAimPoint</c> (<c>00010ee0</c>), gives the eye (0, record <c>+0x64</c>,
	/// <c>+0x66</c>) and the orbit's centre (0, <c>+0x68</c>, <c>+0x6a</c>) (docs/es1/simulation/mech.md#the-cockpit),
	/// passed to <see cref="AttachEye"/> and then <see cref="AttachOrbit"/>, so an attach ends in mode 1.
	/// </summary>
	public void AttachTo(Es1Mech target) {
		var chassis = target.Type.Chassis;
		AttachEye((0, chassis.EyeOffsetY, chassis.EyeOffsetZ), target);
		AttachOrbit((0, chassis.OrbitCentreY, chassis.OrbitCentreZ), target);
	}

	/// <summary><c>Cam_AttachEye</c> (<c>00000454</c>): the eye and the object stored, mode 2.</summary>
	public void AttachEye((short X, short Y, short Z) eye, Es1Mech target) {
		Eye = eye;
		Object = target;
		Mode = Attached;
	}

	/// <summary>
	/// <c>Cam_AttachOrbit</c> (<c>000003d0</c>): the centre and the object stored, the distance, the three orbit angles,
	/// the zoom rate and the three rates zeroed, mode 1; so an outside view starts behind the object at the least
	/// distance, level and still.
	/// </summary>
	public void AttachOrbit((short X, short Y, short Z) centre, Es1Mech target) {
		OrbitCentre = centre;
		Object = target;
		OrbitAngles = (0, 0, 0);
		Distance = 0;
		Rates = (0, 0, 0);
		Speed = 0;
		Mode = Orbit;
	}

	/// <summary><c>Cam_SetMode</c> (<c>00000480</c>): modes 0, 1 and 2; any other value is ignored.</summary>
	public void SetMode(short mode) {
		if (mode is Free or Orbit or Attached) {
			Mode = mode;
		}
	}

	/// <summary>
	/// <c>TSCamera_CopyFrom</c> (<c>00057af8</c>)(this, <paramref name="from"/>): every field this type keeps, the
	/// position and view through <c>00057bb4</c> and <c>00057be0</c>, then <c>+0x22</c> to <c>+0x49</c> one by one.
	/// </summary>
	public void CopyFrom(Es1Camera from) {
		Position = from.Position;
		View = from.View;
		Distance = from.Distance;
		Speed = from.Speed;
		OrbitAngles = from.OrbitAngles;
		Rates = from.Rates;
		Object = from.Object;
		Mode = from.Mode;
		OrbitCentre = from.OrbitCentre;
		Eye = from.Eye;
		AddedRotation = from.AddedRotation;
	}

	/// <summary>
	/// <c>Cam_Update</c> (<c>000004b8</c>), once a frame on <c>ViewObjectPtr</c> (docs/es1/simulation/external-views.md#cam_update).
	/// Mode 0 turns by its rates and moves along its view by its speed, kept at or above <see cref="Floor"/>; mode 1
	/// orbits the object's centre, and an eye that would sit below the floor either leaves the camera where it began the
	/// call (zoom rate below 1) or tilts the orbit to put it on the floor; mode 2 rides the object's camera node.
	/// </summary>
	public void Update() {
		switch (Mode) {
			case Free:
				UpdateFree();
				break;
			case Orbit:
				UpdateOrbit();
				break;
			case Attached:
				UpdateAttached();
				break;
		}
	}

	/// <summary>
	/// <c>Cam_Steer</c> (<c>00000a04</c>)(camera, <paramref name="trigger"/>, <paramref name="throttle"/>,
	/// <paramref name="steer"/>), ES2's table for modes 0 and 1 with no lock test
	/// (docs/retail/simulation/external-views.md#steering-the-camera--cam_steer): the heading rate follows the steering, and
	/// the throttle the zoom (or speed) with the trigger held and the pitch rate without it. Nothing in mode 2.
	/// </summary>
	public void Steer(byte trigger, short throttle, short steer) {
		if (Mode == Free) {
			ApproachRate((short)-steer, ref _headingRate);
			ApproachZoom(trigger != 0 ? (short)-throttle : (short)0, ref _speed);
			ApproachRate(trigger != 0 ? (short)0 : throttle, ref _pitchRate);
		} else if (Mode == Orbit) {
			ApproachRate(steer, ref _headingRate);
			ApproachZoom(trigger != 0 ? throttle : (short)0, ref _speed);
			ApproachRate(trigger != 0 ? (short)0 : throttle, ref _pitchRate);
		}
	}

	/// <summary><c>Cam_ApproachRate</c> (<c>000009b8</c>): the rate toward <c>Q8(0x800, axis)</c> by at most <c>0xc0</c>.</summary>
	public static void ApproachRate(short axis, ref short rate) =>
		SimMath.RateLimitedMoveToward(ref rate, (short)SimMath.Q8Multiply(0x800, axis), 0xc0);

	/// <summary><c>Cam_ApproachZoom</c> (<c>000009e0</c>): the rate toward <c>Q8(0x400, axis)</c> by at most <c>0x50</c>.</summary>
	public static void ApproachZoom(short axis, ref short rate) =>
		SimMath.RateLimitedMoveToward(ref rate, (short)SimMath.Q8Multiply(0x400, axis), 0x50);

	/// <summary>
	/// Puts the camera's position and view on <paramref name="camera"/> with the projection the view window gives: the
	/// focal length <see cref="FocalPixels"/> in the screen's pixels over the window's rows, its axis on the window's
	/// projection centre.
	/// </summary>
	public void ApplyTo(Camera camera, Es1ViewWindow window) {
		camera.Position = Position;
		camera.Pitch = View.X;
		camera.Roll = View.Y;
		camera.Yaw = -View.Z;
		ApplyProjection(camera, window);
	}

	/// <summary>The window's projection on <paramref name="camera"/>, for a viewport the window's own size in the screen's pixels.</summary>
	public static void ApplyProjection(Camera camera, Es1ViewWindow window) {
		var (width, height) = Size(window);
		ApplyProjection(camera, width, height, (window.CentreX - window.X0, window.CentreY - window.Y0), NearPlane,
			FocalPixels);
	}

	/// <summary>
	/// <paramref name="view"/>'s camera put on <paramref name="camera"/>, as <see cref="ApplyTo(Camera, Es1ViewWindow)"/>
	/// puts this one's, with the view's own projection for a rect of <paramref name="width"/> × <paramref name="height"/>
	/// screen pixels.
	/// </summary>
	public static void ApplyTo(Camera camera, Es1RectView view, int width, int height) {
		camera.Position = view.Position;
		camera.Pitch = view.Angles.Pitch;
		camera.Roll = view.Angles.Roll;
		camera.Yaw = -view.Angles.Heading;
		ApplyProjection(camera, Math.Max(width, 1), Math.Max(height, 1), view.Centre, view.NearPlane, 1 << view.FocalShift);
	}

	/// <summary>
	/// A projection of <paramref name="focalPixels"/> in the screen's pixels on <paramref name="camera"/>, for a viewport
	/// of <paramref name="width"/> × <paramref name="height"/> of them with its axis at <paramref name="centre"/> from
	/// the top-left, and the near plane <paramref name="nearPlane"/> in world units.
	/// </summary>
	private static void ApplyProjection(Camera camera, int width, int height, (int X, int Y) centre, int nearPlane,
			int focalPixels) {
		camera.FieldOfView = 2f * MathF.Atan(height / 2f / focalPixels);
		camera.PrincipalPoint = new System.Numerics.Vector2(centre.X / (float)width, centre.Y / (float)height);
		camera.NearPlane = WorldScale.DistanceToRender(nearPlane);
	}

	/// <summary>The window's size in screen pixels, its right column and bottom row included and held to the screen.</summary>
	public static (int Width, int Height) Size(Es1ViewWindow window) => (
		Math.Max(Math.Min(window.X1, Es1CockpitViews.ScreenWidth - 1) - window.X0 + 1, 1),
		Math.Max(Math.Min(window.Y1, Es1CockpitViews.ScreenHeight - 1) - window.Y0 + 1, 1));

	/// <summary>Mode 0: the rates added, the pitch held to ±<see cref="PitchLimit"/> with its rate zeroed, then the move.</summary>
	private void UpdateFree() {
		var (pitch, roll, heading) = View;
		heading += _headingRate;
		pitch += _pitchRate;
		if (pitch > PitchLimit || pitch < -PitchLimit) {
			pitch = pitch > PitchLimit ? PitchLimit : (short)-PitchLimit;
			_pitchRate = 0;
		}

		roll += _rollRate;
		View = (pitch, roll, heading);
		var moved = Transform3.FromEuler(pitch, roll, heading).RotateVector(0, _speed, 0) + Position;
		Position = new Vec3i(moved.X, moved.Y, Math.Max(moved.Z, Floor));
	}

	/// <summary>
	/// Mode 1: the distance stepped and held to its limits, the orbit's angles stepped, the pitch held; the view the
	/// orbit's angles with the object's heading added; the eye the centre through the object's frame plus the view's
	/// rotation of (0, −distance, 0). Below the floor, a zoom rate under 1 puts back the position, the view and the orbit's
	/// angles the call began with, and otherwise the pitch becomes <c>Math_AsinBam</c>((centre's height − 100) ·
	/// <c>0x4000</c> / distance), in the view and the orbit, and the eye is placed again.
	/// </summary>
	private void UpdateOrbit() {
		var target = Object!;
		var startPosition = Position;
		var startView = View;
		var startOrbit = OrbitAngles;

		int distance = Distance + _speed;
		Distance = (short)Math.Clamp(distance, MinDistance, MaxDistance);
		_orbitHeading += _headingRate;
		_orbitPitch += _pitchRate;
		_orbitRoll += _rollRate;
		if (_orbitPitch > PitchLimit || _orbitPitch < -PitchLimit) {
			_orbitPitch = _orbitPitch > PitchLimit ? PitchLimit : (short)-PitchLimit;
			_pitchRate = 0;
		}

		View = (_orbitPitch, _orbitRoll, (short)(_orbitHeading + target.Rotation.Z));
		var centre = target.Frame.TransformPoint(OrbitCentre.X, OrbitCentre.Y, OrbitCentre.Z);
		Position = centre + Behind(View);
		if (Position.Z >= Floor) {
			return;
		}

		if (_speed < 1) {
			Position = startPosition;
			View = startView;
			OrbitAngles = startOrbit;
			return;
		}

		int sine = (centre.Z - Floor) * 0x4000 / Distance;
		_orbitPitch = (short)SimTrig.Asin((short)sine);
		View = (_orbitPitch, View.Y, View.Z);
		Position = centre + Behind(View);
	}

	/// <summary>The view's rotation of (0, −<see cref="Distance"/>, 0), the eye's offset from the orbit's centre.</summary>
	private Vec3i Behind((short X, short Y, short Z) view) =>
		Transform3.FromEuler(view.X, view.Y, view.Z).RotateVector(0, -Distance, 0);

	/// <summary>
	/// Mode 2: the eye through the node the object's slot <c>+0x28</c> gives, <c>Mech_GetAimNodeTransform</c>
	/// (<c>00013204</c>), the chassis' camera part's, composed with the object's frame, and the view from that matrix
	/// through <c>Transform_RotationToEuler</c> plus <see cref="AddedRotation"/>; with no such part, the object's frame and
	/// its own angles.
	/// </summary>
	private void UpdateAttached() {
		var target = Object!;
		var frame = target.Frame;
		int transformId = target.Type.Animation?.TransformIdOfPart(target.Type.Chassis.CameraPartId) ?? -1;
		if (transformId < 0 || target.Shape == null) {
			Position = frame.TransformPoint(Eye.X, Eye.Y, Eye.Z);
			View = target.Rotation;
			return;
		}

		var node = Transform3.Concat(target.Shape.NodeTransform(transformId), frame);
		var (x, y, z) = node.ToEuler();
		Position = node.TransformPoint(Eye.X, Eye.Y, Eye.Z);
		View = ((short)(x + AddedRotation.X), (short)(y + AddedRotation.Y), (short)(z + AddedRotation.Z));
	}
}
