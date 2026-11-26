# Chapter 15, sections 15.15 to 15.25 - the real-time events a bonus host relies on.
#
# Most of these sections carry no steps of their own and send the reader to chapter 11, where the
# same events are tested in full. They are written here as cases all the same, so the chapter's
# count is the document's: where the event can be produced here the case makes it once and checks
# its payload, and where it cannot the case skips naming what it needs, as its chapter 11 twin does.
#
# 15.20 is the one with content of its own: a bonus awarded during a game is not part of that
# game's win, so the game end event reports the game alone.

test "15.15 reporting is switched on and off" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle

  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  egm play games=1 bet=1 win=0
  host expect field credits_wagered of event game_started == 1 within 8s

  host forget exceptions within 2s
  host send lp 0E data 00
  host forget exceptions within 2s
  egm play games=1 bet=1 win=0
  host expect exception game_started within 8s

  note "The full case is 11.1."
}

test "15.16 the idle answers in the event format" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.player_input_state

  note "The full case is 11.4: with reporting on, an idle machine answers $00 in the event format and one waiting for the player $1F."
}

test "15.17 the bonus event carries its payload" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.real_time_events
  requires decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  host send lp 8A data 0000000500
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s

  note "Before the run, enable legacy bonusing. The document points to 15.1 to 15.8, which check the same event in each state a bonus can arrive in."
}

test "15.18 a game start" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=2 win=0
  host expect field credits_wagered of event game_started == 2 within 8s
  host expect field total_coin_in of event game_started >= 2 within 8s

  note "The full case is 11.17."
}

test "15.19 a game end" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=1 win=0
  host expect field win_amount of event game_ended == 0 within 8s

  note "The full case is 11.18."
}

test "15.20 a bonus is not part of the game's win" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.real_time_events, stimulus.bill
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 1000
  host forget exceptions within 2s

  # A winning game: $7F carries the game's own five credits, and the bonus comes apart in $7C.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8A data 0000000500
  egm game end win=5
  host expect field win_amount of event game_ended == 500 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host forget exceptions within 2s

  # A losing game: $7F carries nothing.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8A data 0000000500
  egm game end
  host expect field win_amount of event game_ended == 0 within 8s
  host expect field bonus_amount of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s

  note "Before the run, enable legacy bonusing. Each game is started and left running until the steps end it."
  note "The win is in the accounting denomination, as 11.18 says; the five-credit win is the game's own and must match the machine's display."
}

test "15.21 a draw request" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.poker

  note "The full case is 11.19."
}

test "15.22 a reel stopping" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  requires stimulus.reel, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event88, decoder.lp8F
  machine reels on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=1
  host expect field reel_number of event reel_n_stopped == 1 within 8s
  host expect field physical_stop of event reel_n_stopped <= 255 within 1s

  # Where each of the five came to rest, and no sixth, once the queue has drained.
  host forget exceptions within 2s
  host send lp 8F
  host expect field reel_5 of lp 8F != $FF
  host expect field reel_6 of lp 8F == $FF

  note "The full case is 11.20."
}

test "15.23 a credit wagered" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires stimulus.bet_button

  note "The full case is 11.21."
}

test "15.24 a game recalled" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.game_recall

  note "The full case is 11.22."
}

test "15.25 cards held" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.poker

  note "The full case is 11.23."
}
