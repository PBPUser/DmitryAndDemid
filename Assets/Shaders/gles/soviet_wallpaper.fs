#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// A floral wallpaper pattern creeping constantly up the title screen, glowing light blue to purple, but only
// where the backdrop behind it is DARK — so it reads as damp glowing wallpaper showing through the black
// shapes of the painting, and never touches the white paper.
//
// Three things worth knowing about the art and how they shape this shader:
//
//   * soviet-wallpaper.png is white RGB with the ornament in its ALPHA channel, so the pattern signal is
//     texture0.a, not its luminance. Everything below keys on alpha.
//   * The tile is NOT seamless - its opposite edges differ as much as two random interior rows. A plain
//     fract() wrap would therefore drag a visible seam down the screen every time it scrolled a tile length.
//     wallpaper() cross-fades two copies half a tile apart, so whichever copy is near its seam is faded out
//     and the other (mid-tile) one carries through. Motion stays continuously upward - a mirrored ping-pong
//     wrap would also hide the seam, but the pattern would visibly reverse direction every other tile.
//   * Only the vertical axis wraps: the caller keeps tiling.x at or below one tile per screen width, so u
//     never crosses a seam at all.
//
// Uniforms:
//   resolution        wallpaper texture's pixel size (bloom taps are sized in its texels)
//   time              seconds; scrolls the pattern and drives the colour and brightness pulses
//   tiling            how many tiles span the screen, per axis (x is expected to be at most 1)
//   scroll            upward speed, in tiles per second
//   strength          overall opacity of the effect
//   color_a, color_b  the two colours the glow pulses between (rgb; a is unused)
//   backdropTexture   the backdrop this is laid over; its DARK areas are where the wallpaper shows

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform sampler2D backdropTexture;
uniform vec4 colDiffuse;
uniform vec2 resolution;
uniform vec2 tiling;
uniform float time;
uniform float scroll;
uniform float strength;
uniform vec4 color_a;
uniform vec4 color_b;

float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

/// The ornament's alpha at a point in tile space, seam-free across the vertical wrap (see the note above).
float wallpaper(vec2 p)
{
    float va = fract(p.y);
    float vb = fract(p.y + 0.5);
    // 0 in the middle of copy A, ramping to 1 as A approaches its seam (where B is safely mid-tile).
    float w = smoothstep(0.32, 0.5, abs(va - 0.5));
    return mix(texture(texture0, vec2(p.x, va)).a,
               texture(texture0, vec2(p.x, vb)).a, w);
}

void main()
{
    _fragColorOut = vec4(0.0);
    // Scrolling up: sampling further DOWN the texture over time moves the pattern up the screen, because v
    // grows downward.
    vec2 p = vec2(fragTexCoord.x * tiling.x, fragTexCoord.y * tiling.y + time * scroll);
    float pattern = wallpaper(p);

    // Bloom the ornament so it glows past its own outline. Two rings, six taps each - the pattern is soft and
    // busy, so a wide sparse gather is plenty and this runs full-screen.
    vec2 texel = 1.0 / resolution;
    float glow = 0.0;
    float wsum = 0.0;
    for (int ring = 1; ring <= 2; ring++)
    {
        float dist = 5.0 * float(ring);
        float w = 1.0 / float(ring);
        for (int i = 0; i < 6; i++)
        {
            float a = 6.2831853 * float(i) / 6.0 + float(ring) * 0.52;
            glow += wallpaper(p + vec2(cos(a), sin(a)) * dist * texel) * w;
            wsum += w;
        }
    }
    glow /= wsum;

    // Only over the backdrop's dark paint. The threshold sits above its mid blues, so this picks out the
    // near-black shapes and leaves the blue and the paper alone.
    float dark = 1.0 - luma(texture(backdropTexture, fragTexCoord).rgb);
    float mask = smoothstep(0.55, 0.85, dark);

    // Colour drifts between the two tints along the screen and over time; brightness pulses separately, on a
    // different period, so the two never lock into one obvious beat.
    float blend = 0.5 + 0.5 * sin(time * 0.8 - fragTexCoord.y * 2.5);
    vec3 tint = mix(color_a.rgb, color_b.rgb, blend);
    float pulse = 0.78 + 0.22 * sin(time * 1.9 + fragTexCoord.y * 1.5);

    // The caller keeps the two tints deep enough (low red, blue at 1) that this overdrive brightens them
    // without washing to white - only the brightest flower cores clip, which is what a bloom should do.
    vec3 col = tint * (0.5 + pattern * 0.5 + glow * 0.9);
    float amount = clamp(pattern * 0.9 + glow * 1.5, 0.0, 1.0) * mask * pulse * strength * fragColor.a;
    _fragColorOut = vec4(col, amount);
}
