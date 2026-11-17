#version 330 core

// The sky pass: a single full-viewport triangle built from gl_VertexID alone, so it needs no vertex
// buffer — only a bound (empty) VAO, which core-profile GL still requires.
//
// The fragment stage is the original's hzline backdrop evaluated per pixel: which band a pixel
// takes depends only on its distance above the horizon line, in the original's pixels. The line
// and the rule are Content.SkyGradient's (Place and BandAt); keep BandAt and bandAt below in step.
//
// Both stages live in this file, selected by VERTEX_SHADER / FRAGMENT_SHADER, which
// ShaderProgram.Load defines when it compiles each one. See Gl/ShaderSource.

#ifdef VERTEX_SHADER

void main() {
	// (-1,-1), (3,-1), (-1,3): one oversized triangle covering the whole clip rect.
	vec2 corner = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
	gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}

#endif

#ifdef FRAGMENT_SHADER

// Horizon colour first, zenith colour last. Size matches SkyGradient.MaxBands.
uniform vec3 uBands[32];
uniform int uBandCount;
uniform float uBandHeight;
uniform float uGap;
uniform vec2 uLineMid;
uniform vec2 uLineUp;
uniform float uScale;
uniform int uRolled;
uniform float uCosRoll;

out vec4 FragColor;

int bandAt(float d) {
	int last = uBandCount - 1;
	if (uRolled == 0) {
		return clamp(int(floor((d + 0.5 - uGap) / uBandHeight)) + 1, 0, last);
	}

	if (d < -uCosRoll) {
		return 0;
	}

	return clamp(int(ceil((d - (uGap - uBandHeight) * uCosRoll * uCosRoll) / uBandHeight)), 0, last);
}

void main() {
	float d = dot(gl_FragCoord.xy - uLineMid, uLineUp) / uScale;
	FragColor = vec4(uBands[bandAt(d)], 1.0);
}

#endif
