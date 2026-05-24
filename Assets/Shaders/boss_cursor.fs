#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float time;
uniform float realTime;
uniform float scale;
uniform vec2 position;
uniform sampler2D textureCursor;
const vec2 res = vec2(384., 448.);
const float size = 192.;
const float bloomSpread = 5;
const float bloomIntensity = 5;
const vec4 color = vec4(0,.7,.2,1);

vec2 zoom(vec2 coord, vec2 pivot, float strength){
    return (coord-pivot) * strength + pivot;
}

vec2 rotate(vec2 coord, vec2 pivot, float x, float y, float z){
    float sx = sin(x), sy = sin(y), sz = sin(z), cx = cos(x), cy = cos(y), cz = cos(z);
    vec3 p = vec3(coord, 1);
    p -= vec3(pivot, 1);
    mat3 mx = mat3(vec3(1,0,0), vec3(0,cx,-sx),vec3(0, sx, cx));
    mat3 my = mat3(vec3(cy,0,sy),vec3(0,1,0),vec3(-sy,0,cy));
    mat3 mz = mat3(vec3(cz,-sz,0),vec3(sz,cz,0),vec3(0,0,1));
    p *= mx;
    p *= my;
    p *= mz;
    p += vec3(pivot,1);
    return p.xy / p.z;
}

float tex(vec2 uv){
    if(mod(abs(uv),1) != uv)
        return 0;
    return texture(textureCursor, uv)[0];
}

float bloom(vec2 uv){
    vec2 texelSize = vec2(1)/vec2(textureSize(textureCursor,0));
    float sum = 0, hsum = 0;
    for(float n = 0; n < 9; n++){
        hsum = 0;
        for(float i = 0; i < 9; i++)
            hsum += tex(uv + texelSize * bloomSpread * vec2(n-4,i-4));
        sum += hsum / 9; 
    }
    return sum / 9 * bloomIntensity;
}

void main(){
    gl_FragColor = texture(texture0, fragTexCoord);
    vec2 tSize = vec2(textureSize(textureCursor, 0));
    vec2 ftc=vec2(fragTexCoord.x, 1-fragTexCoord.y);
    vec2 p = (((ftc * res) - position) / size) * vec2(res.x/res.y * tSize.y/tSize.x,1) + vec2(.5);
    p = rotate(p, vec2(.5), sin(cos(realTime) * 2) * 0.3, cos(realTime * 2 + sin(ftc.x * 20 * cos(realTime)) * 0.1) * 0.3, realTime*2);
    p = zoom(p, vec2(.5), 1 + sin(realTime * 1.25) * 0.15);
    gl_FragColor = mix(gl_FragColor, color, bloom(p));
}