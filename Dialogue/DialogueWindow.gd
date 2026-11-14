
extends Control

signal dialog_closed
signal request_action(item_ids: String, total_price: int)
signal purchase_decided(item_ids: String, total_price: int, accepted: bool)
signal model_unavailable

const MODEL_PATH := "res://AImodels/Qwen3.5-9B-Q3_K_M.gguf"
@onready var text_edit: TextEdit = $OuterMargin/Window/InnerMargin/Layout/Composer/InputRow/TextEdit
@onready var send_button: Button = $OuterMargin/Window/InnerMargin/Layout/Composer/InputRow/Button
@onready var status_label: Label = $OuterMargin/Window/InnerMargin/Layout/TopBar/TitleStack/Status
@onready var offer_panel: PanelContainer = $OuterMargin/Window/InnerMargin/Layout/OfferPanel
@onready var offer_label: Label = $OuterMargin/Window/InnerMargin/Layout/OfferPanel/OfferVBox/OfferLabel
@onready var accept_button: Button = $OuterMargin/Window/InnerMargin/Layout/OfferPanel/OfferVBox/OfferButtons/AcceptButton
@onready var decline_button: Button = $OuterMargin/Window/InnerMargin/Layout/OfferPanel/OfferVBox/OfferButtons/DeclineButton
@onready var money_label: Label = $OuterMargin/Window/InnerMargin/Layout/TopBar/MoneyPill/MoneyLabel
@onready var history_scroll: ScrollContainer = $OuterMargin/Window/InnerMargin/Layout/HistoryScroll
@onready var messages: VBoxContainer = $OuterMargin/Window/InnerMargin/Layout/HistoryScroll/Messages

var chat = null
var is_ready := false
var is_generating := false
var inventory: Array[Dictionary] = []
var history: Array[Dictionary] = []
var pending_offer_ids := ""
var pending_offer_price := -1
var pending_offer_names := ""
var pending_model_context := ""


func _ready() -> void:
	offer_panel.hide()
	status_label.text = "Подключаем торговца…"
	text_edit.gui_input.connect(_on_text_edit_gui_input)


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
	status_label.text = "Торговец готов к разговору"
	print("ИИ-торговец готов!")


# Системный контекст модели.
func get_system_prompt() -> String:
	var context := """
Ты — торговец КэтИнЁб в фэнтезийном мире. Общайся на русском.


ТОВАРЫ:
Используй только указанный ассортимент, ПИШИ ТОЛЬКО НАЗВАНИЕ ПРЕДМЕТА, БЕЗ ID. Ничего не выдумывай.

ТОРГ:
Не соглашайся автоматически на предложенную цену. Учитывай аргументы, но не обещай скидку без достаточных оснований.

ПОКУПКА:
Добавляй блок <request> только когда покупатель и торговец достигли согласия о конкретной покупке и цена определена.
Если товар или цена не согласованы, блок не добавляй.
Если товар неясен, задай уточняющий вопрос.

НЕ ПИШИ ID ВНЕ request

ФОРМАТ ПОДТВЕРЖДЁННОЙ ПОКУПКИ:

<request>
id:1
id:2
id:n
price:100
</request>

ПИШИ НЕСКОЛЬКО ID ЕСЛИ ПОКУПАТЕЛЬ ХОЧЕТ КУПИТЬ НЕСКОЛЬКО ТОВАРОВ


Не утверждай, что предмет уже передан покупателю: сделку завершает игровая система.
НЕ ПИШИ ФОРМАТНЫЕ ЗВЕЗДОЧКИ ** И СМАЙЛЫ
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
	send_button.disabled = is_generating or not is_ready
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
	status_label.text = "Торговец обдумывает ответ…"
	_add_history_message("Вы", player_message)
	print("\nИгрок: ", player_message)
	print("Торговец думает...")

	var model_message := player_message
	if not pending_model_context.is_empty():
		model_message = "%s\n\n[Событие игровой системы: %s]" % [player_message, pending_model_context]
		pending_model_context = ""
	var stream = chat.ask(model_message)
	var answer = await stream.completed()

	print("Торговец: ", answer)

	var index = answer.find("</think>")
	var final = answer.substr(index + len("</think>")) if index >= 0 else answer
	final = remove_smileys(final)
	var request_block_regex := RegEx.new()
	request_block_regex.compile("(?is)<request>(.*?)</request>")
	var request_match := request_block_regex.search(final)
	var request_ids: Array[int] = []
	var request_price := -1
	if request_match:
		var request_body := request_match.get_string(1)
		var id_regex := RegEx.new()
		id_regex.compile("(?i)\\bid\\s*:\\s*(\\d+)")
		for id_match in id_regex.search_all(request_body):
			request_ids.append(id_match.get_string(1).to_int())
		var price_regex := RegEx.new()
		price_regex.compile("(?i)\\bprice\\s*:\\s*(\\d+)")
		var price_match := price_regex.search(request_body)
		if price_match:
			request_price = price_match.get_string(1).to_int()
		final = request_block_regex.sub(final, "", true)
	var visible_answer = final.strip_edges()
	_add_history_message("Торговец", visible_answer)
	is_generating = false
	status_label.text = "Торговец готов к разговору"
	if not request_ids.is_empty() and request_price >= 0:
		var id_strings := PackedStringArray()
		for item_id in request_ids:
			id_strings.append(str(item_id))
		request_action.emit(",".join(id_strings), request_price)


func _add_history_message(speaker: String, message: String) -> void:
	if message.is_empty():
		return
	history.append({"speaker": speaker, "message": message})
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_theme_constant_override("separation", 0)
	var bubble := PanelContainer.new()
	bubble.custom_minimum_size = Vector2(clampf(history_scroll.size.x * 0.68, 260.0, 620.0), 0.0)
	bubble.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	var bubble_color := Color("#243447")
	var speaker_color := Color("#a9c7e5")
	if speaker == "Вы":
		bubble_color = Color("#24483d")
		speaker_color = Color("#a4e0c3")
		row.add_child(_make_spacer())
	elif speaker == "Сделка":
		bubble_color = Color("#403854")
		speaker_color = Color("#d5baf2")
	var style := StyleBoxFlat.new()
	style.bg_color = bubble_color
	style.set_corner_radius_all(12)
	style.content_margin_left = 14.0
	style.content_margin_right = 14.0
	style.content_margin_top = 10.0
	style.content_margin_bottom = 10.0
	bubble.add_theme_stylebox_override("panel", style)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 4)
	var speaker_label := Label.new()
	speaker_label.text = speaker
	speaker_label.add_theme_color_override("font_color", speaker_color)
	speaker_label.add_theme_font_size_override("font_size", 12)
	var body := Label.new()
	body.text = message
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.add_theme_color_override("font_color", Color("#f1f4f8"))
	body.add_theme_font_size_override("font_size", 14)
	content.add_child(speaker_label)
	content.add_child(body)
	bubble.add_child(content)
	row.add_child(bubble)
	if speaker != "Вы":
		row.add_child(_make_spacer())
	messages.add_child(row)
	call_deferred("_scroll_history_to_bottom")


func _make_spacer() -> Control:
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return spacer


func _scroll_history_to_bottom() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	var scrollbar := history_scroll.get_v_scroll_bar()
	history_scroll.scroll_vertical = int(scrollbar.max_value)


func show_trade_offer(item_ids: String, price: int, item_names: String) -> void:
	status_label.text = "Торговец предлагает сделку"
	pending_offer_ids = item_ids
	pending_offer_price = price
	pending_offer_names = item_names
	offer_label.text = "Купить %s за %d золотых?" % [item_names, price]
	accept_button.disabled = false
	decline_button.disabled = false
	offer_panel.show()


func set_money(amount: int) -> void:
	money_label.text = "Деньги: %d золотых" % amount


func show_trade_result(message: String, close_offer: bool) -> void:
	_add_history_message("Сделка", message)
	if close_offer:
		status_label.text = "Торговец готов к разговору"
		if message.begins_with("Куплено:"):
			pending_model_context = "Игрок подтвердил сделку и успешно купил: %s. Общая цена: %d золотых." % [pending_offer_names, pending_offer_price]
		elif message == "Покупка отклонена.":
			pending_model_context = "Игрок отказался от предложенной покупки: %s за %d золотых." % [pending_offer_names, pending_offer_price]
		offer_panel.hide()
		pending_offer_ids = ""
		pending_offer_price = -1
		pending_offer_names = ""
	elif not pending_offer_ids.is_empty():
		accept_button.disabled = false
		decline_button.disabled = false


func _on_button_pressed() -> void:
	if is_generating or not is_ready:
		return
	var message := text_edit.text
	if message.strip_edges().is_empty():
		return
	text_edit.clear()
	await talk_to_merchant(message)


func _on_text_edit_gui_input(event: InputEvent) -> void:
	if event is InputEventKey:
		var key_event := event as InputEventKey
		if (key_event.pressed and not key_event.echo and
			(key_event.keycode == KEY_ENTER or key_event.keycode == KEY_KP_ENTER) and not key_event.shift_pressed):
			text_edit.accept_event()
			_on_button_pressed()


func interact_end() -> void:
	dialog_closed.emit()


func _on_accept_button_pressed() -> void:
	if pending_offer_ids.is_empty():
		return
	accept_button.disabled = true
	decline_button.disabled = true
	purchase_decided.emit(pending_offer_ids, pending_offer_price, true)


func _on_decline_button_pressed() -> void:
	if pending_offer_ids.is_empty():
		offer_panel.hide()
		return
	purchase_decided.emit(pending_offer_ids, pending_offer_price, false)
	offer_panel.hide()
	pending_offer_ids = ""
	pending_offer_price = -1
