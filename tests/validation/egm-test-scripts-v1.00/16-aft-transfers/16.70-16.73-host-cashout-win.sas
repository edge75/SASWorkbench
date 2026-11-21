# Chapter 16, sections 16.70 to 16.73 - the host taking a win the machine cannot keep.
#
# The same arrangement as the cashout button, triggered instead by a win that would take the
# credit meter over its limit. The machine raises $6B and offers the host the win as a transfer;
# unanswered, or with the feature off, it falls back to paying it the usual way, which on a win over
# the limit means a hand pay.
#
# The software machine takes a credit limit from a script and, as the platform it mirrors does, hands a
# win past it to an attendant without offering it to the host: 16.73 passes, and the three that wait
# for $6B are findings. 16.70 also needs the repeat timed.

test "16.70 a win over the credit limit asks the host first" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, machine.transfer_limit, stimulus.play, host.timing

  note "Set the credit limit low enough that one win goes over it. Load ten cents of each type, then send a zero transfer to the host with flags 0, 1 and 2 set; both are pending or complete ($40 or $00)."
  note "Play to a win over the limit: $6B comes on a general poll and repeats about every 800 ms for eight seconds, then the machine pays the usual way."
  note "Send the pair again with bit 2 clear and the transfer limit below five dollars: a win over the credit limit raises no $6B. With the limit restored above a hundred dollars and the credits reloaded, the next such win raises $6B for eight seconds before the machine pays."
}

# @finding: no $6B, and the win is a hand pay. The library offers a win to the host only when the platform
# asks it to (sasAftGmToHost, libsas_wrapper.cpp:3285), and QSP never does: a win past the credit limit is
# handed to an attendant (QSP CombinedAwardIsHandpaid).
test "16.71 the host answers by taking the win" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @finding
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play, stimulus.credit_limit, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  # Five dollars on, with cash-out to the host turned on by the same transfer, and a limit one win goes over.
  host send lp 72 data 36000000000000050000000000000000000000033930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  egm set credit limit 1000
  host forget exceptions within 2s

  egm play games=1 bet=1 win=10
  host expect exception aft_request_for_host_to_cashout_win within 5s

  note "Within five seconds send a partial win-to-host transfer of everything with a new transaction number: every credit moves to the host, the status shows a partial success ($01), and $6B stops."
  note "Win over the limit again and answer with a zero full transfer to the host: the status is $00, $6B stops, and the machine pays the usual way."
}

# @finding: no $6B, and the win is a hand pay. The library offers a win to the host only when the platform
# asks it to (sasAftGmToHost, libsas_wrapper.cpp:3285), and QSP never does: a win past the credit limit is
# handed to an attendant (QSP CombinedAwardIsHandpaid).
test "16.72 the win request goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @finding
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play, stimulus.door.main, host.poll_sequence
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

  # Only long polls on the line, ordinary exceptions queued, then the win over the limit.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm play games=1 bet=1 win=10
  host send gp draws aft_request_for_host_to_cashout_win

  note "Then the queue after it, $6B repeating, and the machine paying. Again, stopping the round right after the win: a synchronisation poll, three polls to the machine, a long poll for its identity and one more poll, the last answered with $6B again."
}

# One cent is declared so a ten-credit win is ten dollars against a ten-dollar limit.
test "16.73 with cashout to the host off, a big win is a hand pay" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, decoder.lp74, machine.aft
  requires machine.credit_limit, stimulus.bill, stimulus.play, stimulus.credit_limit, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345

  # A zero transfer to the host with both host-cashout flags clear: cash-out to the host is off.
  host send lp 72 data 36000080000000000000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 74 data FF000000
  host expect field host_cashout_status == $01

  egm set credit limit 1000
  egm bill insert 500
  host forget exceptions within 2s
  egm play games=1 bet=1 win=10
  host expect exception handpay_is_pending within 5s
  host expect no exception aft_request_for_host_to_cashout_win within 3s
}
