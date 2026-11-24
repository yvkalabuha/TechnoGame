shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, blend_mix;

// TASK-0137: atmosphere shell around the globe. The shell is a sphere of radius shell_r; what is drawn depends only on the
// impact parameter h of the view ray (distance of the ray from the globe centre, globe radius 1), so the haze starts at
// the limb and stays outside the disk (B may go up to 4 % inside). Fills of the disk are not touched by A and A+.
uniform int look = 1;                     // 1 A thin rim, 2 A+ haze, 3 B physical
uniform bool light_world = false;
uniform vec3 light_dir = vec3(0.7, 0.35, 0.6);
uniform vec3 cam_light = vec3(-0.5, 0.55, 0.7);
uniform float shell_r = 1.08;

void fragment() {
	vec3 c = (VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;   // globe centre in view space
	vec3 dir = normalize(VERTEX);
	float t = dot(c, dir);
	vec3 p = dir * t;                                         // closest point of the ray to the centre
	float h = length(p - c);
	vec3 n = normalize(p - c);                                // limb direction from the centre
	vec3 lv = light_world ? normalize((VIEW_MATRIX * vec4(normalize(light_dir), 0.0)).xyz) : normalize(cam_light);
	float lit = dot(n, lv);
	float day = smoothstep(-0.35, 0.45, lit);
	float x = h - 1.0;                                        // distance outside the limb, in globe radii
	vec3 col = vec3(0.0);
	float a = 0.0;
	if (look == 1) {
		// thin rim: 2 px wide whatever the zoom
		float ppu = (VIEWPORT_SIZE.y * 0.5 * abs(PROJECTION_MATRIX[1][1])) / sqrt(max(dot(c, c) - 1.0, 0.01));
		float w = 2.0 / ppu;
		a = (1.0 - smoothstep(0.0, w, x)) * 0.6 * mix(0.3, 1.0, day);
		col = mix(vec3(0.62, 0.72, 0.85), vec3(0.78, 0.88, 0.97), day);
		if (x < 0.0) a = 0.0;
	} else if (look == 2) {
		// strong haze: about 4 % of the radius, Fresnel-like: densest at the limb, warm near the terminator
		float k = clamp(1.0 - x / 0.045, 0.0, 1.0);
		a = pow(k, 2.2) * 0.8 * mix(0.25, 1.0, day);
		float warm = 1.0 - smoothstep(0.0, 0.4, abs(lit - 0.05));
		col = mix(vec3(0.36, 0.60, 1.0), vec3(1.0, 0.68, 0.42), warm * 0.8);
		if (x < 0.0) a = 0.0;
	} else {
		// physical haze: exponential density, chord length through the shell, Rayleigh-like wavelength weights, reddened sunlight at the terminator
		float H = 0.03;
		float rho = exp(-max(x, 0.0) / H);
		float chord = 2.0 * sqrt(max(shell_r * shell_r - h * h, 0.0));
		vec3 beta = vec3(0.9, 2.0, 4.6);
		vec3 od = beta * rho * chord * 0.35;
		vec3 sun = mix(vec3(1.0, 0.5, 0.28), vec3(1.0, 0.97, 0.92), smoothstep(-0.05, 0.6, lit));
		float lightf = mix(0.12, 1.0, day);
		vec3 sc = (vec3(1.0) - exp(-od)) * sun * lightf;
		col = sc / max(max(sc.r, max(sc.g, sc.b)), 1e-4);
		a = clamp(max(sc.r, max(sc.g, sc.b)) * 0.95, 0.0, 0.9);
		if (x < 0.0) a *= smoothstep(-0.04, 0.0, x) * 0.7;   // at most 4 % inside the disk
	}
	ALBEDO = col;
	ALPHA = a;
}
