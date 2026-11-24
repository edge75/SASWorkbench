# Chapter 12, sections 12.27 to 12.30 - the progressive's events with real-time reporting on.
#
# The hand pay pending, hand pay reset and progressive win events carry no data, so in the event
# format each is the machine's address, $FF, the code and a check sequence. Game start reports group
# zero, as every other reply in this chapter does, and the game end of a game that won the
# progressive carries the progressive as its win, with no external bonus in it.
#
# All four run on the software machine, built with the declared non-SAS level
# (machine.progressives) and winning it with 'egm progressive win' (stimulus.progressive); 12.27 sets
# a jackpot limit below the level's $10.00 so the win is paid by hand. With reporting on, a long poll
# sent while events are queued draws one of them (D44), so 12.27 drains them before reading the hand pay. 12.29 asks for nothing the progressive changes except the group, but it is gated
# like the rest: zero is what a machine without a progressive reports anyway.

test "12.27 the progressive hand pay and its reset as events" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, stimulus.bill
  requires stimulus.real_time_events, stimulus.handpay.reset, decoder.lp0E, decoder.lp1B
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  egm bill insert 1000
  host send lp 0E data 01
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception handpay_is_pending within 30s
  host forget exceptions within 2s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, and select the progressive game."
  note "Each of the two events arrives as the address without the wakeup bit, $FF, the code and a two-byte check sequence; read the shape in the frames."
}

test "12.28 a progressive win as an event" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.real_time_events, stimulus.bill, decoder.lp0E
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 1000
  host send lp 0E data 01
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception progressive_win within 30s

  note "Before the run, configure the machine for a non-SAS progressive at level 1 and select the progressive game."
  note "The event is the address without the wakeup bit, $FF, $54 and a check sequence. The document's criterion labels $54 as a hand pay pending, which it is not; $54 is the progressive win paid by the machine."
  note "The document expects the event on a long poll. This host always sends general polls, so which poll carried it is read in the frames (see 11.1)."
}

test "12.29 a game start reports group zero" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 0E data 01

  egm bill insert 10000
  host forget exceptions within 2s

  egm play games=1 bet=3 win=0
  host expect field credits_wagered of event game_started == 3 within 8s
  host expect field total_coin_in of event game_started >= 3 within 8s
  host expect field progressive_group of event game_started == 0 within 8s

  note "Before the run, configure the machine for a non-SAS progressive at level 1 and select the progressive game."
  note "One $7E for every game started. Bit 7 of the wager type is set only when three credits is the maximum bet. On a multi-denomination machine bit 6 is set and the low six bits carry the denomination played."
  note "The document wants the low six bits zero on a single-denomination machine; 11.17 reads them as the code for what a game credit is worth, which is what the software machine sends. The two readings disagree, and this case does not assert either."
}

test "12.30 a game end carries the progressive as its win" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.real_time_events
  requires stimulus.bill, decoder.lp0E, decoder.event7E, decoder.event7F
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 0E data 01

  egm bill insert 10000
  host forget exceptions within 2s

  egm progressive win level=1
  host expect exception game_started within 30s
  host expect field win_amount of event game_ended > 0 within 30s

  note "Before the run, configure the machine for a non-SAS progressive at level 1 and select the progressive game."
  note "Every $7E has its $7F. The win of the game that hit the level is the progressive amount in accounting credits and leaves out any external bonus; every losing game reports zero."
}
