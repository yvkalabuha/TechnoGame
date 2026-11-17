shader_type spatial;
render_mode unshaded, cull_disabled;

// One shader for all 3D objects: untextured, flat 3-step shading, `progress` reveals the model bottom-up;
// the not-yet-built part is a ghost (hatched ink-3 lines on the discarded rest).
uniform vec3 base : source_color = vec3(0.78, 0.76, 0.71);
uniform vec3 ghost : source_color = vec3(0.43, 0.42, 0.40);
uniform float progress : hint_range(0.0, 1.0) = 1.0;
uniform float y_min = 0.0;
uniform float y_size = 1.0;
varying float h;

void vertex() {
	h = (VERTEX.y - y_min) / max(y_size, 1e-5);
}

void fragment() {
	vec3 n = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	if (dot(n, VIEW) < 0.0) n = -n;
	float l = dot(n, normalize(vec3(-0.45, 0.65, 0.6))); // light fixed to the camera: top-left
	float step3 = l > 0.55 ? 1.0 : (l > 0.1 ? 0.84 : 0.68);
	vec3 col = base * step3;
	if (h < progress + 0.001) {
		ALBEDO = col;
	} else {
		float s = (FRAGCOORD.x + FRAGCOORD.y) / 1.41421356;
		float d = abs(fract(s / 5.0) - 0.5) * 5.0;
		if (d > 0.6) discard;
		ALBEDO = ghost;
	}
}
