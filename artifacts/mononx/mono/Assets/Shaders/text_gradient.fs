#version 410
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;

void main()
{
    vec2 ftc = fragTexCoord;
    ftc.y = 1-ftc.y;
    gl_FragColor = texture(texture0,ftc) * (1 - pow(ftc.y, 2) * 0.2);
}
