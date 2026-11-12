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
  requires machine.rom_signature, host.poll_sequence, host.bad_crc
  requires decoder.lp21

  note "Ask the manufacturer for the signature of the machine's code for two seeds. Send the signature poll with the first seed: the machine acknowledges with its address, stays playable and idle, and a later general poll carries $21, the two-byte signature and the check sequence. It matches the manufacturer's value. Do the same with the second seed."
  note "Send a broadcast and the first seed again, then the second seed straight after: both acknowledged, and the signature that eventually arrives is the second one's. Note how long it took."
  note "Stop the round and ask again, then wait that long without polling: nothing is sent. Poll the identification poll until it is answered, then general polls: the first two carry the signature, the third repeats it or is unanswered, and a fourth after an identification poll carries it too. After that, a general poll is answered with a one-byte exception."
  note "A signature poll with a broken check sequence is ignored or refused, and the machine keeps answering the host throughout. This build cannot decode the signature reply or send a broken check sequence."
}
