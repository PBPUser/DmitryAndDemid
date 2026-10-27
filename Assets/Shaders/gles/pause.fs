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
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float scale;
uniform vec2 position;
const vec2 res = vec2(384., 448.);
void main(){
    _fragColorOut = vec4(0.0);
    vec2 texel = vec2(1) / res;
    vec4 color1 = texture(texture0, fragTexCoord + texel * vec2(16,0));
    vec4 color2 = texture(texture0, fragTexCoord - texel * vec2(16,0));
    _fragColorOut = texture(texture0, fragTexCoord);
    _fragColorOut = mix(_fragColorOut, color1, time * .2);
    _fragColorOut = mix(_fragColorOut, color2, time * .2);
    _fragColorOut = mix(_fragColorOut, vec4(0.2, 0.7, 0.1,1), time * .3);
}
