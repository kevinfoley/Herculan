#version 330 core

// One of ES1's cloud panels, filled as VGA8_FillBitmapPolygon (ES1DBSIM 000534ac) fills brush mode 5 in clip mode 1
// (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7): along each screen column, the texture
// point is interpolated linearly along the two edges the column crosses, by the column, and then linearly down the
// column between them, with no perspective; the pixel takes the nearest texel, and palette index 0 is left unpainted.
// Es1CloudLayer sets the polygon, already cut at the near plane and projected, in window coordinates.

// Size matches Es1CloudLayer.MaxPoints.
uniform vec2 uPoints[8];
uniform int uCount;

#ifdef VERTEX_SHADER

uniform vec2 uViewportOrigin;
uniform vec2 uViewportSize;

void main() {
	vec2 point = uPoints[gl_VertexID];
	gl_Position = vec4((point - uViewportOrigin) / uViewportSize * 2.0 - 1.0, 0.0, 1.0);
}

#endif

#ifdef FRAGMENT_SHADER

uniform vec2 uTexels[8];
uniform sampler2D uFrame;

out vec4 FragColor;

void main() {
	float x = gl_FragCoord.x;
	float lowY = 0.0;
	float highY = 0.0;
	vec2 lowTexel = vec2(0.0);
	vec2 highTexel = vec2(0.0);
	int crossings = 0;

	// The edges the column crosses, each stepped by the column; an edge along the column is passed over.
	for (int i = 0; i < uCount; i++) {
		int j = i + 1 < uCount ? i + 1 : 0;
		vec2 a = uPoints[i];
		vec2 b = uPoints[j];
		if (a.x == b.x || x < min(a.x, b.x) || x > max(a.x, b.x)) {
			continue;
		}

		float t = (x - a.x) / (b.x - a.x);
		float y = mix(a.y, b.y, t);
		vec2 texel = mix(uTexels[i], uTexels[j], t);
		if (crossings == 0 || y < lowY) {
			lowY = y;
			lowTexel = texel;
		}

		if (crossings == 0 || y > highY) {
			highY = y;
			highTexel = texel;
		}

		crossings++;
	}

	if (crossings < 2) {
		discard;
	}

	vec2 texel = highY > lowY ? mix(lowTexel, highTexel, (gl_FragCoord.y - lowY) / (highY - lowY)) : lowTexel;
	ivec2 size = textureSize(uFrame, 0);
	ivec2 at = clamp(ivec2(floor(texel + 0.5)), ivec2(0), size - 1);
	vec4 colour = texelFetch(uFrame, at, 0);
	if (colour.a < 0.5) {
		discard;
	}

	FragColor = vec4(colour.rgb, 1.0);
}

#endif
