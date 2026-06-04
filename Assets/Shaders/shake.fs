#version 400
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
  gl_FragColor = texture(texture0, 
fragTexCoord + shakeStrength * vec2(
sin(realTime * shakeSpeed * 1.44362 + 1.3524524),
cos(realTime * shakeSpeed  * 1.623455 + 2.523452)
)
);
}
