#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

// Lights a background: multiplies the unlit scene by the light map built from its lights (see
// Common/BackgroundLighting.cs and light_point.fs), then blooms whatever came out bright.
//
// Multiply is what makes the lights read as illumination rather than as glowing discs pasted on top - an
// unlit corner falls to the ambient level, and a lit one takes the lamp's hue. The bloom is gathered from the
// LIT scene, not the raw one, so only what the lights actually pick out glows.
//
// Ambient is added HERE rather than being the colour the light map was cleared to, and that is deliberate:
// the map is an 8-bit target that saturates at 1, so an ambient baked into it would eat the range the lights
// need and they could then only ever darken the scene. Kept separate, ambient + light reaches up to 2, which
// is what lets a lamp overexpose a bright daylight background instead of vanishing into it.
//
// Both textures are render targets written in the same orientation, so they line up when sampled at the same
// coordinate - the vertical flip is in the source rect the scene is drawn with, not in the sampling here.
//
// Uniforms:
//   ambient     light level unlit parts of the scene fall to (rgb; a unused)
//   resolution  light map / scene size in pixels (bloom taps are sized in its texels)
//   bloom       strength of the glow off lit areas; 0 skips the gather entirely (low graphics)
//   threshold   how bright a lit pixel must be before it starts to bloom
//   lightMap    the accumulated lights, WITHOUT ambient (see above)

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform sampler2D lightMap;
uniform vec4 colDiffuse;
uniform vec4 ambient;
uniform vec2 resolution;
uniform float bloom;
uniform float threshold;

vec3 lightAt(vec2 c) { return ambient.rgb + texture(lightMap, c).rgb; }
vec3 litAt(vec2 c) { return texture(texture0, c).rgb * lightAt(c); }

void main()
{
    _fragColorOut = vec4(0.0);
    vec4 scene = texture(texture0, fragTexCoord) * fragColor;
    vec3 lit = scene.rgb * lightAt(fragTexCoord);

    vec3 glow = vec3(0.0);
    if (bloom > 0.0)
    {
        vec2 texel = 1.0 / resolution;
        float wsum = 0.0;
        for (int ring = 1; ring <= 2; ring++)
        {
            float dist = 4.0 * float(ring);
            float w = 1.0 / float(ring);
            for (int i = 0; i < 8; i++)
            {
                float a = 6.2831853 * float(i) / 8.0 + float(ring) * 0.41;
                vec3 s = litAt(fragTexCoord + vec2(cos(a), sin(a)) * dist * texel);
                glow += max(s - threshold, vec3(0.0)) * w;
                wsum += w;
            }
        }
        glow = glow / wsum * bloom;
    }

    _fragColorOut = vec4(lit + glow, scene.a);
}
