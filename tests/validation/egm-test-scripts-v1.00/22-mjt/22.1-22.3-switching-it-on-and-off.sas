# Chapter 22, sections 22.1 to 22.3 - turning multiplied jackpots on and off, and what a lost link
# does to them.
#
# Long poll $8B asks the machine to multiply every game win inside a range: four BCD bytes of
# minimum win, four of maximum win, a multiplier of 1 to 10 with bit 7 set for non-deductible, an
# enable byte where 00 enables and 01 disables, and a wager type where 01 limits it to max-bet
# games. A multiplied win pays the extra part as a legacy bonus: $7F reports the game's own win and
# $7C follows with the multiplier and the extra amount. A machine that loses the host stops
# multiplying, and the host must enable it again.
#
# The machine library ignores $8B unless both the legacy-bonus and the jackpot-multiplier feature
# bits are set. 22.1 turns legacy bonusing off first, which the software machine and a person at the
# cabinet can both do. The software machine takes legacy bonuses but, like the platform it mirrors,
# never sets the jackpot-multiplier bit, so every case that needs a multiplied win skips on
# machine.multiplied_jackpot, which is read off the machine.
#
# The document recommends real-time reporting on, so the cases turn it on; with it off, $7F and $7C
# arrive as one byte each and the credit meter carries the evidence instead.

test "22.1 multiplied jackpots are ignored with legacy bonusing off" {
  tag @egm-test-scripts @chapter-22 @wire-only
  requires decoder.lpA0, stimulus.legacy_bonus

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm set legacy bonus off
  host forget exceptions within 2s

  host send lp A0 with game_number=0
  host expect field game_number == 0

  # The disable form of the command: no range, multiplier one, disable, any wager.
  host expect no reply to lp 8B data 0000000000000000010100

  note "The setter step switches legacy bonusing off at the machine. Bit 0 (jackpot multiplier) and bit 2 (legacy bonus) of the first feature byte in the enabled-features reply are then clear; this build names neither bit, so read them in the frames."
  note "Not run: the same command sent with a broken check sequence must be ignored, or refused with the address and bit 7 set (host.bad_crc). The document names the key for an enable command here although the case defines only the disable one."
}

test "22.2 a lost link switches multiplied jackpots off" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus @destructive
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.power_cycle
  requires stimulus.real_time_events, stimulus.bill, stimulus.play, host.poll_sequence
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # Doubled, deductible, every win, any wager.
  host send lp 8B data 0000000099999999020000
  egm play games=1 bet=1 win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 2 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s

  # Ten seconds with nobody polling, a win while the link is down, then polling again: the win is
  # reported and not multiplied.
  host poll sequence
  host wait 10s
  egm play games=1 bet=1 win=10
  host poll every 200ms
  host expect exception game_ended within 10s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  # Still off after the link is back, until the host enables it again.
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  host send lp 8B data 0000000099999999020000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 2 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host forget exceptions within 2s

  # And off again after a power cycle.
  egm power cycle
  host expect link up within 60s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. After every multiplied win the credit meter has grown by twice the game's win; after every plain one, by the win alone - a person confirms both on the machine."
}

test "22.3 multiplied jackpots are switched on and off" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999020000
  egm play games=1 bet=1 win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 2 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host forget exceptions within 2s

  host send lp 8B data 0000000000000000010100
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The credit meter grows by twice the first win and by the second win alone, as the machine shows."
  note "The document sends a key it never defines to switch the mode off; the disable command is clearly meant and is what this case sends."
}
