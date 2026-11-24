shader_type spatial;
render_mode unshaded, cull_back;

// Surface A "Globe-sheet": flat tints from an ID raster, ink borders in screen pixels,
// screen-space hatching, 30-degree graticule. No lighting, no glow.
// ids: RG8, region number (low byte, high byte), 0 = sea. region_lut: 512x2 RGBA8 sRGB bytes read by region number:
// row 0 = fill colour, A = hatch kind (0 none, 1 colony/mandate 45 deg, 2 occupied -45 deg, in steps of 1/3);
// row 1 = hatch line colour, A = owner number (borders between owners are political, between regions of one owner faint).
uniform sampler2D ids : filter_nearest, repeat_disable;
uniform sampler2D region_lut : filter_nearest, repeat_disable;
uniform vec3 sea_color : source_color = vec3(0.827, 0.882, 0.894);
uniform vec3 coast_color : source_color = vec3(0.13, 0.14, 0.15);   // one dark coast stroke for all land, L* <= 35
uniform vec3 border_color : source_color = vec3(0.243, 0.235, 0.22);
uniform vec3 grid_color : source_color = vec3(0.62, 0.72, 0.75);
uniform vec3 ink : source_color = vec3(0.114, 0.118, 0.125);
uniform float border_px = 1.2;
uniform int selected_id = 0;     // owner number
uniform bool show_hatch = true;
uniform vec3 hatch_colony = vec3(8.0, 1.0, 0.9);     // period px, width px, strength; set from HatchLogic.Params
uniform float control_alpha = 0.8;                  // line of control, from HatchLogic.ControlAlpha
uniform vec3 hatch_occupied = vec3(11.0, 1.0, 0.65);
uniform float faint_border = 0.3;   // strength of the line between regions of one owner
uniform sampler2D slice_lut : filter_nearest, repeat_disable, hint_default_black; // 256x1, R = 1 for the countries of the slice
uniform float dim_others = 0.0;  // 0..1: pulls the tints of countries outside the slice towards paper
uniform vec3 paper = vec3(0.949, 0.937, 0.902);

// Light: one fixed directional key light (world space), the same direction the day/night terminator will use (#288).
// Soft Lambert with wrap so the terminator is gentle, plenty of ambient so the night side stays readable.
uniform float light_amount : hint_range(0.0, 1.0) = 1.0;
uniform vec3 light_dir = vec3(0.7, 0.35, 0.6);   // towards the light, world space (lon ~49E, lat ~20N)
uniform float ambient = 0.72;                      // shade factor on the night side
uniform vec3 night_tint = vec3(0.86, 0.9, 1.0);    // cool tint of the shaded side (multiplies the fill)
uniform float sea_spec = 0.2;
uniform vec3 rim_color : source_color = vec3(0.55, 0.72, 0.95);
uniform float rim_amount = 0.3;

uniform int look = 0;                     // 0 current, 1 A, 2 A+, 3 B
uniform bool light_world = false;         // true: light_dir is a fixed world direction (sun by date stand-in, used for terminator and night frames)
uniform vec3 cam_light = vec3(-0.5, 0.55, 0.7);   // view space: from the top left, a little in front

const vec2 SZ = vec2(4096.0, 2048.0);

int id_at(vec2 uv) {
	ivec2 p = ivec2(clamp(uv, vec2(0.0), vec2(0.99999)) * SZ);
	vec2 v = texelFetch(ids, p, 0).rg * 255.0 + 0.5;
	return int(v.x) + 256 * (int(v.y) & 127);
}
// 1 for land that carries a coast stroke: islet specks (a few texels) have the bit cleared on the CPU (CoastMask).
float coast_land_at(vec2 uv) {
	ivec2 p = ivec2(clamp(uv, vec2(0.0), vec2(0.99999)) * SZ);
	return texelFetch(ids, p, 0).g * 255.0 + 0.5 >= 128.0 ? 1.0 : 0.0;
}
vec3 srgb_to_lin(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
vec3 tint_of(int id) { return srgb_to_lin(texelFetch(region_lut, ivec2(id, 0), 0).rgb); }
vec3 hatch_line_of(int id) { return srgb_to_lin(texelFetch(region_lut, ivec2(id, 1), 0).rgb); }
vec3 hatch_key(int id) { return texelFetch(region_lut, ivec2(id, 1), 0).rgb; }
int hatch_kind(int id) { return int(texelFetch(region_lut, ivec2(id, 0), 0).a * 3.0 + 0.5); }
int owner_of(int id) { return id == 0 ? 0 : int(texelFetch(region_lut, ivec2(id, 1), 0).a * 255.0 + 0.5); }
bool is_slice(int own) { return own != 0 && texelFetch(slice_lut, ivec2(own, 0), 0).r > 0.5; }

void fragment() {
	vec2 uv = UV;
	int id = id_at(uv);
	int own = owner_of(id);
	float ndv = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float fade = smoothstep(0.0, 0.26, ndv); // alpha fades over the last ~15 deg at the limb
	vec3 col = (id == 0) ? sea_color : tint_of(id);
	if (own != 0 && texelFetch(slice_lut, ivec2(own, 0), 0).r < 0.5) col = mix(col, paper, dim_others);
	if (own == selected_id && own != 0) col = mix(col, ink, 0.08);

	// Status hatching in screen pixels (A5): colony and mandate 45 deg, 1 px line every 8 px; occupation -45 deg, 1.5 px every 6 px, the controller's line colour.
	int hk = id == 0 ? 0 : hatch_kind(id);
	if (show_hatch && hk != 0) {
		bool occ = hk == 2;
		vec3 hp = occ ? hatch_occupied : hatch_colony;
		float period = hp.x, width = hp.y;
		vec2 q = FRAGCOORD.xy;
		float s = ((occ ? q.x - q.y : q.x + q.y)) / 1.41421356;
		float d = abs(fract(s / period) - 0.5) * period;
		float h = 1.0 - smoothstep(width * 0.5 - 0.5, width * 0.5 + 0.5, d);
		col = mix(col, hatch_line_of(id), h * hp.z * fade);
	}

	if (look == 0) {
		// Lighting on the fills only: borders, hatching and selection are drawn after it and are not shaded.
		vec3 n_w = normalize((INV_VIEW_MATRIX * vec4(normalize(NORMAL), 0.0)).xyz);
		vec3 l_w = normalize(light_dir);
		float ndl = dot(n_w, l_w);
		float lit = smoothstep(-0.3, 0.55, ndl);                       // wrapped Lambert: a wide soft terminator
		vec3 shade = mix(night_tint * ambient, vec3(1.03), lit);
		col = mix(col, col * shade, light_amount);
		if (id == 0) {                                                  // ocean only: a faint specular glint
			vec3 l_v = normalize((VIEW_MATRIX * vec4(l_w, 0.0)).xyz);
			vec3 hv = normalize(l_v + normalize(VIEW));
			float sp = pow(max(dot(normalize(NORMAL), hv), 0.0), 48.0) * smoothstep(0.0, 0.3, ndl);
			col += vec3(1.0, 0.98, 0.92) * sp * sea_spec * light_amount;
		}
		float ndv0 = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
		float rim = pow(1.0 - ndv0, 3.0) * rim_amount * light_amount * (0.45 + 0.55 * lit);
		col = mix(col, rim_color, clamp(rim, 0.0, 0.6));
	} else {
		// TASK-0137 looks (1 matte, 2 matte + strong limb haze, 3 physical haze): one key light, tied to the camera unless light_world is set.
		// Lighting multiplies the fills only (every channel >= 0.70 after the cool tint, ceiling 1.05); the atmosphere is a separate shell drawn outside the disk (globe_atmosphere.gdshader).
		vec3 nv = normalize(NORMAL);
		vec3 lv = light_world ? normalize((VIEW_MATRIX * vec4(normalize(light_dir), 0.0)).xyz) : normalize(cam_light);
		float nl = dot(nv, lv);
		float lit2 = smoothstep(-0.22, 0.22, nl);                       // about 25 degrees of soft terminator
		float lum = mix(0.70 / 0.94, 1.05, lit2);                       // the coolest channel (0.94) is still >= 0.70 at full night
		vec3 temp = mix(vec3(0.94, 0.97, 1.0), vec3(1.0, 0.99, 0.96), lit2);   // cooler on the shaded side
		float limb = 1.0 - clamp(dot(nv, normalize(VIEW)), 0.0, 1.0);
		if (id == 0) col *= mix(1.0, 0.9, limb * limb);                 // the sea is a little darker towards the limb
		col *= mix(vec3(1.0), lum * temp, light_amount);                // light_amount 0 is the unlit control of the shot suite
	}

	// graticule 30 deg
	vec2 g = uv * vec2(12.0, 6.0);
	vec2 gf = fwidth(g);
	vec2 gd = abs(fract(g - 0.5) - 0.5) / max(gf, vec2(1e-5));
	float grid = 1.0 - smoothstep(0.0, 1.0, min(gd.x, gd.y));
	col = mix(col, grid_color, grid * 0.6);

	// Three line weights, drawn over the hatching (A4, A5, C2). Neighbours are probed a fixed number of screen pixels away.
	// 1. coast (land against sea): one dark stroke for all land, independent of the fill; 2. border between powers: thick ink;
	// 3. line between regions of one owner: thin and faint.
	vec2 dx = dFdx(uv), dy = dFdy(uv);
	float k = border_px * 0.5, k2 = border_px * 1.1;
	int nb[4] = int[4](id_at(uv + dx * k), id_at(uv - dx * k), id_at(uv + dy * k), id_at(uv - dy * k));
	int nw[4] = int[4](id_at(uv + dx * k2), id_at(uv - dx * k2), id_at(uv + dy * k2), id_at(uv - dy * k2));
	float coast = 0.0, faint = 0.0, power = 0.0, ctrl = 0.0; bool sl = false; bool sel_edge = false;
	for (int i = 0; i < 4; i++) {
		int o = owner_of(nb[i]);
		if (o != own) {
			if (is_slice(o) || is_slice(own)) sl = true;
			if (o == selected_id) sel_edge = true;
		} else if (nb[i] != id) {
			faint += 1.0;
			// Line of control: neighbouring regions of one owner that differ in status or in controller (G3).
			if (id != 0 && (hatch_kind(nb[i]) != hk || (hk != 0 && hatch_key(nb[i]) != hatch_key(id)))) ctrl += 1.0;
		}
		int ow = owner_of(nw[i]);
		if (ow != own && nw[i] != 0 && id != 0) power += 1.0;
	}
	// Coast (G8): the contour of the bilinear land mask, so that the 4096 px raster gives a smooth line instead of a staircase.
	vec2 tp = clamp(uv, vec2(0.0), vec2(0.99999)) * SZ - 0.5;
	ivec2 t0 = ivec2(floor(tp)); vec2 tf = fract(tp);
	float m00 = coast_land_at((vec2(t0) + 0.5) / SZ), m10 = coast_land_at((vec2(t0 + ivec2(1, 0)) + 0.5) / SZ);
	float m01 = coast_land_at((vec2(t0 + ivec2(0, 1)) + 0.5) / SZ), m11 = coast_land_at((vec2(t0 + ivec2(1, 1)) + 0.5) / SZ);
	float lm = mix(mix(m00, m10, tf.x), mix(m01, m11, tf.x), tf.y);
	float lw = max(fwidth(lm), 1e-4);
	coast = 1.0 - smoothstep(0.55 * border_px, 1.2 * border_px, abs(lm - 0.5) / lw);
	vec3 bc = border_color; float a = 0.0;
	if (faint > 0.0) { bc = mix(border_color, col, 0.35); a = min(faint * 0.25, 1.0) * faint_border; }
	if (ctrl > 0.0) { bc = ink; a = min(ctrl * 0.5, 1.0) * control_alpha; }
	if (power > 0.0) { bc = ink; a = min(power * 0.5, 1.0); }
	if (coast > 0.0) { bc = coast_color; a = coast; }
	if ((own == selected_id || sel_edge) && selected_id != 0 && power > 0.0) { bc = ink; a = 1.0; }
	if (sl && power > 0.0) { bc = ink; a = 1.0; }
	col = mix(col, bc, a * fade);
	ALBEDO = col;
}
