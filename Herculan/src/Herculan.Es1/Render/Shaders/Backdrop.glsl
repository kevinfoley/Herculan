#version 330 core

// ES1's backdrop: the sky above the horizon line, the ground below it, textured or banded, and the skyline standing
// on the line, evaluated per pixel. Each is ES1DBSIM's rule for the pixel's place in the original's own pixels
// (docs/es1/rendering/ground-and-sky.md), run at the window's resolution rather than on the original's 320-column
// grid. Es1Backdrop sets the uniforms; Es1Horizon, Es1TexturedGround and Es1Skyline own the tables.
//
// Coordinates: p is the pixel's offset from the projection centre in the original's pixels, x right and y down; a
// pixel's integer offset is floor(p), so pixel centres sit at .5. rf is p turned into the roll-free frame: the screen
// is the roll-free image turned by the roll, (1, 0) going to (cos r, -sin r) and (0, 1) to (sin r, cos r).

#ifdef VERTEX_SHADER

void main() {
	vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
	gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}

#endif

#ifdef FRAGMENT_SHADER

uniform vec2 uCentre;
uniform float uScale;
uniform int uRolled;
uniform float uCosRoll;
uniform float uSinRoll;

// The sky's colours outward from the line, the last the zenith's. Size matches Es1Horizon.MaxBands.
uniform vec3 uSky[32];
uniform int uSkyCount;
uniform float uSkyBandHeight;
uniform float uGap;

// Level: the sky line's row, below the centre. Rolled: the line's distance below the centre along the rolled
// vertical, and the first band's far edge from it, h sin^2 r + t cos r.
uniform float uLineRow;
uniform float uLineDown;
uniform float uFirstSkyEdge;

// The banded ground's colours down from the line, the last filling to the bottom of the view.
uniform vec3 uGroundBands[32];
uniform int uGroundBandCount;
uniform float uGroundBandHeight;
uniform float uFirstGroundEdge;

// The textured ground. uGroundStart is the roll-free row its first bitmap row lies on; uRayShift is how far the row
// table GndTex_Draw reads was built from a different first row (Es1TexturedGround.RowTableFirst).
uniform int uTextured;
uniform sampler2D uGroundTexture;
uniform sampler2D uFadeColours;
uniform float uGroundStart;
uniform float uRayShift;
uniform int uFullFadeRows;
uniform float uDistances[14];
uniform int uDistanceCount;
uniform float uFocal;
uniform float uHeight;
uniform float uCosPitch;
uniform float uSinPitch;
uniform float uCosHeading;
uniform float uSinHeading;
uniform vec2 uGroundOrigin;
uniform float uColumnOrigin;
uniform float uColumnStep;

// The skyline. uSkylineStart is X - 160 along the line from the moved line's midpoint, whose roll-free x is
// uSkylineMidX; uSkylineLine is that line's row (level) or distance below the centre (rolled).
uniform int uSkylineFrames;
uniform sampler2D uSkyline;
uniform float uSkylineWidth;
uniform float uSkylineSlot;
uniform float uSkylineRows[16];
uniform float uSkylineStart;
uniform float uSkylineMidX;
uniform float uSkylineLine;
uniform float uLineOffset;

out vec4 FragColor;

vec3 skyColour(vec2 p, vec2 rf) {
	int band;
	if (uRolled == 0) {
		// Hzline_FillSky's rects: band i (i >= 1) spans rows t + (i-1)h to t + ih - 1 above the line's row.
		float above = uLineRow - floor(p.y);
		band = above < uGap ? 0 : 1 + int(floor((above - uGap) / uSkyBandHeight));
	} else {
		// Its quads: the first from the line to h sin^2 r + t cos r, each next one h further.
		float above = uLineDown - rf.y;
		band = above < uFirstSkyEdge ? 0 : 1 + int(floor((above - uFirstSkyEdge) / uSkyBandHeight));
	}

	return uSky[clamp(band, 0, uSkyCount - 1)];
}

// Hzline_FillGround's bands, from the line moved one row down. Returns false above them.
bool bandedGround(vec2 p, vec2 rf, out vec3 colour) {
	int band;
	if (uRolled == 0) {
		float below = floor(p.y) - (uLineRow + 1.0);
		if (below < 0.0) {
			return false;
		}

		band = below < uGap ? 0 : 1 + int(floor((below - uGap) / uGroundBandHeight));
	} else {
		float below = rf.y - (uLineDown + uCosRoll);
		if (below < 0.0) {
			return false;
		}

		band = below < uFirstGroundEdge ? 0 : 1 + int(floor((below - uFirstGroundEdge) / uGroundBandHeight));
	}

	colour = uGroundBands[clamp(band, 0, uGroundBandCount - 1)];
	return true;
}

// GndTex_Draw's row, and GndTex_DrawSpan's texel, for this pixel. Returns false above the ground's first row.
bool texturedGround(vec2 rf, out vec3 colour) {
	int row = int(floor(rf.y - uGroundStart));
	if (row < 0) {
		return false;
	}

	// GndTex_BuildRowTable: the ray through the row, levelled, meets z = 0 at forward distance D; s1 is that
	// point's depth in the view. Bitmap row i's ray is (-(first + i + 1), f).
	float up = -(rf.y + 0.5 + uRayShift);
	float q0 = up * uCosPitch + uFocal * uSinPitch;
	float q1 = -up * uSinPitch + uFocal * uCosPitch;
	float D = q0 != 0.0 ? (-uHeight * q1) / q0 : 10000000.0;
	float s1 = -uHeight * uSinPitch + D * uCosPitch;

	// The span steps 204/256 of an even column's lateral offset a pixel from the view's left edge, bitmap column 0.
	float column = rf.x - 0.5 + uColumnOrigin;
	float lateral = (-160.0 + column * uColumnStep) * s1 / uFocal;
	vec2 ground = vec2(
		lateral * uCosHeading - D * uSinHeading,
		lateral * uSinHeading + D * uCosHeading) + uGroundOrigin;
	ivec2 texelAt = ivec2(floor(ground / 256.0)) & 255;
	int texel = int(texelFetch(uGroundTexture, texelAt, 0).r * 255.0 + 0.5);

	int table = 0;
	if (row < uFullFadeRows) {
		table = 14;
	} else {
		while (table < uDistanceCount && D >= uDistances[table]) {
			table++;
		}
	}

	// Retail data keeps every texel in 240-255; one outside would index past the table in the original.
	colour = texelFetch(uFadeColours, ivec2(clamp(texel - 240, 0, 15), table), 0).rgb;
	return true;
}

// Hzline_BlitHorizonStrip's frames, standing on the line with their bottom row at its row plus the line offset.
bool skyline(vec2 p, vec2 rf, out vec3 colour) {
	if (uSkylineFrames == 0) {
		return false;
	}

	float along = rf.x - uSkylineMidX - uSkylineStart;
	float above = uRolled == 0 ? uSkylineLine - floor(p.y) : uSkylineLine - rf.y;
	float panorama = uSkylineWidth * float(uSkylineFrames);
	float offset = mod(floor(along), panorama);
	int frame = int(floor(offset / uSkylineWidth));
	float column = offset - float(frame) * uSkylineWidth;
	float frameRow = floor(uSkylineRows[frame] - uLineOffset - above);
	if (frameRow < 0.0 || frameRow >= uSkylineRows[frame]) {
		return false;
	}

	vec4 texel = texelFetch(uSkyline, ivec2(int(column), frame * int(uSkylineSlot) + int(frameRow)), 0);
	colour = texel.rgb;
	return texel.a > 0.5;
}

void main() {
	vec2 p = vec2(gl_FragCoord.x - uCentre.x, uCentre.y - gl_FragCoord.y) / uScale;
	vec2 rf = vec2(p.x * uCosRoll - p.y * uSinRoll, p.x * uSinRoll + p.y * uCosRoll);

	vec3 colour;
	bool ground = uTextured != 0 ? texturedGround(rf, colour) : bandedGround(p, rf, colour);
	if (!ground) {
		colour = skyColour(p, rf);
	}

	vec3 strip;
	if (skyline(p, rf, strip)) {
		colour = strip;
	}

	FragColor = vec4(colour, 1.0);
}

#endif
