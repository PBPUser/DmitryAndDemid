#version 330

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
const float padding = 2.5;

float superElipse(float n, vec2 pos){
	return pow(abs(pos.x/.5), n) + pow(abs(pos.y/1.7), n);
}

void main(){
	float angle = atan(fragTexCoord.y-.5, fragTexCoord.x-.5);
	float ftcy = fragTexCoord.y;
	if(ftcy < .5)
		ftcy = 1-ftcy;
    float d = superElipse((1.-pow(abs(ftcy),.4)) * 8., 
		(fragTexCoord-vec2(0.5))/.7);
	float transparency = smoothstep(-.2, 0.5, 1-d);
	float colorness = smoothstep(1-d, 1., .73);
	float grayColor = 1-(1-d)*.1;
    gl_FragColor = vec4(mix(vec3(grayColor), color, colorness), transparency);
} 