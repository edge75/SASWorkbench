# A machine in standard validation says so in its enabled-features reply, and the corpus's
# pass criterion is exactly that: feature codes byte 1, bits 6 to 5, read as zero.
#
# This case used to send long poll $4C, which is the command that sets a secure enhanced
# validation identifier — a setter for one of the two styles this case is about the absence
# of. It skipped, so nothing was reported wrongly, but it would have asserted the wrong
# thing the moment the reply it named could be decoded.
#
# Not covered here, and named rather than left to be discovered: the machine's memory clear
# and its configuration, which are the corpus's first step and are done to the machine
# before a run rather than over the link.

test "10.1.1 standard validation configuration" {
  tag @wire-only

  requires egm.validation == standard
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 10s

  # Game zero is the machine itself rather than any one of its games.
  host send lp A0 with game_number=0
  host expect field game_number of lp A0 == 0
  host expect field validation_style of lp A0 == 0

  # The extended validation commands belong to the two host-validated styles, so a machine
  # in this one reports them absent.
  host expect field validation_extensions of lp A0 == 0
}
