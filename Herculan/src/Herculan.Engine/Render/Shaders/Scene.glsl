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
VARYING float vTextured;
VARYING float vUnlit;
VARYING float vShade;
VARYING float vShadeRamp;
// The palette index a flat solid face names, or -1. See MeshVertex.SolidPaletteIndex.
VARYING float vSolidPaletteIndex;
// The fill ramp a shaded poly's outline is tested against, or -1. See MeshVertex.OutlineFillRamp.
VARYING float vOutlineFillRamp;
VARYING float vLightShade;
VARYING float vViewDistance;

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

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uLightDirection;

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

	// The shade byte Light_ComputeShadeForFace (0048bedc) gives this corner:
	//     t = (dot - 0x400000) >> 1;  if (t < 0) shade -= (0x100 * t) >> 22
	// which with normals at length 0x800 and the sun at 0x1000/0x100 collapses to
	// max(128 + 256 * facing, 0). See MissionSun.ShadeForFace.
	//
	// Computed HERE, per vertex, and interpolated — which is what makes a TSGouraudPoly
	// Gouraud. TSGouraudPoly_Render (004755c8) calls the light function once per vertex,
	// walking NormalList and VertexList in step, stashes the bytes and lets the span routine
	// interpolate between them. Doing it per fragment from an interpolated normal instead is
	// Phong, and it differs wherever the clamp bites: the original clamps each corner first
	// and then interpolates, so a corner that bottoms out at 0 still ramps linearly to its
	// neighbour rather than holding a dead flat region. Flat polys are unaffected — their
	// three corners share a normal, so this is constant across the face.
	vec3 litNormal = normal * sideSign;
	float facing = dot(litNormal, -normalize(uLightDirection));

	// Light_ComputeShadeForFace sums every light's term and clamps the SUM at 255, so the sun's own
	// term is floored at 0 here but not capped — capping it first would swallow an effect light's
	// contribution to a face the sun has already overdriven, and the sun overdrives anything within
	// 60 degrees of it.
	float shade = max(128.0 + 256.0 * facing, 0.0);

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

	vColor = aColor;
	vUV = aUV;
	vUvWeight = aUvWeight;
	vTextured = aTextured;
	vUnlit = aUnlit;
	vShade = aShade;
	vShadeRamp = aShadeRamp;
	vSolidPaletteIndex = aSolidPaletteIndex;
	vOutlineFillRamp = aOutlineFillRamp;
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
uniform sampler2D uShadeRampTable;
uniform bool uShadeRampEnabled;
uniform float uShadeRampRows;
uniform float uShadeRampGouraudRow;
uniform sampler2D uPaletteRamp;
uniform bool uPaletteRampEnabled;
uniform float uShadeLevels;
uniform float uPaletteRampRows;
// The row a flat solid face reads: ShadeRamp.UnlitShade's row in slice 0, the fixed shade
// TSSolidPoly_Render passes. See PaletteRampTable.UnlitRow.
uniform float uPaletteRampUnlitRow;
// The row an untextured terrain cell reads, before its depth slice: TerrainMeshBuilder.UntexturedRowShade's
// row in slice 0. See PaletteRampTable.GroundRow.
uniform float uPaletteRampGroundRow;
uniform bool uGroundFill;
uniform float uDepthSlices;
uniform float uFogDepthBias;
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

void main() {
	if (uPaintRankTest && texelFetch(uPaintRank, ivec2(gl_FragCoord.xy), 0).r > uGroundShapeRank) {
		discard;
	}

	// Interpolated from the three corners' own shade bytes — the vertex shader computes them,
	// which is what makes a TSGouraudPoly Gouraud rather than Phong. See there.
	float shade = clamp(vLightShade, 0.0, 255.0);

	// A surface the original shades once ahead of time and stores carries its own byte rather
	// than one computed here — terrain, whose per-cell shades Terrain_BuildSurface bakes at
	// zone load and Terrain_DrawCellQuad hands straight to the span setup.
	if (vUnlit > 0.5) {
		shade = clamp(vShade, 0.0, 255.0);
	}

	// Per-vertex, not per-draw: a mesh mixes textured and fallback-coloured triangles, and
	// vTextured is flat across each triangle so this never interpolates between the two.
	// Distance is spent as a slice of the theater ramp wherever the fragment reads one, and as a
	// blend toward uFogColor only where it does not — see depthSlice above, and PaletteRampTable.
	//
	// uFogDepthBias pulls the terrain back to the depth of its cell's leading corner, which is where
	// the original measures a cell's fog from. It is zero for everything else, which the original
	// fogs from one distance per object rather than per cell. See SceneRenderer.FogCellSize.
	float fogDepth = max(vViewDistance - uFogDepthBias, 0.0);
	float slice = depthSlice(fogDepth);
	bool rampFogged = false;

	vec3 baseColor = vColor;
	bool textured = uTextureEnabled && vTextured > 0.5;
	bool texturedExact = false;
	if (textured) {
		// A textured quad's corners carry homogeneous UVs so that both of its triangles
		// resolve to the one projective map the original's quad rasterizer walks — see
		// MeshVertex.UvWeight. Everything else carries a plain coordinate and weight 0.
		vec2 uv = vUvWeight > 0.0 ? vUV / vUvWeight : vUV;

		vec4 texel = texture(uTexture, uv);

		// Palette index 0 decodes to alpha 0 in a bank whose frames are cutouts — the lattice
		// girders on a structure. The original's span routine skips that index rather than
		// writing it, so the hole is a hole and not a black polygon. See
		// SceneModelLibrary.LoadAtlas.
		if (texel.a < 0.5) {
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
			rampFogged = true;
		} else {
			baseColor = texel.rgb;
		}
	}

	vec3 lit;
	if (texturedExact) {
		lit = baseColor;
	} else if (uShadeRampEnabled && !textured && vShadeRamp >= 0.0) {
		// A lit flat poly (TSShadedPoly and its Gouraud sibling) has no colour for a light
		// term to multiply — its surface names a material ramp and the face's shade byte
		// picks a step along that ramp, and that lookup IS the shading. uShadeRampTable is
		// the whole ramp-by-shade grid, so this is one sample rather than the original's two
		// table reads.
		// vShadeRamp carries the ramp number biased by SurfaceRampTable.GouraudRowOffset when the
		// face is a TSGouraudPoly, which selects the chain rather than the row. The shaded chain has
		// one block of ramps per depth slice, because TSShadedPoly_Render ends in
		// Raster_ShadeRampRow and is fogged by the same bias everything else is; the Gouraud chain
		// has no .RMP step to bias, so it is stored once and fogs by the blend below instead.
		float chainRamp = vShadeRamp;
		float row;
		if (chainRamp >= 256.0) {
			row = uShadeRampGouraudRow + (chainRamp - 256.0);
		} else {
			row = chainRamp + slice * 256.0;
			rampFogged = true;
		}

		vec4 cell = texture(uShadeRampTable,
			vec2((floor(shade) + 0.5) / 256.0, (row + 0.5) / uShadeRampRows));

		// A TSShadedPoly's outline: TSShadedPoly_Render resolves its line entry through the same
		// lookups as the fill, at the same shade and slice, and PolyFill_FillThenOutline draws the
		// edge loop only when the two palette bytes differ. The table's alpha is that byte.
		if (vOutlineFillRamp >= 0.0 && chainRamp < 256.0) {
			float fillRow = vOutlineFillRamp + slice * 256.0;
			float fillByte = texture(uShadeRampTable,
				vec2((floor(shade) + 0.5) / 256.0, (fillRow + 0.5) / uShadeRampRows)).a;
			if (floor(fillByte * 255.0 + 0.5) == floor(cell.a * 255.0 + 0.5)) {
				discard;
			}
		}

		lit = cell.rgb;
	} else if (uGroundFill && uPaletteRampEnabled && vSolidPaletteIndex >= 0.0) {
		// A terrain cell drawn without its texture, Terrain_FillCellUntextured's flat fill: the
		// palette index the cell's material ramp 2 reaches at its baked shade, read through the ramp
		// row for a fixed shade of 0x4b plus the depth slice the cell's fade installed. The mesh
		// carries the index; see TerrainMeshBuilder.
		float row = uPaletteRampGroundRow + slice * uShadeLevels;
		lit = texture(uPaletteRamp,
			vec2((floor(vSolidPaletteIndex + 0.5) + 0.5) / 256.0, (row + 0.5) / uPaletteRampRows)).rgb;
		rampFogged = true;
	} else if (uPaletteRampEnabled && vSolidPaletteIndex >= 0.0) {
		// A plain TSSolidPoly. Its surface value is a palette INDEX, and the original resolves it
		// as rampRow(UnlitShade)[index] — one fixed row of the same table a lit textured texel is
		// read from, never lit and never fogged through the ramp. Resolving it here rather than on
		// the CPU is what lets it follow the damage flash's palette swap, and it is the identical
		// byte either way: DtsMeshBuilder.ResolveSolidColors computes this very lookup.
		lit = texture(uPaletteRamp,
			vec2((floor(vSolidPaletteIndex + 0.5) + 0.5) / 256.0,
				(uPaletteRampUnlitRow + 0.5) / uPaletteRampRows)).rgb;
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

	// Whatever did not resolve through a ramp — a Gouraud face, or any surface in a theater whose
	// ramp did not load — has nothing to carry a slice, so it fades toward the ramp's own fog colour
	// over the same interval instead. It is the approximation, not the rule.
	if (!rampFogged) {
		float fog = clamp((fogDepth - uFogStart) / max(uFogEnd - uFogStart, 0.001), 0.0, 1.0);
		lit = mix(lit, uFogColor, fog);
	}

	FragColor = vec4(lit, 1.0);
}

#endif
