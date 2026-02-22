#Author: FIREBORNDEV
#Adopted from https://youtu.be/wbTdRJacCMA?t=83

extends Area2D

onready var big_circle = $BigCircle
onready var small_circle = $BigCircle/SmallCircle
onready var big_circle_max_modulate = big_circle.self_modulate
onready var big_circle_max_alpha = big_circle_max_modulate.a

#divide by big_circle scale to adjust 
onready var max_distance = $CollisionShape2D.shape.radius

var touched = false

func _input(event):
	if event is InputEventScreenTouch:
		var distance = event.position.distance_to(big_circle.global_position)
		if not touched:
			if distance < max_distance:
				touched = true
		else:
			small_circle.position = Vector2(0,0)
			big_circle.self_modulate = Color(1, 1, 1, 0)
			touched = false 

func _process(delta):
	$BigCircle/SmallCirclePosText.text = "(" + str(small_circle.position.x) + ", " + str(small_circle.position.y) + ")"
	$BigCircle/JoyVeloText.text = "(" + str(get_velo().x) + ", " + str(get_velo().y) + ")"
	if touched:
		small_circle.global_position = get_global_mouse_position()
		#clamp the small circle
		small_circle.global_position = big_circle.position + (small_circle.global_position - big_circle.position).clamped(max_distance)
		#adjust transparency of large circle
		big_circle.self_modulate = Color(1, 1, 1, get_velo().length() * big_circle_max_alpha)
		#TODO: ^ make based on distance to edge, rather than max of velocities
		#TODO: Highlight especially the direction the player is pointing with their finger
		#TODO: Add pointer in direction player is pointing	
		
func get_velo():
	var joy_velo = Vector2(0,0)
	#adjust position by big circle scale
	var distance = Vector2(small_circle.position.x, small_circle.position.y) * $BigCircle.scale.x
	joy_velo.x = distance.x / max_distance
	joy_velo.y = distance.y / max_distance
	return joy_velo
