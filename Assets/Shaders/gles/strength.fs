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
uniform sampler2D textureLeaves;
uniform float time;
uniform float scale;
uniform vec2 position;
uniform float timeStarted;
uniform vec4 particlesColor;
uniform vec4 circleColor;
const vec2 res = vec2(384., 448.);

// Every literal below is explicitly a float. NVIDIA's GLSL compiler silently promotes int literals
// (mod(uv, 1), clamp(x, 0, 1), / 9, ...); Intel's rejects them as "no matching overload" and this shader
// then fails to compile, taking the game down on Intel GPUs. Keep the decimal points.
const float bloomSpread = 9.0;
const float bloomIntensity = 1.5;

vec4 invert(vec2 uvs, bool invert, float il){
  vec4 color = texture(texture0, uvs);
  return mix(vec4(invert ? 1.0 - color.xyz : color.xyz, color[3]), color, il);
}

float c1leavesColor(vec2 uv){
  if(mod(uv, 1.0) != uv)
    return 0.0;
  return texture(textureLeaves, uv)[3];
}

vec4 glow(vec2 uv, vec2 texel){
  vec4 result = vec4(0.0);
  vec4 hr;
  for(float x = -4.0; x < 5.0; x++){
    hr = vec4(0.0);
    for(float y = -4.0; y < 5.0; y++)
      hr += c1leavesColor(uv + vec2(x, y) * bloomSpread * texel) * particlesColor;
    result += hr / 9.0;
  }
  return c1leavesColor(uv) + result / 9.0 * bloomIntensity;
}

float pseudoRandom(float j){
  return mod(tan(sin(timeStarted * cos(j))) + 1000.0, 1.0);
}

vec4 leavesColor(vec2 uv, vec2 size, vec2 center, float angle, vec2 texel){
  vec2 uvi = uv;
  float c = cos(angle);
  float s = sin(angle);
  vec2 pivot = (center + size / 2.0);
  uvi = uvi - pivot;
  uvi *= mat2(vec2(c, -s), vec2(s, c));
  vec2 p = (uvi / size) + pivot + vec2(0.5);
  if(mod(p, 2.0) != p)
    return vec4(0.0);
  p -= vec2(0.5);
  return clamp(glow(p, texel), 0.0, 1.0);
}

vec2 direction(float angle){
  return vec2(cos(angle * 4.0), sin(angle * 4.0));
}

float angle(vec2 p1, vec2 p2){
  return atan(p2.y - p1.y, p2.x - p1.x);
}

vec4 circle(vec2 uv, float size){
  float d = distance(uv * res, position) / max(size, 0.0);
  float p3 = smoothstep(0.81, 0.8, d);
  vec4 borderColor = circleColor * p3;
  borderColor[3] *= 0.25;
  return borderColor;
}

void main(){
    _fragColorOut = vec4(0.0);
  _fragColorOut = texture(texture0, fragTexCoord);
  float rTime = 1.0 - time;
  vec2 ftc = vec2(fragTexCoord.x, 1.0 - fragTexCoord.y);
  vec4 c1 = circle(ftc, rTime * 96.0);
  _fragColorOut = mix(_fragColorOut, c1, c1[3]);
  ivec2 tSize = textureSize(textureLeaves, 0);
  vec2 texel = vec2(1.0) / vec2(float(tSize.y), float(tSize.x));
  vec2 texel2 = vec2(1.0) / vec2(tSize);
  vec2 maxForkSize = texel * 24.0;
  vec4 fColor;
  vec2 fPos, fSize;
  float fAngle, tTime, fOpacity;
  vec2 pp = position / res;
  float overlayOpacity = 1.0 - (max(0.0, rTime - 0.75) * 4.0);
  for(float i = 0.0; i < 64.0; i++){
    tTime = max(rTime - pseudoRandom(i - 5.7) * 2.0, 0.0);
    fOpacity = tTime * 10.0;
    if(tTime > 0.0){
      fPos = pp + direction(pseudoRandom(9.1 + i) * 1.8) * pseudoRandom(i - 3.7) * 4.0 * tTime;
      fSize = maxForkSize * (pseudoRandom(i - 2.0) * 0.5 + 0.5 + (pseudoRandom(4.1 + i) * pseudoRandom(i - 1.4) * 2.0 * tTime));
      if(mod(fPos, max(fSize.x, fSize.y) + 1.0) != fPos)
        continue;
      fPos.y = 1.0 - fPos.y;
      fAngle = pseudoRandom(2.4 + i) + tTime * (pseudoRandom(5.2 + i) - 0.5) * 10.0;
      fColor = leavesColor(fragTexCoord, fSize, fPos, fAngle, texel2);
      _fragColorOut = mix(_fragColorOut, vec4(fColor.rgb, 1.0), fColor[3] * overlayOpacity * fOpacity * 0.4);
    }
  }
}
