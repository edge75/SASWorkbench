# One note, read back through every command that counts notes. The point is not any single
# assertion but that they agree: the dedicated count for that denomination, the credit value of
# every note taken, what went in last, and what is now in the stacker are four different replies
# describing one event, and a machine that moved one of them without the others would be caught
# here rather than by whoever reconciled the meters afterwards.
#
# The credit figures are in accounting units. This machine publishes code $02, five cents, so a
# twenty-dollar note is four hundred units and not two thousand.
#
# Against a real machine the two preconditions of note-moves-bill-meters.sas apply here too.

test "a note shows up in every reply that counts notes" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp35
  requires decoder.lp46
  requires decoder.lp48
  requires decoder.lp49
  requires decoder.lp4A

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s

  # The dedicated count for that one denomination.
  host send lp 35
  host expect field bills_20 == 1

  # Every note taken, valued in accounting units.
  host send lp 46
  host expect field bills_accepted_credits == 400

  # What went in last. The count is that denomination's own, this note included.
  host send lp 48
  host expect field country_code == 37
  host expect field denomination_code == 4
  host expect field count == 1

  # It went to the stacker, and nothing on this machine empties one.
  host send lp 49
  host expect field bills_in_stacker == 1

  host send lp 4A
  host expect field stacker_credits == 400
}

test "a denomination this acceptor never takes reads zero" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp3B
  requires decoder.lp44

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s

  # Zero because the machine has never been offered one of these, not because it fails to count
  # them - which is the distinction that decides whether a zero here is worth asserting on at all.
  host send lp 3B
  host expect field bills_25 == 0

  host send lp 44
  host expect field bills_100000 == 0
}
