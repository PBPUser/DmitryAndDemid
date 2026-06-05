#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform sampler2D textureLeaves;
uniform float time;
uniform float scale;
uniform vec2 position;


const int count = 16;
const float offset = 32.;
const vec2 res = vec2(384., 448.);
const float bloomSpread = 5;
const float bloomIntensity = 5;

vec4 invert(vec2 uvs, bool invert, float il){
  vec4 color = texture(texture0, uvs);
  return mix(vec4(invert ? 1.-color.xyz : color.xyz, color[3]), color, il);
}

vec4 c1leavesColor(vec2 uv){
  if(mod(uv,1) != uv)
    return vec4(0);
  return texture(textureLeaves, uv);
}

vec4 bloomLeavesColor(vec2 uv){
  vec2 texelSize = vec2(1)/res;
  vec4 sum = vec4(0);
  for(float n = 0; n < 9; n++){
    vec4 hs = vec4(0);
    for(float i = 0; i < 9; i++)
      hs+=c1leavesColor(uv + texelSize * bloomSpread * vec2(n-4,i-4));
     sum += hs / 9;  
    }
  return c1leavesColor(uv) + sum / 9. * bloomIntensity;
}

float pseudoRandom(float j){
  return mod(sin(tan((cos(position.x) + position.y) * j)) + 1000, 1);
}

vec4 leavesColor(vec2 uv, vec2 size, vec2 center, float angle){
  vec2 uvi = uv;
  float c = cos(angle);
  float s = sin(angle);
  vec2 pivot = (center + size / 2);
  uvi = uvi - pivot;
  uvi *= mat2(vec2(c,-s),vec2(s,c));
  vec2 p = (uvi / size) + pivot;
  vec4 tc = texture(textureLeaves, p);
  return bloomLeavesColor(p);
}

vec2 direction(float angle){
  return vec2(cos(angle * 4), sin(angle * 4));
}


void main(){
  ivec2 tSize = textureSize(textureLeaves, 0);
  
  vec2 texel = vec2(1)/res;
  vec2 maxForkSize = texel * 96;
  vec2 ftc = fragTexCoord;
  mat4x2[16] arr;
  for(int i = 0; i < arr.length(); i++)
  {
    arr[i][0] = vec2(pseudoRandom(2.4+i), pseudoRandom(i-2.0) + 1);
    arr[i][1] = vec2(pseudoRandom(4.1+i), pseudoRandom(i-5.7) * 2);
    arr[i][2] = vec2((pseudoRandom(5.2+i) - .5) * 10, (pseudoRandom(-1.4)) * 2);
    arr[i][3] = vec2(pseudoRandom(9.1+i), 3 * (pseudoRandom(i-3.7) * 0.5 + 0.5));
  }
  ftc.y = 1-ftc.y;
  ftc = ftc * res;
  float t = clamp(time * 3 - 1, 0, 1);
  float il = (max(0.75, t) - .75) * 4.;
  bool j = distance(ftc, position)>t*448.;
  j = j!=(distance(ftc, position+vec2(0., offset))<t*448.);
  j = j!=(distance(ftc, position-vec2(0., offset))<t*448.);
  j = j!=(distance(ftc, position+vec2(offset, 0.))<t*448.);
  j = j!=(distance(ftc, position-vec2(offset, 0.))<t*448.);
  j = j!=(distance(ftc, position)<(t-.2)*448.);  
  if(t > .4){
    t = t - .4;
    j = j!=(distance(ftc, position)<t*448.);
  }
  vec4 _color = texture(textureLeaves, fragTexCoord);
  j = j && t > 0;
  gl_FragColor = invert(fragTexCoord, j, il);
  vec4 fColor;
  vec2 fPos;
  vec2 fSize;
  float fAngle;
  float tTime;
  vec2 pp = position / res;
  float overlayOpacity = 1 - (max(0, time-0.75) * 4);
  for(int i = 0; i < arr.length(); i++){
    tTime = max(time - arr[i][1][1],0);
    fPos = pp + direction(arr[i][3][0]) * arr[i][3][1] * tTime;
    fPos.y = 1 - fPos.y;
      fSize = maxForkSize * arr[i][0][1] * (arr[i][1][0] * arr[i][2][1] * tTime);
    fAngle = arr[i][0][0] + tTime * arr[i][2][0];
    fColor = leavesColor(fragTexCoord, fSize, fPos, fAngle);
    gl_FragColor = mix(gl_FragColor, vec4(fColor.rgb,1), fColor[3] * overlayOpacity);
  }
}