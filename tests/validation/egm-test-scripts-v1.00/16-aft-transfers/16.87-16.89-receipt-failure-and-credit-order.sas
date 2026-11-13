# Chapter 16, sections 16.87 to 16.89 - a receipt that cannot print, and the order credit is spent
# and paid out in.
#
# A transfer that asks for a receipt the machine cannot print must fail and say why. Once the three
# kinds of credit sit on one machine, play takes restricted credit first and non-restricted next,
# and the meters must show it; at cashout only cashable and non-restricted credit can leave as
# money, while restricted credit stays or leaves as a ticket that can only be played.
#
# The transfers need the funds-transfer command decoded and a machine that performs them.

test "16.87 a receipt the machine cannot print" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.printer_fault

  note "With the receipt printer out of action, or on a machine with none, ask for a full cashable transfer with a receipt and patron details: it is answered pending with the receipt pending ($40 and $40)."
  note "The status read then shows the transfer failed for want of a receipt device ($90) and receipt status $FF."
}

test "16.88 restricted credit is played first" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp6F, host.meter_arithmetic, stimulus.play, machine.restricted_credits

  note "Put five dollars of each type on with a single full transfer; it succeeds ($00)."
  note "Play. Meter 0000 grows by every wager whatever credit paid for it. While 001B is above zero each wager raises 0019 and lowers 001B by the same amount; once 001B reaches zero the display shows no restricted credit, and each wager raises 001A instead until the non-restricted credit is gone too."
}

test "16.89 what a cashout pays out of mixed credit" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, stimulus.button.collect, stimulus.printer_fault, printer.wallet, decoder.lp4D

  note "Start empty with the printer disabled. With only restricted credit on the machine the cashout button does nothing."
  note "Five cashable and five restricted: cashout pays the five cashable dollars by hopper or hand pay and leaves the restricted five. Five restricted and five non-restricted: the non-restricted five are paid as cash and the restricted stay. Five cashable and five non-restricted: ten dollars are paid. Five of each: ten dollars are paid and the restricted five stay."
  note "With the printer enabled for restricted tickets and five of each loaded, cashout prints a cashable ticket for ten dollars with $3D, then a play-only ticket for five with $3D again."
}
