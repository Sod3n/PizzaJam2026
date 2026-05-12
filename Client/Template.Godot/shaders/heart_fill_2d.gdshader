shader_type canvas_item;

uniform float fill : hint_range(0.0, 1.0, 0.01) = 0.0;
uniform vec3 empty_tint : source_color = vec3(0.25, 0.4, 0.65);
uniform float empty_tint_strength : hint_range(0.0, 1.0, 0.01) = 0.75;
uniform float wave_amplitude : hint_range(0.0, 0.1, 0.005) = 0.0175;
uniform float wave_frequency : hint_range(0.0, 30.0, 0.1) = 9.0;
uniform float wave_speed : hint_range(0.0, 10.0, 0.05) = 2.5;
uniform float wave_thickness : hint_range(0.0, 0.2, 0.005) = 0.045;
uniform vec3 wave_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float wave_strength : hint_range(0.0, 1.0, 0.01) = 0.85;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    if (tex.a < 0.5) discard;

    vec3 col = tex.rgb;
    float wave_offset = sin(UV.x * wave_frequency + TIME * wave_speed) * wave_amplitude;
    wave_offset += sin(UV.x * wave_frequency * 0.5 - TIME * wave_speed * 0.7) * wave_amplitude * 0.5;
    float fill_line = (1.0 - fill) + wave_offset;

    float empty_mask = smoothstep(fill_line + 0.02, fill_line - 0.02, UV.y);
    vec3 tinted = mix(col * 0.4, empty_tint, 0.65);
    col = mix(col, tinted, empty_mask * empty_tint_strength);

    float dist = abs(UV.y - fill_line);
    float wave_mask = (1.0 - smoothstep(0.0, wave_thickness, dist)) * step(0.001, fill) * step(fill, 0.999);
    col = mix(col, wave_color, wave_mask * wave_strength);

    COLOR = vec4(col, tex.a);
}
