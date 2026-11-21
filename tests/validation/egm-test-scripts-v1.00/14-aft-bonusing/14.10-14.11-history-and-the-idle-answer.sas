# Chapter 14, sections 14.10 and 14.11 - the transfer history, and what an idle machine says.
#
# Every bonus lands in the machine's transaction history, and the host can read any entry back by
# its index with the interrogation form of long poll $72. And a machine that will take an AFT
# bonus says so when idle: it answers a general poll with $1F rather than $00, so a host knows
# there is somebody to pay. A door open takes that back.

# Three bonuses, read back out of the history one by one.
test "14.10 the history holds every bonus" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, decoder.lp1B, stimulus.asset_number
  requires stimulus.handpay.reset, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  # A coin-out bonus of nothing.
  host send lp 72 data 3700001000000000000000000000000000000001393000000000000000000000000000000000000000000000023130000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  # A jackpot bonus of nothing, which is a hand pay (14.4).
  host send lp 72 data 3700001100000000000000000000000000000001393000000000000000000000000000000000000000000000023131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  note "Before the run, enable AFT bonusing and leave the machine idle and playable."
  note "After each completion, note the transaction index the interrogation reports. Then interrogate each of the three indexes in turn (transfer code $FF, index as noted): each reply carries that transfer's own identifier, type and amounts. A script cannot yet put a remembered value into the bytes it sends, so these three reads are a person's."
}

# Bonusing off, the idle machine reports nothing; on, it reports that it is waiting; a door open,
# nothing again.
test "14.11 an idle machine says whether it takes a bonus" {
  tag @egm-test-scripts @chapter-14 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # With AFT bonusing off, the idle machine has nothing to say.
  host poll sequence
  host send sync
  host send lp 1F
  host send gp draws nothing
  host send lp 1F

  # A door open and closed in between is reported, and afterwards nothing again.
  egm door main open
  host send sync
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws nothing
  host send lp 1F
  egm door main close
  host send sync
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F

  note "Before the run, disable AFT bonusing and tournament play, and leave the machine idle and playable. The steps assert the disabled half: an idle general poll draws $00, and so does one with the door open."
  note "Then, by hand: enable AFT bonusing, and the idle general poll draws $1F instead; open the door, and it draws $00 again once the door exception has been taken. The software machine offers no bonusing, so on it the steps run only the half that holds with bonusing off."
}
