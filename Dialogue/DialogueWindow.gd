
extends Control

signal dialog_closed

const MODEL_PATH := "res://models/Qwen_Qwen3-0.6B-Q4_K_M.gguf"
@onready var text_edit: TextEdit = $TextEdit

var chat = null
var is_ready := false
var is_generating := false

var inventory: Array[Dictionary] = [
	{
		"name": "Железный меч",
		"id": 0,
		"description": "Простой одноручный меч из железа. Надёжный и недорогой.",
		"price": 100,
		"category": "weapon",
		"stats": {
			"damage": 12,
			"weight": 3.5,
			"durability": 100
		}
	},
	{
		"name": "Стальной меч",
		"id": 1,
		"description": "Качественный меч из стали с хорошим балансом.",
		"price": 250,
		"category": "weapon",
		"stats": {
			"damage": 22,
			"weight": 4.0,
			"durability": 180
		}
	},
	{
		"name": "Деревянный посох",
		"id": 2,
		"description": "Простой посох для начинающих магов.",
		"price": 80,
		"category": "weapon",
		"stats": {
			"magic_power": 8,
			"weight": 2.0,
			"durability": 70
		}
	}
]


func _ready() -> void:
	print("Запуск ИИ-торговца...")
	await load_model()


# Загрузка локальной модели.
func load_model() -> void:
	if not FileAccess.file_exists(MODEL_PATH):
		push_error("Модель не найдена: " + MODEL_PATH)
		return

	print("Загружаю модель: ", MODEL_PATH)

	chat = await NobodyWhoChat.create(
		MODEL_PATH,
		{
			"n_ctx": 2048,
			"use_gpu": true,
			"system_prompt": get_system_prompt()
		}
	)

	if chat == null:
		push_error("Не удалось создать чат с моделью.")
		return

	is_ready = true
	print("ИИ-торговец готов!")


# Системный контекст модели.
func get_system_prompt() -> String:
	print("New version")
	var context := """
	
Ты — торговец КэтИнЁб в фэнтезийном мире. Общайся на русском.

ТОВАРЫ:
Используй только указанный ассортимент, (Название, БЕЗ ID) . Ничего не выдумывай.

ТОРГ:
Не снижай цену без веских аргументов покупателя. Не соглашайся автоматически на предложенную цену. Учитывай аргументы, но не обещай скидку без достаточных оснований.

ПОКУПКА:
Добавляй блок <request> только когда покупатель и торговец достигли согласия о конкретной покупке и цена определена.
Если товар или цена не согласованы, блок не добавляй.
Если товар неясен, задай уточняющий вопрос.

ФОРМАТ ПОДТВЕРЖДЁННОЙ ПОКУПКИ:

<request>
id:1
price:100
</request>

ID и price должны точно соответствовать согласованной сделке и допустимым данным ассортимента. Не утверждай, что предмет уже передан покупателю: сделку завершает игровая система.

НЕ ПИШИ ФОРМАТНЫЕ ЗВЕЗДОЧКИ И СМАЙЛ** пожалуйста **

АССОРТИМЕНТ:
"""

	for item in inventory:
		context += "\nПредмет: %s\n" % item["name"]
		context += "\nid: %s\n" % item["id"]
		context += "Описание: %s\n" % item["description"]
		context += "Цена: %d золотых\n" % item["price"]
		context += "Категория: %s\n" % item["category"]
		context += "Характеристики:\n"

		for stat in item["stats"]:
			context += "- %s: %s\n" % [
				stat,
				str(item["stats"][stat])
			]

	return context

func _process(delta: float) -> void:
	if is_generating:
		$Button.disabled = true
	else:
		$Button.disabled = false
# Отправка сообщения игрока модели.
func talk_to_merchant(player_message: String) -> void:
	if not is_ready:
		print("Модель ещё не загружена.")
		return

	if is_generating:
		print("Торговец пока занят.")
		return

	if player_message.strip_edges().is_empty():
		return

	is_generating = true
	print("\nИгрок: ", player_message)
	print("Торговец думает...")

	var stream = chat.ask(player_message)
	var answer = await stream.completed()

	print("Торговец: ", answer)
	
	var index = answer.find('</think>')
	
	var final = answer.substr(index+len('</think> '), len(answer)-1)
	
	$Label.text = final
	is_generating = false


func _on_button_pressed() -> void:
	await talk_to_merchant(
				text_edit.text
			) 


func interact_end() -> void:
	dialog_closed.emit()
