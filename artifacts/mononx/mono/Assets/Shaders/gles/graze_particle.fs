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
uniform float angle;

const float dist = 32.0;
const float rd = 8.0;
const vec2 res = vec2(384., 448.);

vec2 direction(float angle){
    return vec2(cos(angle), sin(angle));
}

void main(){
    _fragColorOut = vec4(0.0);
    _fragColorOut = texture(texture0, fragTexCoord);
    vec2 ftc = fragTexCoord * vec2(1, -1) * res;
    vec2 dc = position + direction(angle) * dist * time;
    float radius = clamp(rd - distance(dc, ftc), 0.0, rd) / rd;
    _fragColorOut = mix(_fragColorOut, vec4(vec3(0,1,0),1), radius); 
}
