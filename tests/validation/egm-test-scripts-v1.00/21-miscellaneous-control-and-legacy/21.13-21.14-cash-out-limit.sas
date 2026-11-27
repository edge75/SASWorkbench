# Chapter 21, sections 21.13 and 21.14 - the cash-out limit, for the machine and for each game.
#
# Long poll $A4 asks for the largest amount the machine will pay from its hopper (or by whatever
# cash-out it uses) before it locks up for an attendant. The request names a game, two BCD digits;
# the reply echoes the game and gives the limit in accounting units, also BCD. The library answers
# only once the machine has set a limit, which one without a hopper never does, so both cases need a
# machine that reports one (machine.cashout_limit); the limit is then compared with the figure in the
# machine's own set-up screens (D40). Both declare a hopper, whose limit the software machine reports
# for every game (D61), and set it, so the figure is known.

test "21.13 the cash-out limit of the machine" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  machine hopper on
  machine accounting denomination 5c
  requires decoder.lpA4, machine.cashout_limit, stimulus.hopper

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Fifty dollars is a thousand five-cent units.
  egm set hopper limit 5000

  # Game zero, the machine as a whole.
  host send lp A4 data 0000
  host expect field game_number of lp A4 == 0
  host expect field cashout_limit of lp A4 == 1000

  note "The reply is the address without the wakeup bit, $A4, the game number 0000, two BCD bytes of limit and a check sequence. The limit is the base game's hopper or cash-out limit as the set-up display shows it, in accounting units (D40)."
  note "Not run: the poll sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# The same question for every game the machine has, and silence for one it does not have.
test "21.14 the cash-out limit of each game" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  machine hopper on
  machine games 5c 25c
  machine accounting denomination 5c
  requires decoder.lp51, decoder.lpA4, machine.cashout_limit, stimulus.hopper

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm set hopper limit 5000
  host send lp 51
  host expect field games == 2

  host send lp A4 data 0000
  host expect field cashout_limit of lp A4 == 1000
  host send lp A4 data 0001
  host expect field game_number of lp A4 == 1
  host expect field cashout_limit of lp A4 == 1000
  host send lp A4 data 0002
  host expect field game_number of lp A4 == 2
  host expect field cashout_limit of lp A4 == 1000
  host expect no reply to lp A4 data 0003

  note "Ask for every game from 0 to the count long poll 51 gave, then for one past it. Each implemented game answers as in 21.13 with its own limit, or with the base game's limit if the machine has no per-game limits; the game one past the last is not answered. The case declares two games; on a machine with another count, ask for each and end with the one past the last."
}
