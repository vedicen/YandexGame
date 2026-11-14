shader_type spatial;
render_mode cull_back, depth_prepass_alpha, diffuse_burley, specular_schlick_ggx;

uniform float dissolve_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec4 base_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap;
uniform float roughness : hint_range(0.0, 1.0) = 0.9;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap;
uniform float normal_scale : hint_range(-16.0, 16.0) = 1.0;
uniform sampler2D ao_texture : hint_default_white, filter_linear_mipmap;
uniform sampler2D emission_texture : source_color, filter_linear_mipmap;
uniform vec4 emission_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform vec4 dissolve_edge_color : source_color = vec4(0.95, 0.48, 0.16, 1.0);
uniform float edge_width : hint_range(0.005, 0.25) = 0.07;
uniform float noise_scale = 7.0;

varying vec3 local_pos;

float hash31(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

float noise3d(vec3 p) {
	vec3 cell = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float n000 = hash31(cell + vec3(0.0, 0.0, 0.0));
	float n100 = hash31(cell + vec3(1.0, 0.0, 0.0));
	float n010 = hash31(cell + vec3(0.0, 1.0, 0.0));
	float n110 = hash31(cell + vec3(1.0, 1.0, 0.0));
	float n001 = hash31(cell + vec3(0.0, 0.0, 1.0));
	float n101 = hash31(cell + vec3(1.0, 0.0, 1.0));
	float n011 = hash31(cell + vec3(0.0, 1.0, 1.0));
	float n111 = hash31(cell + vec3(1.0, 1.0, 1.0));
	return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
		mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	float noise_value = noise3d(local_pos * noise_scale);
	float threshold = dissolve_amount * 1.15 - 0.075;

	// 1 = пиксель виден, 0 = растворён
	float remaining = smoothstep(threshold - edge_width, threshold + edge_width, noise_value);

	// светящаяся кромка вокруг границы растворения
	float edge = smoothstep(threshold - edge_width, threshold, noise_value)
		* (1.0 - smoothstep(threshold, threshold + edge_width, noise_value));

	vec4 albedo_sample = texture(albedo_texture, UV) * base_color;
	ALBEDO = albedo_sample.rgb;
	ROUGHNESS = roughness;
	METALLIC = metallic;
	NORMAL_MAP = texture(normal_texture, UV).rgb;
	NORMAL_MAP_DEPTH = normal_scale;
	AO = texture(ao_texture, UV).r;
	EMISSION = texture(emission_texture, UV).rgb * emission_color.rgb
		+ dissolve_edge_color.rgb * edge * 0.8;
	ALPHA = remaining * albedo_sample.a;
}
