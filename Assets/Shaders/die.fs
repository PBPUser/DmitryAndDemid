#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float scale;
uniform vec2 pos;

const float offset = 32.;
const vec2 res = vec2(384., 448.);

vec4 invert(vec2 uvs, bool invert, float il){
  vec4 color = texture(texture0, uvs);
  return mix(vec4(invert ? 1.-color.xyz : color.xyz, color[3]), color, il);
}

void main(){
  vec2 ftc = fragTexCoord;
  ftc.y = 1-ftc.y;
  ftc = ftc * res;
  float t = time;
  float il = (max(0.75, t) - .75) * 4.;
  bool j = distance(ftc, pos)>t*448.;
  j = j!=(distance(ftc, pos+vec2(0., offset))<t*448.);
  j = j!=(distance(ftc, pos-vec2(0., offset))<t*448.);
  j = j!=(distance(ftc, pos+vec2(offset, 0.))<t*448.);
  j = j!=(distance(ftc, pos-vec2(offset, 0.))<t*448.);
  j = j!=(distance(ftc, pos)<(t-.2)*448.);  
  if(t > .4){
    t = t - .4;
    j = j!=(distance(ftc, pos)<t*448.);
  }
  gl_FragColor = invert(fragTexCoord, j, il);
}