shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform sampler2D cloud_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec2  wind          = vec2(0.6, 0.25);
uniform float cloud_scale   = 0.008;     // smaller = bigger, slower clouds
uniform float coverage      : hint_range(0.0, 1.0) = 0.88;  // higher = rarer
uniform float softness      : hint_range(0.001, 0.3) = 0.06;
uniform float layer2_mix    : hint_range(0.0, 1.0) = 0.5;

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Sample in world XZ so the pattern is anchored to the world,
    // not to the plane (lets you move the plane with the camera).
    vec2 uv1 = world_pos.xz * cloud_scale          + wind * TIME * 0.02;
    vec2 uv2 = world_pos.xz * cloud_scale * 2.3    - wind * TIME * 0.013;

    float n1 = texture(cloud_tex, uv1).r;
    float n2 = texture(cloud_tex, uv2).r;
    float n = n1 * mix(1.0, 0.5 + n2 * 0.5, layer2_mix); // breaks up tiling, keeps lumpy shapes
	n = smoothstep(0.45, 0.75, n);
    // Only the densest peaks become clouds → sparse shadows
    float cloud = smoothstep(coverage - softness, coverage + softness, n);

    ALBEDO = vec3(0.0);
    ALPHA  = cloud;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}