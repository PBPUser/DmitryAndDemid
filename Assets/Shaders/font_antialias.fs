#version 410
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 resolution;
uniform int scale;

void main()
{
    vec2 ftc = fragTexCoord;
    vec2 texelSize = vec2(1) / resolution;
    vec2 scaledTexelSize = vec2(1) / (resolution * float(scale));
    vec4 c = vec4(0);
    for(float x = 0; x < scale; x++)
    for(float y = 0; y < scale; y++)
        c += texture(texture0, ftc * scale + scaledTexelSize * vec2(x,y));
    c /= pow(scale, 2);
    gl_FragColor = c;
}
