# Chapter 13, sections 13.36 to 13.40 - the progressive exceptions and the game events with
# real-time reporting on.
#
# With reporting on, $53, $51 and $54 arrive as an event - the machine's address, $FF and the code -
# rather than as a single byte, and the game start event carries the group the machine is enrolled
# in. The game end event carries what the game won, which for a progressive win is the progressive
# amount and never includes an external bonus.
#
# The software machine reports these events, but it belongs to no group and cannot win a
# progressive, so every case here needs an enrolled machine and skips.

test "13.36 the loss of broadcasts as an event" {
  tag @egm-test-scripts @chapter-13 @needs-a-person
  requires machine.sas_progressives, stimulus.real_time_events, host.progressive_broadcast
  requires decoder.lp0E, decoder.lp86

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 86 data 0701010000000500
  host send lp 0E data 01
  host forget exceptions within 2s

  # Level 1 is no longer broadcast: one event, then none for a minute.
  host expect exception no_progressive_info_received within 8s
  host expect no exception no_progressive_info_received within 60s
  host send lp 0E data 00

  note "Before the run, enrol the machine in group 1 with level 1 and select the progressive game. The host broadcasts level 1 every second until the event is wanted, then stops - this build cannot keep broadcasting in the background (host.progressive_broadcast)."
  note "The event is the address, $FF, $53 and the check sequence; read its shape in the frames."
}

test "13.37 a progressive hand pay as an event" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.real_time_events, machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lp0E, decoder.lp80, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host send lp 0E data 01
  host forget exceptions within 2s

  # A person wins level 1 here, as a hand pay.
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B == 1
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 0E data 00

  note "Before the run, set the jackpot limit low, turn partial pay off, enrol the machine in group 1 with level 1, and select the progressive game. The hand pay pending event is the address, $FF, $51 and the check sequence."
}

test "13.38 a progressive the machine pays as an event" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.real_time_events, decoder.lp0E, decoder.lp86

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 86 data 1301010000000500020000000300030000000200
  host send lp 0E data 01
  host forget exceptions within 2s

  # A person wins a level paid to the credit meter or the cash-out device here.
  host expect exception progressive_win within 10s
  host send lp 0E data 00

  note "Before the run, enrol the machine in group 1 and select the progressive game. The event is the address, $FF, $54 and the check sequence."
}

test "13.39 the game start event carries the group" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, host.progressive_broadcast, stimulus.real_time_events
  requires stimulus.bill, stimulus.play, decoder.lp0E, decoder.lp80, decoder.event7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # One event per game: three credits, and group 1.
  egm play games=1 bet=3 win=0
  host expect field credits_wagered of event game_started == 3 within 8s
  host expect field progressive_group of event game_started == 1 within 8s
  host send lp 0E data 00

  note "Before the run, enrol the machine in group 1 with level 1 and select the progressive game."
  note "Read the wager type in the frames: on a multi-denomination machine bits 0 to 5 name the denomination played and bit 6 is set; otherwise both are zero. Bit 7 is set only if three credits is the game's maximum bet. The coin-in meter is in accounting units."
}

test "13.40 the game end event reports the progressive won" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp86, decoder.event7E, decoder.event7F
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 86 data 1301010000000500020000000300030000000200
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # A losing game: one start, one end, nothing won.
  egm play games=1 bet=1 win=0
  host expect exception game_started within 8s
  host expect field win_amount of event game_ended == 0 within 8s

  # A person wins level 1 here, at the maximum bet: the end reports the progressive amount.
  host expect exception game_started within 30s
  host expect field win_amount of event game_ended == 500 within 30s
  host send lp 0E data 00

  note "Before the run, enrol the machine in group 1 with levels 1, 2 and 3 and select the progressive game."
  note "Every game start is followed by exactly one game end. A bonus paid during the game is not part of the amount the end reports. The document puts the amount in accounting units; at the one-cent unit declared here that is the same number as the five hundred cents won."
}
