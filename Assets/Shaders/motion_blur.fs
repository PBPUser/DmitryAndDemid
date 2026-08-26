#version 330

// Directional motion blur: averages SAMPLES taps spread +/- strength UV units along `direction`.
// strength 0 collapses every tap onto the pixel itself, so the same draw doubles as the sharp path.

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 direction;   // unit vector along the motion
uniform float strength;   // blur radius, in UV units of the sampled texture

const int SAMPLES = 16;

void main()
{
    vec4 sum = vec4(0.0);
    for (int i = 0; i < SAMPLES; i++)
    {
        float t = (float(i) / float(SAMPLES - 1)) * 2.0 - 1.0;   // -1 .. 1
        sum += texture(texture0, fragTexCoord + direction * strength * t);
    }
    gl_FragColor = sum / float(SAMPLES) * fragColor * colDiffuse;
}
