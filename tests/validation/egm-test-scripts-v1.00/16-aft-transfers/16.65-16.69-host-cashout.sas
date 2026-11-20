# Chapter 16, sections 16.65 to 16.69 - the host taking over the cashout button.
#
# A host can tell the machine that, when the player presses cashout, it should first offer the
# host the chance to take the credits as a transfer. The machine then raises $6A and repeats it for
# a few seconds; the host either transfers everything out or answers with a zero transfer, and if
# it does neither the machine falls back to its own way of paying. Turned off, or over the machine's
# transfer limit, the button behaves as it always did.
#
# The enabling flags travel in the funds-transfer command and take effect when that transfer
# succeeds. Restricted and non-restricted credits wait for phase two (D45), so the software machine
# runs these with cashable credits only.

test "16.65 the host turns cashout to the host on and off" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, decoder.lp74, machine.aft, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Flags 0 and 1: the host takes control of cash-out and turns it on.
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transfer_flags == $03
  host send lp 74 data FF000000
  host expect field host_cashout_status == $03

  # Flag 0 alone: still the host's to decide, and now off.
  host send lp 72 data 36000000000000050000000000000000000000013930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field transfer_flags == $01
  host send lp 74 data FF000000
  host expect field host_cashout_status == $01

  note "A cashable transfer into the machine carrying the two host-cashout flags is followed by a status read that reports cashout to the host as enabled."
  note "The same with the enable flag clear and a new transaction number: the status reports it disabled."
  note "The document's requirement line for this section reads 15.64, which is a misprint for 16.65."
}

test "16.66 the cashout button asks the host first" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.button.collect, stimulus.bill, machine.transfer_limit, host.timing

  note "The host ignores $6A throughout. Load ten cents of each credit type with transfer flag bit 2 set, then send a zero transfer to the host with flags 0, 1 and 2 set, which enables cashout to the host."
  note "Press cashout: $6A comes on a general poll and repeats about every 800 ms for eight seconds; then the machine pays by its usual means."
  note "Send the same pair with bit 2 clear and the transfer limit below five dollars: pressing cashout raises no $6A and the machine pays at once. With the limit back above a hundred dollars and the credits reloaded, the button again raises $6A for eight seconds before the machine pays."
}

test "16.67 the host answers a cashout by taking the credits" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, decoder.lp74, machine.aft
  requires stimulus.button.collect, stimulus.asset_number, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm press collect
  host forget exceptions within 2s

  # Five dollars on, and cash-out to the host turned on by the same transfer.
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # The host takes everything with a partial transfer of all it can.
  egm press collect
  host expect exception aft_request_for_host_cashout within 5s
  host send lp 72 data 36010080999999999900000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $01
  host expect field cashable_amount == 500
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 0
  host forget exceptions within 2s
  host expect no exception aft_request_for_host_cashout within 3s

  # Reloaded, and this time a zero transfer: the machine pays the usual way.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000133000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  egm press collect
  host expect exception aft_request_for_host_cashout within 5s
  host send lp 72 data 36000080000000000000000000000000000000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  egm expect ticket printed within 20s
  host forget exceptions within 2s
  host expect no exception aft_request_for_host_cashout within 3s

  note "Load five dollars of each type and enable cashout to the host. Press cashout, and within five seconds ask for a partial transfer of everything to the host: it takes all the credits, the status shows a partial success ($01), and $6A stops."
  note "Reload and press again; this time answer with a zero full transfer to the host. Its status and the status read both show $00, $6A stops, and the machine pays by its usual means."
}

test "16.68 the cashout request goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.button.collect, stimulus.bill, stimulus.door.main, host.poll_sequence
  requires stimulus.asset_number, stimulus.settle, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host forget exceptions within 2s

  # Only long polls while the doors queue; then the press, and general polls within five seconds.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press collect
  host send gp draws aft_request_for_host_cashout

  # $6A recurs every 800 ms ahead of whatever is queued, so the doors are checked for arriving, not for their place.
  host poll every 200ms
  host expect exception slot_door_opened within 3s
  host expect exception slot_door_closed within 3s
  host expect exception aft_request_for_host_cashout within 3s
  egm expect ticket printed within 15s

  note "With only long polls running, put credits on, queue some ordinary exceptions and enable cashout to the host. Press cashout and start general polls within five seconds: $6A comes first and the queue after it, $6A keeps repeating, and the machine then pays."
  note "Again, then stop the round straight after the press and send a synchronisation poll, three polls to the machine, a long poll for its identity, and one more poll: that last one is answered with $6A again."
}

test "16.69 with cashout to the host off, the button just pays" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, decoder.lp74, machine.aft
  requires stimulus.button.collect, stimulus.bill, stimulus.asset_number, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm bill insert 500
  host forget exceptions within 2s

  # A zero transfer to the host with flag 0 set and flag 1 clear: the host's to decide, and off.
  host send lp 72 data 36000080000000000000000000000000000000013930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 74 data FF000000
  host expect field host_cashout_status == $01

  egm press collect
  egm expect ticket printed within 5s
  host expect no exception aft_request_for_host_cashout within 3s

  note "Put credits on and send a zero transfer to the host with both host-cashout flags clear; the status reports cashout to the host disabled. Press cashout: no $6A, and the machine pays by its usual means."
}
