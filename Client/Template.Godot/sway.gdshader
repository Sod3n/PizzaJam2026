shader_type spatial;
render_mode blend_mix, cull_disabled, depth_prepass_alpha;

uniform sampler2D albedo_texture : filter_linear, source_color;
uniform float wind_strength : hint_range(0.0, 1.0, 0.02) = 0.1;
uniform float wind_speed : hint_range(0.5, 5.0, 0.1) = 1.0;
uniform float transparency : hint_range(0.0, 1.0, 0.01) = 1.0;

void vertex() {
    // Simple wind: sway the top (UV.y = 0.0) and keep bottom (UV.y = 1.0) still
    float move = sin(TIME * wind_speed + NODE_POSITION_WORLD.x) * (1.0 - UV.y) * wind_strength;
    VERTEX.x += move;
}

void fragment() {
    vec4 tex = texture(albedo_texture, UV);
    ALBEDO = tex.rgb;
    ALPHA = tex.a * COLOR.a * transparency;
}
