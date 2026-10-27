#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// AMD FidelityFX Super Resolution 1.0 — EASU, the edge-adaptive spatial upsampling pass, after the
// reference in ffx_fsr1.h (MIT). The 12-tap kernel around the source position: luma gradients pick the
// edge direction and its strength, the taps are weighted with a lobe stretched along that edge, and the
// result is clamped to the 2x2 core so nothing rings. Second pass is fsr_rcas.fs.
//
// texture0 is the frame at the internal resolution (a render target read with a positive full-size rect,
// so it arrives upside down — the 1 - y below turns it back). inputSize / outputSize are in pixels.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 inputSize;
uniform vec2 outputSize;

vec3 fetch(ivec2 p) {
    p = clamp(p, ivec2(0), ivec2(inputSize) - 1);
    return texelFetch(texture0, p, 0).rgb;
}

float luma(vec3 c) { return c.b * 0.5 + (c.r * 0.5 + c.g); }

// Accumulates the direction and length estimate from one 2x2 quadrant of the 12-tap neighbourhood.
void easuSet(inout vec2 dir, inout float len, vec2 pp, bool biS, bool biT, bool biU, bool biV,
             float lA, float lB, float lC, float lD, float lE) {
    float w = 0.0;
    if (biS) w = (1.0 - pp.x) * (1.0 - pp.y);
    if (biT) w = pp.x * (1.0 - pp.y);
    if (biU) w = (1.0 - pp.x) * pp.y;
    if (biV) w = pp.x * pp.y;
    float dc = lD - lC;
    float cb = lC - lB;
    float lenX = max(abs(dc), abs(cb));
    lenX = 1.0 / max(lenX, 1.0 / 32768.0);
    float dirX = lD - lB;
    dir.x += dirX * w;
    lenX = clamp(abs(dirX) * lenX, 0.0, 1.0);
    lenX *= lenX;
    len += lenX * w;
    float ec = lE - lC;
    float ca = lC - lA;
    float lenY = max(abs(ec), abs(ca));
    lenY = 1.0 / max(lenY, 1.0 / 32768.0);
    float dirY = lE - lA;
    dir.y += dirY * w;
    lenY = clamp(abs(dirY) * lenY, 0.0, 1.0);
    lenY *= lenY;
    len += lenY * w;
}

// One tap: its offset rotated into the edge frame, stretched, run through the windowed lobe.
void easuTap(inout vec3 aC, inout float aW, vec2 off, vec2 dir, vec2 len, float lob, float clp, vec3 c) {
    vec2 v = vec2(off.x * dir.x + off.y * dir.y, off.x * (-dir.y) + off.y * dir.x);
    v *= len;
    float d2 = min(dot(v, v), clp);
    float wB = 2.0 / 5.0 * d2 - 1.0;
    float wA = lob * d2 - 1.0;
    wB *= wB;
    wA *= wA;
    wB = 25.0 / 16.0 * wB - (25.0 / 16.0 - 1.0);
    float w = wB * wA;
    aC += c * w;
    aW += w;
}

void main() {
    _fragColorOut = vec4(0.0);
    // Output pixel in memory space (render targets are stored bottom-up), then its position in the source.
    vec2 ip = vec2(fragTexCoord.x, 1.0 - fragTexCoord.y) * outputSize;
    vec2 pp = ip * (inputSize / outputSize) - 0.5;
    vec2 fp = floor(pp);
    pp -= fp;
    ivec2 base = ivec2(fp);

    //  b c
    //  e f g h
    //  i j k l
    //    n o
    vec3 bC = fetch(base + ivec2( 0, -1)); float bL = luma(bC);
    vec3 cC = fetch(base + ivec2( 1, -1)); float cL = luma(cC);
    vec3 eC = fetch(base + ivec2(-1,  0)); float eL = luma(eC);
    vec3 fC = fetch(base + ivec2( 0,  0)); float fL = luma(fC);
    vec3 gC = fetch(base + ivec2( 1,  0)); float gL = luma(gC);
    vec3 hC = fetch(base + ivec2( 2,  0)); float hL = luma(hC);
    vec3 iC = fetch(base + ivec2(-1,  1)); float iL = luma(iC);
    vec3 jC = fetch(base + ivec2( 0,  1)); float jL = luma(jC);
    vec3 kC = fetch(base + ivec2( 1,  1)); float kL = luma(kC);
    vec3 lC = fetch(base + ivec2( 2,  1)); float lL = luma(lC);
    vec3 nC = fetch(base + ivec2( 0,  2)); float nL = luma(nC);
    vec3 oC = fetch(base + ivec2( 1,  2)); float oL = luma(oC);

    vec2 dir = vec2(0.0);
    float len = 0.0;
    easuSet(dir, len, pp, true,  false, false, false, bL, eL, fL, gL, jL);
    easuSet(dir, len, pp, false, true,  false, false, cL, fL, gL, hL, kL);
    easuSet(dir, len, pp, false, false, true,  false, fL, iL, jL, kL, nL);
    easuSet(dir, len, pp, false, false, false, true,  gL, jL, kL, lL, oL);

    vec2 dir2 = dir * dir;
    float dirR = dir2.x + dir2.y;
    bool zro = dirR < 1.0 / 32768.0;
    dirR = inversesqrt(max(dirR, 1.0 / 32768.0));
    dirR = zro ? 1.0 : dirR;
    dir.x = zro ? 1.0 : dir.x;
    dir *= vec2(dirR);
    len = len * 0.5;
    len *= len;
    float stretch = (dir.x * dir.x + dir.y * dir.y) * (1.0 / max(abs(dir.x), abs(dir.y)));
    vec2 len2 = vec2(1.0 + (stretch - 1.0) * len, 1.0 + -0.5 * len);
    float lob = 0.5 + ((1.0 / 4.0 - 0.04) - 0.5) * len;
    float clp = 1.0 / lob;

    vec3 min4 = min(min(fC, gC), min(jC, kC));
    vec3 max4 = max(max(fC, gC), max(jC, kC));
    vec3 aC = vec3(0.0);
    float aW = 0.0;
    easuTap(aC, aW, vec2( 0.0, -1.0) - pp, dir, len2, lob, clp, bC);
    easuTap(aC, aW, vec2( 1.0, -1.0) - pp, dir, len2, lob, clp, cC);
    easuTap(aC, aW, vec2(-1.0,  1.0) - pp, dir, len2, lob, clp, iC);
    easuTap(aC, aW, vec2( 0.0,  1.0) - pp, dir, len2, lob, clp, jC);
    easuTap(aC, aW, vec2( 0.0,  0.0) - pp, dir, len2, lob, clp, fC);
    easuTap(aC, aW, vec2(-1.0,  0.0) - pp, dir, len2, lob, clp, eC);
    easuTap(aC, aW, vec2( 1.0,  1.0) - pp, dir, len2, lob, clp, kC);
    easuTap(aC, aW, vec2( 2.0,  1.0) - pp, dir, len2, lob, clp, lC);
    easuTap(aC, aW, vec2( 2.0,  0.0) - pp, dir, len2, lob, clp, hC);
    easuTap(aC, aW, vec2( 1.0,  0.0) - pp, dir, len2, lob, clp, gC);
    easuTap(aC, aW, vec2( 1.0,  2.0) - pp, dir, len2, lob, clp, oC);
    easuTap(aC, aW, vec2( 0.0,  2.0) - pp, dir, len2, lob, clp, nC);

    vec3 pix = min(max4, max(min4, aC * (1.0 / max(aW, 1e-5))));
    _fragColorOut = vec4(pix, 1.0);
}
