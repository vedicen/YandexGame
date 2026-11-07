shader_type spatial;
render_mode unshaded, cull_disabled, blend_add;

// Процедурный генератор шума прямо в коде (никаких внешних текстур в инспекторе!)
float random(vec2 st) {
    return fract(sin(dot(st.xy, vec2(12.9898, 78.233))) * 43758.5453123);
}

float noise(vec2 st) {
    vec2 i = floor(st);
    vec2 f = fract(st);
    float a = random(i);
    float b = random(i + vec2(1.0, 0.0));
    float c = random(i + vec2(0.0, 1.0));
    float d = random(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void fragment() {
    // UV.x — длина трейла, UV.y — ширина (от 0 до 1)
    vec2 uv = UV;
    
    // Цифровой сдвиг (глитч) по времени
    float time_step = floor(TIME * 25.0);
    float glitch = (noise(vec2(time_step, uv.x * 5.0)) - 0.5) * 0.1;
    
    // Процедурные полосы энергии, бегущие вдоль трейла
    float plasma = noise(vec2(uv.x * 15.0 - TIME * 8.0, uv.y * 5.0 + glitch));
    
    // Маска краев трейла (плавное скругление к краям меша)
    float edge_mask = sin(uv.y * 3.14159);
    
    // Затухание к хвосту трейла
    float length_fade = smoothstep(0.0, 0.2, uv.x) * (1.0 - uv.x);
    
    // Яркое энергетическое ядро посередине
    float core = pow(edge_mask, 3.0) * plasma;
    
    // Итоговый цвет (неоновый циан/фиолетовый киберпанк-стиль)
    vec3 base_color = vec3(0.1, 0.8, 1.0);
    vec3 hot_core = vec3(1.0, 1.0, 1.5); // HDR-засветка для свечения
    
    vec3 final_color = mix(base_color, hot_core, core) * 3.0;
    
    // Прозрачность с учетом формы и затухания
    float alpha = edge_mask * length_fade * (plasma + 0.5);
    
    ALBEDO = final_color;
    ALPHA = clamp(alpha, 0.0, 1.0);
}