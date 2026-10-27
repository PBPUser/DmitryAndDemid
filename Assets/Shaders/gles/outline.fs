#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float border_width;
uniform vec2 res;
const vec4 border_color = vec4(0.,0.,0.,1.);

// Clean, even text outline. The glyph's coverage is dilated over a DISC of radius `border_width` (taking the max
// neighbour alpha, weighted by a ~1px soft falloff at the rim), then the glyph is laid back on top. This replaces
// the old nearest-opaque-texel + "zoom" heuristic, which sampled unevenly and left the border lumpy and thick —
// especially on high-DPI screens where border_width is large. Empty texels are (0,0,0,0), so the dilated ring
// keeps the glyph texture's black RGB — i.e. a black outline — exactly as before; only its SHAPE is improved.
// This runs once when a line of text is baked to a texture, so the disc loop is affordable.
void main(){
    _fragColorOut = vec4(0.0);
	_fragColorOut = texture(texture0, fragTexCoord);
	vec2 texel = vec2(1.0) / res;
	// Cap the radius so a very high-DPI bake can't blow the sample count up unboundedly.
	float r = clamp(border_width, 1.0, 12.0);
	float outline = 0.0;
	for (float x = -r; x <= r; x += 1.0)
	for (float y = -r; y <= r; y += 1.0) {
		float d = length(vec2(x, y));
		if (d <= r + 0.5) {
			float a = texture(texture0, fragTexCoord + vec2(x, y) * texel).a;
			// Soft 1px rim so the outer edge stays antialiased instead of a hard step.
			outline = max(outline, a * clamp(r + 0.5 - d, 0.0, 1.0));
		}
	}
	_fragColorOut.a = max(_fragColorOut.a, outline);
}
