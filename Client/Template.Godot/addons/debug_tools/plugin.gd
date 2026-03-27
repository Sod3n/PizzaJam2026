@tool
extends EditorPlugin

const AUTOLOAD_NAME = "DebugVisualizer"
const AUTOLOAD_PATH = "res://addons/debug_tools/DebugVisualizer.cs"

func _enter_tree() -> void:
	add_autoload_singleton(AUTOLOAD_NAME, AUTOLOAD_PATH)

func _exit_tree() -> void:
	remove_autoload_singleton(AUTOLOAD_NAME)
