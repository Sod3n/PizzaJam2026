shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, blend_mix;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_nearest;
uniform float pixel_size : hint_range(1.0, 100.0) = 24.0;
uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
uniform float brightness : hint_range(0.5, 3.0) = 1.0;
// How far inside the cloud (in p-space units, i.e. quad-widths) the center-white wash
// reaches its peak. Smaller = tighter highlight in the middle.
uniform float gradient_depth : hint_range(0.05, 0.5) = 0.22;
// How strongly to mix toward pure white in the cloud's center. 0 = none, 1 = full white.
uniform float gradient_strength : hint_range(0.0, 1.0) = 0.85;
// Soft edge half-width in UV units (smaller = crisper).
uniform float edge_softness : hint_range(0.001, 0.1) = 0.008;
// quad world height / world width — set by CaughtCensorView each frame so SDF disks
// stay round in WORLD space regardless of how the quad is sized.
uniform float quad_aspect : hint_range(0.1, 5.0) = 1.0;

float smin(float a, float b, float k) {
    float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
    return mix(b, a, h) - k * h * (1.0 - h);
}

float disk(vec2 p, vec2 c, float r) { return length(p - c) - r; }

// Rounded-rectangle SDF (Inigo Quilez) — used as a flat-bottomed cloud base.
float rounded_box(vec2 p, vec2 half_size, float r) {
    vec2 d = abs(p) - half_size + vec2(r);
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
}

// Cartoon cumulus: flat-bottomed pill base with one big right dome and a smaller left dome,
// matching the classic clipart silhouette.
//
// Coordinate convention: p is in WORLD-units / quad-width. We flip UV.x because LookAt
// orients the quad with its back face toward the camera, mirroring UV.x.
float cloud_sdf(vec2 uv) {
    vec2 p = vec2(1.0 - uv.x, 1.0 - uv.y) - vec2(0.5);
    p.y *= quad_aspect;

    const float k = 0.05;

    // Flat-bottomed pill base — wide, low, slight rounded corners.
    float d = rounded_box(p - vec2(0.0, -0.12), vec2(0.45, 0.08), 0.08);

    // Big top-right dome (the dominant puff).
    d = smin(d, disk(p, vec2( 0.10, 0.02), 0.26), k);

    // Smaller top-left dome.
    d = smin(d, disk(p, vec2(-0.24, -0.04), 0.16), k);

    return d;
}

void fragment() {
    float d = cloud_sdf(UV);
    float mask = smoothstep(edge_softness, -edge_softness, d);
    if (mask < 0.001) discard;

    vec2 viewport = VIEWPORT_SIZE;
    vec2 grid = floor(SCREEN_UV * viewport / pixel_size) * pixel_size;
    vec2 sample_uv = (grid + pixel_size * 0.5) / viewport;
    vec3 col = textureLod(SCREEN_TEXTURE, sample_uv, 0.0).rgb;

    // Edge keeps the raw pixelated screen — no tint multiply so reducing brightness
    // can't darken it. brightness now only controls how aggressively the center
    // washes to white.
    vec3 edge_color = col * tint;
    float depth = clamp(-d / gradient_depth, 0.0, 1.0);
    float wash = clamp(depth * gradient_strength * brightness, 0.0, 1.0);
    vec3 final = mix(edge_color, vec3(1.0), wash);

    ALBEDO = final;
    ALPHA = mask;
}
