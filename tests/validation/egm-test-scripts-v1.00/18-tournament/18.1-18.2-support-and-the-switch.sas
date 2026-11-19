# Chapter 18, sections 18.1 and 18.2 - whether the machine plays tournaments, and the poll that
# starts and ends one.
#
# A machine says it supports host-controlled tournaments in bit 3 ($08) of the first feature byte
# of its enabled-features reply. Long poll $8C then starts a session or ends one: a game number,
# a time as minutes and seconds, a starting credit amount, and a byte switching tournament pulses
# on. Zero time and zero credits ends the session. A machine without tournaments ignores $8C and
# the four tournament meter polls outright - it neither answers nor refuses them.
#
#   game 0, 5 minutes, 100 credits, no pulses     8C 0000 0500 00000100 00
#
# The machine library checks the same feature bit before it acknowledges $8C and before it
# answers $95 to $99, so a machine that clears the bit is one that ignores the polls. The software
# machine offers no tournaments, so both cases run here as the machine-without-tournaments half.

test "18.1 the enabled-features reply says whether tournaments are on" {
  tag @egm-test-scripts @chapter-18 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero, the machine as a whole.
  host send lp A0 with game_number=0
  host expect field game_number == 0

  note "Bit 3 ($08) of the first feature byte is set on a machine that offers tournaments and has them turned on, and clear otherwise. This build names no single bit of that byte (host.field_bits), so read it in the frame and compare it with the machine's configuration."
}

test "18.2 a machine without tournaments ignores the start poll" {
  tag @egm-test-scripts @chapter-18 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number == 0

  # Game zero, five minutes, a hundred credits, pulses off: ignored, not refused.
  host expect no reply to lp 8C data 000005000000010000

  note "Before the run, turn tournaments off at the machine if it offers them; bit 3 of the first feature byte is then clear. By hand, turn them on and send the same poll: the machine acknowledges it."
  note "Not run: the same poll with a broken check sequence must be ignored, or refused with the machine's address with its top bit set (host.bad_crc)."
}
