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
uniform vec3 color;
uniform vec2 resolution;

const float strengthMax = 3.;
const vec3 z = vec3(0);
const float outlineSize = 3.;
const float _distance = 1.;
const vec2 c = vec2(.5);
const float padding = 3.4;

void main(){
    _fragColorOut = vec4(0.0);
    float d = distance(c, fragTexCoord) * padding;
	float transparency = smoothstep(-.2, 0.5, 1.0-d);
	float colorness = smoothstep(1.0-d, 1., .73);
	float grayColor = 1.0-(1.0-d)*.1;
    _fragColorOut = vec4(mix(vec3(grayColor), color, colorness), transparency);
}
