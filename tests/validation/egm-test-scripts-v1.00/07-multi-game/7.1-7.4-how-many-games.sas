# Chapter 7, sections 7.1 to 7.4 - how many games the machine says it holds.
#
# The count is of games the machine implements, not of games a player can reach today, so it must
# not move when an operator disables some of them, and it must not move across a power cycle. A
# machine that has no multi-game support at all answers zero, which is a different statement from
# "one game" and is the only case where zero is right.
#
# Where the expected count is known in advance - a single game - it is written as a number.
# Elsewhere the first answer is remembered and every later one compared with it; what a person
# still does is check that first answer against the machine's configuration.

# One game, with the multi-game commands supported: the count is exactly one, before and after a
# power cycle. Only a person (or the manual stimulus) can cycle the power, so this skips on the
# software machine although the first half of it would pass there.
test "7.1 a single game reports a count of one, and keeps it across a power cycle" {
  tag @egm-test-scripts @chapter-7 @needs-a-person @destructive
  requires decoder.lp51, machine.power_cycle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games == 1

  egm power cycle
  host expect link up within 120s

  # Asked three times so a first answer built before the machine finished starting is caught.
  host send lp 51
  host expect field games == 1
  host send lp 51
  host expect field games == 1
  host send lp 51
  host expect field games == 1

  note "This case is for a machine that holds one game. A machine that holds several answers its own count and is covered by 7.3."
}

# One game with several paytables is still one implemented game per paytable the machine offers,
# and what counts is what is implemented. The wire half is that the count is a real count and not
# the zero of a machine without multi-game support; the other half is a comparison with the
# machine's own configuration.
test "7.2 a single game with several paytables reports what it implements" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp51, decoder.lp56, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1
  host remember field games as implemented

  # The enabled list cannot be longer than what is implemented, and a machine that implements
  # games has at least one of them enabled when it is playable.
  host send lp 56
  host expect field count >= 1
  host expect field count <= saved.implemented

  note "The count must equal the number of games or paytables the machine implements, whether or not each one is offered to the player; a person compares it with the machine's configuration screens (D40)."
}

# The two halves of 7.3: with every game the machine can hold switched on, and with some switched
# off. Both need the machine reconfigured by a person beforehand and then power cycled. The count
# before the power cycle is remembered and every answer after it compared with that.
test "7.3.1 every game configured: the count is what is implemented and survives a power cycle" {
  tag @egm-test-scripts @chapter-7 @needs-a-person @destructive
  requires decoder.lp51, machine.multiple_games, machine.enable_games, machine.power_cycle
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1
  host remember field games as implemented

  egm power cycle
  host expect link up within 120s

  # Asked three times so a first answer built before the machine finished starting is caught.
  host send lp 51
  host expect field games == saved.implemented
  host send lp 51
  host expect field games == saved.implemented
  host send lp 51
  host expect field games == saved.implemented

  note "Configure the machine with every game it can offer before the case starts. The count is the number of games implemented; a person checks the first answer against the configuration. On the demo machine that is four."
}

test "7.3.2 some games disabled: the count still names every game implemented" {
  tag @egm-test-scripts @chapter-7 @needs-a-person @destructive
  requires decoder.lp51, machine.multiple_games, machine.enable_games, machine.power_cycle
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1
  host remember field games as implemented

  egm power cycle
  host expect link up within 120s

  host send lp 51
  host expect field games == saved.implemented
  host send lp 51
  host expect field games == saved.implemented
  host send lp 51
  host expect field games == saved.implemented

  note "Configure the machine so the player can reach only some of its games before the case starts. The count is unchanged from 7.3.1, because disabling a game does not stop it being implemented; a remembered value does not outlive its case, so a person compares the two."
}

# A machine with no multi-game support answers zero. The software machine supports it and answers
# a count, so it is not the machine this case is about, and running it there would be a failure
# for the wrong reason.
test "7.4 a machine without multi-game support reports zero games" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp51, machine.no_multi_game_extensions

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games == 0

  note "Only for a machine whose manufacturer says it does not support the multi-game commands. Any machine that does belongs to 7.1 to 7.3 instead."
}
