# Chapter 21, sections 21.13 and 21.14 - the cash-out limit, for the machine and for each game.
#
# Long poll $A4 asks for the largest amount the machine will pay from its hopper (or by whatever
# cash-out it uses) before it locks up for an attendant. The request names a game, two BCD digits;
# the reply echoes the game and gives the limit in accounting units, also BCD. The library answers
# only once the machine has set a limit, which one without a hopper never does, so both cases need a
# machine that reports one (machine.cashout_limit); the limit is then compared with the figure in the
# machine's own set-up screens (D40).

test "21.13 the cash-out limit of the machine" {
  tag @egm-test-scripts @chapter-21 @wire-only
  requires decoder.lpA4, machine.cashout_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero, the machine as a whole.
  host send lp A4 data 0000

  note "The reply is the address without the wakeup bit, $A4, the game number 0000, two BCD bytes of limit and a check sequence. The limit is the base game's hopper or cash-out limit as the set-up display shows it, in accounting units (D40)."
  note "Not run: the poll sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# The same question for every game the machine has, and silence for one it does not have.
test "21.14 the cash-out limit of each game" {
  tag @egm-test-scripts @chapter-21 @wire-only
  requires decoder.lp51, decoder.lpA4, machine.cashout_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1

  host send lp A4 data 0000
  host send lp A4 data 0001

  note "Ask for every game from 0 to the count long poll 51 gave, then for one past it. Each implemented game answers as in 21.13 with its own limit, or with the base game's limit if the machine has no per-game limits; the game one past the last is not answered. The language has no variables or loops, so only games 0 and 1 are sent above; on a machine with a known count, add the rest and end with `host expect no reply to lp A4 data <n+1>`."
}
