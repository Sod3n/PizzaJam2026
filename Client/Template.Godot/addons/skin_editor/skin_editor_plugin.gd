@tool
extends EditorPlugin

const MENU_LABEL := "Open Skin Editor"

var _window: Window = null


func _enter_tree() -> void:
	add_tool_menu_item(MENU_LABEL, _open_window)


func _exit_tree() -> void:
	remove_tool_menu_item(MENU_LABEL)
	if is_instance_valid(_window):
		_window.queue_free()
		_window = null


func _open_window() -> void:
	if is_instance_valid(_window):
		_window.popup_centered(Vector2i(1100, 700))
		return

	var scene: PackedScene = load("res://addons/skin_editor/SkinEditorWindow.tscn")
	if scene == null:
		push_error("[SkinEditor] Could not load SkinEditorWindow.tscn.")
		return

	_window = scene.instantiate()
	EditorInterface.get_base_control().add_child(_window)
	_window.close_requested.connect(_on_window_closed)
	_window.popup_centered(Vector2i(1100, 700))


func _on_window_closed() -> void:
	if is_instance_valid(_window):
		_window.queue_free()
		_window = null
