extends "res://animated_sprite_3d.gd"

@export var duration: float = 1.0

@export_group("Breathing")
@export var scale_factor: float = 0.5

@export_group("Rotation")
@export var tilt_angle: float = 0.0

var start_scale_y: float
var start_rot_z: float

func _ready() -> void:
	start_scale_y = scale.y
	start_rot_z = rotation_degrees.z
	start_fancy_idle_3d()

func start_fancy_idle_3d() -> void:
	var rot_tween = get_tree().create_tween().set_loops()
	var scale_tween = get_tree().create_tween().set_loops()
	
	rot_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	scale_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	
	# HALF DURATION for scale makes it happen 2x faster 
	# than the rotation within the same total timeframe.
	var scale_speed = duration / 2.0
	
	# --- ROTATION: LEFT TO RIGHT (1 Cycle) ---
	rot_tween.tween_property(self, "rotation_degrees:z", start_rot_z + tilt_angle, duration)
	rot_tween.chain().tween_property(self, "rotation_degrees:z", start_rot_z - tilt_angle, duration)
	
	# --- SCALE: STRETCH AND SQUASH (2 Cycles in the same time) ---
	scale_tween.tween_property(self, "scale:y", start_scale_y * (1.0 + scale_factor), scale_speed)
	scale_tween.chain().tween_property(self, "scale:y", start_scale_y * (1.0 - scale_factor), scale_speed)
	scale_tween.chain().tween_property(self, "scale:y", start_scale_y * (1.0 + scale_factor), scale_speed)
	scale_tween.chain().tween_property(self, "scale:y", start_scale_y * (1.0 - scale_factor), scale_speed)
