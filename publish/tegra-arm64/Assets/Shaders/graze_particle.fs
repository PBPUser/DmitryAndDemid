#version 330

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

const float dist = 32;
const float rd = 8;
const vec2 res = vec2(384., 448.);

vec2 direction(float angle){
    return vec2(cos(angle), sin(angle));
}

void main(){
    gl_FragColor = texture(texture0, fragTexCoord);
    vec2 ftc = fragTexCoord * vec2(1, -1) * res;
    vec2 dc = position + direction(angle) * dist * time;
    float radius = clamp(rd - distance(dc, ftc), 0, rd) / rd;
    gl_FragColor = mix(gl_FragColor, vec4(vec3(0,1,0),1), radius); 
}
