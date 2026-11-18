shader_type canvas_item;

// Paper texture of panels and buttons (CLIENT_V05C_SPEC §3.3, revised): a grey multiplier on the colour the node draws itself, luminance only.
// k is in [0.955, 1.02] inside; down to 0.90 only in the edge band (e <= 8.5 du), where no text lives (T6).
// Children keep no material (use_parent_material is false), so text, icons, alert plates, the halo, the map and the globe are never textured.
// The formula is mirrored in PaperTexture.K (CPU, tested); keep both in step.

global uniform float tex_strength;   // 0 off, 0.5 weak, 1 normal (Settings)

uniform sampler2D tile : repeat_enable, filter_linear;   // R grain, G fibre, B blotches; 384 px = 192 du
uniform vec2 rect_size = vec2(100.0);
uniform vec2 tile_off = vec2(0.0);
uniform vec3 amp = vec3(0.02, 0.03, 0.035);   // grain, fibre, blotch
uniform float wear = 0.075;
uniform float wear_w = 7.0;
uniform float press = 0.0;
uniform vec2 rub_c = vec2(0.5);

void fragment() {
	vec2 px = UV * rect_size;
	vec2 t = tile_off.x > 0.5 ? px.yx : px;   // fibre direction by node hash
	vec3 n = texture(tile, t / 192.0 + tile_off).rgb;
	float e = min(min(px.x, rect_size.x - px.x), min(px.y, rect_size.y - px.y));
	float k_int = clamp(0.99 + 2.0 * dot(amp, n - 0.5), 0.955, 1.02);
	float e2 = e + 1.5 * (n.b - 0.5) + 1.0 * (n.r - 0.5);
	float band = 1.0 - smoothstep(0.0, wear_w, e2);
	vec2 r = (px - rub_c * rect_size) / vec2(18.0, 9.0);
	float rub = 1.0 - smoothstep(0.0, 1.0, length(r));
	float k = k_int - wear * band * (1.0 + 0.5 * press) + 0.02 * press * rub;
	k = 1.0 + tex_strength * (k - 1.0);
	COLOR.rgb *= clamp(k, e <= 8.5 ? 0.90 : 0.955, 1.02);
}
