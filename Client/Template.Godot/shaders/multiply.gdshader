shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture;
uniform vec4 multiply_color : source_color = vec4(0.5, 0.5, 0.5, 1.0);

void fragment() {
    vec4 screen_color = texture(screen_texture, SCREEN_UV);
    vec3 multiplied = screen_color.rgb * multiply_color.rgb;
    COLOR = vec4(mix(screen_color.rgb, multiplied, multiply_color.a), screen_color.a);
}