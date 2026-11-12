# Chapter 9, sections 9.1 to 9.3 - whether the machine is a multi-denomination machine at all.
#
# 9.1 is the question every other case in this chapter depends on: a machine that does not claim
# the multi-denomination extensions is excused from nearly all of them. This build cannot yet ask
# the machine that question as a capability, so the cases that apply only to a multi-denomination
# machine require `machine.multiple_denominations` and skip naming it, rather than running against
# a machine that may have said it is not one.

test "9.1 the enabled-features reply says whether the extensions are supported" {
  tag @egm-test-scripts @chapter-9 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number of lp A0 == 0

  # The two flags this case is about sit in the second feature byte, which this build decodes as
  # a whole byte and does not name bit by bit, so neither can be asserted on its own.
  note "In the second feature byte, a machine that sets the multi-denomination extensions flag must also set the extended meters flag. A machine that clears the multi-denomination flag may set extended meters or not. Read the byte in the reply until the two flags are named fields."
}

test "9.2 the token denomination does not follow the player's choice" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires machine.token_denomination_config
  requires decoder.lpB3

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B3
  host expect field token_denomination of lp B3 >= 0

  note "Only for a machine that claims the extensions in 9.1, with every player denomination enabled. Choose each player denomination in turn and ask for the token denomination after each: the code never changes from the first reading."
  note "Then change the token denomination in the machine's configuration and ask again, once for every token value the machine allows: each time the code is the one just configured."
}

test "9.3 the game start event says whether the game was multi-denomination" {
  tag @egm-test-scripts @chapter-9 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01

  egm bill insert 10000
  host forget exceptions within 2s

  egm play games=1 bet=2 win=0
  host expect field credits_wagered of event game_started == 2 within 8s

  # Back to plain exception reporting, once the game's last event has been read: with reporting
  # on, a queued event answers the long poll in place of its reply.
  host expect field win_amount of event game_ended == 0 within 8s
  host forget exceptions within 2s
  host send lp 0E data 00

  # The wager type criterion depends on the answer to 9.1, which a script cannot branch on, so it
  # is stated here rather than asserted one way and wrong on the other kind of machine.
  note "On a machine that claims the extensions in 9.1, the wager type of the game start carries the code of the denomination the game was played at in its low six bits and sets bit 6. On a machine that does not, the low six bits are zero and bit 6 is clear."
  note "Measured on the software machine, which does not claim the extensions: the wager type reads $06, so its low six bits are not zero. Chapter 11 (11.17) asserts that same $06 as correct. The two readings disagree, and which one a machine owes has not been settled against the protocol."
}
