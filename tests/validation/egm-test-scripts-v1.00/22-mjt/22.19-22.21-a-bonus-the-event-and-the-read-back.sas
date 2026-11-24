# Chapter 22, sections 22.19 to 22.21 - a legacy bonus alongside a multiplier, the $7C payload in
# full, and reading the last award back with long poll $90.
#
# A legacy bonus sent during a game is held until the game ends and is never multiplied; when the
# game also wins under a multiplier, one $7C reports both, with the bonus and the extra part kept
# apart. $90 returns the same four fields as the $7C payload for the most recent award. Once the
# host has acknowledged that reply the machine clears it, so the next $90 reads zeros; a repeat of
# the poll that implies the reply was lost gets the award again. The machine library clears it in
# exactly that way, queued behind the host's acknowledgement and skipped while a repeat is pending.
#
# Each case declares multiplied jackpots, which the software machine then performs; 22.19 holds each
# game open with 'egm game start' and sends the bonus before 'egm game end'.

test "22.19 a bonus during a multiplied game is paid but not multiplied" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.bill
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999020000
  host forget exceptions within 2s

  # A deductible bonus of five credits while idle.
  host send lp 8A data 0000000500
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host forget exceptions within 2s

  # The same bonus during a losing game: paid at its end, nothing multiplied.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8A data 0000000500
  egm game end
  host expect field win_amount of event game_ended == 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host forget exceptions within 2s

  # The same bonus during a winning game: one $7C with the extra part and the bonus kept apart.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8A data 0000000500
  egm game end win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 2 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. Each bonus is acknowledged; the credit meter grows by five, five, and five plus twice the win, as the machine shows. On a real machine send the bonus during every game until one wins."
  note "The document's losing-game criterion gives the extra part as the base win; with no win there is nothing to multiply, so zero is what this case expects. It also names an undefined key's acknowledgement after the disable and enable pair."
}

test "22.20 the bonus event carries the multiplier and its tax status" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s

  # Four times, non-deductible: bit 7 of the multiplier byte, while the tax status byte - which
  # belongs to the legacy bonus part - stays deductible.
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The credit meter grows by three times and then four times the win, as the machine shows. With reporting off, each $7C is one byte."
}

test "22.21 the last multiplied win is read back once" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp90, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host send lp 90
  host expect field multiplier == 3
  host expect field multiplied_win == 20
  host expect field tax_status == 0
  host expect field bonus == 0

  # Acknowledged by the general polls in between, so it has been cleared.
  host send lp 90
  host expect field multiplier == 0
  host expect field multiplied_win == 0
  host expect field tax_status == 0
  host expect field bonus == 0
  host forget exceptions within 2s

  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host send lp 90
  host expect field multiplier == $84
  host expect field multiplied_win == 30
  host expect field tax_status == 0
  host expect field bonus == 0
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine."
  note "Not run: with polling stopped, the document sends a broadcast general poll and then $90 three times back to back after a further three-times win. The first two replies carry the award - the second is the machine answering what it takes for a lost reply - and the third is either ignored or reads zeros. Accepting either outcome has no step in the language."
  note "The document asks for the four-times award to be read back by a first and a second $90 but sends only one; one is what this case sends."
}
