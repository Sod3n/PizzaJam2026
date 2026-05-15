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

	# Feed the shader the live ViewportTexture instead of a baked snapshot so the
	# blurred background keeps updating while the gacha reveal plays.
	original_material.set_shader_parameter("screen_texture", game_viewport.get_texture())
	material = original_material
	visible = true
	baked = true

func unbake() -> void:
	if not baked:
		return
	material = original_material
	visible = true
	baked = false
