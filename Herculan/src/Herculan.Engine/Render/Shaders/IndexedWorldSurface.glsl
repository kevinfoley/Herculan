#version 330 core

// IndexedWorldSurface's present: the monitor's window onto a software display's video memory. Each pixel of the
// window is a cell of the store, read from the start address in the store's own linear order, rows of uWidth cells
// wrapping at uCellCount; a cell holding a palette index shows that entry of the live palette, and a cell holding the
// world marker shows the world layer's pixels for that cell, drawn at uScale times the store's resolution. A cursor
// sprite, when shown, is drawn over the window in the same palette. The same program paints a rect of the cells into
// the world layer (uKeepWorld), the world cells left as the layer holds them.

#if defined(VERTEX_SHADER)
	#define VARYING out
#else
	#define VARYING in
#endif

// The window's position, 0 to 1 from its top-left corner.
VARYING vec2 vWindow;

#ifdef VERTEX_SHADER

void main() {
	// A strip of four corners from the vertex index alone, so no vertex buffer is needed.
	vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
	gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
	vWindow = vec2(corner.x, 1.0 - corner.y);
}

#endif

#ifdef FRAGMENT_SHADER

uniform usampler2D uCells;
uniform sampler2D uPalette;
uniform sampler2D uWorld;

// The cell the window's top-left pixel shows, as a linear address.
uniform int uStart;
// The window's size in cells.
uniform int uWindowWidth;
uniform int uWindowRows;
// The store's row length and its addressable cell count.
uniform int uWidth;
uniform int uCellCount;
// The world layer's scale and its height in its own pixels.
uniform int uScale;
uniform int uWorldHeight;
uniform uint uWorldCell;
// Set while the cells are painted into the world layer itself: a world cell then keeps the layer's pixel.
uniform int uKeepWorld;
// A sprite over the window, in the window's cells: its palette indices, its rect (x, y, width, height), whether it is
// shown, and whether index 0 is drawn or leaves the window's pixel.
uniform usampler2D uCursor;
uniform int uCursorX;
uniform int uCursorY;
uniform int uCursorWidth;
uniform int uCursorRows;
uniform int uCursorShown;
uniform int uCursorOpaque;
// Set to draw the sprite alone: every pixel it does not draw leaves the window's pixel.
uniform int uCursorOnly;

out vec4 FragColor;

void main() {
	ivec2 windowSize = ivec2(uWindowWidth, uWindowRows);
	vec2 position = vWindow * vec2(windowSize);
	ivec2 pixel = clamp(ivec2(floor(position)), ivec2(0), windowSize - 1);
	if (uCursorShown != 0) {
		ivec2 within = pixel - ivec2(uCursorX, uCursorY);
		if (all(greaterThanEqual(within, ivec2(0))) && all(lessThan(within, ivec2(uCursorWidth, uCursorRows)))) {
			uint index = texelFetch(uCursor, within, 0).r;
			if (uCursorOpaque != 0 || index != 0u) {
				FragColor = vec4(texelFetch(uPalette, ivec2(int(index & 0xffu), 0), 0).rgb, 1.0);
				return;
			}
		}
	}

	if (uCursorOnly != 0) {
		discard;
	}

	int address = (uStart + pixel.y * uWidth + pixel.x) % uCellCount;
	if (address < 0) {
		address += uCellCount;
	}

	ivec2 cell = ivec2(address % uWidth, address / uWidth);
	uint value = texelFetch(uCells, cell, 0).r;
	if (value == uWorldCell && uKeepWorld != 0) {
		discard;
	} else if (value == uWorldCell) {
		ivec2 within = clamp(ivec2(floor(fract(position) * float(uScale))), ivec2(0), ivec2(uScale - 1));
		ivec2 world = cell * uScale + within;
		// The world layer keeps GL's bottom-left origin; the store's rows run down.
		FragColor = vec4(texelFetch(uWorld, ivec2(world.x, uWorldHeight - 1 - world.y), 0).rgb, 1.0);
	} else {
		FragColor = vec4(texelFetch(uPalette, ivec2(int(value & 0xffu), 0), 0).rgb, 1.0);
	}
}

#endif
