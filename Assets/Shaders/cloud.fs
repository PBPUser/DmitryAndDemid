#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec4 fragColor;

const float PI = 3.14159265358979323846;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float radius;
uniform vec2 dimenssions;
uniform float angle;
uniform float width;
uniform float size;


// Output fragment color
out vec4 finalColor;

float superElipse(vec2 coord, vec2 dims, float n){
    float preElipse = pow(coord.x/dims.x,n) + pow(coord.y/dims.y,n);
    return 1.-smoothstep(preElipse, 1., 0.);
}

float normalizeAngle(float a){
  return mod(a+PI, PI*2.)-PI;
}

float getAngleDiff(float angle1, float angle2){
  float diff = abs(normalizeAngle(angle1) - normalizeAngle(angle2));
  float diffV = abs(normalizeAngle(angle1+(PI/2.)) - normalizeAngle(angle2+(PI/2.)));
  if(diff - diffV > 0.1)
    diff = PI * 2. - diff;
  return diff;
}

float getArrowLevel(float a, float target, float size){
  float diff = getAngleDiff(a, target);
  return max(size - diff, 0.)/size;
}

float getAngle(vec2 coords)
{
  float angle = atan(coords.y / coords.x);
  if(coords.x < 0.)
    angle += PI;
  return normalizeAngle(angle);
}

void main()
{
    vec2 c = fragTexCoord * 2. - 1.;
    vec2 ac = abs(c) / pow(0.5, 0.5);
    float _angle = getAngle(c);
    float acMix = getArrowLevel(_angle, angle, width);
    float mirrorAngle = atan(ac.y/ac.x);
    float mirrorAngleLevel = 1.+sin(_angle*6.)*(0.03*sin(_angle)+0.03);
    vec2 fillCoords = mix(ac*mirrorAngleLevel, ac/size, acMix);
    vec2 borderCoords = fillCoords * 0.9;
    float fill = superElipse(fillCoords, dimenssions, radius);
    float transparency = superElipse(borderCoords, dimenssions, radius);
    finalColor = vec4(vec3(fill), transparency);
}
