# Chapter 16, sections 16.82 to 16.86 - the lock a transfer can demand, and the history it leaves.
#
# A host can insist that a transfer only happen while it holds the machine locked, so the lock and
# status command and the transfer command have to agree: unlocked, such a transfer fails; locked
# for the right kind of transfer and inside the timeout, it succeeds. Every completed transfer
# then lands in a numbered history the host can read back by position, which has a fixed depth,
# wraps when full, holds nothing a refusal produced, and is empty after a memory clear.
#
# 16.82 and 16.83 run against the software machine (D45) and carry @finding: the library refuses
# a transfer that needs a lock synchronously in its reply and raises no $69 for it (lp.cpp:5373 and
# 5515), where the document expects $69 to follow and repeat. 16.85 is in a file of its own, because
# filling the history takes a step per transfer.

test "16.82 a transfer that needs the machine locked" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding
  requires decoder.lp72, machine.aft
  requires decoder.lp74, stimulus.asset_number, stimulus.button.collect

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm press collect
  host forget exceptions within 2s

  # Unlocked, a transfer with flag 6 set fails with not locked, and nothing moves.
  host send lp 72 data 36000000000000050000000000000000000000403930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $94
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $94
  host expect field cashable_amount == 0

  # Locked for transfers in and out, and game locked while it holds.
  host send lp 74 data 00039999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00
  host expect exception game_locked within 7s

  # The same transfer with a new number now succeeds, and says the host required the lock.
  host send lp 72 data 36000000000000050000000000000000000000403930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 500
  host expect field transfer_flags == $41

  note "On an empty machine, a full cashable transfer in with flag 6 (lock required) set fails while unlocked: $69 follows, and the status read shows not locked ($94) and nothing moved."
  note "Ask for the lock for transfers in and out with a long timeout: the reply shows the lock pending, then an interrogation shows it held, and $6F repeats about every five seconds while it is. The same transfer with a new number now succeeds ($00, 500) and the status reports that the host required a lock."
  note "Repeat with the lock taken for one kind of transfer only and a transfer of that kind - out to the host, to a ticket, into the machine - each succeeding with the lock flag shown."
  note "With the lock timeout set to twenty seconds, and then to three, the locked transfer still succeeds as long as it is sent inside the time."
}

test "16.83 a refused transfer takes no place in the history" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding
  requires decoder.lp72, machine.aft, stimulus.asset_number, stimulus.button.collect

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm press collect
  host forget exceptions within 2s

  # A plain five-dollar transfer succeeds and takes a place in the history.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transaction_buffer_position > 0

  # Emptied, a transfer needing a lock fails, and both reads show it at position 0 with nothing moved.
  egm press collect
  host forget exceptions within 2s
  host send lp 72 data 36000000000000050000000000000000000000403930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status == $94
  host send lp 72 data 02FE00
  host expect field transfer_status == $94
  host expect field transaction_buffer_position == 0
  host expect field cashable_amount == 0
  host send lp 72 data 02FF00
  host expect field transfer_status == $94
  host expect field transaction_buffer_position == 0
  host expect field cashable_amount == 0
  host expect exception aft_transfer_complete within 20s

  # The history's next position is empty or an earlier success, never the refusal.
  host send lp 72 data 02FF02
  host expect field transfer_status != $94

  # A transfer of nothing completes at position 0 and moves nothing.
  host send lp 72 data 36000000000000000000000000000000000000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect field transfer_status <= $40
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FE00
  host expect field transfer_status == $00
  host expect field transaction_buffer_position == 0
  host expect field cashable_amount == 0
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transaction_buffer_position == 0
  host send lp 72 data 02FF02
  host expect field transfer_status != $94

  note "On an empty machine a plain full transfer of five dollars succeeds; note its position in the history."
  note "Empty the machine again; a transfer with the lock flag set and a new number fails. Both the peek at the current record and the status read show not locked ($94), position 0 and nothing moved, and $69 repeats about every fifteen seconds until read."
  note "Reading the history at the next position after the success answers either that nothing is there ($FF) or an earlier success with a non-zero amount; it is never the refusal."
  note "A full transfer of nothing at all is answered complete or pending; the peek and the status read then show $00, position 0 and nothing moved, and the same read of the history gives the same answer as before."
}

test "16.84 the current record before and after a memory clear" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp72, machine.aft
  requires machine.nvram_clear

  note "On an empty machine, a transfer needing a lock fails unlocked: the status read shows $94, position 0, nothing moved, and reading it again gives the same."
  note "Clear the machine's memory so its history is empty and set it up for transfers again. The status read now shows position 0, transfer status $FF and receipt status $FF."
}

test "16.86 a machine with no transfers yet" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp72, machine.aft
  requires machine.nvram_clear

  note "Clear the machine's memory so no transfer has ever completed, and read the status: position 0, transfer status $FF and receipt status $FF."
}
