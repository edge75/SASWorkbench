# Chapter 16, sections 16.80 and 16.81 - which pool restricted credit belongs to, and when it dies.
#
# Restricted credit is promotional money tied to a pool and an expiry date. The machine can only
# hold one pool at a time, so a transfer from a different pool must be refused while any of the
# first remains; and the expiry the host sends must come back in the transfer record, or be
# replaced by the machine's own default when the host sent none, or refuse a ticket that would be
# born already expired.
#
# Both need the funds-transfer command decoded and a machine that performs transfers and holds
# restricted credit.

test "16.80 restricted credit from two pools" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits

  note "On an empty machine, two full transfers of five restricted dollars in pool 1 both succeed ($00) and the record shows 500 and pool 1 each time."
  note "Five restricted dollars in pool 2, asked with transfer flag 0 set, are refused because pool 1 credit is already there ($8F); the record shows nothing moved and pool 2."
  note "Three dollars back to the host with no pool named succeeds in full; three dollars to the host naming pool 2 is a partial success ($01). Both records show pool 1, the pool the credit really came from."
  note "Empty the machine, put five pool-1 dollars on again, and take four back naming pool 1: it succeeds in full ($00)."
}

test "16.81 the expiry date of restricted credit" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, decoder.lp7E, printer.wallet

  note "With the machine's clock on today's date and restricted tickets enabled, start empty. Send five restricted dollars in and then out to the host with an expiry before today: both succeed, $69 follows each, and the record carries the date that was sent."
  note "The same with an expiry of today, after today, zero and one."
  note "A transfer of five restricted dollars to a ticket with today's expiry prints the ticket, succeeds, and records that date. With an expiry before today it is refused as already expired ($8D) and moves nothing. With zero or one it prints and records the machine's default expiry."
}
