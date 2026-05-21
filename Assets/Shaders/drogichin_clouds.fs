#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float rotation;

const vec2 res = vec2(448, 384);
const mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
const vec4 skyColor1 = vec4(.51, .40, .25, .5);
const vec4 skyColor2 = vec4(1, 1, 1, .4);
const float cloudscale = 1.0;
const float speed = 0.00001;
const float clouddark = 0.5;
const float cloudlight = 0.;
const float cloudcover = 0.;
const float cloudalpha = 0.0;
const float skytint = 0.;

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

vec4 renderClouds(vec2 fragCoord, vec4 skycolor, float time) {
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
    
    vec4 cloudcolour = vec4(1.1, 1.1, 0.9, 1.0) * 
                       clamp(clouddark + cloudlight * c, 0.0, 1.0);
   
    f = cloudcover + cloudalpha * f * r;
    
    return mix(skycolor, 
               clamp(skytint * skycolor + cloudcolour, 0.0, 1.0), 
               clamp(f + c, 0.0, 1.0));
}

vec2 wrapCoordinates(vec2 uv){
	vec2 uv1 = uv;
	if(mod(uv.x,2) > 1)
		uv1.x = 1-mod(uv.x,1);
	if(mod(uv.y,2) > 1)
		uv1.y = 1-uv.y;
	return uv1;
}

vec2 rotate(vec2 pos, vec2 pivot, float angle){
    float s = sin(angle), c = cos(angle);
    mat2 m = mat2(vec2(c,-s), vec2(s,c));
    return (pos - pivot) * m + pivot;
}

vec2 zoom(vec2 pos, vec2 pivot, float strength){
    return (pos - pivot) / strength + pivot;
}

void main()
{
    gl_FragColor = texture(texture0, zoom(fragTexCoord, vec2(.5), (1-pow(1-distance(fragTexCoord,vec2(.5, 1.5)), 4))) * .25 + .75);
    gl_FragColor = mix(gl_FragColor, vec4(.3, .7, 1,1), pow(distance((fragTexCoord + vec2(0,-1)) * res, vec2(.5, .5) * res) / 448, 3));
	vec4 c = renderClouds(rotate((fragTexCoord + vec2(0,time)) * res, vec2(0.5, -0.5 + time) * res, rotation), skyColor1, time);
	vec4 c2 = renderClouds(rotate((fragTexCoord + vec2(0, time*0.75)) * res, vec2(192, 224), rotation), skyColor2, time*2.);
    gl_FragColor = vec4(gl_FragColor.rgb * (1-c[3]) + c.rgb * c[3] * 1,1.0);
	gl_FragColor = vec4(gl_FragColor.rgb * (1-c2[3]) + c2.rgb * c2[3] * 1,1.0);
}
