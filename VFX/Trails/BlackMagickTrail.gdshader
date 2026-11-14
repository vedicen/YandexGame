shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// Мазок кистью сумиэ: никакого свечения, только непрозрачная тушь.
// Щетина кисти (сухая кисть) расходится к хвосту, рваная кромка, брызги вдоль мазка,
// киноварная полоса по острию. UV как у остальных шейдеров:
//   UV.x = возраст (0 голова у клинка, 1 хвост), UV.y = 0 основание, 1 остриё.
// Лучше всего смотрится на светлом фоне. Для тёмных сцен задай ink_color светлым.

group_uniforms shape;
uniform bool flip_width = false;
uniform float max_thickness : hint_range(0.1, 1.0) = 0.7;
uniform float taper : hint_range(0.1, 6.0) = 0.8;
uniform float edge_roughness : hint_range(0.0, 0.5) = 0.12;
uniform float edge_freq : hint_range(1.0, 120.0) = 30.0;
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;

group_uniforms brush;
uniform float bristle_freq : hint_range(10.0, 300.0) = 90.0;
uniform float dry_amount : hint_range(0.0, 2.0) = 1.2;
uniform float dry_curve : hint_range(0.2, 5.0) = 1.5;
uniform float edge_dry : hint_range(0.0, 1.0) = 0.35;
uniform float break_amount : hint_range(0.0, 1.0) = 0.5;

group_uniforms ink;
uniform vec4 ink_color : source_color = vec4(0.02, 0.02, 0.03, 1.0);
uniform float mottling : hint_range(0.0, 1.0) = 0.25;
uniform float pool : hint_range(0.0, 1.0) = 0.35;
uniform float grain : hint_range(0.0, 1.0) = 0.15;

group_uniforms splatter;
uniform vec2 splat_grid = vec2(34.0, 14.0);
uniform float splat_density : hint_range(0.0, 1.0) = 0.3;
uniform float splat_size : hint_range(0.05, 0.6) = 0.32;
uniform float splat_reach : hint_range(0.05, 1.0) = 0.5;

group_uniforms accent;
uniform vec4 accent_color : source_color = vec4(0.85, 0.08, 0.05, 1.0);
uniform float accent_width : hint_range(0.005, 0.3) = 0.05;
uniform float accent_amount : hint_range(0.0, 1.0) = 1.0;
uniform float accent_fade : hint_range(0.1, 6.0) = 1.0;

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
	float w = clamp(flip_width ? 1.0 - UV.y : UV.y, 0.0, 1.0);

	// Форма мазка: толстая голова, сужение к хвосту, неровная внутренняя кромка.
	float thick = max_thickness * pow(1.0 - age, taper);
	float rough = (vnoise(vec2(age * edge_freq, 1.7)) - 0.5)
		+ 0.5 * (vnoise(vec2(age * edge_freq * 3.1, 8.3)) - 0.5);
	float edge_y = 1.0 - thick + rough * edge_roughness;
	float d = w - edge_y; // > 0 внутри мазка, < 0 снаружи
	float aa = fwidth(w) * 1.5 + 0.002;
	float body = smoothstep(-aa, aa, d);
	float depth_in = clamp(d / max(thick, 0.001), 0.0, 1.0);

	// Сухая кисть: у каждого волоска свой "запас туши", он кончается к хвосту и у кромки.
	float bid = floor(w * bristle_freq);
	float br = hash21(vec2(bid, 5.1));
	float bn = vnoise(vec2(age * 9.0, bid * 0.37));
	float bristle = mix(br, bn, break_amount);
	float dry = pow(age, dry_curve) * dry_amount + (1.0 - depth_in) * edge_dry * age;
	float alive = smoothstep(dry, dry + 0.1, bristle + 0.1);

	// Плотность туши: пятнистость, у кромки тушь скапливается и темнеет.
	float mottle = fbm(vec2(age * 4.0, w * 8.0));
	float ink_a = clamp(1.0 - mottling * mottle + pool * (1.0 - depth_in), 0.0, 1.0);
	ink_a *= 1.0 - grain * vnoise(vec2(age * 140.0, w * 140.0));

	// Брызги за пределами мазка.
	vec2 sg = vec2(age * splat_grid.x, w * splat_grid.y);
	vec2 sid = floor(sg);
	vec2 sf = fract(sg) - 0.5;
	vec2 soff = (hash22(sid + 8.1) - 0.5) * 0.6;
	float radius = splat_size * hash21(sid + 1.9);
	float drop = smoothstep(radius, radius * 0.7, length(sf - soff));
	float outside = -d;
	float near = smoothstep(0.0, 0.02, outside) * (1.0 - smoothstep(0.0, splat_reach, outside));
	float present = step(1.0 - splat_density, hash21(sid + 3.3))
		* near
		* smoothstep(0.05, 0.4, age)
		* (1.0 - smoothstep(0.85, 1.0, age));

	// Киноварная полоса по острию (только у головы, как печать).
	float accent = smoothstep(1.0 - accent_width, 1.0 - accent_width * 0.6, w)
		* pow(1.0 - age, accent_fade);

	float stroke = body * alive * ink_a;
	float a = max(stroke, drop * present);

	vec3 col = ink_color.rgb;
	col = mix(col, accent_color.rgb, clamp(accent * accent_amount * stroke, 0.0, 1.0));

	ALBEDO = col * COLOR.rgb;
	ALPHA = clamp(a * (1.0 - smoothstep(0.93, 1.0, age)), 0.0, 1.0) * COLOR.a * global_alpha;
}
