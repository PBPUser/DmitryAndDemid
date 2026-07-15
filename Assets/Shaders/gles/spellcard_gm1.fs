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
uniform float time;
uniform vec2 pos;

const vec2 p1 = vec2(0, 30);
const vec2 p2 = vec2(75, 262);
const vec2 p3 = vec2(156, 335);
const vec2 ph = vec2(350,215);
const vec2 size = vec2(384,448);

vec2 rotate(vec2 pos, vec2 center, float angle){
	vec2 r = pos - center;
	float c = cos(angle), s = sin(angle);
	r *= mat2(c, s, -s, c);
	return r + center;
}

vec2 nearest(vec2 sp1, vec2 sp2, vec2 point){
	vec2 d = sp2 - sp1;
	vec2 p = point - sp1;
	float dt = p.x*d.x+p.y*d.y;
	float lensq = d.x*d.x+d.y*d.y;
	float t = clamp(dt/lensq,0.0,1.0);
	return sp1 + d * t;
}

void main()
{
    _fragColorOut = vec4(0.0);
	float t = time * 1000.0;
	vec2 p = fragTexCoord;
	vec2 pr = size * p;
	vec2 opr = pr;
	vec2 n = nearest(p1, p2, pr);
	vec2 n2 = nearest(p2, p3, pr);
	float s2 = clamp((96.0-distance(n2, pr)) / 96.0, 0.0, 1.0);
	float z2 = clamp(distance(p3, pr) / 10.0, 0.0, 1.0);
	float fpr2 = s2 * z2;
	pr = rotate(pr, p2, 0.4 * cos(t)) * fpr2 + (1.0-fpr2) * pr;
	float s = clamp((48.0-distance(n, pr)) / 48.0, 0.0, 1.0);
	float z = clamp(distance(p2, pr) / 192.0, 0.0, 1.0);
	float v = pow(clamp(1.0 - distance(ph, pr) / 105.0, 0.0, 1.0), .5);
	float fpr = s * z;
	pr = rotate(pr, (p2 - (opr-pr)), 0.2 * sin(t)) * fpr + (1.0-fpr) * pr;
	pr += vec2(0.0,v * 40.0 * sin(t));
	_fragColorOut = texture(texture0, pr / size);
	_fragColorOut = vec4(_fragColorOut.rgb, 1.0);
}
