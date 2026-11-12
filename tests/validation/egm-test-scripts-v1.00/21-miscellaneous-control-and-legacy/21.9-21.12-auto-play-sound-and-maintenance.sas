# Chapter 21, sections 21.9 to 21.12 - four optional controls a machine may honour or ignore.
#
# Auto re-bet ($AA), sound off and on ($03, $04), game sounds off ($05) and maintenance mode ($0A,
# $0B). A machine is free not to implement any of them, so each case's wire criterion is "either
# acknowledged or ignored" and its real criterion is what a person sees or hears afterwards. That
# puts all four out of reach twice over: this build decodes none of the six commands, and a
# `host send lp` passes only on an answer, so the language has no way to accept silence as an
# equally good outcome. They are written as notes and skip on the first decoder they name.
#
# All four share a shape: the setting takes effect, does not survive a power cycle, can be set
# again, and is undone by its partner. Each also ends with a copy of a poll carrying a broken check
# sequence, to be ignored or refused (host.bad_crc).

test "21.9 auto re-bet is switched on and off by long poll AA" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @destructive
  requires decoder.lpAA, machine.power_cycle, stimulus.door.main

  note "Put enough credit on the machine for several games. Send $AA with the enable value and open and close the main door. If the machine acknowledged, it starts playing by itself, stops when the door opens, and does not resume on its own; if it ignored the poll, nothing starts and normal play resumes once the door is shut."
  note "Power cycle the machine: it comes back without auto play and can be played by hand. Send the enable again: if acknowledged, auto play starts. Send $AA with the disable value: if acknowledged, auto play stops and the machine is playable by hand; if ignored, it stays playable."
  note "Not run: the enable sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "21.10 all sound is switched off by long poll 03 and back on by 04" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @destructive
  requires decoder.lp03, decoder.lp04, machine.power_cycle

  note "With sound on and the volume up, take the machine through a few ordinary operations - a note, a game, a door - and remember what it plays. Send $03 and repeat them: if acknowledged, no sound effects play; if ignored, nothing changes."
  note "Power cycle and repeat: the sounds are back. Send $03 again: if acknowledged, silent again. Send $04: if acknowledged, the sounds return; if ignored, they were never off."
  note "Not run: $03 and $04 each sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# Narrower than 21.10: only what the game itself plays - reels, wins - goes quiet.
test "21.11 game sounds are switched off by long poll 05" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @destructive
  requires decoder.lp05, decoder.lp04, machine.power_cycle

  note "With sound on and the volume up, play a few games and remember what the game plays. Send $05 and play again: if acknowledged, no reel or game sounds play; if ignored, nothing changes."
  note "Power cycle and play: the game sounds are back. Send $05 again: if acknowledged, quiet again. Send $04: if acknowledged, the game sounds return; if ignored, they never stopped."
  note "Not run: $05 sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# Maintenance mode lets a technician open doors without setting off the door alarms.
test "21.12 maintenance mode is entered by long poll 0A and left by 0B" {
  tag @egm-test-scripts @chapter-21 @needs-a-person @destructive
  requires decoder.lp0A, decoder.lp0B, machine.power_cycle, stimulus.door.main

  note "Open and close the machine's doors to hear its door alarms. Send $0A and do it again: if acknowledged, the alarms stay silent; if ignored, they sound as before."
  note "Power cycle: the alarms sound again when a door opens, so the mode did not survive. Send $0A again, then $0B, opening and closing the doors after each: after $0B the alarms sound, whether $0B was acknowledged or ignored."
  note "The document's criterion for the second $0A reads as though the alarms should sound, which contradicts the first $0A; this case expects them silent, as the first time, and the reading is open."
  note "Not run: $0A and $0B each sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}
