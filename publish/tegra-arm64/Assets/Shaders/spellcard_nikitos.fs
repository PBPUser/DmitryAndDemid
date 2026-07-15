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

const vec2 res = vec2(448, 384);
const mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
const vec4 skycolour2 = vec4(0.0, 0.2, 0.3, 0.0);
const float cloudscale = 1.0;
const float speed = 0.00001;
const float clouddark = 0.5;
const float cloudlight = 0.;
const float cloudcover = 0.;
const float cloudalpha = 0.0;
const float skytint = 0.;
const vec4 skycolour1 = vec4(0.0, 0.4, 0.1, 0.0);

vec2 hash(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), 
             dot(p, vec2(269.5, 183.3)));
    return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise(vec2 p) {
    const float K1 = 0.366025404;
    const float K2 = 0.211324865;
    
    vec2 i = floor(p + (p.x + p.y) * K1);    
    vec2 a = p - i + (i.x + i.y) * K2;
    vec2 o = (a.x > a.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
    vec2 b = a - o + K2;
    vec2 c = a - 1.0 + 2.0 * K2;
    
    vec3 h = max(0.5 - vec3(dot(a, a), dot(b, b), dot(c, c)), 0.0);
    vec3 n = h * h * h * h * vec3(
        dot(a, hash(i + 0.0)), 
        dot(b, hash(i + o)), 
        dot(c, hash(i + 1.0))
    );
    return dot(n, vec3(70.0));    
}

float fbm(vec2 n) {
    float total = 0.0, amplitude = 0.1;
    for (int i = 0; i < 7; i++) {
        total += noise(n) * amplitude;
        n = m * n;
        amplitude *= 0.4;
    }
    return total;
}

vec4 renderClouds(vec2 fragCoord) {
    vec2 p = fragCoord.xy / res.xy;
    vec2 uv = p * vec2(res.x / res.y, 1.0);    
    float _time = time * speed * .1;
    float q = fbm(uv * cloudscale * 0.5);
    
    float r = 0.0;
    uv *= cloudscale;
    uv -= q - _time;
    float weight = 0.8;
    for (int i = 0; i < 8; i++) {
        r += abs(weight * noise(uv));
        uv = m * uv + _time;
        weight *= 0.7;
    }
    
    float f = 0.0;
    uv = p * vec2(res.x / res.y, 1.0);
    uv *= cloudscale;
    uv -= q - _time;
    weight = 0.7;
    for (int i = 0; i < 8; i++) {
        f += weight * noise(uv);
        uv = m * uv + _time;
        weight *= 0.6;
    }
    
    f *= r + f;
    
    float c = 0.0;
    _time = time * speed * .1;
    uv = p * vec2(res.x / res.y, 1.0);
    uv *= cloudscale * 2.0;
    uv -= q - _time;
    weight = 0.4;
    for (int i = 0; i < 7; i++) {
        c += weight * noise(uv);
        uv = m * uv + _time;
        weight *= 0.2;
    }
    
    float c1 = 0.0;
    _time = time * speed * .3;
    uv = p * vec2(res.x / res.y, 1.0);
    uv *= cloudscale * 3.0;
    uv -= q - _time;
    weight = 0.4;
    for (int i = 0; i < 7; i++) {
        c1 += abs(weight * noise(uv));
        uv = m * uv + _time;
        weight *= 0.6;
    }
    
    c += c1;
    
    vec4 skycolour = mix(skycolour2, skycolour1, p.y);
    vec4 cloudcolour = vec4(1.1, 1.1, 0.9, 1.0) * 
                       clamp(clouddark + cloudlight * c, 0.0, 1.0);
   
    f = cloudcover + cloudalpha * f * r;
    
    return mix(skycolour, 
               clamp(skytint * skycolour + cloudcolour, 0.0, 1.0), 
               clamp(f + c, 0.0, 1.0));
}

vec2 rotate2D(vec2 coord, vec2 pivot, float angle){
	float s = sin(angle);
	float c = cos(angle);
	return pivot + (mat2(vec2(c,s),vec2(-s,c)) * (coord-pivot));
}

vec2 wrapCoordinates(vec2 uv){
	vec2 uv1 = uv;
	if(mod(uv.x,2) > 1)
		uv1.x = 1-mod(uv.x,1);
	if(mod(uv.y,2) > 1)
		uv1.y = 1-uv.y;
	return uv1;
}

void main()
{
	float aspect = res.x / res.y;
	vec2 texel = vec2(1) / res;
    vec2 uv = vec2(fragTexCoord.x, fragTexCoord.y);
	uv.x += sin(uv.y * 40 * sin(time * 40) + time * 40) * .01;
	uv.y += sin(uv.x * 40 * cos(time * 17) + time * 40) * .01;
	vec2 quv = uv + vec2(sin(time * 200), cos(time* 200)) * .01;
	vec2 ruv = rotate2D(quv, pos / res, sin(time * 100) * .1);
    vec4 texelColor = texture2D(texture0,wrapCoordinates(ruv));
    gl_FragColor = texelColor * colDiffuse;


	float x = mod(atan(uv.y - pos.x, uv.x - pos.y) * 
                        180.0 / 3.14159 + time * 10.0, 360.0);
	vec2 ftc = vec2(x,
		distance(uv, pos/res) * aspect) * res;
	vec4 c = renderClouds(ftc);
	gl_FragColor = vec4(gl_FragColor.rgb + c.rgb * c[3] * 2,1.0);
}
