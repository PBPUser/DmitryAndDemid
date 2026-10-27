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
uniform vec3 color;
void main(){
    _fragColorOut = vec4(0.0);
    vec4 c = texture(texture0, fragTexCoord);
    _fragColorOut = vec4(color, c[3] * .5);
}
