#version 400
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform int signal;
uniform float scale;
uniform vec2 resolution;
const float _17 = 0.142857143;
const float _18 = 0.125;
void main()
{
    vec2 tSize = vec2(1) / resolution / 1.2;
    vec2 ftc = fragTexCoord * 1.2 - vec2(.1);
    if(mod(abs(ftc), 1) != ftc)
        return;
    float s = floor(ftc.x / _17);
    float z = 1 - s * 0.1;
    float h = 1 - z;
    if(mod(ftc.x, _17) < _18 && z < ftc.y){
        if(abs(ftc.y - 1) < tSize.y * scale)
            gl_FragColor = vec4(1);
    }
}
