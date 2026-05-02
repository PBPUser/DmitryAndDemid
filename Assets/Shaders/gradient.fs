#version 330

// Input vertex attributes (from vertex shader)
in vec2 fragTexCoord;
in vec2 uv;
in vec4 fragColor;

// Input uniform values
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform float border_width;
uniform vec2 res;

const vec3 from_color = vec3(1.);
const vec3 to_color = vec3(.6);
const vec4 border_color = vec4(0.,0.,0.,1.);


float getSmoothColor(vec2 uv, float border_width){
    float dx = 1. / res.x * border_width;
    float dy = 1. / res.y * border_width;
    
    float color = 0.;
    for(int x = 0; x < border_width; x++)
    for(int y = 0; y < border_width; y++)
        color += texture(texture0, uv+vec2(dx*(x-border_width/2), dy*(y-border_width/2)))[3] / max(1., abs((x-border_width)*(y-border_width)));
    color /= pow(border_width, 2.);
    return color;
}

float delta(vec2 uv1, vec2 uv2){
  return abs(getSmoothColor(uv1, 1.) - getSmoothColor(uv2, 1.));
}
void main(){
    vec4 color = texture(texture0, fragTexCoord);
    float dx = 1. / res.x * border_width;
    float dy = 1. / res.y * border_width;
    float delta_value = 0.;
    delta_value = max(delta_value, delta(fragTexCoord, vec2(dx*0.,-1.*dy) + fragTexCoord));
    delta_value = max(delta_value, delta(fragTexCoord, vec2(-1.*dx,0.*dy) + fragTexCoord));
    delta_value = max(delta_value, delta(fragTexCoord, vec2(-1.*dx,-1.*dy) + fragTexCoord));
    vec4 color_grad = vec4(mix(from_color,to_color,uv.y+2.-1.), color[3]);
    gl_FragColor = mix(color_grad, border_color, delta_value);
}
