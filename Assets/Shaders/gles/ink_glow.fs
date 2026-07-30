#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Makes the painted marks in an image glow and bloom, like the neon_sign treatment on the title logo but
// keyed on INK instead of alpha. The logos are transparent sprites, so neon_sign can bloom their alpha
// channel; the menu backdrop is one fully OPAQUE image (paper-white with dark and coloured paint on top),
// where alpha carries no shape at all. So "ink" is derived from the pixel itself: anything dark or
// saturated counts as paint, blank paper does not.
//
// Two things then happen, and they are deliberately different, because a plain additive bloom fails on art
// like this in both directions:
//   * SPILL - light thrown onto the paper AROUND the paint (blurred ink minus this pixel's own ink). Paper
//     is already near white, so ADDING light there would only clip to white and read as nothing; the spill
//     tints it toward the glow colour instead, the way coloured light actually falls on a white wall. The
//     tint colour is normalised to full brightness first - multiplying paper by a dim tube colour would
//     darken it into a dirty wash rather than lighting it. Because the pixel's own ink is subtracted, the
//     inside of a solid shape stays solid: the black radish casts blue light without turning into a
//     glowing blue silhouette.
//   * BLOOM - vivid paint (saturated, not merely dark) additionally lights up from within. Keyed on
//     saturation alone so dark paint stays a silhouette while the blues burn.
//
// The halo is gathered on three rings rather than a full square kernel: this runs full-screen on the
// backdrop, and 24 taps buys a smooth bloom for a third of the cost of a 9x9 box.
//
// Uniforms:
//   resolution  sampled texture's pixel size (halo taps are sized in its texels)
//   time        seconds; drives a slow breathing pulse and a drifting light band
//   glow_color  rgb = tube colour the light glows in, a = overall glow strength
//   radius      halo reach, in texels of the sampled texture

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 resolution;
uniform float time;
uniform vec4 glow_color;
uniform float radius;

const float PAPER = 0.12;   // below this much ink, a pixel counts as blank paper and never glows

float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }
float saturation(vec3 c) { return max(max(c.r, c.g), c.b) - min(min(c.r, c.g), c.b); }
float overPaper(float v) { return clamp(v - PAPER, 0.0, 1.0) / (1.0 - PAPER); }

/// How strongly a pixel reads as paint rather than blank paper: dark OR saturated. Scaled by alpha so the
/// transparent margin around a sprite (zeroed, i.e. black) is not mistaken for black ink.
float inkness(vec4 s) { return s.a * overPaper(max(1.0 - luma(s.rgb), saturation(s.rgb))); }

void main()
{
    _fragColorOut = vec4(0.0);
    vec4 base = texture(texture0, fragTexCoord) * fragColor;

    // Ring bloom: inner rings weigh most, so the light is dense against the paint and fades outward.
    vec2 texel = 1.0 / resolution;
    float glow = 0.0;
    float wsum = 0.0;
    vec3 glowColor = vec3(0.0);
    for (int ring = 1; ring <= 3; ring++)
    {
        float dist = radius * float(ring) / 3.0;
        float w = 1.0 / float(ring);
        for (int i = 0; i < 8; i++)
        {
            // Rings are rotated against each other so the taps do not line up into 8 visible spokes.
            float a = 6.2831853 * float(i) / 8.0 + float(ring) * 0.39;
            vec4 s = texture(texture0, fragTexCoord + vec2(cos(a), sin(a)) * dist * texel);
            float k = inkness(s);
            glow += k * w;
            glowColor += s.rgb * k * w;
            wsum += w;
        }
    }
    glowColor /= max(glow, 0.0001);   // weighted mean colour of the ink that lit this pixel
    glow /= wsum;

    // Breathing, plus a band of light drifting across the image on a diagonal - enough motion to read as
    // live light, without the harsh brown-out flicker the title's tired neon tubes get.
    float breathe = 0.82 + 0.18 * sin(time * 1.15);
    float travel = 0.86 + 0.14 * sin((fragTexCoord.x + fragTexCoord.y) * 3.4 - time * 0.7);
    float pulse = breathe * travel;

    // Glow in the ink's own hue at full brightness, blended toward the tube colour. Near-black ink has no
    // hue to lift (the floor keeps the division tame), so it glows in the tube colour alone.
    vec3 inkHue = glowColor / max(max(glowColor.r, max(glowColor.g, glowColor.b)), 0.35);
    vec3 tube = glow_color.rgb * (0.9 + 0.1 * sin(time * 1.7));
    vec3 halo = mix(inkHue, tube, 0.5);
    halo /= max(max(halo.r, max(halo.g, halo.b)), 0.001);   // full brightness, so lighting never darkens

    // fragColor.a is the screen's fade-in tint: the glow has to fade in with the art, not hang above it.
    float ink = inkness(base);
    float spill = clamp((glow - ink) * glow_color.a * pulse * fragColor.a, 0.0, 1.0);

    vec3 lit = base.rgb * halo + halo * 0.35;   // the surface under coloured light, plus the light itself
    vec3 col = mix(base.rgb, lit, spill);

    float vivid = base.a * overPaper(saturation(base.rgb));
    col += base.rgb * vivid * 0.7 * pulse * fragColor.a;                 // vivid paint burns
    col = mix(col, halo, vivid * 0.15 * pulse * fragColor.a);            // and pulls toward the glow hue

    float alpha = clamp(base.a + spill * 0.9, 0.0, 1.0);
    _fragColorOut = vec4(col, alpha);
}
