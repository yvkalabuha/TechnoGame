shader_type spatial;
render_mode unshaded, cull_back;

// Surface A "Globe-sheet": flat tints from an ID raster, ink borders in screen pixels,
// screen-space hatching, 30-degree graticule. No lighting, no glow.
uniform sampler2D ids : filter_nearest, repeat_disable;
uniform sampler2D tint_lut : filter_nearest, repeat_disable; // 256x1, R = tint index 0..7
uniform vec3 tints[8];
uniform vec3 sea_color : source_color = vec3(0.827, 0.882, 0.894);
uniform vec3 coast_color : source_color = vec3(0.494, 0.588, 0.612);
uniform vec3 border_color : source_color = vec3(0.243, 0.235, 0.22);
uniform vec3 grid_color : source_color = vec3(0.62, 0.72, 0.75);
uniform vec3 ink : source_color = vec3(0.114, 0.118, 0.125);
uniform float border_px = 1.2;
uniform int selected_id = 0;
uniform int hatch_id = 0;        // demo "occupied" country
uniform int hatch_owner_id = 0;  // colour source for the hatch lines
uniform float hatch_period = 6.0;
uniform float hatch_width = 1.5;
uniform bool hatch_in_screen_space = true;
uniform bool show_hatch = true;

const vec2 SZ = vec2(4096.0, 2048.0);

int id_at(vec2 uv) {
	ivec2 p = ivec2(clamp(uv, vec2(0.0), vec2(0.99999)) * SZ);
	return int(texelFetch(ids, p, 0).r * 255.0 + 0.5);
}
vec3 tint_of(int id) {
	int t = int(texelFetch(tint_lut, ivec2(id, 0), 0).r * 255.0 + 0.5);
	return tints[t];
}
vec3 line_of(vec3 t) { return t * 0.42; } // stand-in for tint.N.line (L* 40-45)

void fragment() {
	vec2 uv = UV;
	int id = id_at(uv);
	vec3 col = (id == 0) ? sea_color : tint_of(id);
	if (id == selected_id && id != 0) col = mix(col, ink, 0.14);

	if (show_hatch && id == hatch_id && id != 0) {
		vec2 q = hatch_in_screen_space ? FRAGCOORD.xy : uv * SZ * 0.5;
		float s = (q.x + q.y) / 1.41421356; // 45 degrees
		float d = abs(fract(s / hatch_period) - 0.5) * hatch_period;
		float h = 1.0 - smoothstep(hatch_width * 0.5 - 0.5, hatch_width * 0.5 + 0.5, d);
		col = mix(col, line_of(tint_of(hatch_owner_id)), h * 0.9);
	}

	// graticule 30 deg
	vec2 g = uv * vec2(12.0, 6.0);
	vec2 gf = fwidth(g);
	vec2 gd = abs(fract(g - 0.5) - 0.5) / max(gf, vec2(1e-5));
	float grid = 1.0 - smoothstep(0.0, 1.0, min(gd.x, gd.y));
	col = mix(col, grid_color, grid * 0.6);

	// borders: neighbours half a screen pixel away; thickness stays constant in pixels
	vec2 dx = dFdx(uv), dy = dFdy(uv);
	float k = border_px * 0.5;
	int n0 = id_at(uv + dx * k), n1 = id_at(uv - dx * k), n2 = id_at(uv + dy * k), n3 = id_at(uv - dy * k);
	float cnt = 0.0; float coast = 0.0;
	if (n0 != id) { cnt += 1.0; if (n0 == 0 || id == 0) coast += 1.0; }
	if (n1 != id) { cnt += 1.0; if (n1 == 0 || id == 0) coast += 1.0; }
	if (n2 != id) { cnt += 1.0; if (n2 == 0 || id == 0) coast += 1.0; }
	if (n3 != id) { cnt += 1.0; if (n3 == 0 || id == 0) coast += 1.0; }
	float ndv = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float fade = smoothstep(0.0, 0.26, ndv); // alpha fades over the last ~15 deg at the limb
	float a = cnt * 0.25 * fade;
	bool is_coast = coast > cnt * 0.5;
	vec3 bc = is_coast ? coast_color : border_color;
	if (id == selected_id || n0 == selected_id) { if (selected_id != 0) { bc = ink; a = min(1.0, a * 1.6); } }
	col = mix(col, bc, a);
	ALBEDO = col;
}
