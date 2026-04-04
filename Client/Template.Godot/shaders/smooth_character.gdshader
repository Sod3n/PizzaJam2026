shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D albedo_texture : filter_linear, source_color;
uniform float wind_strength : hint_range(0.0, 1.0) = 0.0;
uniform float wind_speed : hint_range(0.0, 5.0) = 1.0;
uniform float pixels_per_unit = 16.0;
uniform vec4 color_replace : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void vertex() {
    float move = sin(TIME * wind_speed + NODE_POSITION_WORLD.x) * (1.0 - UV.y) * wind_strength;
    float unit = 1.0 / pixels_per_unit;
    VERTEX.x += round(move / unit) * unit;
}

void fragment() {
    // Sample with original UV (no pixel snapping)
    vec4 tex = texture(albedo_texture, UV);

    if (tex.a < 0.5) discard;

    if (!FRONT_FACING) NORMAL = -NORMAL;

    // color_replace: replaces non-lineart pixels (white = no replace)
    float brightness = max(tex.r, max(tex.g, tex.b));
    float lineart_mask = smoothstep(0.15, 0.3, brightness);
    float replace_active = 1.0 - smoothstep(0.95, 1.0, min(color_replace.r, min(color_replace.g, color_replace.b)));
    vec3 replaced = mix(tex.rgb, color_replace.rgb, lineart_mask * replace_active);
    ALBEDO = replaced * COLOR.rgb;
    ALPHA = tex.a * COLOR.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
