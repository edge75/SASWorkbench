# Chapter 16, sections 16.74 to 16.79 - the cashout button exception, and every host-cashout case
# again with real-time events on.
#
# $66 says only that the button was pressed, once per press, whatever the host has asked for; it
# sits alongside $6A rather than replacing it. The last five sections take 16.66, 16.68, 16.69,
# 16.72 and 16.74 and ask for the same behaviour with event reporting on, where the exceptions come
# in the event format and answer long polls as well as general ones.
#
# All six run against the software machine. The two that wait for $6B, the win request, are findings
# for the reason 16.71 gives.

# One cent is declared so five dollars of each kind is five hundred of each.
test "16.74 the cashout button exception beside the host request" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, decoder.lp74, machine.aft
  requires stimulus.button.collect, machine.transfer_limit, machine.restricted_credits, stimulus.bill
  requires stimulus.asset_number, printer.wallet

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 74 data FF000000
  host expect field available_transfers != 0

  # Five dollars of each kind and cash-out to the host on; the press raises $66 and $6A, then the machine pays.
  host send lp 72 data 36000000000000050000000005000000000500033930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host forget exceptions within 2s
  egm press collect
  host expect exception cashout_button_pressed within 3s
  host expect exception aft_request_for_host_cashout within 3s
  egm expect ticket printed within 15s

  # Cash-out to the host off: the press is paid the usual way with no $6A.
  host send lp 72 data 36000080000000000000000000000000000000013930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  egm bill insert 500
  host forget exceptions within 2s
  egm press collect
  egm expect ticket printed within 5s
  host expect no exception aft_request_for_host_cashout within 3s

  note "Bit 1 of the available transfers is the one that says the machine can send to the host now; a single bit is not compared on its own yet (host.field_bits)."
  note "$6A repeats about every 800 ms for eight seconds among the other exceptions before the machine pays, and the machine waits about 800 ms before its usual cash-out when the host has turned it off."
  note "With a cash-out transfer limit below five dollars the press raises one $66 and the machine pays at once, and a partial transfer of everything to the host then empties it with $01. The software machine has no limit on what it cashes out to the host, as the platform's Max Cashless Out is not modelled."
}

test "16.75 the host cashout request as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.button.collect, machine.transfer_limit
  requires stimulus.asset_number, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host forget exceptions within 2s

  host send lp 0E data 01
  egm press collect
  host expect exception aft_request_for_host_cashout within 3s
  egm expect ticket printed within 15s

  note "General polls carry the events here because a long poll answered with an event fails its step (D43)."
  note "16.66 with event reporting on: $6A repeats about every 800 ms for eight seconds before the machine pays, and arrives in the event format on long polls as well as general polls."
}

test "16.76 the host cashout event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.button.collect, stimulus.door.main, host.poll_sequence
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

  # Reporting on, then the doors and the press with only this case on the line.
  host poll sequence
  host send sync
  host send lp 0E data 01
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

  note "General polls carry the events here because a long poll answered with an event fails its step (D43)."
  note "16.68 with event reporting on: everything passes as it does there, and $6A arrives in the event format on long polls as well as general polls."
}

# @finding: no $6B, and the win is a hand pay. The library offers a win to the host only when the platform
# asks it to (sasAftGmToHost, libsas_wrapper.cpp:3285), and QSP never does: a win past the credit limit is
# handed to an attendant (QSP CombinedAwardIsHandpaid).
test "16.77 the host win request as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @finding
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, machine.credit_limit, machine.transfer_limit, stimulus.play
  requires stimulus.credit_limit, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  egm set credit limit 1000
  host forget exceptions within 2s

  host send lp 0E data 01
  egm play games=1 bet=1 win=10
  host expect exception aft_request_for_host_to_cashout_win within 5s

  note "16.70 with event reporting on: $6B repeats for eight seconds before the machine pays, and arrives in the event format on long polls as well as general polls."
  note "The document points this section at 16.69, but its title and its exception are those of 16.70; the pointer is taken as a misprint."
}

# @finding: no $6B, and the win is a hand pay. The library offers a win to the host only when the platform
# asks it to (sasAftGmToHost, libsas_wrapper.cpp:3285), and QSP never does: a win past the credit limit is
# handed to an attendant (QSP CombinedAwardIsHandpaid).
test "16.78 the host win event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @finding
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, machine.credit_limit, stimulus.play, stimulus.door.main, host.poll_sequence
  requires stimulus.credit_limit, stimulus.asset_number, stimulus.settle

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  egm set credit limit 1000
  host forget exceptions within 2s

  host poll sequence
  host send sync
  host send lp 0E data 01
  egm door main open
  egm door main close
  egm play games=1 bet=1 win=10
  host send gp draws aft_request_for_host_to_cashout_win

  note "16.72 with event reporting on: $6B comes ahead of the queue in the event format, on long polls as well as general polls."
}

test "16.79 the cashout button exception as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, decoder.lp74, stimulus.button.collect, machine.transfer_limit
  requires stimulus.asset_number, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host forget exceptions within 2s

  host send lp 0E data 01
  egm press collect
  host expect exception cashout_button_pressed within 3s
  host expect exception aft_request_for_host_cashout within 3s
  egm expect ticket printed within 15s

  note "General polls carry the events here because a long poll answered with an event fails its step (D43)."
  note "16.74 with event reporting on: everything else passes as it does there, and $66 arrives in the event format on long polls as well as general polls."
}
