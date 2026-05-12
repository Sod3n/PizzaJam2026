@tool
extends Node3D
@export var chunk_scene: PackedScene
@export var grid_size: Vector2i = Vector2i(10, 10)
@export var chunk_spacing: float = 10.0
@export var regenerate: bool = false : set = _regen

func _regen(_v):
	for child in get_children():
		child.queue_free()
	var half = Vector2(grid_size - Vector2i.ONE) * 0.5
	for x in grid_size.x:
		for z in grid_size.y:
			var c = chunk_scene.instantiate()
			add_child(c)
			c.owner = get_tree().edited_scene_root
			c.position = Vector3(
				(x - half.x) * chunk_spacing,
				0,
				(z - half.y) * chunk_spacing
			)
