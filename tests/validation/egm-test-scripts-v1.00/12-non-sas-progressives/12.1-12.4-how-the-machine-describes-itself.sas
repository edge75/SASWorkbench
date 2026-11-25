# Chapter 12, sections 12.1 to 12.4 - what a machine on a non-SAS progressive link says about
# itself.
#
# A non-SAS progressive is run by a controller wired to the machine, not by this host, so as far as
# SAS is concerned the machine belongs to no progressive group: every reply that carries a group
# carries zero, and the extended game information lists no SAS levels even when the game has
# non-SAS levels one to four. The machine library agrees: it counts only levels configured as SAS
# levels in that reply, and reports a non-SAS win with group zero.
#
# Each case declares the machine's non-SAS levels, so the software machine is built with them and
# claims 'machine.progressives'; no poll can tell a non-SAS level is there, so any other machine
# skips. Running them on a machine without one would pass for the wrong reason: zero is what any
# machine with no progressive reports. The document's host simulator settings - no multi-level
# updates, group zero, no levels - are this host's behaviour already, since it never sends a
# progressive update unless told to.

test "12.1 the identification reply gives group zero" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, decoder.lp1F
  machine progressive non_sas levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 1F
  host expect field progressive_group of lp 1F == 0

  note "Before the run, configure the machine for a non-SAS progressive and leave it idle."
  note "The rest of the reply is checked as in 5.9."
}

test "12.2 the extended information of the machine gives no group and no levels" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, decoder.lpB5
  machine progressive non_sas levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B5 with game_number=0
  host expect field game_number of lp B5 == 0
  host expect field progressive_group of lp B5 == 0
  host expect field progressive_levels of lp B5 == 0

  note "Before the run, configure the machine for a non-SAS progressive with levels one to four."
  note "The rest of the reply is checked as in 5.21."
}

test "12.3 the extended information of each game gives no group and no levels" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, decoder.lpB5, decoder.lp51, decoder.lp56
  machine progressive non_sas levels 1-4

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host send lp 56
  host expect field count >= 1

  host send lp B5 with game_number=1
  host expect field game_number of lp B5 == 1
  host expect field progressive_group of lp B5 == 0
  host expect field progressive_levels of lp B5 == 0

  note "Before the run, configure the machine's progressive game with non-SAS levels one to four."
  note "A machine that reports no games implemented has no per-game form of this reply, and the case is a pass by saying so. Otherwise repeat the reading for every game number in the enabled list: each gives group zero and no levels, and the rest of each reply is checked as in 5.21."
}

# The document's set-up for this case configures every player denomination and says nothing about
# the progressive, so it is gated on a multi-denomination machine only.
test "12.4 the extended information by denomination gives no group and no levels" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations, decoder.lpB0, decoder.lpB2, decoder.lpB5, decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host expect field count of lp B2 >= 2
  host send lp B0 data 0401B50000
  host expect field base_command of lp B0 == 0xB5
  host expect field denomination of lp B0 == 1
  host expect field game_number of lp B0 == 0
  host expect field progressive_group of lp B0 == 0
  host expect field progressive_levels of lp B0 == 0
  host send lp 56
  host expect field count of lp 56 >= 1

  note "Before the run, enable every player denomination. The steps ask for game zero at one cent; ask instead at the denomination currently in use."
  note "The answer wraps $B5 behind the preamble: the machine's address without the wakeup bit, $B0, a length equal to the reply's size less five, the denomination asked for, $B5, the inner length, game 0000, the maximum bet in game credits, group 00, levels 00000000, and the game name. For game zero the maximum bet is the largest wager any game allows."
  note "Repeat for every player denomination the machine reports: the maximum bet is the largest wager any game allows at that denomination. Then, at the selected denomination, ask for the selected game from the enabled list: the maximum bet is that game's."
  note "Run only on a machine that reports multi-denomination support (9.1)."
}
