#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float statement;
uniform vec2 resolution;
uniform vec2 position;
uniform vec2 size;

const float strengthMax = 3.;
const vec3 z = vec3(0);
const float outlineSize = 3.;
const float distance = 1.;

float elastic(float x){
	float c5 = (6.29) / 4.5;
        return x == 0
        ? 0
        : x == 1
        ? 1
        : x < 0.5
        ? -(pow(2, 20 * x - 10) * sin((20 * x - 11.125) * c5)) / 2
        : (pow(2, -20 * x + 10) * sin((20 * x - 11.125) * c5)) / 2 + 1;
}

void main(){
	float scale = 2.-elastic(statement);
	scale -= clamp(1.-elastic(1-(statement-1)),0.,2); 
	vec2 tSize = vec2(1.) / resolution;
	vec2 center = tSize * (position + (size/2));
	center.y = 1-center.y;
    float strength = 6. * (1.-statement);
    float opacity = 1.-abs(1.-statement);
    vec2 off = tSize * scale;
    vec2 clamped = clamp(center + (fragTexCoord-center) / scale, position*tSize, (vec2(position.x,resolution.y-position.y)+size)*tSize);
	float pixelsCount = pow(strength+1,2.);
	vec4 result = texture(texture0, clamped);
	float _opacity = result[3];
    vec4 _color = vec4(0.0);
	vec2 off1 = vec2(1.333) * tSize;
	vec2 off2 = vec2(3.329) * tSize;
	_color += texture2D(texture0, clamped) * 0.30;
	_color += texture2D(texture0, clamped + off1) * 0.3;
	_color += texture2D(texture0, clamped - off1) * 0.3;
	_color += texture2D(texture0, clamped + off1 * vec2(1,-1)) * 0.3;
	_color += texture2D(texture0, clamped - off1 * vec2(1,-1)) * 0.3;
	_color += texture2D(texture0, clamped + off2) * 0.15;
	_color += texture2D(texture0, clamped - off2) * 0.15;
	_color += texture2D(texture0, clamped + off2 * vec2(1,-1)) * 0.15;
	_color += texture2D(texture0, clamped - off2 * vec2(1,-1)) * 0.15;
	gl_FragColor = 
		mix(
		vec4(result.xyz, result[3]),
		_color,
		clamp(1.-statement, 0., 1.)
	);
}
