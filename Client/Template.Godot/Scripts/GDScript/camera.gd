# Attach this to your Camera3D. Camera is authored as a child of the player so
# its initial transform defines the framing offset; at runtime it detaches
# (top_level), tracks the player rigidly, and leans very slightly in the
# direction of movement so the world ahead of the player is a touch more visible.
extends Camera3D

@export var pixels_per_meter: float = 32.0
@export var lead_smoothing: float = 2.5    # how fast the lean ramps in/out
@export var lead_distance: float = 0.5     # max world-units of lean at full speed
@export var lead_speed_ref: float = 6.0    # player speed (u/s) at which lean reaches max

var _target: Node3D
var _offset: Vector3
var _prev_target_pos: Vector3
var _lead: Vector3 = Vector3.ZERO

func _ready() -> void:
	_target = get_parent() as Node3D
	if _target == null:
		return
	_offset = global_position - _target.global_position
	_prev_target_pos = _target.global_position
	top_level = true
	# Run after ViewSmoothingManager so we read the smoothed player position
	# from this frame, not last frame's.
	process_priority = 1000

func _process(delta: float) -> void:
	if _target == null or delta <= 0.0:
		return

	var target_pos := _target.global_position
	var vel := (target_pos - _prev_target_pos) / delta
	_prev_target_pos = target_pos

	# Project velocity onto camera's screen axes so the lean shifts along
	# screen X/Y instead of into/out of the ground plane.
	var cam_right := global_transform.basis.x
	var cam_up := global_transform.basis.y
	var planar := cam_right * vel.dot(cam_right) + cam_up * vel.dot(cam_up)

	var desired_lead := Vector3.ZERO
	var speed := planar.length()
	if speed > 0.01:
		var amt: float = clamp(speed / lead_speed_ref, 0.0, 1.0)
		desired_lead = planar.normalized() * (lead_distance * amt)

	var t: float = 1.0 - exp(-lead_smoothing * delta)
	_lead = _lead.lerp(desired_lead, t)

	global_position = target_pos + _offset + _lead
