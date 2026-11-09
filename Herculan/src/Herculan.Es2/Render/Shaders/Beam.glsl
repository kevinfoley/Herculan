#version 330 core

// The beam pass: one camera-facing quad per live BeamTracer. The expansion happens here in clip
// space rather than in screen pixels as the original does it — see BeamRenderer for how that
// construction relates to FUN_0040bc14's, and for why the fill is a bare texture copy with no shade
// level, tint or blending.
//
// uEntryProjection places the vertex, the offset measured through uProjection, and the fragment stage has Scene.glsl's
// paint-rank test, for WorldDrawPass's painter's order; the per-pixel depth draw sets both projections alike and the test
// off. Neither clip distance cuts anything here: BeamRenderer clips the segment at the view's near plane itself and draws
// it with depth clamping in either draw.
//
// Both stages live in this file, selected by VERTEX_SHADER / FRAGMENT_SHADER, which
// ShaderProgram.Load defines when it compiles each one. See Gl/ShaderSource.

#if defined(VERTEX_SHADER)
	#define VARYING out
#else
	#define VARYING in
#endif

// Declared once for both stages, so the two can never drift out of step.
VARYING float vProfile;

#ifdef VERTEX_SHADER

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aAxis;
layout (location = 2) in float aSide;
layout (location = 3) in float aProfile;

uniform mat4 uView;
uniform mat4 uProjection;
uniform mat4 uEntryProjection;
uniform vec3 uCameraPosition;
uniform vec2 uViewport;
uniform float uHalfWidth;
uniform float uMinimumHalfPixels;

void main() {
	// Perpendicular to the beam and to the line of sight, so the quad turns to face the
	// viewer as they move around it. Degenerate when looking straight down the beam, which is
	// exactly when the quad has no visible area anyway.
	vec3 toCamera = uCameraPosition - aPosition;
	vec3 perpendicular = cross(aAxis, toCamera);
	float length2 = length(perpendicular);
	perpendicular = length2 > 0.0 ? perpendicular / length2 : vec3(0.0);

	vec4 viewCenter = uView * vec4(aPosition, 1.0);
	vec4 viewOffset = uView * vec4(perpendicular * uHalfWidth, 0.0);
	vec4 center = uProjection * viewCenter;
	vec4 offset = uProjection * viewOffset;

	// The half-width floor, in the only units it can be stated in: how many pixels the offset
	// covers once divided through by this vertex's own w. See MinimumHalfPixels.
	float scale = 1.0;
	float pixels = length((offset.xy / max(center.w, 0.0001)) * 0.5 * uViewport);
	if (pixels > 0.0 && pixels < uMinimumHalfPixels) {
		scale = uMinimumHalfPixels / pixels;
	}

	vProfile = aProfile;
	gl_ClipDistance[0] = 1.0;
	gl_ClipDistance[1] = 1.0;
	gl_Position = uEntryProjection * (viewCenter + viewOffset * (scale * aSide));
}

#endif

#ifdef FRAGMENT_SHADER

uniform sampler2D uProfileTexture;
uniform usampler2D uPaintRank;
uniform bool uPaintRankTest;
uniform uint uEntryRank;

out vec4 FragColor;

void main() {
	if (uPaintRankTest && texelFetch(uPaintRank, ivec2(gl_FragCoord.xy), 0).r > uEntryRank) {
		discard;
	}

	// Straight texel copy, which is all mode 0's span routine does — no tint, no shade level,
	// no blending. See BeamAppearance.
	FragColor = vec4(texture(uProfileTexture, vec2(0.5, vProfile)).rgb, 1.0);
}

#endif
