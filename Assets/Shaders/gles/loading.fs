#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
out vec4 _fragColorOut;

in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDifuse;
uniform vec2 textureRes;
uniform vec2 outputRes;
uniform float time;
const float speed = 1.5;

void main(){
    _fragColorOut = vec4(0.0);
    vec2 pos = fragTexCoord / outputRes * textureRes + vec2(time * speed);
    _fragColorOut = texture(texture0, pos);
}
