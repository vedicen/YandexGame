shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// Конвенция UV для меша следа:
//   UV.x = возраст сегмента: 0 = голова (у клинка), 1 = хвост (самый старый)
//   UV.y = поперёк: 0 = рукоять/основание клинка, 1 = остриё
// Для свечения включи в WorldEnvironment: Glow + HDR 2D/3D.

group_uniforms colors;
uniform vec4 color_core : source_color = vec4(1.0, 0.96, 0.85, 1.0);
uniform vec4 color_mid : source_color = vec4(0.15, 0.65, 1.0, 1.0);
uniform vec4 color_edge : source_color = vec4(0.45, 0.1, 1.0, 1.0);
uniform vec4 burn_color : source_color = vec4(1.0, 0.45, 0.1, 1.0);
uniform float intensity : hint_range(0.0, 20.0) = 4.0;
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;

group_uniforms shape;
uniform float width_power : hint_range(0.1, 8.0) = 2.2;
uniform float base_fade : hint_range(0.0, 1.0) = 0.25;
uniform float tip_line_width : hint_range(0.01, 0.5) = 0.08;
uniform float tip_line_strength : hint_range(0.0, 3.0) = 1.4;
uniform float tail_power : hint_range(0.1, 6.0) = 1.6;

group_uniforms energy;
uniform float noise_stretch : hint_range(0.1, 20.0) = 3.0;
uniform float noise_scale : hint_range(0.5, 40.0) = 9.0;
uniform float flow_speed : hint_range(-10.0, 10.0) = 2.5;
uniform float dissolve_curve : hint_range(0.2, 5.0) = 1.3;
uniform float dissolve_softness : hint_range(0.01, 0.6) = 0.12;
uniform float burn_width : hint_range(0.01, 0.6) = 0.14;
uniform float burn_strength : hint_range(0.0, 5.0) = 1.6;

group_uniforms sparkles;
uniform vec2 sparkle_grid = vec2(26.0, 10.0);
uniform float sparkle_density : hint_range(0.0, 0.5) = 0.06;
uniform float sparkle_speed : hint_range(0.0, 30.0) = 9.0;
uniform float sparkle_strength : hint_range(0.0, 10.0) = 3.0;

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
	for (int i = 0; i < 4; i++) {
		v += a * vnoise(p);
		p = p * 2.03 + vec2(17.1, 9.7);
		a *= 0.5;
	}
	return v;
}

void fragment() {
	float age = clamp(UV.x, 0.0, 1.0);
	float w = clamp(UV.y, 0.0, 1.0);

	// Профиль поперёк следа: ярче к острию, мягко гаснет к основанию.
	float body = pow(w, width_power) * smoothstep(0.0, max(base_fade, 0.001), w);
	float tip_line = smoothstep(1.0 - tip_line_width, 1.0, w) * tip_line_strength;

	// Затухание по возрасту.
	float life = pow(1.0 - age, tail_power);

	// Бегущие энергетические полосы (вытянуты вдоль следа).
	vec2 nuv = vec2(age * noise_stretch - TIME * flow_speed, w * noise_scale);
	float n1 = fbm(nuv);
	float n2 = fbm(nuv * 2.7 + vec2(-TIME * flow_speed * 1.7, 4.3));
	float streak = smoothstep(0.15, 0.85, mix(n1, n2, 0.5));

	// Растворение хвоста с раскалённой кромкой.
	float thresh = pow(age, dissolve_curve) * 1.1;
	float visible = smoothstep(thresh, thresh + dissolve_softness, streak);
	float burn = smoothstep(thresh, thresh + dissolve_softness, streak)
		* (1.0 - smoothstep(thresh + dissolve_softness, thresh + dissolve_softness + burn_width, streak));

	// Градиент цвета: край -> середина -> раскалённое ядро.
	float heat = clamp(body * life * (0.55 + 0.9 * streak) + tip_line * life, 0.0, 1.0);
	vec3 col = mix(color_edge.rgb, color_mid.rgb, smoothstep(0.0, 0.55, heat));
	col = mix(col, color_core.rgb, smoothstep(0.55, 1.0, heat));
	col += burn_color.rgb * burn * burn_strength * (0.4 + w);

	// Искры.
	vec2 g = vec2(age * sparkle_grid.x, w * sparkle_grid.y);
	float spark_id = hash21(floor(g) + floor(TIME * sparkle_speed));
	float spark_on = step(1.0 - sparkle_density, spark_id);
	float spark_shape = smoothstep(0.5, 0.0, length(fract(g) - 0.5));
	float sparkle = spark_on * spark_shape * life * body;

	float a = (body * 0.9 + tip_line) * life * visible + burn * 0.5 * life + sparkle * sparkle_strength;

	ALBEDO = (col * (0.6 + 0.8 * heat) + color_core.rgb * sparkle * sparkle_strength) * intensity * COLOR.rgb;
	ALPHA = clamp(a, 0.0, 1.0) * COLOR.a * global_alpha;
}
