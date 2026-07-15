#version 330

// Makes a drawn texture (the title logo) read like an old neon/glowing sign: a soft emissive halo bleeds
// out of the art, a warm colour pulses through it, and the whole thing flickers — a fast shimmer plus an
// occasional brown-out, the way a tired gas sign does. Driven by a single `time` uniform (seconds);
// `resolution` is the sampled texture's pixel size, used to size the halo taps.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 resolution;
uniform float time;

float hash(float n) { return fract(sin(n) * 43758.5453123); }

void main()
{
    vec4 base = texture(texture0, fragTexCoord) * fragColor;

    // 2-D halo (bloom): accumulate neighbouring alpha AND colour, weighted by distance, so light bleeds
    // outward from the glyph edges in every direction. The colour is kept so the halo glows in a blend of the
    // sign's own hue and a warm tube colour, which reads far stronger than a flat tint over bright art.
    vec2 texel = 1.0 / resolution;
    const int R = 4;
    float glow = 0.0;
    float wsum = 0.0;
    vec3 glowColor = vec3(0.0);
    for (int x = -R; x <= R; x++)
        for (int y = -R; y <= R; y++)
        {
            vec2 o = vec2(float(x), float(y));
            float w = 1.0 - length(o) / (float(R) + 1.0);
            if (w <= 0.0) continue;
            vec4 s = texture(texture0, fragTexCoord + o * texel * 3.0);
            glow += s.a * w;
            glowColor += s.rgb * s.a * w;
            wsum += w;
        }
    glow /= max(wsum, 0.0001);
    glowColor /= max(wsum, 0.0001);

    // Flicker. A fast subtle shimmer keeps it alive; a slow random gate dips the sign now and then. The core
    // stays readable (never fully dark) while the halo flickers harder, like the tubes struggling.
    float shimmer = 0.90 + 0.10 * sin(time * 34.0 + sin(time * 7.0));
    float tick = time * 4.0;
    float level = mix(hash(floor(tick)), hash(floor(tick) + 1.0), smoothstep(0.0, 1.0, fract(tick)));
    float gate = mix(0.6, 1.0, smoothstep(0.15, 0.6, level));   // occasional brown-out, never black
    float coreFlicker = mix(0.85, 1.0, 0.5 + 0.5 * sin(time * 22.0));
    float haloFlicker = shimmer * gate;

    // Warm sign colour the halo glows through, hue drifting slowly. The emissive halo blends this warm tube
    // colour with the sign's own bloomed colour so it glows convincingly whatever the art's palette is.
    vec3 neon = mix(vec3(1.0, 0.30, 0.10), vec3(1.0, 0.72, 0.22), 0.5 + 0.5 * sin(time * 1.3));
    vec3 haloColor = mix(glowColor, neon, 0.55);

    vec3 col = base.rgb * coreFlicker;                     // the lit glyphs
    col += base.rgb * base.a * 0.45 * coreFlicker;         // inner core boost
    col += haloColor * glow * haloFlicker * 2.6;           // emissive outer halo

    float alpha = clamp(base.a + glow * haloFlicker * 1.15, 0.0, 1.0);
    gl_FragColor = vec4(col, alpha);
}
