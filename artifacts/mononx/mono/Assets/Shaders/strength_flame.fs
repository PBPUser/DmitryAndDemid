#version 330

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
    gl_FragColor = texture(texture0, fragTexCoord);
}
