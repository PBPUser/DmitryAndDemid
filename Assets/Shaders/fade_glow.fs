#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float statement;
uniform vec3 color;
uniform vec2 position;
uniform vec2 resolution;

const float strengthMax = 3.;
const vec3 z = vec3(0);
const float outlineSize = 3.;

const float distance = 1.;

void main(){
    float strength = 6.;
    float opacity = 1.-abs(1.-statement);
    vec2 texelSize = vec2(2.) / resolution;
    vec2 off = texelSize * vec2(sin(distance), cos(distance));
    vec4 result = texture(texture0, fragTexCoord) * 0.227;
    float wb = 0.227 / strength;
    for(float i = 1.; i < strength; i++){
      result += texture(texture0, fragTexCoord + off * i) * wb * (strength-i);
      result += texture(texture0, fragTexCoord - off * i) * wb * (strength-i);
    }
    gl_FragColor = vec4(result.rgb, clamp(result[3],0.,1.)*opacity);
}
