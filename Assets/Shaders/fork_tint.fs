#version 330
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec3 color;
void main(){
    vec4 c = texture(texture0, fragTexCoord);
    gl_FragColor = vec4(color, c[3] * .5);
}