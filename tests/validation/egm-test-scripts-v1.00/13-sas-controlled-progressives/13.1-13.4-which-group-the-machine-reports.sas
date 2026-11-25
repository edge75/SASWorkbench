# Chapter 13, sections 13.1 to 13.4 - which progressive group, and which levels, the machine says it
# belongs to.
#
# A machine linked to a host-controlled progressive carries a group number from 01 to FF and a set
# of levels, and three replies publish them: the identification reply ($1F) for the machine, the
# extended game information ($B5) for one game, and the same reply wrapped in the multi-denomination
# preamble ($B0) for one denomination. Group 00 means no SAS progressive at all, and then the level
# bits are all clear.
#
# The software machine belongs to no group unless a case declares one, so the half of 13.1 and 13.3
# that reads group 00 runs on it as built; 13.2 declares group 1 and is checked against the group the
# machine reports in $1F (machine.sas_progressives). Every chapter-13 case assumes the host is the one that owns the progressive category on
# the machine's channel - the machine library ignores the progressive polls on any other channel.

test "13.1 the identification reply carries the group" {
  tag @egm-test-scripts @chapter-13 @wire-only @needs-a-person
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Not enrolled: group zero.
  host send lp 1F
  host expect field progressive_group of lp 1F == 0

  note "Before the run, take the machine out of any SAS progressive group. The rest of the reply is checked as 5.10 checks it."
  note "Then, by hand: enrol the machine in group 1 and read $1F again - the group byte is now 01. Repeat with every group the machine lets an operator choose; the byte always names the group configured."
}

test "13.2 the extended information of the machine as a whole carries group and levels" {
  tag @egm-test-scripts @chapter-13 @wire-only @needs-a-person
  requires machine.sas_progressives, decoder.lpB5
  machine progressive sas group 1 levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Group 1 with levels 1 to 4: bits 0 to 3 of the level word.
  host send lp B5 with game_number=0
  host expect field game_number of lp B5 == 0
  host expect field progressive_group of lp B5 == 1
  host expect field progressive_levels of lp B5 == $0F

  note "Before the run, enrol the machine in group 1 with SAS levels 1, 2, 3 and 4. The rest of the reply is checked as 5.21 checks it."
  note "If the machine offers other levels, the level word has exactly one bit set per enabled SAS level, bit 0 for level 1."
}

test "13.3 the extended information of each game carries group and levels" {
  tag @egm-test-scripts @chapter-13 @wire-only @needs-a-person
  requires decoder.lp51, decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games of lp 51 >= 1

  # A game outside any group: zero for both.
  host send lp B5 with game_number=1
  host expect field game_number of lp B5 == 1
  host expect field progressive_group of lp B5 == 0
  host expect field progressive_levels of lp B5 == 0

  note "Before the run, leave the machine outside any SAS progressive group."
  note "Then, by hand: enrol game 1 in group 1 with levels 1 to 4 and read it again - group 01 and the level bits of exactly the levels enabled. Do the same for every enabled game, taking each one out of the group and back in."
}

test "13.4 the per-denomination information carries group and levels" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1-4
  requires machine.sas_progressives, machine.multiple_denominations
  requires decoder.lpB0, decoder.lpB2, decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host expect field count of lp B2 >= 2

  # Game zero at one cent, then at five cents: the machine's group and levels at each.
  host send lp B0 data 0401B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field denomination of lp B0 == 1
  host expect field game_number of lp B0 == 0
  host expect field progressive_group of lp B0 == 1
  host expect field progressive_levels of lp B0 >= 1
  host send lp B0 data 0402B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field progressive_group of lp B0 == 1

  host send lp 56
  host expect field count of lp 56 >= 1

  note "Only for a multi-denomination machine. Enable every player denomination and enrol the machine in group 1 with levels 1 to 4."
  note "Send $B5 for game 0 through the $B0 preamble at the denomination in play. The reply echoes that denomination and the wrapped command, its length byte counts what follows, the game number is 0000, the maximum bet is the largest wager of any game, and group and level bits are the machine's."
  note "Repeat at every denomination $B2 lists. The maximum bet is now the largest at that denomination, and group and levels are those of any game offered at it that is enrolled; a denomination with no enrolled game reports zero for both."
  note "Last, ask for the game $56 shows in play at the denomination in play: group and levels are that game's when it is enrolled at that denomination, and zero when it is not."
}
