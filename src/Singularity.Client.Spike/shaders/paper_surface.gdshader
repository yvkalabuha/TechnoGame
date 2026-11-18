shader_type canvas_item;

// Paper texture of panels and buttons (CLIENT_V05C_SPEC §3): a grey multiplier on the colour the node draws itself, luminance only, k in [0.94, 1.02].
// Children keep no material (use_parent_material is false), so text, icons, alert plates, the halo, the map and the globe are never textured.
// The formula is mirrored in PaperTexture.K (CPU, tested); keep both in step.

global uniform float tex_strength;   // 0 off, 0.5 weak, 1 normal (Settings)

uniform sampler2D tile : repeat_enable, filter_linear;   // R grain, G fibre, B blotches; 256 px = 128 du
uniform vec2 rect_size = vec2(100.0);
uniform vec2 tile_off = vec2(0.0);
uniform vec3 amp = vec3(0.015, 0.02, 0.02);   // grain, fibre, blotch
uniform float wear = 0.04;
uniform float wear_w = 6.0;
uniform float press = 0.0;
uniform vec2 rub_c = vec2(0.5);

void fragment() {
	vec2 px = UV * rect_size;
	vec3 n = texture(tile, px / 128.0 + tile_off).rgb;
	float e = min(min(px.x, rect_size.x - px.x), min(px.y, rect_size.y - px.y));
	float wear_mask = 1.0 - smoothstep(0.0, wear_w, e + 1.5 * (n.b - 0.5));
	vec2 r = (px - rub_c * rect_size) / vec2(18.0, 9.0);
	float rub = 1.0 - smoothstep(0.0, 1.0, length(r));
	float k = 1.0 + 2.0 * dot(amp, n - 0.5) - wear * wear_mask * (1.0 + 0.5 * press) + 0.02 * press * rub;
	k = clamp(1.0 + tex_strength * (k - 1.0), 0.94, 1.02);
	COLOR.rgb *= k;
}
