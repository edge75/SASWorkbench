# Chapter 16, sections 16.87 to 16.89 - a receipt that cannot print, and the order credit is spent
# and paid out in.
#
# A transfer that asks for a receipt the machine cannot print must fail and say why. Once the three
# kinds of credit sit on one machine, play takes restricted credit first and non-restricted next,
# and the meters must show it; at cashout only cashable and non-restricted credit can leave as
# money, while restricted credit stays or leaves as a ticket that can only be played.
#
# 16.88 runs against the software machine; the other two need a printer that can be put out of action.

test "16.87 a receipt the machine cannot print" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.printer_fault

  note "With the receipt printer out of action, or on a machine with none, ask for a full cashable transfer with a receipt and patron details: it is answered pending with the receipt pending ($40 and $40)."
  note "The status read then shows the transfer failed for want of a receipt device ($90) and receipt status $FF."
}

# One cent is declared so a wager of five credits is five dollars.
test "16.88 restricted credit is played first" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, decoder.lp74, machine.aft
  requires decoder.lp6F, host.meter_arithmetic, stimulus.play, machine.restricted_credits, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  host send lp 72 data 36000000000000050000000005000000000500003930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # Each wager raises 0000; while 001B holds credit it pays, raising 0019 and lowering 001B.
  host send lp 6F data 0A0000000019001A001B00
  host remember field meters[0].value as p0000
  host remember field meters[1].value as p0019
  host remember field meters[2].value as p001a
  host remember field meters[3].value as p001b
  egm play games=1 bet=5
  host send lp 6F data 0A0000000019001A001B00
  host expect field meters[0].value == saved.p0000 + 500
  host expect field meters[1].value == saved.p0019 + 500
  host expect field meters[2].value == saved.p001a
  host expect field meters[3].value == saved.p001b - 500

  # With 001B at zero the non-restricted credit pays next, raising 001A.
  egm play games=1 bet=5
  host send lp 6F data 0A0000000019001A001B00
  host expect field meters[0].value == saved.p0000 + 1000
  host expect field meters[1].value == saved.p0019 + 500
  host expect field meters[2].value == saved.p001a + 500
  host expect field meters[3].value == saved.p001b - 500
  host send lp 74 data FF000000
  host expect field current_restricted_amount == 0
  host expect field current_nonrestricted_amount == 0
  host expect field current_cashable_amount == 500

  note "Once 001B reaches zero the display shows no restricted credit."
}

test "16.89 what a cashout pays out of mixed credit" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, stimulus.button.collect, stimulus.printer_fault, printer.wallet, decoder.lp4D

  note "Start empty with the printer disabled. With only restricted credit on the machine the cashout button does nothing."
  note "Five cashable and five restricted: cashout pays the five cashable dollars by hopper or hand pay and leaves the restricted five. Five restricted and five non-restricted: the non-restricted five are paid as cash and the restricted stay. Five cashable and five non-restricted: ten dollars are paid. Five of each: ten dollars are paid and the restricted five stay."
  note "With the printer enabled for restricted tickets and five of each loaded, cashout prints a cashable ticket for ten dollars with $3D, then a play-only ticket for five with $3D again."
}
