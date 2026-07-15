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

// Size of the source texture in pixels. Needed to step by exact texels when dilating for the frame.
uniform vec2 res;

// Frame thickness, in pixels. 0 disables the frame.
uniform float border_width;
uniform vec4 border_color;

// Vertical gradient across the glyph body, top -> bottom.
uniform vec4 color_top;
uniform vec4 color_bottom;

// Animated highlight sweeping down the text. Set highlight_strength to 0 to switch it off.
uniform float time;
uniform float highlight_strength;

const float HIGHLIGHT_WIDTH = 0.22;
const int MAX_STEPS = 8;

// The text is a mask: only its alpha matters. The frame is the mask dilated by border_width, so we take the
// largest alpha found within that radius. Sampling a ring per step (rather than a full square) keeps this at
// MAX_STEPS * 8 taps instead of (2*MAX_STEPS+1)^2.
float dilated_alpha(vec2 uv_in, float radius_px)
{
    if (radius_px <= 0.0)
        return 0.0;

    vec2 texel = 1.0 / res;
    float steps = min(float(MAX_STEPS), radius_px);
    float found = 0.0;

    for (int i = 1; i <= MAX_STEPS; i++)
    {
        if (float(i) > steps)
            break;

        float r = radius_px * (float(i) / steps);

        for (int k = 0; k < 8; k++)
        {
            float angle = 6.2831853 * float(k) / 8.0;
            vec2 offset = vec2(cos(angle), sin(angle)) * r * texel;
            found = max(found, texture(texture0, uv_in + offset).a);
        }
    }

    return found;
}

void main()
{
    _fragColorOut = vec4(0.0);
    float body = texture(texture0, fragTexCoord).a;
    float frame = dilated_alpha(fragTexCoord, border_width);

    // Gradient down the glyph, plus a highlight band travelling through it.
    vec4 fill = mix(color_top, color_bottom, clamp(fragTexCoord.y, 0.0, 1.0));

    float sweep = fract(time * 0.5);
    float distance_to_band = abs(fract(fragTexCoord.y - sweep + 0.5) - 0.5);
    float highlight = smoothstep(HIGHLIGHT_WIDTH, 0.0, distance_to_band) * highlight_strength;
    fill.rgb = clamp(fill.rgb + highlight, 0.0, 1.0);

    // Frame underneath, body on top; alpha is whichever covers this pixel.
    vec4 result = mix(border_color, fill, body);
    result.a = max(body, frame) * mix(border_color.a, fill.a, body);

    // NOTE: deliberately NOT multiplied by colDiffuse. Raylib does not set colDiffuse for custom shaders —
    // it stays zero — so anything multiplied by it comes out invisible. Every other shader in this game
    // declares colDiffuse and ignores it for exactly that reason.
    _fragColorOut = result;
}
