shader_type spatial;
render_mode unshaded, cull_disabled, depth_test_disabled;

uniform sampler2D albedo_texture : filter_nearest, source_color;
uniform float wind_strength : hint_range(0.0, 1.0, 0.02) = 0.0;
uniform float wind_speed : hint_range(0.5, 5.0, 0.1) = 2.0;

void vertex() {
    float move = sin(TIME * wind_speed + NODE_POSITION_WORLD.x) * (1.0 - UV.y) * wind_strength;
    VERTEX.x += move;
}

void fragment() {
    float a = texture(albedo_texture, UV).a;
    if (a < 0.5) discard;
    ALBEDO = vec3(1.0);
    ALPHA = 1.0;
}
