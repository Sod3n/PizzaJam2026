@tool
extends EditorPlugin

var _button: Button


func _enter_tree() -> void:
	_button = Button.new()
	_button.text = "Import PSDs"
	_button.pressed.connect(_on_import_pressed)
	add_control_to_container(CONTAINER_TOOLBAR, _button)


func _exit_tree() -> void:
	if _button:
		remove_control_from_container(CONTAINER_TOOLBAR, _button)
		_button.queue_free()
		_button = null


func _on_import_pressed() -> void:
	print("[PSD Importer] Starting import...")
	var script = load("res://Scripts/Editor/PsdImporter.cs")
	if script == null:
		push_error("[PSD Importer] Could not load PsdImporter.cs script")
		return
	var importer: Node = script.new()
	# Add temporarily to tree so it can use Godot APIs if needed
	add_child(importer)
	importer.call("_Run")
	importer.queue_free()
	print("[PSD Importer] Done!")
