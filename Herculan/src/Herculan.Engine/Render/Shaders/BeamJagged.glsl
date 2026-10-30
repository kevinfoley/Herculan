#version 330 core

// The ELF pass: the chain of world-space quads FUN_0040b804 builds for subtype ids 1 and 7, painted
// as a flat colour. There is deliberately no texture and no lighting here — the original submits
// these through its own point-list path with the rasterizer's fill brush set to BEAM.DAT's colour
// index and mode 0, which is a flat fill of that palette entry. See BeamRenderer.
//
// Nothing is expanded or turned to face the viewer either: unlike a straight beam's quad, the
// ribbon's width is baked into the geometry as a z offset, so the vertices arrive final.
//
// uEntryProjection places the vertex and uProjection clips it, as in Scene.glsl, and the fragment stage has the same
// paint-rank test, for WorldDrawPass's painter's order; the per-pixel depth draw sets both projections alike and the test
// off.
//
// Both stages live in this file, selected by VERTEX_SHADER / FRAGMENT_SHADER, which
// ShaderProgram.Load defines when it compiles each one. See Gl/ShaderSource.

#ifdef VERTEX_SHADER

layout (location = 0) in vec3 aPosition;

uniform mat4 uView;
uniform mat4 uProjection;
uniform mat4 uEntryProjection;

void main() {
	vec4 viewPosition = uView * vec4(aPosition, 1.0);
	vec4 clipPosition = uProjection * viewPosition;
	gl_ClipDistance[0] = clipPosition.z + clipPosition.w;
	gl_ClipDistance[1] = clipPosition.w - clipPosition.z;
	gl_Position = uEntryProjection * viewPosition;
}

#endif

#ifdef FRAGMENT_SHADER

uniform vec3 uColor;
uniform usampler2D uPaintRank;
uniform bool uPaintRankTest;
uniform uint uEntryRank;

out vec4 FragColor;

void main() {
	if (uPaintRankTest && texelFetch(uPaintRank, ivec2(gl_FragCoord.xy), 0).r > uEntryRank) {
		discard;
	}

	FragColor = vec4(uColor, 1.0);
}

#endif
