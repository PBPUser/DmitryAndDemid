#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Apple-style "Liquid Glass": a CLEAR pane — no frost, no caustics, no wobble — whose background stays
// sharp. The glass read comes from a lens-bent rim: content just outside the pane is pulled around the
// curved edge (with chromatic dispersion fringing), lit by a specular streak from the top-left, an
// inner shadow on the far side, a hairline border, and a soft drop shadow outside the pane.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;   // a full-screen capture of whatever is drawn BEHIND this panel this frame
uniform vec2 res;             // screen resolution, px
uniform vec2 position;        // panel top-left, px
uniform vec2 size;            // panel size, px
uniform float radius;         // corner radius, px
uniform float time;           // kept so the uniform interface is unchanged; the Apple look does not animate
uniform vec4 tint;            // glass tint colour + overall opacity

// Rounded-box SDF (Inigo Quilez).
float sdRoundBox(vec2 p, vec2 b, float r)
{
    vec2 q = abs(p) - b + r;
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}

void main()
{
    _fragColorOut = vec4(0.0);
    vec2 pc = fragTexCoord * res;
    vec2 center = position + size * 0.5;
    vec2 p = pc - center;
    vec2 b = max(size * 0.5 - vec2(radius), vec2(0.0));
    float d = sdRoundBox(p, b, radius);

    // Past the drop shadow's reach there is nothing to draw.
    if (d > 24.0)
        discard;

    // 0 at and beyond the rim .. 1 deep inside: how far into the glass this pixel sits.
    float edge = clamp(-d / 22.0, 0.0, 1.0);
    // Outward-pointing direction of the pane's edge — the SDF's gradient, valid inside and out.
    vec2 grad = vec2(dFdx(d), dFdy(d));
    vec2 dir = grad / max(length(grad), 0.0001);

    // --- Lens refraction at the rim ----------------------------------------------------------
    // The curved rim acts as a lens: sample points are pushed OUTWARD, strongest right at the rim
    // and easing off inward, so content from just outside the pane wraps around the bend.
    float bend = pow(edge, 2.5) * 16.0;
    vec2 offset = dir * bend / res;
    // Dispersion: each channel bends by a slightly different amount — the coloured fringing at the rim.
    vec3 bg;
    bg.r = texture(texture0, fragTexCoord + offset * 1.08).r;
    bg.g = texture(texture0, fragTexCoord + offset).g;
    bg.b = texture(texture0, fragTexCoord + offset * 0.92).b;

    // The pane's body: mostly the clear, sharp background with a whisper of tint.
    vec3 col = mix(bg, tint.rgb, 0.08);

    // --- Lighting ----------------------------------------------------------------------------
    vec2 light = normalize(vec2(-0.55, -0.84));   // from the top-left (screen space, y down)
    float facing = dot(dir, light);
    // Specular streak on the rim that faces the light; inner shadow on the far side.
    float spec = pow(max(facing, 0.0), 3.0) * pow(edge, 2.0);
    float shade = pow(max(-facing, 0.0), 2.0) * pow(edge, 1.5);
    col += spec * 0.55;
    col *= 1.0 - shade * 0.28;
    // Fresnel sheen: the whole rim brightens a touch at grazing angles.
    col += pow(edge, 3.0) * 0.12;
    // The hairline border right on the boundary, brighter on the lit side.
    float hairline = 1.0 - smoothstep(0.0, 1.2, abs(d));
    col += hairline * 0.35 * (0.5 + 0.5 * max(facing, 0.0));

    col = clamp(col, 0.0, 1.0);

    // Coverage: 1 inside, 0 outside, feathered across the boundary for anti-aliasing.
    float insideMask = 1.0 - smoothstep(-1.0, 0.5, d);
    // Soft drop shadow outside the pane: black at partial alpha over what is already there.
    float shadowA = (1.0 - smoothstep(0.0, 24.0, d)) * 0.30;
    // The glass itself is nearly opaque — its CLARITY comes from col being mostly background, not alpha.
    float glassA = mix(0.80, 1.0, max(spec + hairline, pow(edge, 3.0) * 0.5)) * tint.a;

    _fragColorOut = vec4(mix(vec3(0.0), col, insideMask), mix(shadowA, glassA, insideMask));
}
