shader_type canvas_item;

uniform vec4 outline_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float outline_width : hint_range(1.0, 20.0, 1.0) = 6.0;

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float entity = step(0.5, c.a) * step(0.02, c.r + c.g + c.b);

    if (entity > 0.5) {
        COLOR = vec4(0.0);
    } else {
        // Only sample downward and sideways — never upward.
        // This draws outline above and beside the entity but not below
        // (where it meets the ground).
        // Screen UV: y+ = down, so vec2(0, +w) looks downward toward entity
        // from pixels above it, which is what we want.
        vec2 px = TEXTURE_PIXEL_SIZE;
        float w = outline_width;
        float near = 0.0;
        vec4 s;
        s = texture(TEXTURE, UV + vec2( w,  0) * px); near = max(near, step(0.5, s.a) * step(0.02, s.r + s.g + s.b));
        s = texture(TEXTURE, UV + vec2(-w,  0) * px); near = max(near, step(0.5, s.a) * step(0.02, s.r + s.g + s.b));
        s = texture(TEXTURE, UV + vec2( 0,  w) * px); near = max(near, step(0.5, s.a) * step(0.02, s.r + s.g + s.b));
        s = texture(TEXTURE, UV + vec2( w,  w) * px); near = max(near, step(0.5, s.a) * step(0.02, s.r + s.g + s.b));
        s = texture(TEXTURE, UV + vec2(-w,  w) * px); near = max(near, step(0.5, s.a) * step(0.02, s.r + s.g + s.b));
        COLOR = near > 0.5 ? outline_color : vec4(0.0);
    }
}
