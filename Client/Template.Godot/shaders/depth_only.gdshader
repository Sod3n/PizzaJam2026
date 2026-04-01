shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;

void fragment() {
    ALBEDO = vec3(0.0);
    ALPHA = 0.99;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}
