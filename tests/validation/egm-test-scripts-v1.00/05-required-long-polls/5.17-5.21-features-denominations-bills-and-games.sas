# Chapter 5, sections 5.17 to 5.21 - enabled features, the token, the last note, and the game.
#
# Five replies that describe how the machine is configured. Each runs against the software
# machine; what a person has to compare with the machine's own configuration - which features it
# really has turned on, which token it really takes - is left in a note, and so is anything that
# needs the machine reconfigured between two readings.

test "5.17 the enabled features of the machine as a whole" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number of lp A0 == 0

  note "The three feature bytes say what the machine supports and has turned on. Compare them with its configuration, then turn on in turn each optional feature it offers - tournament, legacy bonusing, validation style and extensions, ticket redemption, the meter model, tickets to drop, extended meters, component authentication, AFT, multiple denominations, the polling rate, multiple progressive wins - and read again: only the bits for what was changed have moved."
  note "A copy of the poll with a broken check sequence is ignored or refused; this build cannot send one."
}

# Asked about game one rather than the machine. The answer depends on whether the machine speaks
# the multi-game polls at all, and the software machine does, so it runs.
test "5.18 the enabled features of game one" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lpA0, decoder.lp51

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=1
  host expect field game_number of lp A0 == 1

  note "A machine that supports the multi-game polls answers with the game number asked for; with a single paytable its feature bits are those of 5.17. A machine with one paytable and no multi-game polls must ignore this poll instead, and on such a machine this case fails by design: read 5.13 first, which tells the two apart."
}

test "5.19 the token denomination" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.bill, stimulus.play, stimulus.settle
  requires decoder.lpB3, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp B3
  host send lp 1F
  host expect field denomination of lp 1F != 0

  # Whatever the token is worth, the meters count in the accounting unit.
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter coin_in +>= 1 within 3s

  note "The token code is the one for the token the machine is configured to take; the software machine takes none and reports 00. Configure each token denomination the machine offers, read the code back, and play: the coin-in meter always moves by the wager valued in the accounting unit."
  note "On a machine with a single denomination, changing the token changes the accounting unit too, and on some machines changing the player denomination does. Either way the credit meters follow the accounting unit."
}

test "5.20 the last accepted note" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.bill, stimulus.settle
  requires decoder.lp48

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A US twenty: country code 37, denomination code 4, and at least this one counted.
  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host send lp 48
  host expect field country_code of lp 48 == 37
  host expect field denomination_code of lp 48 == 4
  host expect field count of lp 48 >= 1

  # Another of the same: that denomination's count moves by one.
  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host expect meter bills_20 += 1 within 3s
  host send lp 48
  host expect field count of lp 48 >= 2

  note "The count is the machine's lifetime count of that denomination, so only its movement can be asserted; the second reading is one more than the first. Repeat with every note the machine accepts, checking each country and denomination code against the tables."
}

test "5.21 the extended information of the machine as a whole" {
  tag @egm-test-scripts @chapter-5 @wire-only
  requires decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero with no progressives: no group, no levels, and a name of at most twenty characters.
  host send lp B5 with game_number=0
  host expect field length of lp B5 >= 9
  host expect field game_number of lp B5 == 0
  host expect field progressive_group of lp B5 == 0
  host expect field progressive_levels of lp B5 == 0
  host expect field name_length of lp B5 <= 20

  note "Turn progressives off on the machine before the run. The maximum bet is the largest wager of any game on the machine, enabled or not, in game credits, and the name, if any, is printable text naming the machine as a whole."
  note "The document's picture of this reply has no paytable name and no wager categories, which this build's 6.02 layout of it has, so the document's upper bound on the length is not asserted. A copy of the poll with a broken check sequence is ignored or refused; this build cannot send one."
}
