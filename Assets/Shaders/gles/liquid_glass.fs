#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;   // a full-screen capture of whatever is drawn BEHIND this panel this frame
uniform vec2 res;             // screen resolution, px
uniform vec2 position;        // panel top-left, px
uniform vec2 size;            // panel size, px
uniform float radius;         // corner radius, px
uniform float time;
uniform vec4 tint;            // glass tint colour + overall opacity

// Rounded-box SDF (Inigo Quilez).
float sdRoundBox(vec2 p, vec2 b, float r)
{
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

float fbm(vec2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * noise(p);
        p *= 2.02;
        a *= 0.5;
    }
    return v;
}

void main()
{
    vec2 pc = fragTexCoord * res;
    vec2 center = position + size * 0.5;
    vec2 p = pc - center;
    vec2 b = max(size * 0.5 - vec2(radius), 0.0);

    // Ripple the boundary itself with a slow-drifting noise so the pane reads as liquid, not a rigid card.
    float edgeNoise = fbm(pc * 0.03 + vec2(time * 0.12, -time * 0.09)) - 0.5;
    float d = sdRoundBox(p, b, radius) + edgeNoise * 3.0;

    // Outside the (wobbly) rounded rect: leave the pixel untouched (it's already correct on the backbuffer).
    if (d > 2.0)
        discard;

    float edgeAlpha = 1.0 - smoothstep(-1.0, 2.0, d);

    // --- Real refraction: bend the sample point into the captured background, harder near the rim, like
    // light bending through the curved edge of a glass pane. ---
    float rimT = clamp(1.0 - (-d) / 26.0, 0.0, 1.0);   // 0 deep inside .. 1 at the rim
    vec2 flowA = pc * 0.02 + vec2(time * 0.1, time * 0.07);
    vec2 flowB = pc * 0.05 - vec2(time * 0.06, time * 0.04) + 40.0;
    float n1 = fbm(flowA);
    float n2 = fbm(flowB);
    vec2 warp = (vec2(n1, n2) - 0.5) * mix(6.0, 24.0, rimT * rimT);
    vec2 sampleUv = fragTexCoord + warp / res;

    // Cheap 5-tap frosted blur around the warped sample so the background reads as "through glass", not sharp.
    vec2 texel = 2.5 / res;
    vec3 bg = texture(texture0, sampleUv).rgb * 0.4;
    bg += texture(texture0, sampleUv + vec2(texel.x, 0.0)).rgb * 0.15;
    bg += texture(texture0, sampleUv - vec2(texel.x, 0.0)).rgb * 0.15;
    bg += texture(texture0, sampleUv + vec2(0.0, texel.y)).rgb * 0.15;
    bg += texture(texture0, sampleUv - vec2(0.0, texel.y)).rgb * 0.15;

    // Glass tint mixed over the refracted/blurred background — the background must stay clearly visible.
    vec3 col = mix(bg, tint.rgb, 0.3);

    // Liquid caustic bands drifting through the pane.
    float liquid = smoothstep(0.5, 0.9, n1 * 0.6 + n2 * 0.4);
    col += liquid * 0.14;

    // A soft specular sweep looping diagonally across the panel.
    float diag = pc.x + pc.y;
    float sweepPos = mod(time * 55.0, size.x + size.y + 120.0) - 60.0 + position.x + position.y;
    float sweep = smoothstep(50.0, 0.0, abs(diag - sweepPos));
    col += sweep * 0.5;

    // Fresnel-style rim: brighten near the edge, plus a crisp bright line right at the boundary.
    col += rimT * rimT * 0.25;
    float rimLine = 1.0 - smoothstep(0.0, 2.0, abs(d));
    col += rimLine * 0.6;

    col = clamp(col, 0.0, 1.0);

    float alpha = mix(0.55, 0.95, rimT * 0.5 + liquid * 0.25 + sweep * 0.3) * tint.a * edgeAlpha;

    _fragColorOut = vec4(col, alpha);
}
