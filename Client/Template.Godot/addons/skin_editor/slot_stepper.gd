@tool
extends HBoxContainer
class_name SkinEditorSlotStepper

signal changed(new_id: int)
signal edit_requested(current_id: int)
signal conflict_button_pressed(current_id: int)

var slot_type: String = ""
var pieces: Array = []     # array of Dictionaries (skin entries)
var index: int = 0

var _slot_label: Label
var _prev_btn: Button
var _next_btn: Button
var _edit_btn: Button
var _flag_btn: Button
var _center: PanelContainer
var _thumb: TextureRect
var _piece_label: Label

var _normal_style: StyleBoxFlat
var _conflict_style: StyleBoxFlat


func _ready() -> void:
	_normal_style = StyleBoxFlat.new()
	_normal_style.bg_color = Color(0.15, 0.15, 0.17)
	_normal_style.border_width_left = 1
	_normal_style.border_width_right = 1
	_normal_style.border_width_top = 1
	_normal_style.border_width_bottom = 1
	_normal_style.border_color = Color(0.3, 0.3, 0.32)
	_normal_style.content_margin_left = 6
	_normal_style.content_margin_right = 6
	_normal_style.content_margin_top = 2
	_normal_style.content_margin_bottom = 2

	_conflict_style = StyleBoxFlat.new()
	_conflict_style.bg_color = Color(0.35, 0.10, 0.10)
	_conflict_style.border_width_left = 2
	_conflict_style.border_width_right = 2
	_conflict_style.border_width_top = 2
	_conflict_style.border_width_bottom = 2
	_conflict_style.border_color = Color(0.9, 0.3, 0.3)
	_conflict_style.content_margin_left = 6
	_conflict_style.content_margin_right = 6
	_conflict_style.content_margin_top = 2
	_conflict_style.content_margin_bottom = 2

	add_theme_constant_override("separation", 4)

	_slot_label = Label.new()
	_slot_label.text = slot_type
	_slot_label.custom_minimum_size = Vector2(80, 0)
	add_child(_slot_label)

	_prev_btn = Button.new()
	_prev_btn.text = "<"
	_prev_btn.custom_minimum_size = Vector2(32, 0)
	_prev_btn.pressed.connect(func(): _step(-1))
	add_child(_prev_btn)

	_center = PanelContainer.new()
	_center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_center.add_theme_stylebox_override("panel", _normal_style)
	add_child(_center)

	var inner := HBoxContainer.new()
	_center.add_child(inner)

	_thumb = TextureRect.new()
	_thumb.custom_minimum_size = Vector2(32, 32)
	_thumb.expand_mode = TextureRect.EXPAND_FIT_WIDTH_PROPORTIONAL
	_thumb.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	inner.add_child(_thumb)

	_piece_label = Label.new()
	_piece_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_piece_label.clip_text = true
	inner.add_child(_piece_label)

	_next_btn = Button.new()
	_next_btn.text = ">"
	_next_btn.custom_minimum_size = Vector2(32, 0)
	_next_btn.pressed.connect(func(): _step(1))
	add_child(_next_btn)

	_flag_btn = Button.new()
	_flag_btn.text = "⚑"
	_flag_btn.tooltip_text = "Click two ⚑ buttons in a row to toggle incompatibility between those pieces."
	_flag_btn.custom_minimum_size = Vector2(46, 0)
	_flag_btn.pressed.connect(func(): conflict_button_pressed.emit(current_id()))
	add_child(_flag_btn)

	_edit_btn = Button.new()
	_edit_btn.text = "Edit"
	_edit_btn.pressed.connect(func(): edit_requested.emit(current_id()))
	add_child(_edit_btn)

	_refresh()


func set_flag_armed(armed: bool) -> void:
	if _flag_btn == null:
		return
	_flag_btn.modulate = Color(1.4, 1.1, 0.3) if armed else Color(1, 1, 1)


func configure(p_type: String, p_pieces: Array, initial_id: int = 0) -> void:
	slot_type = p_type
	pieces = p_pieces.duplicate()
	pieces.sort_custom(func(a, b): return int(a.get("id", 0)) < int(b.get("id", 0)))
	index = 0
	if initial_id > 0:
		for i in range(pieces.size()):
			if int(pieces[i].get("id", 0)) == initial_id:
				index = i
				break
	if is_inside_tree():
		_slot_label.text = slot_type
		_refresh()


func current() -> Dictionary:
	if pieces.is_empty():
		return {}
	return pieces[index]


func current_id() -> int:
	var c := current()
	return int(c.get("id", 0)) if not c.is_empty() else 0


func select_by_id(p_id: int) -> void:
	for i in range(pieces.size()):
		if int(pieces[i].get("id", 0)) == p_id:
			index = i
			_refresh()
			return


func set_conflict(is_conflict: bool, tooltip: String = "") -> void:
	if _center == null:
		return
	_center.add_theme_stylebox_override("panel", _conflict_style if is_conflict else _normal_style)
	_center.tooltip_text = tooltip


func _step(delta: int) -> void:
	if pieces.is_empty():
		return
	index = (index + delta + pieces.size()) % pieces.size()
	_refresh()
	changed.emit(current_id())


func _refresh() -> void:
	if pieces.is_empty():
		_piece_label.text = "(no pieces)"
		_thumb.texture = null
		return
	var p: Dictionary = pieces[index]
	_piece_label.text = "id %d - %s  (%d/%d)" % [int(p.get("id", 0)), String(p.get("path", "")), index + 1, pieces.size()]
	_thumb.texture = _load_thumb(String(p.get("path", "")))


func refresh_display() -> void:
	_refresh()


static func _load_thumb(path: String) -> Texture2D:
	if path.is_empty():
		return null
	var full := "res://sprites/export/Devochka/%s.png" % path
	if not ResourceLoader.exists(full):
		return null
	return load(full)
