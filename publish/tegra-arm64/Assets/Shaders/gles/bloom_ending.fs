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
uniform vec2 resolution;
uniform float strength;
uniform float opacity;

void main(){
    _fragColorOut = vec4(0.0);
    vec2 texelSize = vec2(2.) / resolution;
    vec2 off = vec2(texelSize.x, 0.);
    vec4 result = texture(texture0, fragTexCoord) * 0.227;
    float wb = 0.227 / strength;
    for(float i = 1.; i < strength; i++){
      result += texture(texture0, fragTexCoord + off * i) * wb * (strength-i);
      result += texture(texture0, fragTexCoord - off * i) * wb * (strength-i);
    }
    _fragColorOut = vec4(result.rgb, clamp(result[3],0.,1.)*opacity);
}
