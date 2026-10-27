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
uniform vec2 offset;
uniform float time;
uniform float realTime;
uniform vec2 position;
uniform int mask;
const vec2 res = vec2(384., 448.);
const float size = 96.;
const float sizeC = 192.;
const mat2 m = mat2(1.6, 1.2, -1.2, 1.6);
const float cloudscale = 4.0;
const float speed = 0.01;
const float clouddark = 0.5;
const float cloudlight = 0.;
const float cloudalpha = 1.0;

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

float renderClouds(vec2 fragCoord, float time, float cloudCover) {
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
    float cloudcolour = clamp(clouddark + cloudlight * c, 0.0, 1.0);
    f = cloudCover + cloudalpha * f * r;
    return clamp(cloudcolour * f, 0.0, 1.0);
}

vec2 nearest(vec2 ftc, vec2 f, vec2 t, float mip, float map){
  float p = (ftc.y - f.y) / (t.y - f.y);
  p = clamp(p, mip, map);
  return f + vec2((t.x - f.x) * pow(p, -p*2.9+1.0), (t.y - f.y) * p);
}

float mthd(vec2 ftc,vec2 mp, float mip, float map, float sizeZ){
  vec2 ftcr = ftc * res;
  vec2 pp = position + mp * offset;
  return clamp(1.0-distance(nearest(ftcr, position, pp, mip, map), ftcr)/sizeZ, 0.0, 1.0);  
}

void main(){
    _fragColorOut = vec4(0.0); 
  _fragColorOut = texture(texture0, fragTexCoord);
  float c = 0.0, c1 = 0.0, c2 = 0.0;
  vec2 ftc = fragTexCoord;
  ftc.y = 1.0-ftc.y;
  float timed = time - 0.2;
  float maxP = clamp(2.0-time * 3.0, 0.0, 1.0);
  float minP = clamp(1.0-time * 3.0, 0.0, 1.0);
  float maxPd = clamp(2.0-timed * 3.0, 0.0, 1.0);
  float minPd = clamp(1.0-timed * 3.0, 0.0, 1.0);
  if((mask & 0x1) == 0x1){
      c = mthd(ftc, vec2(-1), minPd, maxPd, size);
      c1 = mthd(ftc, vec2(-1), minP, maxP, sizeC);
  }
  if((mask & 0x2) == 0x2){
      c = max(mthd(ftc, vec2(1,-1), minPd, maxPd, size), c);
      c2 = mthd(ftc, vec2(1,-1), minP, maxP, sizeC);
  }
  if((mask & 0x4) == 0x4){
        c = max(mthd(ftc, vec2(-1, 1), minPd, maxPd, size), c);
        c2 = max(mthd(ftc, vec2(-1, 1), minP, maxP, sizeC), c2);
    }
  if((mask & 0x8) == 0x8){
        c = max(mthd(ftc, vec2(1), minPd, maxPd, size), c);
        c1 = max(mthd(ftc, vec2(1), minP, maxP, sizeC), c1 );
    }
  float tm = clamp(5.0-abs(time * 10.0 - 5.0),0.0,1.0);
  float tm2 = clamp(5.0-abs(time * 10.0 - 3.0),0.0,1.0);
  float cl = renderClouds(ftc * res, timed * 100.0, tm*c*4.0-1.5);
  float cl2 = renderClouds(ftc * res, time * 100.0, tm*c1*8.0-7.0);
  float cl3 = renderClouds(ftc * res, time * 100.0, tm*c2*8.0-7.0);
  _fragColorOut = mix(_fragColorOut, vec4(0.64, 0.16, 0.16, 1), cl3 * .5);
  _fragColorOut = mix(_fragColorOut, vec4(0,1,0,1), cl2 * .5);
  _fragColorOut = mix(_fragColorOut, vec4(0,0,0,1), cl * (min(timed, 0.2) / 0.2));
}
