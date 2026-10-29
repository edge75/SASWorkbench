# Recorded by hand: money: a note, a cashout, and the host reading the ticket.
#
# 7 action(s) over 10.5s, split into 2 transaction(s) and 103 exchange(s).
#
# Every 'within' below is this tool's number and not the person's: what the machine
# was measured taking, multiplied by 3 and floored at 5s. The gaps
# between what a person clicks are that person thinking, and none of them is written
# down as a wait.
#
# What this recording could not capture:
#
#   a value the machine chose rather than the person: asserting the number it happened to hold would pass on this machine and fail on the next for no good reason
#     - bills_1 of lp $1E = 0
#     - bills_5 of lp $1E = 0
#     - bills_10 of lp $1E = 0
#     - bills_20 of lp $1E = 1
#     - bills_50 of lp $1E = 0
#     - bills_100 of lp $1E = 0

test "money: a note, a cashout, and the host reading the ticket" {
  tag @needs-stimulus
  # The enhanced styles keep the printed record waiting for $4D, and this case never reads it.
  requires egm.validation == standard
  requires decoder.lp1E
  requires decoder.lp3D
  requires printer.wallet
  requires stimulus.bill
  requires stimulus.button.collect
  requires stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  egm settle
  egm bill insert 2000
  host expect exception usd_20_bill_accepted within 5s
  host send lp 1E
  egm press collect
  host expect exception cashout_ticket_printed within 5s
  egm expect ticket printed within 20s
  host send lp 3D
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents
}
