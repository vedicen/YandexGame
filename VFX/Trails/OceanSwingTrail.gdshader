shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// "Разрыв реальности": след не светится, а вырезает в мире осколочное стекло.
// Внутри разреза фон из экрана: разбит на шарды (Voronoi), каждый шард смещён
// по-своему, с хроматической аберрацией, инверсией и дуотоном; по границам
// шардов светятся трещины, по кромке острия идёт бритвенная линия.
// UV как у остальных шейдеров: x = возраст (0 голова, 1 хвост), y = 0 основание, 1 остриё.

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear;

group_uniforms shape;
uniform bool flip_width = false;
uniform float max_thickness : hint_range(0.05, 1.0) = 0.85;
uniform float taper : hint_range(0.1, 6.0) = 0.9;
uniform float jag_freq : hint_range(1.0, 80.0) = 22.0;
uniform float jag_amount : hint_range(0.0, 1.0) = 0.35;
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;

group_uniforms shards;
uniform vec2 shard_density = vec2(7.0, 4.0);
uniform float shard_anim : hint_range(0.0, 6.0) = 0.8;
uniform float shard_refraction : hint_range(0.0, 0.3) = 0.06;
uniform float wobble : hint_range(0.0, 0.1) = 0.012;
uniform float chroma : hint_range(0.0, 1.5) = 0.6;
uniform float shard_brightness_var : hint_range(0.0, 1.0) = 0.35;

group_uniforms grading;
uniform float invert_amount : hint_range(0.0, 1.0) = 0.85;
uniform float tint_amount : hint_range(0.0, 1.0) = 0.55;
uniform vec4 color_shadow : source_color = vec4(0.02, 0.0, 0.12, 1.0);
uniform vec4 color_light : source_color = vec4(0.25, 0.9, 1.0, 1.0);
uniform float void_dark : hint_range(0.0, 1.0) = 0.25;

group_uniforms edges;
uniform vec4 color_crack : source_color = vec4(0.5, 0.95, 1.0, 1.0);
uniform float crack_width : hint_range(0.005, 0.3) = 0.05;
uniform float crack_intensity : hint_range(0.0, 20.0) = 5.0;
uniform float crack_fade_power : hint_range(0.1, 6.0) = 1.2;
uniform vec4 color_razor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float razor_width : hint_range(0.005, 0.3) = 0.05;
uniform float razor_intensity : hint_range(0.0, 30.0) = 10.0;
uniform vec4 color_inner : source_color = vec4(0.9, 0.3, 1.0, 1.0);
uniform float inner_width : hint_range(0.002, 0.1) = 0.015;
uniform float inner_intensity : hint_range(0.0, 20.0) = 6.0;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
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

// Точка внутри ячейки Voronoi, слегка "дышит" во времени.
vec2 cell_point(vec2 id) {
	return 0.5 + 0.45 * sin(TIME * shard_anim + 6.2831 * hash22(id));
}

// Возвращает расстояние до центра, id ячейки и расстояние до границы шарда.
float voronoi(vec2 x, out vec2 cell, out float border) {
	vec2 n = floor(x);
	vec2 f = fract(x);
	float md = 8.0;
	vec2 mg = vec2(0.0);
	vec2 mr = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + cell_point(n + g) - f;
			float d = dot(r, r);
			if (d < md) {
				md = d;
				mr = r;
				mg = g;
			}
		}
	}
	float bd = 8.0;
	for (int j = -2; j <= 2; j++) {
		for (int i = -2; i <= 2; i++) {
			vec2 g = mg + vec2(float(i), float(j));
			vec2 r = g + cell_point(n + g) - f;
			if (dot(mr - r, mr - r) > 0.00001) {
				bd = min(bd, dot(0.5 * (mr + r), normalize(r - mr)));
			}
		}
	}
	cell = n + mg;
	border = bd;
	return sqrt(md);
}

void fragment() {
	float age = clamp(UV.x, 0.0, 1.0);
	float w = clamp(flip_width ? 1.0 - UV.y : UV.y, 0.0, 1.0);

	// Форма разреза: острый серп, сужается к хвосту, рваная внутренняя кромка.
	float thick = max_thickness * pow(1.0 - age, taper);
	float jag = (vnoise(vec2(age * jag_freq, 3.7)) - 0.5) * jag_amount * thick;
	float edge_y = 1.0 - thick + jag;
	float aa = fwidth(w) * 1.5 + 0.002;
	float inside = smoothstep(edge_y - aa, edge_y + aa, w);
	float depth_in = clamp((w - edge_y) / max(thick, 0.001), 0.0, 1.0);

	// Шарды: каждый смещает фон по-своему.
	vec2 suv = vec2(age * shard_density.x, w * shard_density.y);
	vec2 cell;
	float border;
	voronoi(suv, cell, border);

	vec2 shard_offset = (hash22(cell + 11.3) - 0.5) * 2.0;
	vec2 wob = (vec2(vnoise(suv * 0.7 + TIME * 0.5), vnoise(suv * 0.7 + 9.1 - TIME * 0.5)) - 0.5) * wobble;
	vec2 off = (shard_offset * shard_refraction + wob) * mix(0.35, 1.0, depth_in);

	// Хроматическая аберрация: три канала берутся с разным смещением.
	float r = texture(screen_tex, SCREEN_UV + off * (1.0 + chroma)).r;
	float g = texture(screen_tex, SCREEN_UV + off).g;
	float b = texture(screen_tex, SCREEN_UV + off * (1.0 - chroma)).b;
	vec3 scene = vec3(r, g, b);

	// Цветокоррекция: инверсия + дуотон + разная яркость шардов + тьма у внутренней кромки.
	vec3 graded = mix(scene, vec3(1.0) - scene, invert_amount);
	float lum = dot(graded, vec3(0.299, 0.587, 0.114));
	vec3 duo = mix(color_shadow.rgb, color_light.rgb, clamp(lum, 0.0, 1.0));
	graded = mix(graded, duo, tint_amount);
	graded *= 1.0 + (hash21(cell + 3.7) - 0.5) * shard_brightness_var;
	graded *= mix(void_dark, 1.0, smoothstep(0.0, 0.6, depth_in));

	// Свечение: трещины по границам шардов, бритва по острию, акцент по внутренней кромке.
	float crack = 1.0 - smoothstep(0.0, crack_width, border);
	float crack_fade = pow(1.0 - age, crack_fade_power);
	float pulse = 0.75 + 0.25 * sin(TIME * 8.0 + cell.x * 3.1 + cell.y * 1.7);
	vec3 glow = color_crack.rgb * crack * crack_intensity * crack_fade * pulse;

	float razor = smoothstep(1.0 - razor_width, 1.0, w);
	glow += color_razor.rgb * razor * razor_intensity * smoothstep(0.0, 0.1, thick);

	float inner = 1.0 - smoothstep(0.0, inner_width, abs(w - edge_y));
	glow += color_inner.rgb * inner * inner_intensity * smoothstep(0.02, 0.15, thick);

	ALBEDO = (graded + glow) * COLOR.rgb;
	ALPHA = clamp(inside * (1.0 - smoothstep(0.92, 1.0, age)), 0.0, 1.0) * COLOR.a * global_alpha;
}
