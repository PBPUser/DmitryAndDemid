#version 330

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
    vec2 pos = fragTexCoord / outputRes * textureRes + vec2(time * speed);
    gl_FragColor = texture(texture0, pos);
}