#version 330 core

// The billboard pass: SpriteQuad corners already brought into view space by SpriteRenderer, which
// builds them there so that perspective reproduces the original's 1/depth scaling for free. All this
// stage does is apply the projection.
//
// uEntryProjection places the corner and uProjection clips it, as in Scene.glsl, and the fragment stage has the same
// paint-rank test, for WorldDrawPass's painter's order; the per-pixel depth draw sets both projections alike and the test
// off.
//
// Both stages live in this file, selected by VERTEX_SHADER / FRAGMENT_SHADER, which
// ShaderProgram.Load defines when it compiles each one. See Gl/ShaderSource.

#if defined(VERTEX_SHADER)
	#define VARYING out
#else
	#define VARYING in
#endif

// Declared once for both stages, so the two can never drift out of step.
VARYING vec2 vUV;

#ifdef VERTEX_SHADER

layout (location = 0) in vec3 aViewPosition;
layout (location = 1) in vec2 aUV;

uniform mat4 uProjection;
uniform mat4 uEntryProjection;

void main() {
	vUV = aUV;
	vec4 viewPosition = vec4(aViewPosition, 1.0);
	vec4 clipPosition = uProjection * viewPosition;
	gl_ClipDistance[0] = clipPosition.z + clipPosition.w;
	gl_ClipDistance[1] = clipPosition.w - clipPosition.z;
	gl_Position = uEntryProjection * viewPosition;
}

#endif

#ifdef FRAGMENT_SHADER

uniform sampler2D uTexture;
uniform usampler2D uPaintRank;
uniform bool uPaintRankTest;
uniform uint uEntryRank;

out vec4 FragColor;

void main() {
	if (uPaintRankTest && texelFetch(uPaintRank, ivec2(gl_FragCoord.xy), 0).r > uEntryRank) {
		discard;
	}

	// Palette index 0 decoded to alpha 0; the original's span routine skips that index rather
	// than blending it, so this is a test and not a blend.
	vec4 texel = texture(uTexture, vUV);
	if (texel.a < 0.5) {
		discard;
	}

	FragColor = vec4(texel.rgb, 1.0);
}

#endif
