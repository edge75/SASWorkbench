# A test that drives both ends: the note goes in at the machine, and the assertions are
# made over the wire. Either half alone can be right while the machine is wrong.

test "a twenty raises its exception and moves its meters" {
  tag @needs-stimulus
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host expect meter total_bill_in += 2000 within 2s
  host expect meter bills_20 += 1 within 2s

  egm bill insert 500
  host expect exception bill_accepted_5 within 5s
  host expect meter total_bill_in += 500 within 2s
  host expect meter bills_20 unchanged within 2s
}
