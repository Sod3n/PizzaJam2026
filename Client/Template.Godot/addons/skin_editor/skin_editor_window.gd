@tool
extends Window

const SlotStepperScript := preload("res://addons/skin_editor/slot_stepper.gd")
const CLIENT_JSON := "res://GameData/Skins.json"
const DEVOCHKA_JSON := "res://sprites/Devochka.json"
const EDITOR_OFFSETS_JSON := "res://addons/skin_editor/editor_offsets.json"
const SLOT_ORDER := ["Hair", "BackHair", "Eyes", "Torns", "Top", "Bottom1", "Hand"]
const EMPTY_TOGGLEABLE := ["Hair", "BackHair", "Eyes", "Torns", "Top", "Bottom1", "Bottom2", "Hand"]

# Mirrors SkinVisualizer._slotOffsets — per-type pixel correction (X, +Y up).
const SLOT_OFFSETS := {
	"Hair": Vector2(0, 5),
	"Eyes": Vector2(0, 0),
	"Top": Vector2(0, 10),
	"Bottom1": Vector2(0, 0),
	"Bottom2": Vector2(0, 0),
	"Torns": Vector2(0, 0),
	"Ears": Vector2(0, 0),
	"Tail": Vector2(0, 0),
	"BackHair": Vector2(0, 0),
	"Hand": Vector2(0, 0),
}

var _layer_by_path: Dictionary = {}   # full res:// PNG path -> layer dict {X, Y, Width, Height}
var _body_layer: Dictionary = {}
var _anchor_offset: Vector2 = Vector2.ZERO
var _anchor_offset_loaded: bool = false

var _arming_id: int = 0   # id of piece whose ⚑ was clicked first; 0 = nothing armed

# Per-type editor-only offset overrides (added on top of the runtime SLOT_OFFSETS).
# Persisted in EDITOR_OFFSETS_JSON; never written to Skins.json.
var _editor_offsets: Dictionary = {}   # String type -> Vector2

var _pieces: Dictionary = {}                 # string id -> piece dict
var _steppers: Dictionary = {}               # type -> stepper

var _viewport: SubViewport
var _character: Node3D
var _slots_box: VBoxContainer
var _inspector_box: VBoxContainer
var _status_label: Label
var _save_btn: Button
var _sync_banner: PanelContainer
var _sync_banner_label: Label
var _conflicts_panel: VBoxContainer
var _conflicts_list: VBoxContainer

var _editing_id: int = 0
var _ins_id_label: Label
var _ins_path: LineEdit
var _ins_type: OptionButton
var _ins_weight: SpinBox
var _ins_exhaust: SpinBox
var _ins_empty_checks: Dictionary = {}       # type -> CheckBox
var _ins_incompatible_list: ItemList
var _ins_incompatible_add: OptionButton


func _ready() -> void:
	title = "Skin Editor"
	min_size = Vector2i(900, 600)
	size = Vector2i(1100, 700)

	var root := HSplitContainer.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(root)

	# Preview — expands to fill remaining width.
	var vp_container := SubViewportContainer.new()
	vp_container.stretch = true   # auto-resizes the SubViewport to container size
	vp_container.custom_minimum_size = Vector2(300, 0)
	vp_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vp_container.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(vp_container)

	_viewport = SubViewport.new()
	_viewport.size = Vector2i(500, 700)
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_viewport.own_world_3d = true
	_viewport.transparent_bg = false
	vp_container.add_child(_viewport)

	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 3.0
	camera.position = Vector3(0, 1.0, 4)
	camera.current = true
	_viewport.add_child(camera)

	var light := DirectionalLight3D.new()
	light.light_energy = 1.0
	light.rotate_x(deg_to_rad(-50))
	_viewport.add_child(light)

	var preview_scene := load("res://templates/characters/Cow.tscn") as PackedScene
	if preview_scene == null:
		preview_scene = load("res://templates/characters/Character.tscn") as PackedScene
	if preview_scene != null:
		_character = preview_scene.instantiate()
		_viewport.add_child(_character)
	else:
		push_error("[SkinEditor] Could not load Cow.tscn / Character.tscn - preview disabled.")

	_load_devochka_json()
	_load_editor_offsets()

	# Right panel
	# Right panel — fixed width (drag divider to resize); preview expands to fill the rest.
	var right_scroll := ScrollContainer.new()
	right_scroll.size_flags_horizontal = Control.SIZE_FILL
	right_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	right_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	right_scroll.custom_minimum_size = Vector2(650, 0)
	root.add_child(right_scroll)

	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	right.add_theme_constant_override("separation", 8)
	right_scroll.add_child(right)

	_sync_banner = _build_sync_banner()
	_sync_banner.visible = false
	right.add_child(_sync_banner)

	var slots_header := Label.new()
	slots_header.text = "Slots"
	slots_header.add_theme_font_size_override("font_size", 16)
	right.add_child(slots_header)

	_slots_box = VBoxContainer.new()
	_slots_box.add_theme_constant_override("separation", 4)
	right.add_child(_slots_box)

	right.add_child(HSeparator.new())

	var type_offsets_header := Label.new()
	type_offsets_header.text = "Type offsets (editor preview only)"
	type_offsets_header.add_theme_font_size_override("font_size", 16)
	right.add_child(type_offsets_header)
	right.add_child(_build_type_offsets_panel())

	right.add_child(HSeparator.new())

	var inspector_header := Label.new()
	inspector_header.text = "Piece Inspector"
	inspector_header.add_theme_font_size_override("font_size", 16)
	right.add_child(inspector_header)

	_inspector_box = VBoxContainer.new()
	_inspector_box.visible = false
	_build_inspector(_inspector_box)
	right.add_child(_inspector_box)

	right.add_child(HSeparator.new())

	_conflicts_panel = VBoxContainer.new()
	_conflicts_panel.visible = false
	var conflicts_header := Label.new()
	conflicts_header.text = "⚠ Active conflicts"
	conflicts_header.add_theme_color_override("font_color", Color(1, 0.6, 0.5))
	_conflicts_panel.add_child(conflicts_header)
	_conflicts_list = VBoxContainer.new()
	_conflicts_panel.add_child(_conflicts_list)
	right.add_child(_conflicts_panel)

	var bottom := HBoxContainer.new()
	_save_btn = Button.new()
	_save_btn.text = "Save (both files)"
	_save_btn.pressed.connect(_on_save)
	bottom.add_child(_save_btn)
	var revert_btn := Button.new()
	revert_btn.text = "Revert"
	revert_btn.pressed.connect(load_from_disk)
	bottom.add_child(revert_btn)
	_status_label = Label.new()
	_status_label.text = ""
	_status_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom.add_child(_status_label)
	right.add_child(bottom)

	load_from_disk()


# ---------------- JSON load / save ----------------

static func _server_json_abs_path() -> String:
	var client_abs := ProjectSettings.globalize_path(CLIENT_JSON)
	var client_dir := client_abs.get_base_dir()
	# Client/Template.Godot/GameData -> ../../../Server/Template.Shared/GameData
	return client_dir.path_join("../../../Server/Template.Shared/GameData/Skins.json").simplify_path()


func load_from_disk() -> void:
	var client_abs := ProjectSettings.globalize_path(CLIENT_JSON)
	var server_abs := _server_json_abs_path()

	var client_text := _read_file(client_abs)
	var server_text := _read_file(server_abs)

	if client_text == "":
		_set_status("Missing %s" % client_abs, true)
		return

	var diff := server_text != "" and server_text != client_text
	if diff:
		_show_sync_banner(client_abs, server_abs)
	else:
		_sync_banner.visible = false

	_parse_json(client_text)
	_build_steppers()
	_apply_current_to_preview()
	_recompute_conflicts()
	_set_status("Files differ - using client copy. Save will overwrite both." if diff else "Loaded.")


static func _read_file(abs_path: String) -> String:
	if not FileAccess.file_exists(abs_path):
		return ""
	var f := FileAccess.open(abs_path, FileAccess.READ)
	if f == null:
		return ""
	return f.get_as_text()


func _parse_json(text: String) -> void:
	var parsed = JSON.parse_string(text)
	if typeof(parsed) != TYPE_DICTIONARY:
		_pieces = {}
		return
	# Coerce numeric fields back to int — JSON.parse_string returns all numbers as floats,
	# and C# Skin.cs expects int for Id/Weight/Exhaust.
	for key in parsed.keys():
		var s = parsed[key]
		if typeof(s) != TYPE_DICTIONARY:
			continue
		if s.has("id"): s["id"] = int(s["id"])
		if s.has("weight"): s["weight"] = int(s["weight"])
		if s.has("exhaust"): s["exhaust"] = int(s["exhaust"])
	_pieces = parsed


func _on_save() -> void:
	if _save_btn.disabled:
		return
	_mirror_incompatibles()

	# Sort keys numerically.
	var ids: Array[int] = []
	for k in _pieces.keys():
		ids.append(int(k))
	ids.sort()

	var ordered := {}
	for id in ids:
		ordered[str(id)] = _pieces[str(id)]

	# sort_keys=false → preserve our numeric insertion order (default is true in Godot 4).
	var json_text := JSON.stringify(ordered, "  ", false)

	var client_abs := ProjectSettings.globalize_path(CLIENT_JSON)
	var server_abs := _server_json_abs_path()

	if not _write_file(client_abs, json_text):
		_set_status("Save failed: %s" % client_abs, true)
		return
	if not _write_file(server_abs, json_text):
		_set_status("Save failed: %s" % server_abs, true)
		return

	_sync_banner.visible = false
	_set_status("Saved -> %s + %s" % [client_abs, server_abs])


static func _write_file(abs_path: String, content: String) -> bool:
	var f := FileAccess.open(abs_path, FileAccess.WRITE)
	if f == null:
		return false
	f.store_string(content)
	return true


func _mirror_incompatibles() -> void:
	var forward := {}   # int id -> Dictionary (set of ints)
	for s in _pieces.values():
		var id_i := int(s.get("id", 0))
		var set_d := {}
		var inc = s.get("incompatible", null)
		if inc != null and String(inc) != "":
			for tok in String(inc).split(",", false):
				var t := tok.strip_edges()
				if t.is_valid_int():
					var oid := int(t)
					if oid != id_i:
						set_d[oid] = true
		forward[id_i] = set_d
	# Symmetric closure
	for id_i in forward.keys():
		for oid in forward[id_i].keys():
			if forward.has(oid):
				forward[oid][id_i] = true
	for s in _pieces.values():
		var id_i := int(s.get("id", 0))
		var set_d: Dictionary = forward.get(id_i, {})
		if set_d.is_empty():
			s["incompatible"] = null
		else:
			var sorted: Array[int] = []
			for k in set_d.keys():
				sorted.append(int(k))
			sorted.sort()
			var parts: PackedStringArray = []
			for v in sorted:
				parts.append(str(v))
			s["incompatible"] = ",".join(parts)


# ---------------- Sync banner ----------------

func _build_sync_banner() -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.5, 0.4, 0.1)
	style.content_margin_left = 8
	style.content_margin_right = 8
	style.content_margin_top = 4
	style.content_margin_bottom = 4
	panel.add_theme_stylebox_override("panel", style)

	var hb := HBoxContainer.new()
	panel.add_child(hb)
	_sync_banner_label = Label.new()
	_sync_banner_label.text = "Client and Server Skins.json differ."
	_sync_banner_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hb.add_child(_sync_banner_label)

	var use_client := Button.new()
	use_client.text = "Use Client"
	use_client.pressed.connect(func():
		_sync_banner.visible = false
		_set_status("Keeping client copy in memory.")
	)
	hb.add_child(use_client)

	var use_server := Button.new()
	use_server.text = "Use Server"
	use_server.pressed.connect(func():
		var text := _read_file(_server_json_abs_path())
		if text == "":
			_set_status("Server file missing.", true)
			return
		_parse_json(text)
		_build_steppers()
		_apply_current_to_preview()
		_recompute_conflicts()
		_sync_banner.visible = false
		_set_status("Loaded server copy.")
	)
	hb.add_child(use_server)
	return panel


func _show_sync_banner(client_abs: String, server_abs: String) -> void:
	_sync_banner_label.text = "Client/Server Skins.json differ. (%s <-> %s)" % [client_abs.get_file(), server_abs.get_file()]
	_sync_banner.visible = true


# ---------------- Steppers ----------------

func _build_steppers() -> void:
	for c in _slots_box.get_children():
		c.queue_free()
	_steppers.clear()

	var by_type := {}
	for s in _pieces.values():
		var t := String(s.get("type", ""))
		if not by_type.has(t):
			by_type[t] = []
		by_type[t].append(s)

	for t in SLOT_ORDER:
		if not by_type.has(t) or by_type[t].is_empty():
			continue
		var stepper := SlotStepperScript.new()
		_slots_box.add_child(stepper)
		stepper.configure(t, by_type[t])
		stepper.changed.connect(func(_new_id):
			_apply_current_to_preview()
			_recompute_conflicts()
		)
		stepper.edit_requested.connect(func(id):
			_open_inspector_for(id)
		)
		stepper.conflict_button_pressed.connect(_on_conflict_button)
		_steppers[t] = stepper


# ---------------- Preview ----------------

func _apply_current_to_preview() -> void:
	if _character == null:
		return
	var skin_container := _character.get_node_or_null("ScaleAnchor/Skin")
	if skin_container == null:
		skin_container = _character.get_node_or_null("Character/ScaleAnchor/Skin")
	if skin_container == null:
		return

	# Compute hidden slots from Empty fields of current pieces.
	var hidden := {}
	for st in _steppers.values():
		var p: Dictionary = st.current()
		if p.is_empty():
			continue
		var emp = p.get("empty", null)
		if emp == null or String(emp) == "":
			continue
		for t in String(emp).split(";", false):
			var ts := t.strip_edges()
			if ts != "":
				hidden[ts] = true

	for st in _steppers.values():
		var slot: AnimatedSprite3D = skin_container.get_node_or_null(st.slot_type)
		if slot == null:
			continue
		var p: Dictionary = st.current()
		var path := String(p.get("path", "")) if not p.is_empty() else ""
		if p.is_empty() or hidden.has(st.slot_type) or path == "":
			slot.visible = false
			continue
		var full := "res://sprites/export/Devochka/%s.png" % path
		if not ResourceLoader.exists(full):
			slot.visible = false
			continue
		var tex: Texture2D = load(full)
		var frames := SpriteFrames.new()  # already contains a "default" animation
		if frames.get_frame_count("default") > 0:
			frames.set_frame("default", 0, tex)
		else:
			frames.add_frame("default", tex)
		slot.sprite_frames = frames
		slot.animation = "default"
		slot.visible = true
		_apply_offset(slot, tex, full, skin_container)


# ---------------- Two-click conflict marking ----------------

func _on_conflict_button(id: int) -> void:
	if id <= 0:
		_arming_id = 0
		_update_flag_armed_visual()
		return
	if _arming_id == 0:
		_arming_id = id
		_update_flag_armed_visual()
		var p: Dictionary = _pieces.get(str(id), {})
		_set_status("Armed id %d (%s). Click another ⚑ to toggle incompatibility." % [id, String(p.get("path", ""))])
		return
	if _arming_id == id:
		_arming_id = 0
		_update_flag_armed_visual()
		_set_status("Cancelled.")
		return
	var a := _arming_id
	var b := id
	_arming_id = 0
	_update_flag_armed_visual()
	var now_incompatible := _toggle_incompatible_pair(a, b)
	_recompute_conflicts()
	if now_incompatible:
		_set_status("Marked id %d ↔ id %d as incompatible. Save to persist." % [a, b])
	else:
		_set_status("Removed incompatibility between id %d and id %d. Save to persist." % [a, b])


func _update_flag_armed_visual() -> void:
	for st in _steppers.values():
		st.set_flag_armed(st.current_id() == _arming_id and _arming_id != 0)


# Returns true if a,b are now incompatible (was added), false if removed.
func _toggle_incompatible_pair(a: int, b: int) -> bool:
	var was_incompatible := _piece_has_incompatible(a, b)
	if was_incompatible:
		_remove_incompatible(a, b)
		_remove_incompatible(b, a)
		return false
	_add_incompatible(a, b)
	_add_incompatible(b, a)
	return true


func _piece_has_incompatible(piece_id: int, other_id: int) -> bool:
	var s: Dictionary = _pieces.get(str(piece_id), {})
	if s.is_empty():
		return false
	var inc = s.get("incompatible", null)
	if inc == null or String(inc) == "":
		return false
	for tok in String(inc).split(",", false):
		var ts := tok.strip_edges()
		if ts.is_valid_int() and int(ts) == other_id:
			return true
	return false


func _add_incompatible(piece_id: int, other_id: int) -> void:
	var s: Dictionary = _pieces.get(str(piece_id), {})
	if s.is_empty() or piece_id == other_id:
		return
	var ids := _parse_id_list(s.get("incompatible", null))
	if other_id in ids:
		return
	ids.append(other_id)
	ids.sort()
	s["incompatible"] = _format_id_list(ids)


func _remove_incompatible(piece_id: int, other_id: int) -> void:
	var s: Dictionary = _pieces.get(str(piece_id), {})
	if s.is_empty():
		return
	var ids := _parse_id_list(s.get("incompatible", null))
	ids.erase(other_id)
	s["incompatible"] = _format_id_list(ids)


static func _parse_id_list(value) -> Array[int]:
	var out: Array[int] = []
	if value == null or String(value) == "":
		return out
	for tok in String(value).split(",", false):
		var ts := tok.strip_edges()
		if ts.is_valid_int():
			out.append(int(ts))
	return out


static func _format_id_list(ids: Array[int]) -> Variant:
	if ids.is_empty():
		return null
	var parts: PackedStringArray = []
	for v in ids:
		parts.append(str(v))
	return ",".join(parts)


# ---------------- Conflicts ----------------

func _recompute_conflicts() -> void:
	var current_ids := {}
	var current_pieces: Array = []
	for st in _steppers.values():
		var p: Dictionary = st.current()
		if p.is_empty():
			continue
		current_ids[int(p.get("id", 0))] = true
		current_pieces.append(p)

	var any_conflict := false
	for st in _steppers.values():
		var p: Dictionary = st.current()
		if p.is_empty():
			st.set_conflict(false)
			continue
		var pid := int(p.get("id", 0))
		var conflicts: Array[int] = []
		var inc = p.get("incompatible", null)
		if inc != null and String(inc) != "":
			for tok in String(inc).split(",", false):
				var ts := tok.strip_edges()
				if ts.is_valid_int():
					var oid := int(ts)
					if oid != pid and current_ids.has(oid):
						conflicts.append(oid)
		for other in current_pieces:
			var oid := int(other.get("id", 0))
			if oid == pid:
				continue
			var oinc = other.get("incompatible", null)
			if oinc == null or String(oinc) == "":
				continue
			for tok in String(oinc).split(",", false):
				var ts := tok.strip_edges()
				if ts.is_valid_int() and int(ts) == pid:
					conflicts.append(oid)
		if not conflicts.is_empty():
			var dedup := {}
			for v in conflicts:
				dedup[v] = true
			var ids: Array[int] = []
			for k in dedup.keys():
				ids.append(int(k))
			ids.sort()
			var parts: PackedStringArray = []
			for v in ids:
				parts.append(str(v))
			st.set_conflict(true, "Conflicts with: %s" % ", ".join(parts))
			any_conflict = true
		else:
			st.set_conflict(false)
	_save_btn.disabled = any_conflict
	if any_conflict:
		_set_status("Conflict in current selection - fix or change pieces to enable Save.", true)

	_rebuild_conflicts_panel(current_pieces, current_ids)


func _rebuild_conflicts_panel(current_pieces: Array, current_ids: Dictionary) -> void:
	for c in _conflicts_list.get_children():
		c.queue_free()

	# Map id -> "Slot:id" for quick rendering.
	var id_to_label := {}
	for st in _steppers.values():
		var p: Dictionary = st.current()
		if not p.is_empty():
			id_to_label[int(p.get("id", 0))] = "%s:%d" % [String(st.slot_type), int(p.get("id", 0))]

	# Collect unique pairs (a < b) currently active.
	var seen := {}
	var pairs: Array = []   # each: [a, b]
	for p in current_pieces:
		var pid := int(p.get("id", 0))
		var inc = p.get("incompatible", null)
		if inc == null or String(inc) == "":
			continue
		for tok in String(inc).split(",", false):
			var ts := tok.strip_edges()
			if not ts.is_valid_int():
				continue
			var oid := int(ts)
			if oid == pid or not current_ids.has(oid):
				continue
			var lo := mini(pid, oid)
			var hi := maxi(pid, oid)
			var key := "%d_%d" % [lo, hi]
			if seen.has(key):
				continue
			seen[key] = true
			pairs.append([lo, hi])

	if pairs.is_empty():
		_conflicts_panel.visible = false
		return
	_conflicts_panel.visible = true

	for pair in pairs:
		var a: int = pair[0]
		var b: int = pair[1]
		var row := HBoxContainer.new()
		var bullet := Label.new()
		bullet.text = "  • %s ↔ %s" % [id_to_label.get(a, str(a)), id_to_label.get(b, str(b))]
		bullet.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(bullet)
		var clear_btn := Button.new()
		clear_btn.text = "✕"
		clear_btn.tooltip_text = "Remove this incompatibility pair"
		clear_btn.pressed.connect(func():
			_remove_incompatible(a, b)
			_remove_incompatible(b, a)
			_recompute_conflicts()
			_set_status("Removed incompatibility %d ↔ %d. Save to persist." % [a, b])
		)
		row.add_child(clear_btn)
		_conflicts_list.add_child(row)


# ---------------- Inspector ----------------

func _build_inspector(host: VBoxContainer) -> void:
	var grid := GridContainer.new()
	grid.columns = 2
	host.add_child(grid)

	grid.add_child(_label("Id"))
	_ins_id_label = Label.new()
	_ins_id_label.text = "-"
	grid.add_child(_ins_id_label)

	grid.add_child(_label("Path"))
	_ins_path = LineEdit.new()
	_ins_path.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_ins_path.text_changed.connect(func(t):
		var s := _editing_piece()
		if s.is_empty(): return
		s["path"] = t
		for st in _steppers.values():
			if st.current_id() == _editing_id:
				st.refresh_display()
		_apply_current_to_preview()
	)
	grid.add_child(_ins_path)

	grid.add_child(_label("Type"))
	_ins_type = OptionButton.new()
	for t in SLOT_ORDER:
		_ins_type.add_item(t)
	_ins_type.item_selected.connect(func(idx):
		var s := _editing_piece()
		if s.is_empty(): return
		s["type"] = _ins_type.get_item_text(idx)
		_build_steppers()
		_apply_current_to_preview()
		_recompute_conflicts()
	)
	grid.add_child(_ins_type)

	grid.add_child(_label("Weight"))
	_ins_weight = SpinBox.new()
	_ins_weight.min_value = 0
	_ins_weight.max_value = 1000
	_ins_weight.step = 1
	_ins_weight.value_changed.connect(func(v):
		var s := _editing_piece()
		if not s.is_empty():
			s["weight"] = int(v)
	)
	grid.add_child(_ins_weight)

	grid.add_child(_label("Exhaust"))
	_ins_exhaust = SpinBox.new()
	_ins_exhaust.min_value = 0
	_ins_exhaust.max_value = 10000
	_ins_exhaust.step = 1
	_ins_exhaust.value_changed.connect(func(v):
		var s := _editing_piece()
		if not s.is_empty():
			s["exhaust"] = int(v)
	)
	grid.add_child(_ins_exhaust)

	grid.add_child(_label("Empty (hides types)"))
	var empty_box := HBoxContainer.new()
	for t in EMPTY_TOGGLEABLE:
		var cb := CheckBox.new()
		cb.text = t
		cb.toggled.connect(func(_pressed): _update_empty_from_checks())
		_ins_empty_checks[t] = cb
		empty_box.add_child(cb)
	grid.add_child(empty_box)

	grid.add_child(_label("Incompatible"))
	var inc_box := VBoxContainer.new()
	inc_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_ins_incompatible_list = ItemList.new()
	_ins_incompatible_list.custom_minimum_size = Vector2(0, 70)
	_ins_incompatible_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_ins_incompatible_list.allow_reselect = true
	_ins_incompatible_list.item_activated.connect(func(idx):
		_ins_incompatible_list.remove_item(idx)
		_commit_incompatible_from_list()
	)
	inc_box.add_child(_ins_incompatible_list)

	var add_row := HBoxContainer.new()
	_ins_incompatible_add = OptionButton.new()
	_ins_incompatible_add.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_row.add_child(_ins_incompatible_add)
	var add_btn := Button.new()
	add_btn.text = "Add"
	add_btn.pressed.connect(_add_incompatible_selection)
	add_row.add_child(add_btn)
	var hint := Label.new()
	hint.text = " (double-click to remove)"
	add_row.add_child(hint)
	inc_box.add_child(add_row)
	grid.add_child(inc_box)


func _open_inspector_for(id: int) -> void:
	if not _pieces.has(str(id)):
		return
	_editing_id = id
	_inspector_box.visible = true
	var s: Dictionary = _pieces[str(id)]

	_ins_id_label.text = str(id)
	_ins_path.text = String(s.get("path", ""))

	var type_str := String(s.get("type", ""))
	for i in range(_ins_type.item_count):
		if _ins_type.get_item_text(i) == type_str:
			_ins_type.selected = i
			break

	_ins_weight.set_value_no_signal(int(s.get("weight", 0)))
	_ins_exhaust.set_value_no_signal(int(s.get("exhaust", 0)))

	var empty_set := {}
	var emp = s.get("empty", null)
	if emp != null and String(emp) != "":
		for t in String(emp).split(";", false):
			var ts := t.strip_edges()
			if ts != "":
				empty_set[ts] = true
	for t in _ins_empty_checks.keys():
		_ins_empty_checks[t].set_pressed_no_signal(empty_set.has(t))

	_ins_incompatible_list.clear()
	var inc_set := {}
	var inc = s.get("incompatible", null)
	if inc != null and String(inc) != "":
		for tok in String(inc).split(",", false):
			var ts := tok.strip_edges()
			if ts.is_valid_int():
				inc_set[int(ts)] = true
	var inc_ids: Array[int] = []
	for k in inc_set.keys():
		inc_ids.append(int(k))
	inc_ids.sort()
	for oid in inc_ids:
		var o: Dictionary = _pieces.get(str(oid), {})
		var label := "%d - %s" % [oid, String(o.get("path", ""))] if not o.is_empty() else str(oid)
		_ins_incompatible_list.add_item(label)
		_ins_incompatible_list.set_item_metadata(_ins_incompatible_list.item_count - 1, oid)

	_ins_incompatible_add.clear()
	_ins_incompatible_add.add_item("- select a piece -", 0)
	var all_ids: Array[int] = []
	for k in _pieces.keys():
		all_ids.append(int(k))
	all_ids.sort()
	for pid in all_ids:
		if pid == id or inc_set.has(pid):
			continue
		var p: Dictionary = _pieces[str(pid)]
		_ins_incompatible_add.add_item("%d - %s/%s" % [pid, String(p.get("type", "")), String(p.get("path", ""))], pid)


func _editing_piece() -> Dictionary:
	if _editing_id == 0 or not _pieces.has(str(_editing_id)):
		return {}
	return _pieces[str(_editing_id)]


func _update_empty_from_checks() -> void:
	var s := _editing_piece()
	if s.is_empty(): return
	var types: PackedStringArray = []
	for t in _ins_empty_checks.keys():
		if _ins_empty_checks[t].button_pressed:
			types.append(t)
	s["empty"] = null if types.is_empty() else ";".join(types)
	_apply_current_to_preview()


func _add_incompatible_selection() -> void:
	var idx := _ins_incompatible_add.selected
	if idx <= 0:
		return
	var id := int(_ins_incompatible_add.get_item_id(idx))
	if id <= 0:
		return
	var o: Dictionary = _pieces.get(str(id), {})
	var label := "%d - %s" % [id, String(o.get("path", ""))] if not o.is_empty() else str(id)
	_ins_incompatible_list.add_item(label)
	_ins_incompatible_list.set_item_metadata(_ins_incompatible_list.item_count - 1, id)
	_ins_incompatible_add.remove_item(idx)
	_commit_incompatible_from_list()


func _commit_incompatible_from_list() -> void:
	var s := _editing_piece()
	if s.is_empty(): return
	var ids: Array[int] = []
	for i in range(_ins_incompatible_list.item_count):
		var meta = _ins_incompatible_list.get_item_metadata(i)
		if typeof(meta) == TYPE_INT:
			ids.append(meta)
	ids.sort()
	if ids.is_empty():
		s["incompatible"] = null
	else:
		var parts: PackedStringArray = []
		for v in ids:
			parts.append(str(v))
		s["incompatible"] = ",".join(parts)
	_recompute_conflicts()


# ---------------- Offset math (mirrors SkinVisualizer.ApplySkinTexture) ----------------

func _load_devochka_json() -> void:
	_layer_by_path.clear()
	_body_layer = {}
	var f := FileAccess.open(DEVOCHKA_JSON, FileAccess.READ)
	if f == null:
		push_warning("[SkinEditor] No Devochka.json - preview offsets will be wrong.")
		return
	var data = JSON.parse_string(f.get_as_text())
	if typeof(data) != TYPE_DICTIONARY:
		push_warning("[SkinEditor] Could not parse Devochka.json.")
		return
	_index_layers(data)


func _index_layers(node: Dictionary) -> void:
	var tex_path: String = String(node.get("TexturePath", ""))
	if tex_path != "":
		_layer_by_path[tex_path] = node
	if String(node.get("Name", "")) == "Body":
		_body_layer = node
	var children = node.get("Children", null)
	if typeof(children) == TYPE_ARRAY:
		for c in children:
			if typeof(c) == TYPE_DICTIONARY:
				_index_layers(c)


func _apply_offset(sprite: AnimatedSprite3D, tex: Texture2D, full_path: String, skin_container: Node3D) -> void:
	if _body_layer.is_empty() or not _layer_by_path.has(full_path):
		return
	var body_sprite: AnimatedSprite3D = skin_container.get_node_or_null("Body")
	if body_sprite == null:
		return

	# Cache the Body's editor-time offset once — this is the anchor everything aligns to.
	if not _anchor_offset_loaded:
		_anchor_offset = body_sprite.offset
		_anchor_offset_loaded = true

	# Align 3D position to body so depth/Y match (skip the body itself).
	if sprite != body_sprite:
		sprite.position = Vector3(body_sprite.position.x, body_sprite.position.y, sprite.position.z)
		sprite.pixel_size = body_sprite.pixel_size
		sprite.centered = body_sprite.centered

	var layer: Dictionary = _layer_by_path[full_path]
	var diff_x := float(layer.get("X", 0)) - float(_body_layer.get("X", 0))
	var diff_y := float(layer.get("Y", 0)) - float(_body_layer.get("Y", 0))
	var target_w := float(tex.get_width())
	var target_h := float(tex.get_height())
	var anchor_w := float(_body_layer.get("Width", 0))
	var anchor_h := float(_body_layer.get("Height", 0))

	var final_offset := _anchor_offset + Vector2(diff_x, -diff_y)
	if sprite.centered:
		final_offset += Vector2(target_w * 0.5 - anchor_w * 0.5, anchor_h * 0.5 - target_h * 0.5)

	var type_name := String(sprite.name)
	var correction: Vector2 = SLOT_OFFSETS.get(type_name, Vector2.ZERO)
	final_offset += correction

	# Editor-only per-type override (not saved to Skins.json).
	if _editor_offsets.has(type_name):
		final_offset += _editor_offsets[type_name] as Vector2

	sprite.offset = final_offset


# ---------------- Type offsets UI ----------------

func _build_type_offsets_panel() -> Control:
	var grid := GridContainer.new()
	grid.columns = 4
	for t in SLOT_ORDER:
		var type_name := String(t)
		var label := Label.new()
		label.text = type_name
		label.custom_minimum_size = Vector2(80, 0)
		grid.add_child(label)

		var current: Vector2 = _editor_offsets.get(type_name, Vector2.ZERO)

		var x_spin := SpinBox.new()
		x_spin.min_value = -500
		x_spin.max_value = 500
		x_spin.step = 1
		x_spin.prefix = "X"
		x_spin.value = current.x
		x_spin.value_changed.connect(func(v):
			var cur: Vector2 = _editor_offsets.get(type_name, Vector2.ZERO)
			_set_editor_offset(type_name, Vector2(v, cur.y))
		)
		grid.add_child(x_spin)

		var y_spin := SpinBox.new()
		y_spin.min_value = -500
		y_spin.max_value = 500
		y_spin.step = 1
		y_spin.prefix = "Y"
		y_spin.value = current.y
		y_spin.value_changed.connect(func(v):
			var cur: Vector2 = _editor_offsets.get(type_name, Vector2.ZERO)
			_set_editor_offset(type_name, Vector2(cur.x, v))
		)
		grid.add_child(y_spin)

		var reset_btn := Button.new()
		reset_btn.text = "Reset"
		reset_btn.pressed.connect(func():
			_set_editor_offset(type_name, Vector2.ZERO)
			x_spin.set_value_no_signal(0)
			y_spin.set_value_no_signal(0)
		)
		grid.add_child(reset_btn)
	return grid


# ---------------- Editor-only per-type offsets ----------------

func _load_editor_offsets() -> void:
	_editor_offsets.clear()
	if not FileAccess.file_exists(EDITOR_OFFSETS_JSON):
		return
	var f := FileAccess.open(EDITOR_OFFSETS_JSON, FileAccess.READ)
	if f == null:
		return
	var data = JSON.parse_string(f.get_as_text())
	if typeof(data) != TYPE_DICTIONARY:
		return
	for k in data.keys():
		var v = data[k]
		if typeof(v) != TYPE_DICTIONARY:
			continue
		_editor_offsets[String(k)] = Vector2(float(v.get("x", 0)), float(v.get("y", 0)))


func _save_editor_offsets() -> void:
	var ordered := {}
	var types: Array = _editor_offsets.keys()
	types.sort()
	for t in types:
		var v: Vector2 = _editor_offsets[t]
		if is_zero_approx(v.x) and is_zero_approx(v.y):
			continue
		ordered[String(t)] = {"x": v.x, "y": v.y}
	var f := FileAccess.open(EDITOR_OFFSETS_JSON, FileAccess.WRITE)
	if f == null:
		push_warning("[SkinEditor] Could not write editor_offsets.json")
		return
	f.store_string(JSON.stringify(ordered, "  ", false))


func _set_editor_offset(type_name: String, off: Vector2) -> void:
	if is_zero_approx(off.x) and is_zero_approx(off.y):
		_editor_offsets.erase(type_name)
	else:
		_editor_offsets[type_name] = off
	_save_editor_offsets()
	_apply_current_to_preview()


# ---------------- Helpers ----------------

func _label(t: String) -> Label:
	var l := Label.new()
	l.text = t
	return l


func _set_status(text: String, is_error: bool = false) -> void:
	_status_label.text = text
	_status_label.modulate = Color(1, 0.5, 0.5) if is_error else Color(0.7, 1, 0.7)
