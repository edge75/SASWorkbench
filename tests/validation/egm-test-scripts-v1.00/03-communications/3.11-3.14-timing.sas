# Chapter 3, sections 3.11 to 3.14 - the clock on the wire.
#
# Four cases about when bytes appear rather than what they say. This host records when every
# message went out and came back, and the wire log carries it, but nothing in the test language
# asserts on a duration - so a case about a twenty-millisecond turnaround has no way to fail here,
# and one that cannot fail must not be allowed to pass.

# Twenty milliseconds, from the last byte of the poll, whatever the machine happens to be doing.
# The states in the middle are the point: a machine that answers promptly when idle and takes its
# time during a hand pay has not passed.
test "3.11 the machine answers within twenty milliseconds" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.timing
  requires stimulus.handpay, stimulus.hopper, machine.audit_menu

  note "Timestamp the trace relative to the end of the last poll. With the link up, play through a bonus or double-up, force a jackpot hand pay and reset it, open and close the door, enter and leave the meter display, insert a note and some coins, and cash out to the hopper."
  note "Every general and long poll is answered within twenty milliseconds of its last byte, on the first or second poll, in every one of those states."
}

# Five milliseconds between the bytes of one reply. A longer gap is a different message as far as
# SAS is concerned, because silence is the only thing that ends one.
test "3.12 the gap between reply bytes is five milliseconds or less" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.timing

  note "With the link up and the loop cycling, read the gaps between the bytes of the machine's own replies. None exceeds five milliseconds."
}

# The whole supported range of poll rates. The sweep is assertable and runs; what is not is the
# top of the range, which the document establishes by going over it - past five seconds the machine
# declares the loop broken and chirps, and nothing here can watch for a chirp (D40).
test "3.13 the machine answers at any poll rate from 200 ms to 5 seconds" {
  tag @egm-test-scripts @chapter-3 @wire-only
  requires decoder.lp1F

  note "Poll at 5600 ms, timing the next poll from the last one going out rather than from the reply. The machine chirps every 200 ms starting five seconds after each poll, and stops chirping when the next poll arrives. That half needs somebody watching the line."

  host connect loopback address 01

  # Down the range the document names, a long poll answered at each rate. The link staying up is
  # the general polls; the identification poll is the long ones.
  host poll every 4800ms
  host expect link up within 20s
  host send lp 1F

  host poll every 1000ms
  host expect link up within 10s
  host send lp 1F

  host poll every 200ms
  host expect link up within 5s
  host send lp 1F

  # Real-time event reporting raises the ceiling further; 11.7 takes the same sweep down to 40 ms.
}

# Whoever transmits last wins the line: a machine that is part way through a reply when a byte
# with the wakeup bit arrives must stop talking. The tool this suite was written for could not
# produce a collision either, and says so.
test "3.14 a collision stops the machine mid-reply" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.collision

  note "While the machine is replying, transmit a byte with the wakeup bit set. The machine abandons its reply at that byte and sends nothing more of it."
}
