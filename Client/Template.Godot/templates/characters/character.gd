extends CharacterBody3D

@onready var scale_anchor: Node3D = $ScaleAnchor

@export_group("Nodes")
## The visual mesh or container that will bounce up and down.
@export var skin_node: Node3D
@export var duration: float = 1.0

@export_group("Breathing")
@export var scale_factor: float = 0.05

@export_group("Rotation")
@export var tilt_angle: float = 3.0

@export_group("Bounce")
## Toggle the vertical bouncing movement.
@export var enable_bounce: bool = true:
	get(): return enable_bounce
	set(value):
		enable_bounce = value
		if enable_bounce:
			bounce_tween.play()
		else:
			await bounce_tween.loop_finished
			if not enable_bounce: bounce_tween.stop()
## How high the skin node moves during the cycle.
@export var bounce_height: float = 0.1

var start_scale_y: float
var start_rot_z: float
var start_skin_y: float

var bounce_tween: Tween

func _ready() -> void:
	start_scale_y = scale_anchor.scale.y
	start_rot_z = rotation_degrees.z

	if skin_node:
		start_skin_y = skin_node.position.y

	start_fancy_idle_3d()

func start_fancy_idle_3d() -> void:
	var rot_tween = get_tree().create_tween().set_loops()
	var scale_tween = get_tree().create_tween().set_loops()

	rot_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	scale_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

	# Speed shared by scale and bounce to keep them in sync
	var cycle_speed = duration / 2.0

	# --- ROTATION: LEFT TO RIGHT ---
	rot_tween.tween_property(self, "rotation_degrees:z", start_rot_z + tilt_angle, duration)
	rot_tween.chain().tween_property(self, "rotation_degrees:z", start_rot_z - tilt_angle, duration)

	# --- SCALE: STRETCH AND SQUASH ---
	scale_tween.tween_property(scale_anchor, "scale:y", start_scale_y * (1.0 + scale_factor), cycle_speed)
	scale_tween.chain().tween_property(scale_anchor, "scale:y", start_scale_y * (1.0 - scale_factor), cycle_speed)
	scale_tween.chain().tween_property(scale_anchor, "scale:y", start_scale_y * (1.0 + scale_factor), cycle_speed)
	scale_tween.chain().tween_property(scale_anchor, "scale:y", start_scale_y * (1.0 - scale_factor), cycle_speed)

	# --- BOUNCE: UP AND DOWN ---
	if skin_node:
		bounce_tween = get_tree().create_tween().set_loops()
		bounce_tween.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

		# We match the scale speed so the movement feels connected to the "breath"
		bounce_tween.tween_property(skin_node, "position:y", start_skin_y + bounce_height, cycle_speed)
		bounce_tween.chain().tween_property(skin_node, "position:y", start_skin_y, cycle_speed)
