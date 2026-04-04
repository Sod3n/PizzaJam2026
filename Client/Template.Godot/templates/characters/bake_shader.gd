extends ColorRect

@export var game_viewport: SubViewport

var baked := false
var original_material: ShaderMaterial
var baked_rect: TextureRect

func _ready() -> void:
	original_material = material as ShaderMaterial

func bake_background() -> void:
	if baked:
		return
	game_viewport = Global.main_subviewport
	if game_viewport == null:
		push_warning("bake_background: no game viewport available, skipping bake")
		return

	await RenderingServer.frame_post_draw

	var img := game_viewport.get_texture().get_image()
	if img.is_empty():
		push_error("Failed to capture game viewport")
		return

	var screen_size := img.get_size()
	var screen_tex := ImageTexture.create_from_image(img)

	# Temporarily make this ColorRect visible with the shader
	# and feed it the captured screen
	original_material.set_shader_parameter("screen_texture", screen_tex)
	material = original_material
	visible = true

	# Force a few frames so the shader actually renders on screen
	await get_tree().process_frame
	await get_tree().process_frame

	# Now capture the final composited result from the main viewport
	var final_img := get_viewport().get_texture().get_image()
	if final_img.is_empty():
		push_error("Failed to capture final result")
		return

	var result_tex := ImageTexture.create_from_image(final_img)

	# Replace with static baked image
	visible = false
	baked_rect = TextureRect.new()
	baked_rect.texture = result_tex
	baked_rect.anchors_preset = Control.PRESET_FULL_RECT
	baked_rect.stretch_mode = TextureRect.STRETCH_SCALE
	get_parent().add_child(baked_rect)
	get_parent().move_child(baked_rect, get_index())
	baked = true
	print("Bake complete: ", final_img.get_size())

func unbake() -> void:
	if not baked:
		return
	if baked_rect:
		baked_rect.queue_free()
		baked_rect = null
	material = original_material
	visible = true
	baked = false
