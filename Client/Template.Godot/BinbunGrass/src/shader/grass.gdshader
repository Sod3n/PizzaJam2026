shader_type spatial;
render_mode depth_draw_opaque;

uniform bool billboard = false;
uniform sampler2D shape_texture;
uniform sampler2D shape_atlas;
uniform bool use_atlas = false;

group_uniforms Colors;
uniform sampler2D noise_texture;
uniform sampler2D color_gradient;
uniform float random_variation : hint_range(0.0, 1.0, 0.001) = 0.002;

group_uniforms Wind;
uniform sampler2D wind_texture;
uniform vec2 wind_velocity;

group_uniforms Transparency;
uniform float alpha_cut_start : hint_range(0.0, 1.0, 0.05) = 0.1;
uniform float alpha_cut_end : hint_range(0.0, 1.0, 0.05) = 0.9;
uniform int alpha_mode : hint_enum("Smooth", "Dithered", "Cut") = 2;

varying flat int id;
varying vec3 world_pos;
varying vec3 v_color;

#include "util/dither.gdshaderinc"

vec2 atlas_uv(vec2 uv, int index, int size) {
    int tile_count = size * size;
    int i = index % tile_count;
    float x = float(i % size);
    float y = float(i / size);
    vec2 tile_size = 1.0 / vec2(float(size));
    return uv * tile_size + (vec2(x, y) * tile_size);
}

float randomf(int index, int seed) {
    return sin(float(index + seed)) * 0.5 + 0.5;
}

float wind_noise() {
    return texture(wind_texture, (world_pos.xz * 0.03) - (vec2(TIME * 0.01) * wind_velocity)).r;
}

vec3 wind(vec2 uv) {
    float n = wind_noise();
    float wind_affect = pow(1.0 - uv.y, 2.0);
    vec3 value = vec3(n * wind_velocity.x, 0.0, n * wind_velocity.y) * 0.25;
    return value * wind_affect;
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    id = INSTANCE_ID;
    
    if (billboard) {
        mat4 billboard_matrix = mat4(
            MAIN_CAM_INV_VIEW_MATRIX[0],
            MAIN_CAM_INV_VIEW_MATRIX[1],
            MAIN_CAM_INV_VIEW_MATRIX[2],
            MODEL_MATRIX[3]);
        MODELVIEW_MATRIX = VIEW_MATRIX * billboard_matrix;
        VERTEX += (VIEW_MATRIX * vec4(wind(UV), 0.0)).xyz;
    } else {
        VERTEX += wind(UV);
    }
    
    // Color resolved per-vertex; interpolates across the blade.
    // Saves 2 fragment texture samples per pixel.
    float noise_value = texture(noise_texture, world_pos.xz * 0.1).r;
    if (randomf(id, 1) < random_variation) noise_value += 0.4;
    v_color = texture(color_gradient, vec2(noise_value, 0.0)).rgb;
    
    NORMAL = vec3(0.0, 1.0, 0.0);
}

void fragment() {
    float shape_value = texture(shape_texture, UV).r;
    if (use_atlas) {
        vec2 a_uv = atlas_uv(UV, id, 2);
        shape_value = texture(shape_atlas, a_uv).r;
    }
    
    ALBEDO = v_color;
    
    float alpha = shape_value;
    if (alpha_mode == 0) {
        // Smooth — true transparency. Heavy overdraw, avoid on low-end.
        ALPHA = alpha;
    } else {
        alpha = clamp((shape_value - alpha_cut_start) / (alpha_cut_end - alpha_cut_start), 0.0, 1.0);
        if (alpha_mode == 1) {
            // Dithered — stochastic transparency, no sort needed.
            alpha = step(bayer4(FRAGCOORD.xy) + 0.01, alpha);
        }
        if (alpha < 0.1) discard;
        ALPHA = alpha;
    }
    
    ALPHA_SCISSOR_THRESHOLD = 0.5;
}

void light() {
    float ndotl = dot(LIGHT, NORMAL) * ATTENUATION;
    DIFFUSE_LIGHT += ndotl;
}