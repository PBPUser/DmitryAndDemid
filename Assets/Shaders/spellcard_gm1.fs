#version 330

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
	float t = clamp(dt/lensq,0,1);
	return sp1 + d * t;
}

void main()
{
	float t = time * 1000f;
	vec2 p = fragTexCoord;
	vec2 pr = size * p;
	vec2 opr = pr;
	vec2 n = nearest(p1, p2, pr);
	vec2 n2 = nearest(p2, p3, pr);
	float s2 = clamp((96-distance(n2, pr)) / 96, 0, 1);
	float z2 = clamp(distance(p3, pr) / 10, 0, 1);
	float fpr2 = s2 * z2;
	pr = rotate(pr, p2, 0.4 * cos(t)) * fpr2 + (1-fpr2) * pr;
	float s = clamp((48-distance(n, pr)) / 48, 0, 1);
	float z = clamp(distance(p2, pr) / 192, 0, 1);
	float v = pow(clamp(1 - distance(ph, pr) / 105, 0, 1), .5);
	float fpr = s * z;
	pr = rotate(pr, (p2 - (opr-pr)), 0.2 * sin(t)) * fpr + (1-fpr) * pr;
	pr += vec2(0,v * 40 * sin(t));
	gl_FragColor = texture(texture0, pr / size);
	gl_FragColor = vec4(gl_FragColor.rgb, 1.0);
}
