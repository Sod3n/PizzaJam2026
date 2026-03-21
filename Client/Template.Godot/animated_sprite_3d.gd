@tool
extends AnimatedSprite3D

@export var look_at_camera: bool = false

# This runs when you first load the scene or change the script
func _notification(what: int) -> void:
	if what == NOTIFICATION_READY:
		if material_override:
			material_override = material_override.duplicate()
		set_process(true) # Force process to start in editor

func _process(_delta: float) -> void:
	# 1. Update Texture (Fixes White Cube)
	# Check if we have valid frames and a material to talk to
	if sprite_frames and sprite_frames.has_animation(animation):
		var current_tex: Texture2D = sprite_frames.get_frame_texture(animation, frame)
		var mat = material_override as ShaderMaterial
		if mat and current_tex:
			mat.set_shader_parameter("albedo_texture", current_tex)

	# 2. Look At (Editor & Game)
	if look_at_camera:
		var target_cam: Camera3D = null
		
		# Check if we are in Editor vs Running Game
		if Engine.is_editor_hint():
			# Editor camera check
			var editor_viewport = EditorInterface.get_editor_viewport_3d(0)
			if editor_viewport:
				target_cam = editor_viewport.get_camera_3d()
		else:
			target_cam = get_viewport().get_camera_3d()

		if target_cam:
			var target_pos = target_cam.global_position
			target_pos.y = global_position.y # Y-Billboard
			# Ensure we aren't looking at our own position (prevents error)
			if global_position.distance_squared_to(target_pos) > 0.1:
				look_at(target_pos, Vector3.UP)
