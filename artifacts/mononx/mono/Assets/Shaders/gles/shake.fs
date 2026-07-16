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
uniform float time;
uniform float shakeStrength;
uniform float shakeSpeed;
uniform float realTime;

void main(){
    _fragColorOut = vec4(0.0); 
  _fragColorOut = texture(texture0, 
fragTexCoord + shakeStrength * vec2(
sin(realTime * shakeSpeed * 1.44362 + 1.3524524),
cos(realTime * shakeSpeed  * 1.623455 + 2.523452)
)
);
}
