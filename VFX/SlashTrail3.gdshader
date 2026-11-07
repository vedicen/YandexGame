shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// Огненный след: пламя с доменным искажением, дым в хвосте, летящие угли,
// тепловое марево (искажение экрана). Тот же формат UV, что и у sword_trail.gdshader:
//   UV.x = возраст (0 голова у клинка, 1 хвост), UV.y = 0 основание, 1 остриё.
// Для свечения включи Glow + HDR в WorldEnvironment.

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear;

group_uniforms colors;
uniform vec4 color_smoke : source_color = vec4(0.06, 0.05, 0.05, 1.0);
uniform vec4 color_deep : source_color = vec4(0.55, 0.04, 0.0, 1.0);
uniform vec4 color_mid : source_color = vec4(1.0, 0.28, 0.02, 1.0);
uniform vec4 color_hot : source_color = vec4(1.0, 0.75, 0.15, 1.0);
uniform vec4 color_white : source_color = vec4(1.0, 0.97, 0.8, 1.0);
uniform float intensity : hint_range(0.0, 20.0) = 5.0;
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;
uniform bool flip_width = false; // включи, если пламя растёт не от острия, а от основания

group_uniforms flame;
uniform float flame_stretch : hint_range(0.1, 20.0) = 2.5;
uniform float flame_scale : hint_range(0.5, 30.0) = 6.0;
uniform float flame_rise : hint_range(-12.0, 12.0) = 3.5;
uniform float flame_flow : hint_range(-12.0, 12.0) = 1.5;
uniform float warp_amount : hint_range(0.0, 4.0) = 1.6;
uniform float fuel_power : hint_range(0.1, 6.0) = 1.4;
uniform float tail_power : hint_range(0.1, 6.0) = 1.2;
uniform float flame_cut : hint_range(0.2, 1.2) = 0.72;
uniform float flame_softness : hint_range(0.02, 0.6) = 0.22;
uniform float heat_gain : hint_range(0.2, 4.0) = 1.8;

group_uniforms smoke;
uniform float smoke_opacity : hint_range(0.0, 1.0) = 0.55;
uniform float smoke_start : hint_range(0.0, 1.0) = 0.2;
uniform float smoke_drift : hint_range(0.0, 6.0) = 1.2;

group_uniforms embers;
uniform vec2 ember_grid = vec2(22.0, 9.0);
uniform float ember_density : hint_range(0.0, 0.6) = 0.14;
uniform float ember_size : hint_range(0.05, 0.6) = 0.28;
uniform float ember_speed : hint_range(0.0, 20.0) = 5.0;
uniform float ember_brightness : hint_range(0.0, 10.0) = 3.5;

group_uniforms heat_haze;
uniform float distortion : hint_range(0.0, 0.1) = 0.02;
uniform float haze_alpha : hint_range(0.0, 1.0) = 0.7;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * vnoise(p);
		p = p * 2.03 + vec2(17.1, 9.7);
		a *= 0.5;
	}
	return v;
}

vec3 fire_ramp(float t) {
	vec3 c = mix(color_smoke.rgb, color_deep.rgb, smoothstep(0.0, 0.25, t));
	c = mix(c, color_mid.rgb, smoothstep(0.2, 0.55, t));
	c = mix(c, color_hot.rgb, smoothstep(0.5, 0.85, t));
	c = mix(c, color_white.rgb, smoothstep(0.8, 1.0, t));
	return c;
}

// Наложение слоя sc/sa поверх c/a (без премультипликации).
void over(inout vec3 c, inout float a, vec3 sc, float sa) {
	float out_a = sa + a * (1.0 - sa);
	c = (sc * sa + c * a * (1.0 - sa)) / max(out_a, 0.0001);
	a = out_a;
}

void fragment() {
	float age = clamp(UV.x, 0.0, 1.0);
	float w = clamp(flip_width ? 1.0 - UV.y : UV.y, 0.0, 1.0);

	float base_fade = smoothstep(0.0, 0.15, w);
	float end_fade = 1.0 - smoothstep(0.88, 1.0, age);

	// Пламя: искажённый fbm, сносится от острия внутрь следа.
	vec2 p = vec2(age * flame_stretch + TIME * flame_flow, w * flame_scale + TIME * flame_rise);
	vec2 q = vec2(fbm(p + vec2(0.0, TIME * 0.3)), fbm(p + vec2(5.2, 1.3 - TIME * 0.2)));
	float n = fbm(p + warp_amount * q);

	float fuel = pow(w, fuel_power) * pow(1.0 - age, tail_power);
	float f = n + fuel * 1.2 - flame_cut;
	float flame_a = smoothstep(0.0, flame_softness, f);
	float heat = clamp(f * heat_gain + fuel * 0.45, 0.0, 1.0);

	vec3 fire_col = fire_ramp(heat);
	fire_col *= mix(1.0, intensity, smoothstep(0.35, 1.0, heat));

	// Дым: поднимается и расползается в хвосте, там, где пламя уже погасло.
	float sn = fbm(vec2(age * 3.0 - TIME * smoke_drift * 0.4, w * 4.0 + TIME * smoke_drift * 0.2) + q * 1.5);
	float smoke_mask = smoothstep(smoke_start, smoke_start + 0.35, age) * (1.0 - smoothstep(0.85, 1.0, age));
	float smoke_a = smoke_mask * smoothstep(0.3, 0.8, sn) * sqrt(w) * smoke_opacity * (1.0 - flame_a);

	// Угли: ячейки со случайным смещением, плывут от острия и мерцают.
	vec2 eg = vec2(age * ember_grid.x, w * ember_grid.y + TIME * ember_speed);
	vec2 id = floor(eg);
	vec2 fr = fract(eg) - 0.5;
	float rnd = hash21(id);
	vec2 off = vec2(rnd - 0.5, fract(rnd * 7.0) - 0.5) * 0.6;
	float ember = step(1.0 - ember_density, hash21(id + 7.7))
		* smoothstep(ember_size, 0.0, length(fr - off))
		* (0.5 + 0.5 * sin(TIME * 10.0 + rnd * 40.0))
		* (1.0 - age) * w;
	vec3 ember_col = mix(color_mid.rgb, color_white.rgb, 0.5) * ember_brightness;

	// Слой 0: тепловое марево (искажённый фон) вокруг пламени.
	vec3 col = color_smoke.rgb;
	float alpha = 0.0;
	if (distortion > 0.0) {
		vec2 d = (vec2(n, fbm(p + 3.1)) - 0.5) * 2.0 * distortion;
		col = texture(screen_tex, SCREEN_UV + d).rgb;
		alpha = smoothstep(0.0, 0.35, fuel + 0.2 * n) * haze_alpha;
	}

	over(col, alpha, color_smoke.rgb, smoke_a);
	over(col, alpha, fire_col, clamp(flame_a, 0.0, 1.0));
	over(col, alpha, ember_col, clamp(ember, 0.0, 1.0));

	ALBEDO = col * COLOR.rgb;
	ALPHA = clamp(alpha * base_fade * end_fade, 0.0, 1.0) * COLOR.a * global_alpha;
}
