# Chapter 16, sections 16.25 to 16.32 - locking the machine for a transfer, and letting it go.
#
# A host about to move money asks the machine to stop play first. The machine answers pending
# ($40) while it finishes what it is doing, then locked ($00), and stays locked until the transfer
# happens, the host cancels, the timeout runs out or the link is lost; then it is not locked ($FF)
# and playable again. A lock it could never honour - a transfer condition it does not offer, a
# machine already unplayable - it refuses with $FF and takes nothing.
#
#   read     FF 00 0000
#   lock     00 0F 9999     every transfer condition, 99.99 seconds
#   cancel   80 00 0000
#
# Whether the machine can be played is the half of each case nobody can read over the wire, so it
# is a note for a person (D40). The software machine takes locks as the platform does (D45): it
# locks at once whenever it is playable, so a lock request answers $00 rather than $40 there.
#
# Two cases carry @finding. 16.30's lock with no timeout is taken and released at once rather than
# refused, and 16.32's transfer-from-the-machine condition stays offered on an empty machine; the
# reasons are on each case.

test "16.25 the lock" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp74, decoder.lp72, machine.aft, machine.configuration_options
  requires stimulus.printer_fault, stimulus.door.main

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 80000000
  host send lp 74 data 000F9999
  host expect field game_lock_status == $40
  host send lp 74 data 80000000
  host expect field game_lock_status == $FF

  # Only transfer to the printer, with the printer out of action: not taken.
  note "Disconnect or fault the printer before the next step."
  host send lp 74 data 00049999
  host expect field game_lock_status != $00
  host send lp 74 data 80000000

  # Only a bonus to the machine, with bonusing turned off: not taken.
  note "Turn AFT bonusing off before the next step."
  host send lp 74 data 00089999
  host expect field game_lock_status != $00
  host send lp 74 data 80000000

  # No condition at all on an unplayable machine: refused.
  egm door main open
  host send lp 74 data 00009999
  host expect field game_lock_status == $FF
  egm door main close

  # A debit transfer, then a lock: refused.
  host send lp 72 data 36000040000000050000000000000000000000003930000021436587090000000000000000000000000000000131000000000000000000
  host send lp 74 data 000F9999
  host expect field game_lock_status == $FF

  note "At the machine: unplayable while the first lock is pending, playable after each cancel and after each refused lock, and playable again once the door that made it unplayable is closed."
}

test "16.26 cancelling a lock" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp74, machine.aft, stimulus.door.main, stimulus.game_in_progress

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Cancelling with nothing locked is harmless.
  host send lp 74 data 80000000
  host expect field game_lock_status == $FF

  egm door main open
  host send lp 74 data 80000000
  host expect field game_lock_status == $FF
  egm door main close

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data 80000000
  host expect field game_lock_status == $FF

  note "At the machine: playable throughout, except between the lock and its cancel."
  note "Then start a game and send the lock and the cancel while it is still running: the lock answers $40 and the cancel $FF, and the machine stays playable."
}

test "16.27 a lock is lost when the machine becomes unplayable" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp74, machine.aft, stimulus.door.main

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  egm door main open
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF
  egm door main close

  note "At the machine: unplayable while locked, and playable once the door is closed again."
}

test "16.28 a lock is lost with the link" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp74, machine.aft, host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  # Five seconds of silence is enough to drop the lock.
  host poll sequence
  host wait 6s
  host poll every 200ms
  host expect link up within 30s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  # And so, of course, is half a minute.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00
  host poll sequence
  host wait 32s
  host poll every 200ms
  host expect link up within 30s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  note "At the machine: unplayable while locked, playable again once polling resumes."
}

# @finding: the second transfer is taken, not refused $94. The platform locks at once whenever the
# machine is playable (QSP's lock provider), so the lock is never still pending when the transfer that
# insists on it arrives, and the library's lock check (lp.cpp:5373) finds the machine locked.
test "16.29 a lock pending while a transfer is in progress" {
  tag @egm-test-scripts @chapter-16 @finding
  requires decoder.lp74, decoder.lp72, machine.aft, machine.credit_limit, stimulus.button.collect
  requires stimulus.credit_limit, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm set credit limit 1000000
  egm press collect
  host forget exceptions within 2s

  # Lock, then five dollars in-house to the machine: the transfer is taken and the lock goes.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # Lock, then at once a transfer that insists on the lock: the lock is still pending, so refused.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 72 data 36000000000000050000000000000000000000403930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status == $94
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  note "The second transfer must follow the lock request before the machine has had time to lock."
}

# The library refuses a lock only for a transfer condition it does not offer (lp.cpp:6505); a zero
# timeout is stored and the lock taken, so the answer is $00, and the zero-length lock timer then
# fires at once on Windows (libsas_windows.cpp:857) but never on Linux (libsas_linux.cpp:315).
test "16.30 a lock runs out" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding @slow
  requires decoder.lp74, machine.aft

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  # After the whole timeout from the lock request, the lock is gone by itself.
  host wait 100s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  # Cancelled before it runs out instead.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00
  host send lp 74 data 80000000
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  # A lock asked for with no timeout is not taken.
  host send lp 74 data 000F0000
  host expect field game_lock_status == $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  note "At the machine: while locked it shows the player it is locked and refuses play; playable again after the timeout, after the cancel, and throughout the lock with no timeout."
}

test "16.31 a second lock request starts the timeout again" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @slow
  requires decoder.lp74, machine.aft

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  # Half-way through, the same lock again: still locked, and the clock restarts now.
  host wait 50s
  host send lp 74 data 000F9999
  host expect field game_lock_status == $00

  # Past the first timeout and short of the second: still locked.
  host wait 70s
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  host wait 35s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  note "At the machine: unplayable for the whole of the second timeout, and playable once it has run out."
}

# The available transfers are the platform's to publish, and it offers transfer from the machine
# whatever the credit meter holds (QSP AftAvailability.h, AftAvailableTransfersNow); the library
# then grants a lock for that condition (lp.cpp:6505), so the last request leaves the lock in place.
test "16.32 a lock request naming a different condition" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @needs-stimulus @finding @slow
  requires decoder.lp74, machine.aft, stimulus.bill, stimulus.button.collect, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm bill insert 2000
  egm settle

  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00

  # Only transfer from the machine, which it can do while it holds credits: still locked, and the
  # clock restarts from this request.
  host send lp 74 data 00029999
  host expect field game_lock_status == $00
  host wait 100s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  # With the credits gone there is nothing to transfer from the machine, so the same request
  # leaves the lock.
  egm press collect
  egm settle
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send lp 74 data FF000000
  host expect field game_lock_status == $00
  host send lp 74 data 00029999
  host expect field game_lock_status != $00
  host send lp 74 data 80000000

  note "At the machine: while locked it tells the player to wait and refuses play, for the whole of the second timeout; playable again once that has run out."
}
