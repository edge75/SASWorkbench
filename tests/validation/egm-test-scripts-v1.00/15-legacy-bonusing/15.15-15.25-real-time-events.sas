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
  machine bet button on
  requires machine.player_input_state, stimulus.bet_button, stimulus.real_time_events, stimulus.door.main
  requires stimulus.bill, stimulus.play, decoder.lp0E, decoder.lp1A

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host poll sequence

  # Idle with the door open: once the door is reported, nothing - in the event format - and a long poll gets its own reply.
  # A synchronisation poll between two general polls of the case's own acknowledges what the first drew.
  egm door main open
  host send gp draws event slot_door_opened
  host send sync
  host send gp draws event nothing
  host send sync
  host send gp draws event nothing
  host send sync
  host send lp 1A
  egm door main close
  host send gp draws event slot_door_closed
  host send sync
  host send gp draws event nothing
  host send sync

  # A wager placed and no start pressed: the machine waits for the player.
  egm press bet
  host send gp draws event coin_or_credit_wagered
  host send sync
  host send gp draws event waiting_for_player_input
  host send sync
  host send gp draws event waiting_for_player_input
  host send sync

  # Played: the game start and end, then nothing again.
  egm play games=1 bet=1 win=0
  host send gp draws event game_started
  host send sync
  host send gp draws event game_ended
  host send sync
  host send gp draws event nothing
  host poll every 200ms

  note "The full case is 11.4."
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
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  machine poker on
  requires machine.poker, stimulus.poker, stimulus.game_in_progress, stimulus.real_time_events, stimulus.bill
  requires decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host send lp 0E data 01
  host forget exceptions within 2s

  egm game start bet=1
  egm poker draw
  host expect exception player_requested_draw_cards within 8s
  egm game end win=0

  note "The full case, with reporting off as well, is 11.19."
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
  machine bet button on
  requires stimulus.bet_button, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # Five presses on a machine whose largest bet is five: one $89 for each, and none for a sixth.
  egm press bet
  egm press bet
  egm press bet
  egm press bet
  egm press bet
  host expect exception coin_or_credit_wagered within 8s
  host expect exception coin_or_credit_wagered within 8s
  host expect exception coin_or_credit_wagered within 8s
  host expect exception coin_or_credit_wagered within 8s
  host expect exception coin_or_credit_wagered within 8s
  egm press bet
  host expect no exception coin_or_credit_wagered within 3s

  egm play games=1 bet=5 win=0
  host expect exception game_ended within 8s

  # Off: the event is sent in no other format.
  host forget exceptions within 2s
  host send lp 0E data 00
  host forget exceptions within 2s
  egm press bet
  host expect no exception coin_or_credit_wagered within 5s

  note "The full case is 11.21."
}

test "15.24 a game recalled" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  machine games 1c 2c
  requires machine.game_recall, stimulus.game_recall, machine.multiple_games, stimulus.select_game
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp55, decoder.event8A

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  egm select game 1
  egm play games=1 bet=1 win=0
  egm select game 2
  egm play games=1 bet=1 win=0
  host forget exceptions within 2s
  host send lp 55
  host expect field selected_game of lp 55 == 2

  # The last game played, then the one before it.
  egm game recall 0
  host expect field game_number of event game_recall_entry_displayed == 2 within 8s
  host expect field recall_index of event game_recall_entry_displayed == 0 within 1s
  host forget exceptions within 2s
  egm game recall 1
  host expect field game_number of event game_recall_entry_displayed == 1 within 8s
  host expect field recall_index of event game_recall_entry_displayed == 1 within 1s

  # Off: the same entry comes back as a bare code on a general poll.
  host forget exceptions within 2s
  host send lp 0E data 00
  host forget exceptions within 2s
  egm game recall 0
  host expect exception game_recall_entry_displayed within 8s

  note "The full case is 11.22."
}

test "15.25 cards held" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  machine poker on
  requires machine.poker, stimulus.poker, stimulus.game_in_progress, stimulus.real_time_events, stimulus.bill
  requires decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host send lp 0E data 01
  host forget exceptions within 2s

  egm game start bet=1
  egm poker hold 5
  host expect field card of event card_held_or_not_held == $84 within 8s
  host forget exceptions within 2s
  egm poker release 5
  host expect field card of event card_held_or_not_held == $04 within 8s
  host forget exceptions within 2s
  egm game end win=0

  note "The full case, every card and reporting off as well, is 11.23."
}
