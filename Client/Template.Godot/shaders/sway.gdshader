shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

uniform sampler2D albedo_texture : filter_linear, source_color;
uniform float wind_strength : hint_range(0.0, 1.0, 0.02) = 0.1;
uniform float wind_speed : hint_range(0.5, 5.0, 0.1) = 1.0;
uniform vec4 color_replace : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float cooldown_fill : hint_range(0.0, 1.0, 0.01) = 0.0;
uniform float wave_amplitude : hint_range(0.0, 0.1, 0.005) = 0.0175;
uniform float wave_frequency : hint_range(0.0, 30.0, 0.1) = 9.0;
uniform float wave_speed : hint_range(0.0, 10.0, 0.05) = 2.5;
uniform float wave_thickness : hint_range(0.0, 0.2, 0.005) = 0.01;
uniform vec3 wave_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float wave_strength : hint_range(0.0, 1.0, 0.01) = 0.85;

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

    // color_replace: fully replaces non-lineart pixels (white = no replace)
    // Hard cutoff so different base textures produce identical results
    // COLOR (modulate): multiplies the final result as usual
    float brightness = max(tex.r, max(tex.g, tex.b));
    float lineart_mask = step(0.2, brightness);
    float replace_active = 1.0 - step(0.95, min(color_replace.r, min(color_replace.g, color_replace.b)));
    vec3 replaced = mix(tex.rgb, color_replace.rgb, lineart_mask * replace_active);
    // Cooldown fill: darken from top (UV.y=0) down to cooldown_fill fraction
    // UV.y 0 = top, 1 = bottom; fill_line = 1 - cooldown_fill maps to the cutoff
    float wave_offset = sin(UV.x * wave_frequency + TIME * wave_speed) * wave_amplitude;
    wave_offset += sin(UV.x * wave_frequency * 0.5 - TIME * wave_speed * 0.7) * wave_amplitude * 0.5;
    float fill_line = (1.0 - cooldown_fill) + wave_offset;
    float darken = smoothstep(fill_line - 0.02, fill_line + 0.02, 1.0 - UV.y) * 0.7;
    replaced = mix(replaced, replaced * 0.25, darken);

    float wave_dist = abs((1.0 - UV.y) - fill_line);
    float wave_mask = (1.0 - smoothstep(0.0, wave_thickness, wave_dist)) * step(0.001, cooldown_fill) * step(cooldown_fill, 0.999);
    replaced = mix(replaced, wave_color, wave_mask * wave_strength);

    ALBEDO = replaced * COLOR.rgb;
    ALPHA = tex.a * COLOR.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
