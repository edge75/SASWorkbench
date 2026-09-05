# A test that drives both ends: the note goes in at the machine, and the assertions are
# made over the wire. Either half alone can be right while the machine is wrong.
#
# The money meter is in credits, not cents: a twenty-dollar note moves it by twenty at a
# one-dollar credit. The note itself is still named in cents, because that is what is put
# into the acceptor.
#
# Against a real machine two things have to be true first, and neither is in this repository.
# Without them the note is dropped inside the machine and the first assertion fails with
# "nothing in 5s" — a true statement about that machine, and a useless one to debug from.
#
#   sasctl nvram restore                       a note acceptor configured through the machine's
#                                              own menu, saved once and put back
#   sasctl qpm audit open, then audit close    once per boot, to clear the condition that
#                                              inhibits the acceptor. Measured: without this the
#                                              note is refused; with it, accepted.

test "a twenty raises its exception and moves its meters" {
  tag @needs-stimulus
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # A machine fresh from a cold start believes several doors are open, and a note is refused
  # while one is. Settling raises a pair of exceptions per door, so they are left out of what
  # follows rather than being allowed to answer an assertion below.
  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host expect meter total_bill_in += 20 within 2s
  host expect meter bills_20 += 1 within 2s

  egm bill insert 500
  host expect exception bill_accepted_5 within 5s
  host expect meter total_bill_in += 5 within 2s
  host expect meter bills_20 unchanged within 2s
}
