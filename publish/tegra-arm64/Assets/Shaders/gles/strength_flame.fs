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
uniform sampler2D bossTexture;
uniform float time;
uniform float scale;
uniform vec2 position;

const vec2 res = vec2(384., 448.);

void main(){
    _fragColorOut = vec4(0.0);
    _fragColorOut = texture(texture0, fragTexCoord);
}
