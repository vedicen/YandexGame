
extends Control

signal dialog_closed
signal request_action(id: int, price: int)
signal purchase_decided(id: int, price: int, accepted: bool)
signal model_unavailable

const MODEL_PATH := "res://models/Qwen_Qwen3-0.6B-Q4_K_M.gguf"
@onready var text_edit: TextEdit = $TextEdit
@onready var offer_panel: PanelContainer = $OfferPanel
@onready var offer_label: Label = $OfferPanel/OfferVBox/OfferLabel
@onready var money_label: Label = $MoneyLabel

var chat = null
var is_ready := false
var is_generating := false
var inventory: Array[Dictionary] = []
var pending_offer_id := -1
var pending_offer_price := -1


func _ready() -> void:
	offer_panel.hide()


func configure(catalog: Array, current_money: int) -> void:
	inventory.clear()
	for item in catalog:
		inventory.append(item)
	set_money(current_money)
	await load_model()


# Загрузка локальной модели.
func load_model() -> void:

	if not ClassDB.class_exists("NobodyWhoChat"):
		model_unavailable.emit()
		return

	if not FileAccess.file_exists(MODEL_PATH):
		model_unavailable.emit()
		push_error("Модель не найдена: " + MODEL_PATH)
		return

	print("Загружаю модель: ", MODEL_PATH)

	chat = await ClassDB.class_call_static(
		"NobodyWhoChat",
		"create",
		MODEL_PATH,
		{
			"n_ctx": 2048,
			"use_gpu": true,
			"system_prompt": get_system_prompt()
		}
	)

	if chat == null:
		model_unavailable.emit()
		push_error("Не удалось создать чат с моделью.")
		return

	is_ready = true
	print("ИИ-торговец готов!")


# Системный контекст модели.
func get_system_prompt() -> String:
	var context := """
Ты — торговец КэтИнЁб в фэнтезийном мире. Общайся на русском.

НЕ ПИШИ ID ВНЕ request

ТОВАРЫ:
Используй только указанный ассортимент, (Название, БЕЗ ID) . Ничего не выдумывай.

ТОРГ:
Не снижай цену без веских аргументов покупателя. Не соглашайся автоматически на предложенную цену. Учитывай аргументы, но не обещай скидку без достаточных оснований.

ПОКУПКА:
Добавляй блок <request> только когда покупатель и торговец достигли согласия о конкретной покупке и цена определена.
Если товар или цена не согласованы, блок не добавляй.
Если товар неясен, задай уточняющий вопрос.

НЕ ПИШИ ID ВНЕ request

ФОРМАТ ПОДТВЕРЖДЁННОЙ ПОКУПКИ:

<request>
id:1
price:100
</request>

Не утверждай, что предмет уже передан покупателю: сделку завершает игровая система.
НЕ ПИШИ ФОРМАТНЫЕ ЗВЕЗДОЧКИ И СМАЙЛ** пожалуйста **
Ассортимент может быть пустым. В таком случае не предлагай покупку.

АССОРТИМЕНТ:
"""

	for item in inventory:
		context += "\nПредмет: %s\n" % item.get("name", "Неизвестный предмет")
		context += "id: %d\n" % int(item.get("id", -1))
		context += "Описание: %s\n" % item.get("description", "")
		context += "Базовая цена: %d золотых\n" % int(item.get("price", 0))

	return context


func remove_smileys(text: String) -> String:
	var result := ""
	for index in range(text.length()):
		var codepoint := text.unicode_at(index)
		if codepoint >= 0x1F000 and codepoint <= 0x1FAFF:
			continue
		if codepoint >= 0x2600 and codepoint <= 0x27BF:
			continue
		if codepoint == 0xFE0F or codepoint == 0x200D or codepoint >= 0x1F3FB and codepoint <= 0x1F3FF:
			continue
		result += char(codepoint)

	var emoticon_regex := RegEx.new()
	emoticon_regex.compile("(?i)(?:[:;=8][-^']?[)(/DPp]|<3)")
	return emoticon_regex.sub(result, "", true)


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

	var index = answer.find("</think>")
	var final = answer.substr(index + len("</think>")) if index >= 0 else answer
	final = remove_smileys(final)
	var request_regex := RegEx.new()
	request_regex.compile("(?is)<request>\\s*id\\s*:\\s*(\\d+)\\s*price\\s*:\\s*(\\d+)\\s*</request>")
	var request_match := request_regex.search(final)
	var request_id := -1
	var request_price := -1
	if request_match:
		request_id = request_match.get_string(1).to_int()
		request_price = request_match.get_string(2).to_int()
		final = request_regex.sub(final, "", true)
	$Label.text = final.strip_edges()
	is_generating = false
	if request_id >= 0:
		request_action.emit(request_id, request_price)


func show_trade_offer(id: int, price: int, item_name: String) -> void:
	pending_offer_id = id
	pending_offer_price = price
	offer_label.text = "Купить %s за %d золотых?" % [item_name, price]
	$OfferPanel/OfferVBox/AcceptButton.disabled = false
	$OfferPanel/OfferVBox/DeclineButton.disabled = false
	offer_panel.show()


func set_money(amount: int) -> void:
	money_label.text = "Деньги: %d золотых" % amount


func show_trade_result(message: String, close_offer: bool) -> void:
	$Label.text = message
	if close_offer:
		offer_panel.hide()
		pending_offer_id = -1
		pending_offer_price = -1
	elif pending_offer_id >= 0:
		$OfferPanel/OfferVBox/AcceptButton.disabled = false
		$OfferPanel/OfferVBox/DeclineButton.disabled = false


func _on_button_pressed() -> void:
	await talk_to_merchant(text_edit.text)


func interact_end() -> void:
	dialog_closed.emit()


func _on_accept_button_pressed() -> void:
	if pending_offer_id < 0:
		return
	$OfferPanel/OfferVBox/AcceptButton.disabled = true
	$OfferPanel/OfferVBox/DeclineButton.disabled = true
	purchase_decided.emit(pending_offer_id, pending_offer_price, true)


func _on_decline_button_pressed() -> void:
	if pending_offer_id < 0:
		offer_panel.hide()
		return
	purchase_decided.emit(pending_offer_id, pending_offer_price, false)
	offer_panel.hide()
	pending_offer_id = -1
	pending_offer_price = -1
