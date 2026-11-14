shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

// Захват экрана для эффекта преломления
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
// Текстура шума для искажения (можно выбрать FastNoiseLite или любую другую)
uniform sampler2D noise_tex : hint_default_white;

// Настройки
uniform float refraction_strength : hint_range(0.0, 0.2) = 0.04;
uniform vec4 tint_color : source_color = vec4(0.8, 0.9, 1.0, 1.0);
uniform float speed : hint_range(-10.0, 10.0) = 2.0;

void fragment() {
    // Анимация шума по текстурным координатам трейла
    vec2 uv_anim = UV + vec2(TIME * speed, 0.0);
    vec2 noise = texture(noise_tex, uv_anim).rg;
    
    // Смещение координат экрана на основе шума
    vec2 offset = (noise - 0.5) * refraction_strength;
    
    // Плавное затухание по краям трейла (предполагается, что UV.y от 0 до 1 поперек, а UV.x вдоль)
    float edge_fade = sin(UV.y * 3.14159); 
    float length_fade = UV.x; // Затухание к началу/концу меча
    float mask = edge_fade * length_fade;
    
    // Итоговые координаты для выборки экрана с преломлением
    vec2 ref_uv = SCREEN_UV + offset * mask;
    vec3 screen_col = texture(SCREEN_TEXTURE, ref_uv).rgb;
    
    // Добавляем легкий цветной оттенок
    vec3 final_col = mix(screen_col, tint_color.rgb, tint_color.a * 0.3);
    
    ALBEDO = final_col;
    ALPHA = clamp(mask * 2.0, 0.0, 1.0);
}