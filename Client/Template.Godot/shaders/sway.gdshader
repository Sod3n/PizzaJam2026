shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D albedo_texture : filter_linear, source_color;
uniform float wind_strength : hint_range(0.0, 1.0, 0.02) = 0.1;
uniform float wind_speed : hint_range(0.5, 5.0, 0.1) = 1.0;
uniform vec4 color_replace : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void vertex() {
    // Simple wind: sway the top (UV.y = 0.0) and keep bottom (UV.y = 1.0) still
    float move = sin(TIME * wind_speed + NODE_POSITION_WORLD.x) * (1.0 - UV.y) * wind_strength;
    VERTEX.x += move;
}

void fragment() {
    vec4 tex = texture(albedo_texture, UV);
    if (tex.a < 0.5) discard;

    // Fix normal when flipped by negative scale
    if (!FRONT_FACING) NORMAL = -NORMAL;

    // color_replace: replaces non-lineart pixels (white = no replace)
    // COLOR (modulate): multiplies the final result as usual
    float brightness = max(tex.r, max(tex.g, tex.b));
    float lineart_mask = smoothstep(0.15, 0.3, brightness);
    float replace_active = 1.0 - smoothstep(0.95, 1.0, min(color_replace.r, min(color_replace.g, color_replace.b)));
    vec3 replaced = mix(tex.rgb, color_replace.rgb, lineart_mask * replace_active);
    ALBEDO = replaced * COLOR.rgb;
    ALPHA = tex.a * COLOR.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
