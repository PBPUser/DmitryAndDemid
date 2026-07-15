#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float realTime;
uniform float scale;
uniform vec2 position;
const vec2 bulletSize = vec2(6,16);
const vec2 res = vec2(384., 448.);

void main(){
    gl_FragColor = texture(texture0, fragTexCoord);
    vec2 texelSize = vec2(1)/res;
    vec2 textureCoord = vec2(fragTexCoord.x, 1-fragTexCoord.y)*res;
    vec2 coordFromj = position - bulletSize/2, coordToj = position + bulletSize/2, coordFrom, coordTo;
    float opacity = 1-pow(time * 2 - 1, 2);
    float opacity2 = 0;
    for(float i = -3; i < 0; i++){
        coordFrom = coordFromj;
        coordTo = coordToj;
        coordFrom.y += i * 16 * time;
        coordTo.y += i * 16 * time;
        if(textureCoord.x > coordFrom.x && textureCoord.y > coordFrom.y && textureCoord.x < coordTo.x && textureCoord.y < coordTo.y){
            opacity2 += (2-abs(i)+1) * 0.25;
        }
    }
    gl_FragColor = mix(gl_FragColor, vec4(1), opacity * opacity2);
}