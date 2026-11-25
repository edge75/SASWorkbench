# Chapter 3, sections 3.11 to 3.14 - the clock on the wire.
#
# Four cases about when bytes appear rather than what they say. This host times messages, and can
# assert how often an exception comes again, but a transport hands it a reply as one batch of
# symbols with no time of its own on each - so a gap between two bytes of one reply cannot be
# measured here, and a case that cannot fail must not be allowed to pass.

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
  # Every byte timed on its own, which no transport this build opens provides.
  requires host.byte_timing

  note "With the link up and the loop cycling, read the gaps between the bytes of the machine's own replies. None exceeds five milliseconds."
}

# The whole supported range of poll rates, and what happens past its top: five seconds after the
# last poll the machine declares the loop broken and chirps until the next poll arrives.
test "3.13 the machine answers at any poll rate from 200 ms to 5 seconds" {
  tag @egm-test-scripts @chapter-3 @wire-only
  requires decoder.lp1F
  requires host.chirp, host.poll_sequence

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

  # Past the top: one poll, nothing for more than five seconds, the chirp, and the next poll ends it.
  host poll sequence
  host send gp
  host expect no chirp for 2s
  host expect chirp within 5s
  host send sync
  host expect no chirp for 2s
  host poll every 200ms

  # Real-time event reporting raises the ceiling further; 11.7 takes the same sweep down to 40 ms.
}

# Whoever transmits last wins the line: a machine that is part way through a reply when a byte
# with the wakeup bit arrives must stop talking. The tool this suite was written for could not
# produce a collision either, and nor can this host: a transport hands a reply over whole, so there
# is no moment part way through one at which to send.
test "3.14 a collision stops the machine mid-reply" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.collision

  note "While the machine is replying, transmit a byte with the wakeup bit set. The machine abandons its reply at that byte and sends nothing more of it."
}
