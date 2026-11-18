shader_type sky;

// Dark starry sky: a deterministic hash field on the view direction (no time, no randomness), so the stars sit still on the celestial sphere
// while the globe turns. Subtle by design: few small stars, a faint blue-black gradient, nothing close to the globe's own colours.
uniform vec3 deep : source_color = vec3(0.027, 0.035, 0.063);
uniform vec3 horizon : source_color = vec3(0.045, 0.058, 0.098);

float h31(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

float stars(vec3 d, float scale, float density, float size) {
	vec3 q = d * scale;
	vec3 id = floor(q);
	vec3 f = fract(q) - 0.5;
	float r = h31(id);
	if (r > density) return 0.0;
	vec3 o = vec3(h31(id + 1.7), h31(id + 4.1), h31(id + 9.3)) - 0.5;
	float dist = length(f - o * 0.7);
	float b = 0.35 + 0.65 * h31(id + 6.2);
	return smoothstep(size, 0.0, dist) * b;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	vec3 col = mix(deep, horizon, 0.5 + 0.5 * d.y);
	float s = stars(d, 70.0, 0.16, 0.10) + stars(d, 130.0, 0.12, 0.09) * 0.8 + stars(d, 36.0, 0.05, 0.13) * 1.2;
	vec3 tint = mix(vec3(0.85, 0.9, 1.0), vec3(1.0, 0.95, 0.85), h31(floor(d * 70.0) + 3.3));
	COLOR = col + tint * s * 0.85;
}
