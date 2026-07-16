#version 330
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
const float maxSize = 320;
const vec4 color = vec4(0,.7f,0,1);
void main(){
    gl_FragColor = texture(texture0, fragTexCoord);
    float time = realTime - timeStarted;
    float state = clamp(1 - (time / .6f), 0, 1);
    state = pow(state, 4);
    float d = distance(vec2(fragTexCoord.x, 1-fragTexCoord.y) * res, position);
    d /= maxSize * (1-state);
    d = d > 1 ? 0 : d;
    gl_FragColor = mix(gl_FragColor, color, d * state);
}
