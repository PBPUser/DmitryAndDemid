#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform sampler2D textureAttack;
uniform float realTime;
uniform float timeStarted;
uniform vec2 position;
const vec2 res = vec2(384, 448);
const float maxSize = 320.0;
const vec4 color = vec4(0,.7f,0,1);
void main(){
    _fragColorOut = vec4(0.0);
    _fragColorOut = texture(texture0, fragTexCoord);
    float time = realTime - timeStarted;
    float state = clamp(1.0 - (time / .6f), 0.0, 1.0);
    state = pow(state, 4.0);
    float d = distance(vec2(fragTexCoord.x, 1.0-fragTexCoord.y) * res, position);
    d /= maxSize * (1.0-state);
    d = d > 1.0 ? 0.0 : d;
    _fragColorOut = mix(_fragColorOut, color, d * state);
}
