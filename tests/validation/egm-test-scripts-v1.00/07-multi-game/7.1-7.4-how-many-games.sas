# Chapter 7, sections 7.1 to 7.4 - how many games the machine says it holds.
#
# The count is of games the machine implements, not of games a player can reach today, so it must
# not move when an operator disables some of them, and it must not move across a power cycle. A
# machine that has no multi-game support at all answers zero, which is a different statement from
# "one game" and is the only case where zero is right.
#
# Nothing in the language holds a value read in one step for comparison in a later one, so "the
# same count after the power cycle as before" can only be written where the expected count is
# known in advance - a single-game machine - and is a person's comparison everywhere else.

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
  requires decoder.lp51, decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1

  # The enabled list cannot be longer than what is implemented, and a machine that implements
  # games has at least one of them enabled when it is playable.
  host send lp 56
  host expect field count >= 1

  note "The count must equal the number of games or paytables the machine implements, whether or not each one is offered to the player; a person compares it with the machine's configuration screens (D40). The language cannot compare it with the enabled list above, which may be shorter but never longer."
}

# The two halves of 7.3: with every game the machine can hold switched on, and with some switched
# off. Both need the machine reconfigured between runs and then power cycled, and the expected
# count is the machine's own, which the language cannot carry from one read to the next.
test "7.3.1 every game configured: the count is what is implemented and survives a power cycle" {
  tag @egm-test-scripts @chapter-7 @needs-a-person @destructive
  requires decoder.lp51, machine.multiple_games, machine.enable_games, machine.power_cycle

  note "Configure the machine with every game it can offer. Read long poll 51: the count is the number of games implemented, as a two-byte BCD number."
  note "Power cycle the machine, let it reach a playable state, and read 51 several times. Every answer equals the first one."
  note "On the demo machine the configuration holds four games, so the expected count there is four."
}

test "7.3.2 some games disabled: the count still names every game implemented" {
  tag @egm-test-scripts @chapter-7 @needs-a-person @destructive
  requires decoder.lp51, machine.multiple_games, machine.enable_games, machine.power_cycle

  note "Configure the machine so the player can reach only some of its games. Read long poll 51: the count is unchanged from 7.3.1, because disabling a game does not stop it being implemented."
  note "Power cycle the machine and read 51 several times. Every answer equals the first one."
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
