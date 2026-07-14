#version 400
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform sampler2D textureLeaves;
uniform float time;
uniform float scale;
uniform vec2 position;
uniform float timeStarted;
uniform vec4 particlesColor;
uniform vec4 circleColor;
const vec2 res = vec2(384., 448.);
const float bloomSpread = 9;
const float bloomIntensity = 1.5;

vec4 invert(vec2 uvs, bool invert, float il){
  vec4 color = texture(texture0, uvs);
  return mix(vec4(invert ? 1.-color.xyz : color.xyz, color[3]), color, il);
}

float c1leavesColor(vec2 uv){
  if(mod(uv,1) != uv)
    return 0.0;
  return texture(textureLeaves, uv)[3];
}

vec4 glow(vec2 uv, vec2 texel){
  vec4 result = vec4(0.);
  vec4 hr;
  for(float x = -4.; x < 5.; x++){
    hr = vec4(0.);
    for(float y = -4.;y<5.;y++)
      hr += c1leavesColor(uv + vec2(x, y) * bloomSpread * texel) * particlesColor;
    result += hr / 9;
  }
  return c1leavesColor(uv) + result / 9 * bloomIntensity;
}

float pseudoRandom(float j){
  return mod(tan(sin(timeStarted * cos(j))) + 1000, 1);
}

vec4 leavesColor(vec2 uv, vec2 size, vec2 center, float angle, vec2 texel){
  vec2 uvi = uv;
  float c = cos(angle);
  float s = sin(angle);
  vec2 pivot = (center + size / 2);
  uvi = uvi - pivot;
  uvi *= mat2(vec2(c,-s),vec2(s,c));
  vec2 p = (uvi / size) + pivot + vec2(.5);
  if(mod(p, 2) != p)
    return vec4(0);
  p -= vec2(.5);
  return clamp(glow(p, texel), 0, 1);
}

vec2 direction(float angle){
  return vec2(cos(angle * 4), sin(angle * 4));
}

float angle(vec2 p1, vec2 p2){
  return atan(p2.y-p1.y, p2.x-p1.x);
}

vec4 circle(vec2 uv, float size){
  float d = distance(uv * res, position) / size;
  float p = smoothstep(1.4, 0., d);
  float p2 = smoothstep(0., 1.4, d);
  float p3 = smoothstep(1., 0.9, d);
  float p4 = smoothstep(0.8, 0.9, d);
  float a = angle(uv, position / res);
  vec4 border = vec4(vec3(1), clamp(p4*p3,0.,1.)*(.25 * (abs(tan(a*16.*sin(a+sin(d*4.+time)))) * 0.25)) + 0.);
  vec4 fColor = circleColor * p * p2;
  return (border + fColor + circleColor * (pow(p, 5.))) * clamp(1.-time,0.,1.);
}

void main(){ 
  gl_FragColor = texture(texture0, fragTexCoord);
  float rTime = time;
  vec2 ftc = vec2(fragTexCoord.x, 1-fragTexCoord.y);
  vec4 c1 = circle(ftc, rTime * 96);
  gl_FragColor = mix(gl_FragColor, vec4(c1.rgb,1), c1[3]);
  ivec2 tSize = textureSize(textureLeaves, 0);
  vec2 texel = vec2(1)/vec2(tSize.y,tSize.x);
  vec2 texel2 = vec2(1)/vec2(tSize);
  vec2 maxForkSize = texel * 24;
  vec4 fColor;
  vec2 fPos, fSize;
  float fAngle, tTime, fOpacity;
  vec2 pp = position / res;
  float overlayOpacity = 1 - (max(0, rTime-0.75) * 4);
  for(float i = 0; i < 16; i++){
    tTime = max(rTime - pseudoRandom(i-5.7) * 2,0);
    fOpacity = tTime * 10; 
    if(tTime > 0){
      fPos = pp + direction(pseudoRandom(9.1+i) * 1.8) * pseudoRandom(i-3.7) * 4 * tTime;
      fSize = maxForkSize * (pseudoRandom(i-2.0) * 0.5 + 0.5 + (pseudoRandom(4.1+i) * pseudoRandom(i-1.4) * 2 * tTime));
      if(mod(fPos,max(fSize.x, fSize.y) + 1) != fPos)
        continue;
      fPos.y = 1 - fPos.y;
      fAngle = pseudoRandom(2.4+i) + tTime * (pseudoRandom(5.2+i) - .5) * 10;
      fColor = leavesColor(fragTexCoord, fSize, fPos, fAngle, texel2);
      gl_FragColor = mix(gl_FragColor, vec4(fColor.rgb,1), fColor[3] * overlayOpacity * fOpacity * 0.4);
    }
  }
}
