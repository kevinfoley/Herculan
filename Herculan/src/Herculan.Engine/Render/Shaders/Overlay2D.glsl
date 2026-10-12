#version 330 core

// The 2D overlay pass: the cockpit-art quad and the HUD sprite quads over it, in pixel space. See
// Overlay2DRenderer for why the overlay has a program of its own rather than going through the
// scene's lit-3D vertex layout.
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
VARYING vec3 vColor;
VARYING float vTextured;

#ifdef VERTEX_SHADER

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aUV;
layout (location = 2) in vec3 aColor;
layout (location = 3) in float aTextured;

uniform vec2 uViewportSize;

// Everything shifts by 1/32 of a framebuffer pixel, up and left. At a fractional scale (1080 lines
// is 2.25 pixels per device pixel) some device-pixel edges fall exactly on a pixel centre, where the
// rasterizer settles a flat fill's edge and the sampler a texel boundary each its own way, so a
// paper-doll tint misses the doll pixel under it by one column. Off the centres, the two agree.
const vec2 kTieBreak = vec2(1.0 / 32.0);

void main() {
	// aPosition is in pixel space, origin top-left, +Y down (PixelPoint's own convention) —
	// flip Y and rescale to NDC's -1..1, +Y up.
	vec2 position = aPosition - kTieBreak;
	vec2 ndc = vec2(
		position.x / uViewportSize.x * 2.0 - 1.0,
		1.0 - position.y / uViewportSize.y * 2.0);
	gl_Position = vec4(ndc, 0.0, 1.0);
	vUV = aUV;
	vColor = aColor;
	vTextured = aTextured;
}

#endif

#ifdef FRAGMENT_SHADER

uniform sampler2D uTexture;

out vec4 FragColor;

void main() {
	vec4 texColor = texture(uTexture, vUV);
	vec3 rgb = mix(vColor, texColor.rgb, vTextured);
	// Everything drawn here is textured and carries its own alpha: the canopy quad is 0 over
	// the flood-filled 3D-viewport hole and 255 over painted art (see CockpitArt), and a HUD
	// sprite is 0 wherever its source palette index was 0 (see HudSpriteSheet). The untextured
	// path stays in the shader because Overlay2DVertex still offers a flat-colour vertex.
	float alpha = mix(1.0, texColor.a, vTextured);
	FragColor = vec4(rgb, alpha);
}

#endif
