#version 330 core

// The terrain's paint rank per pixel: which cell's ground is showing there, as the rank that cell
// takes in the original's cell walk. SceneRenderer draws the terrain mesh through this into an
// unsigned-integer target, and a ground shape's fragments are then kept only where the ground
// under them was painted no later than the shape's own cell. See TerrainPaintRankBuffer and
// docs/retail/formats/terrain-drawing.md.

#if defined(VERTEX_SHADER)
	#define VARYING out
#else
	#define VARYING in
#endif

// Render X and Z, which are world X and world -Y (WorldScale.ToRender).
VARYING vec2 vRenderXZ;

#ifdef VERTEX_SHADER

layout (location = 0) in vec3 aPosition;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main() {
	vec4 worldPosition = uModel * vec4(aPosition, 1.0);
	vRenderXZ = worldPosition.xz;
	gl_Position = uProjection * (uView * worldPosition);
}

#endif

#ifdef FRAGMENT_SHADER

// One rank per grid cell, TerrainPaintOrder.Rank for this frame's walk.
uniform usampler2D uCellRanks;

// Render units per cell.
uniform float uCellSize;

layout (location = 0) out uint Rank;

void main() {
	ivec2 cell = ivec2(floor(vec2(vRenderXZ.x, -vRenderXZ.y) / uCellSize));
	ivec2 size = textureSize(uCellRanks, 0);
	Rank = texelFetch(uCellRanks, clamp(cell, ivec2(0), size - ivec2(1)), 0).r;
}

#endif
