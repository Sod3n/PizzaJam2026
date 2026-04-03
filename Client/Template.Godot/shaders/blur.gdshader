shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform int samples = 35;
uniform int lod = 2;
uniform vec4 tint_color : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 multiply_color_top : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 multiply_color_bottom : source_color = vec4(0.5, 0.5, 0.5, 1.0);
uniform float multiply_curve : hint_range(0.1, 5.0) = 1.0;
uniform vec4 dodge_color_start : source_color = vec4(1.0, 1.0, 1.0, 0.5);
uniform vec4 dodge_color_mid : source_color = vec4(0.5, 0.5, 0.5, 0.25);
uniform float dodge_mid_point : hint_range(0.0, 1.0) = 0.5;
uniform vec4 dodge_color_end : source_color = vec4(0.0, 0.0, 0.0, 0.0);
uniform float dodge_curve : hint_range(0.1, 5.0) = 1.0;
uniform float dodge_angle : hint_range(0.0, 360.0) = 135.0;
uniform float dodge_smoothness : hint_range(0.0, 1.0) = 0.0;

float gaussian(vec2 i, float sigma) {
    return exp(-0.5 * dot(i /= sigma, i)) / (6.28 * sigma * sigma);
}

vec4 blur(sampler2D text, vec2 uv, vec2 pixel_size) {
    vec4 original_color = vec4(0.0);
    float sigma = float(samples) * 0.25;
    int sLOD = 1 << lod;
    int s = samples / sLOD;
    for (int i = 0; i < s * s; i++) {
        vec2 offset = vec2(float(i % s), float(i / s)) * float(sLOD) - float(samples) / 2.0;
        original_color += gaussian(offset, sigma) * textureLod(text, uv + pixel_size * offset, float(lod));
    }
    return original_color.a > 0.0 ? original_color / original_color.a : vec4(0.0);
}

vec3 color_dodge(vec3 base, vec3 blend) {
    return clamp(base / max(1.0 - blend, 0.001), 0.0, 1.0);
}

vec4 sample_gradient(float t, vec4 start, vec4 mid, vec4 end, float mid_pos) {
    if (t < mid_pos) {
        return mix(start, mid, smoothstep(0.0, mid_pos, t));
    } else {
        return mix(mid, end, smoothstep(mid_pos, 1.0, t));
    }
}

void fragment() {
    // Step 1: Blur
    vec4 blurred = blur(screen_texture, SCREEN_UV, SCREEN_PIXEL_SIZE);
    // Step 2: Tint
    vec3 tinted = mix(blurred.rgb, tint_color.rgb, tint_color.a);
    // Step 3: Multiply gradient (top to bottom)
    float mt = pow(UV.y, multiply_curve);
    vec4 mul_grad = mix(multiply_color_top, multiply_color_bottom, mt);
    vec3 multiplied = tinted * mul_grad.rgb;
    vec3 after_multiply = mix(tinted, multiplied, mul_grad.a);
    // Step 4: Color dodge with 3-stop rotatable gradient
    float rad = radians(dodge_angle);
    vec2 dir = vec2(cos(rad), sin(rad));
    vec2 centered = UV - 0.5;
    float raw_t = clamp(dot(centered, dir) + 0.5, 0.0, 1.0);
    float dt = pow(raw_t, dodge_curve);
    // Blend between linear and smooth interpolation
    vec4 linear_grad = sample_gradient(dt, dodge_color_start, dodge_color_mid, dodge_color_end, dodge_mid_point);
    vec4 dodge_grad = linear_grad;
    vec3 dodged = color_dodge(after_multiply, dodge_grad.rgb);
    vec3 final_rgb = mix(after_multiply, dodged, dodge_grad.a);
    COLOR = vec4(final_rgb, blurred.a);
}