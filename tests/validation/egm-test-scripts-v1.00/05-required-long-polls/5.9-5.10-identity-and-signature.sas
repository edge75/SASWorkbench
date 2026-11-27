# Chapter 5, sections 5.9 and 5.10 - who the machine says it is, and the signature of its code.
#
# The identification reply carries the accounting denomination, and that code is the unit every
# credit meter is counted in. So the two 5.9 cases read the code and then play, and the coin-in
# meter has to move in that unit. The signature case is a long poll answered twice: acknowledged
# at once, and answered for real on a later general poll whenever the calculation finishes.
#
# The exact coin-in movement depends on the value of a credit wagered, which a script cannot know
# for a machine a person is playing, so the meter is asserted to have moved and the exact figure is
# left in a note for the person comparing it.

test "5.9.1 the identification reply" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires host.poll_sequence, stimulus.bill, stimulus.play, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # Steps 1 to 3: with the round stopped, a broadcast, a general poll that finds nothing, and the
  # identification poll.
  host poll sequence
  host send sync
  host send gp draws nothing
  host send lp 1F
  host expect field denomination of lp 1F != 0

  # Steps 6 to 8: a wager moves the coin-in meter.
  host poll every 200ms
  host expect link up within 5s
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter coin_in +>= 1 within 3s

  note "Compare the rest of the reply with the machine and its documentation: the game identifier is the manufacturer's two-letter code, the denomination code is the one for the machine's accounting unit, the maximum bet is the largest wager it takes or 255 when that is larger, the group is zero unless it is configured for SAS progressives, the paytable is the one its audit screens show, and the payback is in hundredths of a percent with the leading digit dropped above 100%."
  note "The coin-in meter moves by the value of the wager in that accounting unit, not by the number of credits wagered. On the software machine a one-credit wager at five cents is twenty units."
}

# Every unit the machine can be configured in, each read back and each played.
test "5.9.2 the identification reply follows the accounting denomination" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.accounting_denomination, stimulus.bill, stimulus.play, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  egm set accounting denomination 1c
  host send lp 1F
  host expect field denomination of lp 1F == $01
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter coin_in +>= 1 within 3s

  egm set accounting denomination 5c
  host send lp 1F
  host expect field denomination of lp 1F == $02
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter coin_in +>= 1 within 3s

  egm set accounting denomination 10c
  host send lp 1F
  host expect field denomination of lp 1F == $03
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter coin_in +>= 1 within 3s

  note "The document goes through every unit the machine can be configured in; these three are the ones the software machine is most often built in. In each, the coin-in meter moves by the wager valued in that unit, which on the software machine is one hundred, twenty and ten units for a one-credit wager."
  note "Then power cycle the machine: once it answers again, every identification reply carries the same code as before. This build cannot power cycle the software machine, so that half is a person's."
}

test "5.10 the ROM signature" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.rom_signature, host.poll_sequence, host.bad_crc, stimulus.settle
  requires decoder.lp21, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # The first seed: acknowledged, the machine answers on while it calculates, and the signature
  # comes on a later general poll. There is no time limit, so the wait is long.
  host send lp 21 data 1234
  host send lp 1F
  host expect rom signature within 120s

  # The second seed, the same way.
  host send lp 21 data ABCD
  host send lp 1F
  host expect rom signature within 120s

  # A signature poll with a broken check sequence is ignored or refused, and the machine goes on
  # answering the host.
  host poll sequence
  host send sync
  host send lp 21 data 1234 with bad crc
  host send lp 1F
  host send gp draws nothing
  host poll every 200ms

  note "Each signature must match the manufacturer's value for its seed; the run's record shows both."
  note "Not done here: a broadcast with the first seed followed at once by the second, where the signature that arrives is the second one's; and the round stopped while it calculates, where nothing is sent until the identification poll is answered, the next two general polls carry the signature, a third repeats it or goes unanswered, a fourth after an identification poll carries it, and the general poll after that a one-byte exception. The language cannot yet compare one signature with another or hold the round through the calculation."
}
