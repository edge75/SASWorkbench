# Chapter 16, sections 16.18 to 16.24 - long poll $74, read for what it says about the machine.
#
# $74 does two jobs. With lock code $FF it only reads: the asset number, whether the machine is
# locked, which transfers it would take now, how cash-outs to the host are handled, the transfer
# status bits, the size of its transaction history, the three credit balances and the transfer
# limit. With $00 it asks the machine to lock for a transfer, and with $80 it cancels that. The
# cases here ask all three and check that what the reply reports about the machine does not depend
# on which of them was asked.
#
#   read     FF 00 0000     (lock code, transfer condition, timeout in hundredths, BCD)
#   lock     00 0F 9999     every transfer condition, 99.99 seconds
#   cancel   80 00 0000
#
# The software machine takes transfers and locks (D45), so the cases that need only that run
# against it, and so do 16.18 and 16.23 now that it can be configured and holds restricted credit.
#
# 16.22 takes the upper bound of Table 8.2b of SAS 6.02, 46-7F, where the document bounds the
# history size below $7F; the machine library answers $7F, one less than its 128-entry history.

# @finding: no reply once transfers are off. The library does not answer $74 at all with the transfer
# feature off (lp.cpp:6430), where the document expects a reply with the lock, the transfers and the status zero.
test "16.18 the reply to a status request" {
  tag @egm-test-scripts @chapter-16 @finding
  requires decoder.lp74, machine.aft, machine.configuration_options, stimulus.asset_number
  requires stimulus.aft_configuration

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 74 data FF000000
  host expect field length == $23
  host expect field asset_number == 12345
  host expect field game_lock_status == $FF
  host expect field restricted_pool_id == 0

  # Asked to lock: pending until the machine can stop play, or locked at once on one that is idle.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF

  host send lp 74 data 80000000
  host expect field game_lock_status == $FF

  # Transfers turned off at the machine: nothing is locked, offered or on.
  egm set aft off
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF
  host expect field available_transfers == 0
  host expect field aft_status == 0
}

test "16.19 which transfers the machine will take now" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @needs-stimulus
  requires decoder.lp74, decoder.lp1B, machine.aft, host.compare_replies
  requires stimulus.bill, stimulus.door.main, stimulus.game_in_progress, stimulus.printer_fault
  requires stimulus.handpay, stimulus.handpay.reset, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  egm bill insert 2000
  egm settle
  host forget exceptions within 2s

  # Credits on the machine: the three forms agree on what it will take.
  host send lp 74 data FF000000
  host remember field available_transfers as available
  host send lp 74 data 000F9999
  host expect field available_transfers == saved.available
  host send lp 74 data 80000000
  host expect field available_transfers == saved.available

  # Door open: nothing.
  egm door main open
  host send lp 74 data FF000000
  host expect field available_transfers == 0
  host send lp 74 data 000F9999
  host expect field available_transfers == 0
  host send lp 74 data 80000000
  host expect field available_transfers == 0
  egm door main close
  host forget exceptions within 2s

  # A hand pay waiting: nothing, until it is cleared.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 74 data FF000000
  host expect field available_transfers == 0
  host send lp 74 data 000F9999
  host expect field available_transfers == 0
  host send lp 74 data 80000000
  host expect field available_transfers == 0
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "The bits of the first reading: 0 transfer to the machine, 1 transfer from it, 2 transfer to its printer, 3 a win waiting to be cashed out to the host, 4 a bonus to the machine. Each is set only if the machine could do that right now; compare them with its configuration."
  note "In the middle of a game or inside the attendant menu, the read, the lock (which answers $40) and the cancel all report nothing available; once the game ends or the menu is left, the first reading comes back."
  note "With the printer disconnected or faulted, the three replies match the first reading except that transfer to the printer is off, and it stays off on the cancel sent while the printer is being reconnected."
  note "Not run: the read sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "16.20 how cash-outs to the host are handled does not depend on the form asked" {
  tag @egm-test-scripts @chapter-16 @wire-only @needs-a-person
  requires decoder.lp74, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data FF000000
  host remember field host_cashout_status as cashout

  # A lock asked for and cancelled straight away; on a machine taking no transfers it is refused.
  host send lp 74 data 000F9999
  host expect field host_cashout_status == saved.cashout
  host send lp 74 data 80000000
  host expect field host_cashout_status == saved.cashout

  note "Compare the first reading with the machine's configuration: bit 0 is set when the host decides whether a cash-out goes to it and clear when the machine forces it; bit 1 when cash-out to the host is enabled now; bit 2 when the host cash-out mode is hard."
}

test "16.21 the transfer status bits" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, decoder.lp74, machine.aft, host.compare_replies, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01

  host send lp 74 data FF000000
  host remember field aft_status as status
  host send lp 74 data 000F9999
  host expect field aft_status == saved.status
  host send lp 74 data 80000000
  host expect field aft_status == saved.status

  note "Compare the first reading with the machine's transfer settings, bit by bit: 0 a printer usable for transfer receipts, 1 a transfer to the host may be for less than the whole amount, 2 custom ticket data, 3 registered (set here, since the case has just registered), 4 in-house transfers on, 5 bonus transfers on, 6 debit transfers on, 7 any transfer on."
}

# The bound is the specification's, not the document's: see the file header.
test "16.22 the size of the transaction history" {
  tag @egm-test-scripts @chapter-16 @wire-only
  requires decoder.lp74

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data FF000000
  host expect field max_buffer_index > 0
  host expect field max_buffer_index <= $7F
  host send lp 74 data 000F9999
  host expect field max_buffer_index > 0
  host expect field max_buffer_index <= $7F
  host send lp 74 data 80000000
  host expect field max_buffer_index > 0
  host expect field max_buffer_index <= $7F

  note "The document recommends a history of at least $46 transactions."
}

# One cent is declared so the balances in cents and the credit meter move by the same number.
test "16.23 the balances and the transfer limit" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp74, decoder.lp72, decoder.lp6F, machine.aft, machine.transfer_limit, machine.restricted_credits
  requires host.meter_arithmetic, stimulus.bill, stimulus.play, stimulus.button.collect, stimulus.settle
  requires stimulus.credit_limit, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm set credit limit 100000

  # Empty, so the limit read now is the configured one with nothing to subtract.
  egm press collect
  egm settle
  host expect meter credits == 0 within 10s
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 0
  host remember field gaming_machine_transfer_limit as limit_empty

  # One dollar of cashable credit: the balance moves by a hundred cents and the limit falls by it.
  egm bill insert 100
  egm settle
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 100
  host expect field gaming_machine_transfer_limit == saved.limit_empty - 100
  host send lp 74 data 000F9999
  host expect field current_cashable_amount == 100
  host expect field gaming_machine_transfer_limit == saved.limit_empty - 100
  host send lp 74 data 80000000
  host expect field current_cashable_amount == 100
  host expect field gaming_machine_transfer_limit == saved.limit_empty - 100

  # Three dollars of restricted and five of non-restricted as well: each balance shows its own, the limit falls by all nine.
  host send lp 72 data 36000000000000000000000003000000000500003930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 100
  host expect field current_restricted_amount == 300
  host expect field current_nonrestricted_amount == 500
  host expect field gaming_machine_transfer_limit == saved.limit_empty - 900

  # Play takes the restricted credit first, then the non-restricted, then the cashable, and the limit follows the meter.
  egm play games=1 bet=4
  host send lp 74 data FF000000
  host expect field current_restricted_amount == 0
  host expect field current_nonrestricted_amount == 400
  host expect field current_cashable_amount == 100
  host expect field gaming_machine_transfer_limit == saved.limit_empty - 500
  egm play games=1 bet=5
  host send lp 74 data FF000000
  host expect field current_nonrestricted_amount == 0
  host expect field current_cashable_amount == 0
  host expect field gaming_machine_transfer_limit == saved.limit_empty

  note "The amounts also match the audit screen."
}

# The library refuses a lock naming no transfer condition and answers $FF (lp.cpp:6505), where the
# document expects the machine locked.
test "16.24 a status request takes no lock" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding
  requires decoder.lp74, machine.aft, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 74 data 80000000
  host expect field game_lock_status == $FF

  # A read naming every transfer condition still only reads.
  host send lp 74 data FF0F0000
  host expect field game_lock_status == $FF
  host remember field available_transfers as available
  host send lp 74 data FF010000
  host expect field game_lock_status == $FF
  host expect field available_transfers == saved.available
  host send lp 74 data FF080000
  host expect field game_lock_status == $FF
  host expect field available_transfers == saved.available

  # A real lock request: locked ($00) or pending ($40), never not locked.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF

  # A lock naming no condition and no timeout. The document expects the machine locked; the
  # library's handler refuses a request naming no condition and answers $FF, so a machine built
  # on it is expected to fail this step.
  host send lp 74 data 00000000
  host expect field game_lock_status == $00

  host send lp 74 data 80000000

  note "Through the reads the machine stays playable; after the lock with no condition it stays unplayable. Both are checked at the machine."
}
