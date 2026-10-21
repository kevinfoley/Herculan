#version 330 core

// The scene program: every piece of opaque world geometry SceneRenderer draws — terrain, each posed
// segment of a machine, structures, projectile models.
//
// One program for all of it because the surface type is a property of the VERTEX, not of the object.
// The original rasterized each poly class through its own routine (TSSolidPoly, TSShadedPoly,
// TSGouraudPoly, the textured pair), and a single DTS shape mixes them freely — a HERC leg carries
// textured, ramp-shaded and flat polys in one mesh. The builders therefore encode which class a
// corner belongs to in its own attributes (aTextured, aUnlit, aShadeRamp) and main() resolves it per
// fragment. Splitting this into a program per surface type would mean splitting every mesh to match,
// and gain nothing: the classes share their geometry, lighting and fog, and differ only in where
// the final colour is looked up.
//
// Both stages live in this file, selected by VERTEX_SHADER / FRAGMENT_SHADER, which
// ShaderProgram.Load defines when it compiles each one. See Gl/ShaderSource.

#if defined(VERTEX_SHADER)
	#define VARYING out
#else
	#define VARYING in
#endif

// Declared once for both stages, so the two can never drift out of step.
VARYING vec3 vColor;
VARYING vec2 vUV;
VARYING float vUvWeight;
VARYING float vShade;

// The codes that say what a face is and which row it reads. Every corner of a primitive carries the
// same value, so they are flat: interpolating a constant is not exact, and the fragment stage tests
// these against thresholds. A Gouraud face on ramp 0 carries exactly 256, which comes out of
// interpolation as 255.99998 on some pixels and fails `>= 256.0`, speckling the face with the
// shaded chain's next row.
flat VARYING float vTextured;
flat VARYING float vUnlit;
flat VARYING float vShadeRamp;
// The palette index a flat solid face names, or -1. See MeshVertex.SolidPaletteIndex.
flat VARYING float vSolidPaletteIndex;
// The fill ramp a shaded poly's outline is tested against, or -1. See MeshVertex.OutlineFillRamp.
flat VARYING float vOutlineFillRamp;
// The face's facing, the vertex stage's front/back decision below: 1 front, -1 back. Read by the textured faces' row
// walk.
flat VARYING float vFacing;
VARYING float vLightShade;
VARYING float vViewDistance;

// A TSGouraudPoly corner's palette index: the ramp entry its own shade picks, looked up per vertex
// and interpolated linearly in SCREEN space, as Raster_FillContourPolygon (004879c0) interpolates it
// across the projected triangle. Zero for every other surface.
noperspective VARYING float vGouraudIndex;

// The theater's shaded-surface table, SurfaceRampTable: the fragment stage reads its shaded chain and
// palette row, the vertex stage the Gouraud block's palette indices.
uniform sampler2D uShadeRampTable;
uniform bool uShadeRampEnabled;
uniform float uShadeRampGouraudRow;

#ifdef EDITOR_GRID
// Only the measuring grid needs a surface point's horizontal world position, so the varying does not
// exist at all in the program the simulator draws with.
VARYING vec2 vWorldXZ;
#endif

#ifdef VERTEX_SHADER

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;
layout (location = 3) in vec2 aUV;
layout (location = 4) in float aTextured;
layout (location = 5) in float aUnlit;
layout (location = 6) in float aShade;
layout (location = 7) in float aShadeRamp;
layout (location = 8) in vec3 aFaceNormal;
layout (location = 9) in float aUvWeight;
layout (location = 10) in float aSolidPaletteIndex;
layout (location = 11) in vec3 aFaceCenter;
layout (location = 12) in float aSide;
layout (location = 13) in float aOutlineFillRamp;
layout (location = 14) in vec4 aDependFace;
layout (location = 15) in float aDependSide;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uLightDirection;

// The 256 in the sun's 128 + 256 * facing below, MissionSun.FaceShadeSlope; set per game.
uniform float uSunShadeSlope;

// The impact effects' dynamic lights, as they apply to the object being drawn — FUN_00407098 picks
// them per render entry and the renderer uploads its answer here. See EffectLightSelection, and
// docs/retail/formats/effect-lights.md for the two shade terms below.
//
// Nine of them, because the original registers these into the same ten-slot active list the mission
// sun already occupies and drops the surplus. uEffectLights[i].xyz is the direction the light
// travels for a directional entry and its render-space position for a point one; .w is the
// original's own light type tag, 1 and 2.
#define MAX_EFFECT_LIGHTS 9
uniform int uEffectLightCount;
uniform vec4 uEffectLights[MAX_EFFECT_LIGHTS];
uniform float uEffectLightIntensity[MAX_EFFECT_LIGHTS];
uniform float uEffectLightFalloff;

void main() {
	vec4 worldPosition = uModel * vec4(aPosition, 1.0);
#ifdef EDITOR_GRID
	vWorldXZ = worldPosition.xz;
#endif
	vec4 viewPosition = uView * worldPosition;

	// Normals only ever see rotation and uniform scale here, so the plain model matrix is
	// enough; a normal matrix becomes necessary if non-uniform scaling ever appears.
	// aNormal is the shape's own per-corner normal, which for a TSGouraudPoly differs between
	// the three corners; aFaceNormal is the flat one they share.
	vec3 normal = normalize(mat3(uModel) * aNormal);

	// The face is turned to meet the eye before it is lit, exactly as the original does it:
	// every poly renderer runs TSPoly_FrontBackVisibilityTest (0048c620) on the poly's own
	// stored normal and centre, and negates the poly's normals when the answer is "back". The
	// test is on the FACE normal and centre, which every corner of one poly carries alike, so all
	// of them agree — in view space the camera is the origin, so a face is turned away when its
	// normal and its centre point the same way. A product of exactly zero answers "back", as the
	// original's does, which is every answer a poly with a zero stored normal gets. Left
	// unnormalized, since only the sign is read and a zero normal has no direction to normalize.
	vec3 viewCenter = (uView * (uModel * vec4(aFaceCenter, 1.0))).xyz;
	vec3 viewFaceNormal = mat3(uView) * (mat3(uModel) * aFaceNormal);
	float sideSign = dot(viewFaceNormal, viewCenter) >= 0.0 ? -1.0 : 1.0;

	// A copy built for one side only (MeshVertex.Side) is dropped while the eye is on the other:
	// the format's "do not draw this face" pair, and a side whose pair differs from the other's.
	// Every corner of the poly reaches the same answer, so the whole triangle, line or point lands
	// on one spot past the far plane and rasterizes nothing.
	if (aSide != 0.0 && aSide != sideSign) {
		gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
		return;
	}

	// A copy drawn only while ANOTHER poly faces a given way (MeshVertex.DependSide): a back-facing
	// three-vertex TSTexture4Poly, whose second corner is what an earlier texture poly left in
	// TSTexture4Poly_Render's position array (TextureCornerSlot). That poly's plane comes as its
	// normal and its centre's offset along it, and the test is the one above on the point of the
	// plane nearest the origin, which gives the same sign as the poly's own centre would.
	if (aDependSide != 0.0) {
		vec3 dependNormal = aDependFace.xyz;
		float normalSquared = dot(dependNormal, dependNormal);
		vec3 onPlane = normalSquared > 0.0 ? dependNormal * (aDependFace.w / normalSquared) : vec3(0.0);
		vec3 viewOnPlane = (uView * (uModel * vec4(onPlane, 1.0))).xyz;
		vec3 viewDependNormal = mat3(uView) * (mat3(uModel) * dependNormal);
		float dependSign = dot(viewDependNormal, viewOnPlane) >= 0.0 ? -1.0 : 1.0;
		if (dependSign != aDependSide) {
			gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
			return;
		}
	}

	// The shade byte Light_ComputeShadeForFace (0048bedc) gives this corner:
	//     t = (dot - 0x400000) >> 1;  if (t < 0) shade -= (0x100 * t) >> 22
	// which with normals at length 0x800 and the sun at 0x1000/0x100 collapses to
	// max(128 + 256 * facing, 0), the 256 being uSunShadeSlope. See MissionSun.ShadeForFace.
	//
	// Computed HERE, per vertex — which is what makes a TSGouraudPoly Gouraud.
	// TSGouraudPoly_Render (004755c8) calls the light function once per vertex, walking NormalList
	// and VertexList in step, and its fill turns each corner's shade into a ramp entry before
	// interpolating anything (vGouraudIndex below). Doing it per fragment from an interpolated normal
	// instead is Phong, and it differs wherever the clamp bites: the original clamps each corner
	// first, so a corner that bottoms out at 0 still ramps linearly to its neighbour rather than
	// holding a dead flat region. Flat polys are unaffected — their three corners share a normal, so
	// this is constant across the face.
	vec3 litNormal = normal * sideSign;
	float facing = dot(litNormal, -normalize(uLightDirection));

	// Light_ComputeShadeForFace sums every light's term and clamps the SUM at 255, so the sun's own
	// term is floored at 0 here but not capped — capping it first would swallow an effect light's
	// contribution to a face the sun has already overdriven, and the sun overdrives anything within
	// 60 degrees of it.
	float shade = max(128.0 + uSunShadeSlope * facing, 0.0);

	// A surface that carries a baked shade byte (terrain) never reads this, so it does not pay for
	// the loop either.
	if (aUnlit < 0.5) {
		for (int i = 0; i < uEffectLightCount; i++) {
			vec4 light = uEffectLights[i];
			if (light.w < 1.5) {
				// Directional. The same curve as the sun, halved: the direction vector is built at
				// length 0x800 rather than 0x1000, which makes the term peak at the light's
				// intensity instead of twice it and leaves a half-lambert (1 + facing) / 2.
				shade += uEffectLightIntensity[i]
					* (1.0 + dot(litNormal, -light.xyz)) * 0.5;
			} else {
				// Point, the type-2 branch: intensity * B * 0x20 * cos / distance, with the
				// original's A of zero dropping out of the denominator. uEffectLightFalloff carries
				// that numerator already divided by the world-units-per-metre this distance is in.
				vec3 displacement = light.xyz - worldPosition.xyz;
				float lightDistance = length(displacement);
				float cosine = lightDistance > 1e-6
					? dot(litNormal, displacement / lightDistance)
					: 0.0;
				if (cosine > 0.0) {
					shade += uEffectLightIntensity[i] * uEffectLightFalloff * cosine / lightDistance;
				}
			}
		}
	}

	vLightShade = min(shade, 255.0);

	// A TSGouraudPoly corner's palette index, Raster_FillContourPolygon's per-vertex lookup: entry
	// (shade * length) >> 8 of the ramp the surface names, which is the Gouraud block's alpha at
	// this shade (SurfaceRampTable). The shade is the byte the light function returns, so it is
	// truncated, not rounded. The fragment stage bands what this interpolates to.
	vGouraudIndex = 0.0;
	if (uShadeRampEnabled && aShadeRamp >= 256.0) {
		ivec2 cell = ivec2(int(min(shade, 255.0)), int(uShadeRampGouraudRow + (aShadeRamp - 256.0) + 0.5));
		vGouraudIndex = floor(texelFetch(uShadeRampTable, cell, 0).a * 255.0 + 0.5);
	}

	vColor = aColor;
	vUV = aUV;
	vUvWeight = aUvWeight;
	vTextured = aTextured;
	vUnlit = aUnlit;
	vShade = aShade;
	vShadeRamp = aShadeRamp;
	vSolidPaletteIndex = aSolidPaletteIndex;
	vOutlineFillRamp = aOutlineFillRamp;
	vFacing = sideSign;
	// Depth along the view axis, not distance from the eye. That is the quantity the original
	// fogs against: its view space is (across, depth, up) — Raster_PerspectiveDivide (0048c4f0)
	// divides components 0 and 2 by component 1 to project — and Terrain_DrawCellQuad hands
	// Raster_SetDepthFadeFromDistance the minimum of its four corners' component 1. Radial
	// distance is larger than this everywhere off the view axis, by 1/cos of the angle off it,
	// which reaches 18% at the corner of the view and fogs the edges of the screen too hard.
	vViewDistance = -viewPosition.z;

	gl_Position = uProjection * viewPosition;
}

#endif

#ifdef FRAGMENT_SHADER

#ifdef EDITOR_GRID
uniform bool uGridEnabled;
uniform vec3 uGridColor;
uniform float uGridSpacing;
uniform float uGridMinorOpacity;
uniform float uGridMajorOpacity;
uniform float uGridLineWidthPixels;
uniform float uGridMajorEvery;
uniform float uGridMajorWidthScale;
uniform float uGridMinCellPixels;
uniform float uGridFadeStart;
uniform float uGridFadeEnd;
#endif

uniform vec3 uFogColor;
uniform float uFogStart;
uniform float uFogEnd;
uniform sampler2D uTexture;
uniform bool uTextureEnabled;
uniform float uShadeRampRows;
// The row of uShadeRampTable holding the palette straight through. See SurfaceRampTable.PaletteRow.
uniform float uShadeRampPaletteRow;
uniform sampler2D uPaletteRamp;
uniform bool uPaletteRampEnabled;
uniform float uShadeLevels;
uniform float uPaletteRampRows;
// The row a flat solid face reads, before its depth slice: ShadeRamp.UnlitShade's row in slice 0,
// the fixed shade TSSolidPoly_Render passes. See PaletteRampTable.UnlitRow.
uniform float uPaletteRampUnlitRow;
// The row an untextured terrain cell reads, before its depth slice: TerrainMeshBuilder.UntexturedRowShade's
// row in slice 0. See PaletteRampTable.GroundRow.
uniform float uPaletteRampGroundRow;
uniform bool uGroundFill;
uniform float uDepthSlices;
uniform float uFogDepthBias;
// The shaded chain's block this item reads in place of its depth slice, or -1. See SceneItem.RampBlock.
uniform float uRampBlock;
uniform bool uFullbright;

// A ground shape is painted in the original straight after its own terrain cell, so the ground of
// every cell painted later covers it. uPaintRank holds, per pixel, the paint rank of the ground
// showing there (TerrainPaintRankBuffer); uGroundShapeRank is the rank of the shape's own cell. See
// SceneRenderer.Render.
uniform usampler2D uPaintRank;
uniform bool uPaintRankTest;
uniform uint uGroundShapeRank;

out vec4 FragColor;

// Which of the theater ramp's depth slices this fragment is drawn in — the original's distance fog,
// which it spends by adding whole slices to the ramp row a fill is about to read rather than by
// blending anything. Raster_SetDepthFadeFromDistance (00467fec):
//
//     if (d >= range)   d = range;
//     if (d <= range/2) return 0;
//     t = min((d - range/2) * 2 / range, 1)
//     return trunc(t * (slices - 1))
//
// so nothing inside half the visibility range fogs at all, the fade runs over the outer half, and it
// lands on a whole slice. uFogEnd is that range and uFogStart is its half; both come off the zone
// itself (Scene.Atmosphere). The truncation is the original's and is kept: the twelve steps are
// visible in retail, and smoothing them would be a different picture.
float depthSlice(float d) {
	if (uDepthSlices < 2.0 || d <= uFogStart) {
		return 0.0;
	}

	float t = clamp((min(d, uFogEnd) - uFogStart) / max(uFogEnd - uFogStart, 0.001), 0.0, 1.0);
	return min(floor(t * (uDepthSlices - 1.0)), uDepthSlices - 1.0);
}

#ifdef EDITOR_GRID

// Anti-aliased coverage of the nearest line of a unit-spaced grid. The width is taken in
// screen pixels via the coordinate's own derivative — a line that is thinning out then fades
// smoothly instead of landing on and off pixel centres and dropping out in bands, which is
// what a rasterized line grid does at a grazing angle. The set also fades out once its cells
// are too small on screen to be told apart, since drawing them can only produce moire.
float gridLine(vec2 grid, vec2 cellsPerPixel, float widthPixels) {
	vec2 toLine = abs(fract(grid + 0.5) - 0.5) / cellsPerPixel;
	float coverage = 1.0 - smoothstep(0.0, widthPixels, min(toLine.x, toLine.y));
	float cellPixels = 1.0 / max(cellsPerPixel.x, cellsPerPixel.y);
	return coverage * clamp(cellPixels / uGridMinCellPixels, 0.0, 1.0);
}

// The measuring grid over one surface point, as a colour to blend toward and how strongly.
// Taken from the point's own world position, so the lines follow whatever the ground does
// underneath them.
float gridCoverage() {
	vec2 cell = vWorldXZ / uGridSpacing;
	vec2 cellsPerPixel = max(fwidth(cell), vec2(1e-8));

	// Two grids over the same ground: every line, and every uGridMajorEvery'th line at its own
	// width and opacity. Taking the stronger where they coincide makes a major line one line
	// at its own weight rather than two stacked and blended.
	float minor = uGridMinorOpacity * gridLine(cell, cellsPerPixel, uGridLineWidthPixels);
	float major = uGridMajorOpacity * gridLine(cell / uGridMajorEvery,
		cellsPerPixel / uGridMajorEvery, uGridLineWidthPixels * uGridMajorWidthScale);

	return max(minor, major);
}

#endif

// The textured faces' row walk, for an item with a SceneItem.TextureWalk: VGA8_FillBitmapPolygon (ES1DBSIM 000534ac)
// in clip mode 2, the mode the scene is drawn in (docs/es1/rendering/polygon-shading.md#the-display-drivers and
// #bitmap-fills-brush-modes-5-and-7), evaluated for this one fragment over the whole face its triangle was fanned out
// of, gl_PrimitiveID naming the triangle's record in TextureWalkTable. The face's ring is cut at the near plane as
// Poly_ClipRingToNearPlaneWithUV cuts it
// (docs/retail/rendering/view-transform-and-projection.md#clipping-at-the-near-plane) and projected with the vertex
// stage's matrices; then the row's two edges, the span between them along the row
// (VGA8_SpanBitmapRow, 000735a4) and the span's nearest texel are found by the doc's steps. Edge A runs backward round
// the ring and edge B forward, from the first corner with the smallest row toward the first with the largest, whose row
// is never drawn; an edge whose next corner is in its own row is passed over, the next edge starting from that corner's
// own column and texture point. The span's cut to each of the clip block's regions is not made here: the world layer's
// cells take the scene's pixels inside the current view's regions alone (Es1VideoMemory.DrawWorld), which keeps the
// pixels the span's cut keeps. Retail steps integers from integer screen points; this evaluates the same lines in float
// at the fragment's row and column, and departs from it in these places:
//   - the projected points keep their fractions, where retail's are whole pixels;
//   - an edge's column is the ceiling of its line at the row, less WALK_EPSILON so that a column the line meets exactly
//     is not pushed one right by rounding, where retail keeps the ceiling exact with a remainder;
//   - u and v are interpolated in float, along the edges and along the span, where retail steps 16.16 values whose
//     steps Math_Q16Divide truncates;
//   - the cut at the clip rect's top (VGA8_BitmapPolyClipTop) is not made: the edges are evaluated where the cut leaves
//     them, without its truncating division;
//   - "the walk ends when A's column exceeds B's" is tested at this row alone, which for a convex ring happens only at
//     its far end;
//   - only fragments the triangles rasterize are evaluated, so a pixel the walk would fill past their edges stays as
//     it was.
// No winding test is made (docs/es1/rendering/polygon-shading.md#the-four-poly-types): a copy drawing the front value
// walks its ring as projected, and one drawing the back value walks the screen ring reversed, as
// Raster_DrawPolygonReversed reverses it, its texture points left in slot order; a ring the walk cannot fill draws
// nothing.
uniform bool uTextureWalk;
uniform samplerBuffer uTextureWalkFaces;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec2 uViewportOrigin;
uniform vec2 uViewportSize;
uniform float uNearPlane;

// TextureWalkTable.TexelsPerTriangle and MaxCorners; a ring of four cut at a plane keeps at most five points.
#define WALK_TEXELS 8
#define WALK_CORNERS 4
#define WALK_POINTS 8
#define WALK_EPSILON (1.0 / 1024.0)

vec2 walkScreen[WALK_POINTS];
vec2 walkTexture[WALK_POINTS];
int walkCount;

// A ring point in the walk's screen space: whole numbers at pixel centres, x to the right and y down from the
// viewport's top-left, as retail's screen points are.
void walkPut(vec3 view, vec2 texturePoint) {
	vec4 clip = uProjection * vec4(view, 1.0);
	vec2 local = (clip.xy / clip.w * 0.5 + 0.5) * uViewportSize;
	walkScreen[walkCount] = vec2(local.x - 0.5, uViewportSize.y - local.y - 0.5);
	walkTexture[walkCount] = texturePoint;
	walkCount++;
}

// The column and texture point of the edge chain running from `from` by `step` toward `far`, at row y. False past the
// chain's end.
bool walkEdge(int from, int step, int far, float y, out float s, out vec2 texturePoint) {
	s = 0.0;
	texturePoint = vec2(0.0);
	int i = from;
	for (int e = 0; e < WALK_POINTS; e++) {
		if (i == far) {
			return false;
		}

		int j = i + step;
		j = j < 0 ? j + walkCount : (j >= walkCount ? j - walkCount : j);
		vec2 a = walkScreen[i];
		vec2 b = walkScreen[j];
		if (y >= a.y && y < b.y) {
			float k = y - a.y;
			float span = b.y - a.y;
			s = ceil(a.x + k * (b.x - a.x) / span - WALK_EPSILON);
			texturePoint = walkTexture[i] + k * (walkTexture[j] - walkTexture[i]) / span;
			return true;
		}

		i = j;
	}

	return false;
}

// 1 with the atlas texel the walk gives this fragment in `at`; 0 when the walk leaves the fragment unpainted; -1 when
// the triangle is no walked face's, and fills through its own mapping.
int walkTexel(out ivec2 at) {
	at = ivec2(0);
	int base = gl_PrimitiveID * WALK_TEXELS;
	vec4 header = texelFetch(uTextureWalkFaces, base);
	int count = int(header.x + 0.5);
	if (count < 3) {
		return -1;
	}

	vec2 frameSize = texelFetch(uTextureWalkFaces, base + 1).xy;
	vec4 points01 = texelFetch(uTextureWalkFaces, base + 6);
	vec4 points23 = texelFetch(uTextureWalkFaces, base + 7);
	vec2 points[WALK_CORNERS] = vec2[WALK_CORNERS](points01.xy, points01.zw, points23.xy, points23.zw);
	vec3 view[WALK_CORNERS];
	for (int i = 0; i < WALK_CORNERS; i++) {
		view[i] = i < count
			? (uView * (uModel * vec4(texelFetch(uTextureWalkFaces, base + 2 + i).xyz, 1.0))).xyz
			: vec3(0.0);
	}

	// Poly_ClipRingToNearPlaneWithUV: each edge from the previous corner to this one emits the crossing when one end
	// is in front of the plane, then this corner when it is in front; the texture point is cut with the same t.
	walkCount = 0;
	for (int i = 0; i < count; i++) {
		int p = i == 0 ? count - 1 : i - 1;
		float depthP = -view[p].z;
		float depthI = -view[i].z;
		bool frontP = depthP > uNearPlane;
		bool frontI = depthI > uNearPlane;
		if (frontP != frontI) {
			float t = (uNearPlane - depthP) / (depthI - depthP);
			walkPut(mix(view[p], view[i], t), mix(points[p], points[i], t));
		}

		if (frontI) {
			walkPut(view[i], points[i]);
		}
	}

	if (walkCount < 3) {
		return 0;
	}

	// The back value: drawn by a copy lit as the back from the front (TextureWalkFace.LitAsBack), or seen from the back.
	if (header.w > 0.5 || vFacing < 0.0) {
		for (int i = 0; i < walkCount / 2; i++) {
			int j = walkCount - 1 - i;
			vec2 screen = walkScreen[i];
			walkScreen[i] = walkScreen[j];
			walkScreen[j] = screen;
		}
	}

	// Step 1, and the first corners with the smallest and the largest row.
	int near = 0;
	int far = 0;
	float lowX = walkScreen[0].x;
	float highX = lowX;
	for (int i = 1; i < walkCount; i++) {
		vec2 point = walkScreen[i];
		near = point.y < walkScreen[near].y ? i : near;
		far = point.y > walkScreen[far].y ? i : far;
		lowX = min(lowX, point.x);
		highX = max(highX, point.x);
	}

	float x = gl_FragCoord.x - uViewportOrigin.x - 0.5;
	float y = uViewportSize.y - (gl_FragCoord.y - uViewportOrigin.y) - 0.5;
	if (walkScreen[near].y >= walkScreen[far].y || lowX >= highX || y < walkScreen[near].y
			|| y >= walkScreen[far].y) {
		return 0;
	}

	// Steps 3 and 4: edge A backward, edge B forward; the far corner's row is never reached.
	float a;
	float b;
	vec2 pointA;
	vec2 pointB;
	if (!walkEdge(near, -1, far, y, a, pointA) || !walkEdge(near, 1, far, y, b, pointB) || a > b
			|| x < a || x > b) {
		return 0;
	}

	// Step 5, VGA8_SpanBitmapRow: the middles of n equal steps from A to B along the row, and the nearest texel.
	// Nothing in retail bounds the address; the middles stay inside the frame, and the clamp keeps a rounding slip out
	// of the atlas's next frame.
	float n = b - a + 1.0;
	vec2 stepPoint = (pointB - pointA) / n;
	vec2 texturePoint = pointA + stepPoint * 0.5 + (x - a) * stepPoint;
	ivec2 texel = clamp(ivec2(floor(texturePoint + 0.5)), ivec2(0), ivec2(frameSize) - 1);
	at = ivec2(header.yz) + texel;
	return 1;
}

void main() {
	if (uPaintRankTest && texelFetch(uPaintRank, ivec2(gl_FragCoord.xy), 0).r > uGroundShapeRank) {
		discard;
	}

	// The corners' shade bytes, which the vertex shader computes. Every lit surface that reads it here
	// is flat — its corners agree — and a TSGouraudPoly reads vGouraudIndex instead.
	float shade = clamp(vLightShade, 0.0, 255.0);

	// A surface the original shades once ahead of time and stores carries its own byte rather
	// than one computed here — terrain, whose per-cell shades Terrain_BuildSurface bakes at
	// zone load and Terrain_DrawCellQuad hands straight to the span setup.
	if (vUnlit > 0.5) {
		shade = clamp(vShade, 0.0, 255.0);
	}

	// Per-vertex, not per-draw: a mesh mixes textured and fallback-coloured triangles, and
	// vTextured is flat across each triangle so this never interpolates between the two.
	// Distance is spent as a slice of the theater ramp wherever the fragment reads one, not at all on
	// a TSGouraudPoly, and as a blend toward uFogColor only where neither rule settles it — see
	// depthSlice above, and PaletteRampTable.
	//
	// uFogDepthBias pulls the terrain back to the depth of its cell's leading corner, which is where
	// the original measures a cell's fog from. It is zero for everything else, which the original
	// fogs from one distance per object rather than per cell. See SceneRenderer.FogCellSize.
	float fogDepth = max(vViewDistance - uFogDepthBias, 0.0);
	float slice = depthSlice(fogDepth);
	bool fogSettled = false;

	vec3 baseColor = vColor;
	bool textured = uTextureEnabled && vTextured > 0.5;
	bool texturedExact = false;
	if (textured) {
		ivec2 walkAt;
		int walked = uTextureWalk ? walkTexel(walkAt) : -1;
		if (walked == 0) {
			discard;
		}

		vec4 texel;
		if (walked > 0) {
			texel = texelFetch(uTexture, walkAt, 0);
		} else {
			// A textured quad's corners carry homogeneous UVs so that both of its triangles
			// resolve to the one projective map the original's quad rasterizer walks — see
			// MeshVertex.UvWeight. Everything else carries a plain coordinate and weight 0.
			vec2 uv = vUvWeight > 0.0 ? vUV / vUvWeight : vUV;
			texel = texture(uTexture, uv);
		}

		// Palette index 0 decodes to alpha 0 in a bank whose frames are cutouts — the lattice
		// girders on a structure. The original's span routine skips that index rather than
		// writing it, so the hole is a hole and not a black polygon. See
		// SceneModelLibrary.LoadAtlas. A walked face is brush mode 7, which writes index 0 through
		// its colour table, so it never skips one.
		if (walked < 1 && texel.a < 0.5) {
			discard;
		}

		// The exact indexed path: uTexture's red channel is the texel's PALETTE INDEX, not its
		// colour, and the original's span writes rampRow(shade)[index] — the light level picks
		// a row of the theater .RMP and the texel picks the column. uPaletteRamp is that table
		// expanded through the palette, so this is one sample and no approximation. The row is
		// Raster_ShadeRampRow's own selection, floor(shade * (levels - 1) / 256).
		if (uPaletteRampEnabled) {
			float row = clamp(floor(shade * (uShadeLevels - 1.0) / 256.0), 0.0, uShadeLevels - 1.0);

			// The depth bias, which the original adds to this same row offset as a whole number of
			// slices. The table is stored slice-major, so it is one multiply.
			row += slice * uShadeLevels;

			// A fullbright draw takes the row past every slice, which is the palette straight
			// through: the original switches TSTexture4Poly_Render to a plain texture copy with
			// neither a light term nor a ramp lookup — and so with no depth bias either, which is why
			// a projectile does not fog. See PaletteRampTable.FullbrightRow and SceneItem.Fullbright.
			if (uFullbright) {
				row = uPaletteRampRows - 1.0;
			}

			baseColor = texture(uPaletteRamp,
				vec2((floor(texel.r * 255.0 + 0.5) + 0.5) / 256.0, (row + 0.5) / uPaletteRampRows)).rgb;
			texturedExact = true;
			fogSettled = true;
		} else {
			baseColor = texel.rgb;
		}
	}

	vec3 lit;
	if (texturedExact) {
		lit = baseColor;
	} else if (uShadeRampEnabled && !textured && vShadeRamp >= 256.0) {
		// A TSGouraudPoly, its ramp number biased by SurfaceRampTable.GouraudRowOffset. Its fill,
		// Raster_FillContourTriangle (00486ddc), cuts each triangle of the fan at every whole palette
		// index between its corners' and fills each band flat with the band's UPPER index, so a pixel
		// takes the ceiling of its interpolated index and the lowest corner's index shows only where
		// every corner has it. The band is a plain palette index: no .RMP row and no depth slice is
		// applied, so the face is not fogged. docs/retail/rendering/dts-texture-binding.md,
		// "TSGouraudPoly — same ramp number, per-vertex light, no .RMP row".
		//
		// The cuts are straight lines through points spaced evenly along the triangle's edges, which
		// is where linear screen-space interpolation of the index crosses each whole value; retail
		// places them on whole pixels, which moves an edge of a band by under a pixel. The slack keeps
		// a face whose corners all carry one index from rounding up to the next where interpolation
		// lands a hair above it.
		const float bandSlack = 1.0 / 1024.0;
		float index = clamp(ceil(vGouraudIndex - bandSlack), 0.0, 255.0);
		lit = texelFetch(uShadeRampTable, ivec2(int(index), int(uShadeRampPaletteRow + 0.5)), 0).rgb;
		fogSettled = true;
	} else if (uShadeRampEnabled && !textured && vShadeRamp >= 0.0) {
		// A TSShadedPoly has no colour for a light term to multiply — its surface names a material
		// ramp and the face's shade byte picks a step along that ramp, and that lookup IS the
		// shading. uShadeRampTable is the whole ramp-by-shade grid, so this is one sample rather than
		// the original's two table reads. The chain has one block of ramps per depth slice, because
		// TSShadedPoly_Render ends in Raster_ShadeRampRow and is fogged by the same bias everything
		// else is. An item that names a block of its own reads that block instead (SceneItem.RampBlock).
		float block = uRampBlock >= 0.0 ? uRampBlock : slice;
		float row = vShadeRamp + block * 256.0;
		vec4 cell = texture(uShadeRampTable,
			vec2((floor(shade) + 0.5) / 256.0, (row + 0.5) / uShadeRampRows));

		// A TSShadedPoly's outline: TSShadedPoly_Render resolves its line entry through the same
		// lookups as the fill, at the same shade and slice, and PolyFill_FillThenOutline draws the
		// edge loop only when the two palette bytes differ. The table's alpha is that byte.
		if (vOutlineFillRamp >= 0.0) {
			float fillRow = vOutlineFillRamp + block * 256.0;
			float fillByte = texture(uShadeRampTable,
				vec2((floor(shade) + 0.5) / 256.0, (fillRow + 0.5) / uShadeRampRows)).a;
			if (floor(fillByte * 255.0 + 0.5) == floor(cell.a * 255.0 + 0.5)) {
				discard;
			}
		}

		lit = cell.rgb;
		fogSettled = true;
	} else if (uGroundFill && uPaletteRampEnabled && vSolidPaletteIndex >= 0.0) {
		// A terrain cell drawn without its texture, Terrain_FillCellUntextured's flat fill: the
		// palette index the cell's material ramp 2 reaches at its baked shade, read through the ramp
		// row for a fixed shade of 0x4b plus the depth slice the cell's fade installed. The mesh
		// carries the index; see TerrainMeshBuilder.
		float row = uPaletteRampGroundRow + slice * uShadeLevels;
		lit = texture(uPaletteRamp,
			vec2((floor(vSolidPaletteIndex + 0.5) + 0.5) / 256.0, (row + 0.5) / uPaletteRampRows)).rgb;
		fogSettled = true;
	} else if (uPaletteRampEnabled && vSolidPaletteIndex >= 0.0) {
		// A plain TSSolidPoly. Its surface value is a palette INDEX, and the original resolves it
		// as rampRow(UnlitShade)[index] — one fixed row of the same table a lit textured texel is
		// read from, never lit. Raster_ShadeRampRow (00468054) adds the depth slice to that row, so it
		// fogs through the ramp as everything else does (docs/retail/rendering/distance-fog-and-sky.md,
		// "A projectile is faded like anything else"). Resolving it here rather than on the CPU is
		// what lets it follow the damage flash's palette swap and the slice.
		float row = uPaletteRampUnlitRow + slice * uShadeLevels;
		vec4 cell = texture(uPaletteRamp,
			vec2((floor(vSolidPaletteIndex + 0.5) + 0.5) / 256.0, (row + 0.5) / uPaletteRampRows));

		// A solid face's outline: PolyFill_FillThenOutline redraws the edge loop only when the line's
		// byte and the fill's differ in this same row, so an outline can come and go with the slice.
		// The table's alpha is that byte. See MeshVertex.OutlineFillRamp.
		if (vOutlineFillRamp >= 0.0) {
			float fillByte = texture(uPaletteRamp,
				vec2((floor(vOutlineFillRamp + 0.5) + 0.5) / 256.0, (row + 0.5) / uPaletteRampRows)).a;
			if (floor(fillByte * 255.0 + 0.5) == floor(cell.a * 255.0 + 0.5)) {
				discard;
			}
		}

		lit = cell.rgb;
		fogSettled = true;
	} else {
		// What is left names no palette index either: a fallback colour for a surface nothing
		// could resolve, or any solid face in a theater whose palette ramp did not load — where
		// baseColor still holds the colour the mesh builder baked. Final as it stands.
		lit = baseColor;
	}

#ifdef EDITOR_GRID
	// The measuring grid, painted onto the surface before the fog so that a distant line
	// washes out along with the ground it is drawn on. It is a tool for reading the ground
	// nearby, so it is also faded out with distance on its own account, well inside the fog.
	//
	// Branching on the uniform alone, never on the fade: gridCoverage reads screen-space
	// derivatives, and those are undefined inside control flow that differs between
	// neighbouring fragments — which is exactly what an early-out on a distance would be.
	if (uGridEnabled) {
		float reach = 1.0 - smoothstep(uGridFadeStart, uGridFadeEnd, vViewDistance);
		lit = mix(lit, uGridColor, reach * gridCoverage());
	}
#endif

	// Whatever no rule above settled — a surface in a theater whose ramp did not load, or a fallback
	// colour — fades toward the ramp's own fog colour over the same interval instead. It is the
	// approximation, not the rule.
	if (!fogSettled) {
		float fog = clamp((fogDepth - uFogStart) / max(uFogEnd - uFogStart, 0.001), 0.0, 1.0);
		lit = mix(lit, uFogColor, fog);
	}

	FragColor = vec4(lit, 1.0);
}

#endif
