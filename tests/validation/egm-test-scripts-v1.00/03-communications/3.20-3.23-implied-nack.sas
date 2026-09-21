# Chapter 3, sections 3.20 to 3.23 - what counts as a refusal.
#
# The other half of section 3.15's rule. A poll repeated once says the host did not get the answer,
# so the machine sends the same thing again; repeated a third time it is final, and the machine may
# answer once more or say nothing, but either way it moves on. Two things end the retry besides a
# third try: a power cycle, and five seconds of silence.
#
# Like the acknowledgement cases, all four need an exact host-driven sequence with nothing else on
# the line, which this host cannot produce.

# Two the same is a refusal; three is final.
test "3.20 the same poll twice is a refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.service_button
  requires decoder.lp1B

  note "Create four exceptions in order - slot door opened, slot door closed, change lamp on, change lamp off. Establish communication, then send a general poll to the machine's address twice, the identification poll, the general poll, the identification poll."
  note "The first two general polls both draw slot door opened - the repeat is a refusal, so the machine sends it again - and the third draws slot door closed. Repeat the sequence for change lamp on and change lamp off."
  note "Then a jackpot hand pay, with the hand pay information poll standing in for the general one: the first two readings carry the same amount, the third comes back all zeros."
  note "The rule holds whatever the gap: the same three general polls forty milliseconds apart behave exactly as they do two hundred apart."
}

# Three the same is final. The third answer is optional - the machine may repeat it or stay quiet -
# and both are a pass, which is the only place in this chapter where two behaviours are allowed.
test "3.21 the same poll three times is final" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.service_button
  requires decoder.lp7E

  note "Create the four door and change lamp exceptions. Establish communication, then a general poll to the machine's address and the identification poll: the general poll draws slot door opened."
  note "Send the general poll three times then the identification poll. The first two draw slot door closed; the third either draws it again or is not answered at all. Then alternate general and identification polls: the remaining exceptions arrive in order and the general polls end with no activity."
  note "The same with a long poll. Send date and time three times then the identification poll: the first two carry the same time, the third either repeats it or is unanswered. Send date and time, the identification poll, date and time again - the first carries that same time and the second a new one, because the identification poll in between acknowledged it."
}

# A power cycle does not clear the retry. The machine comes back still owing the same exception.
test "3.22 a power cycle does not clear a final refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires host.poll_sequence, machine.power_cycle, stimulus.service_button

  note "Create slot door opened, slot door closed and change lamp on. Establish communication, then a general poll to the machine's address and the identification poll: the general poll draws slot door opened. Send the general poll once more: it draws slot door closed."
  note "Power cycle the machine and let it boot. Establish communication and send the general poll twice: both draw slot door closed. Power cycle again and send it three times: the first two draw slot door closed and the third either repeats it or is unanswered. Power cycle again and repeat with the identification poll on the end."
  note "Then general poll, identification poll, general poll: the first draws slot door closed, and the second draws change lamp on - the identification poll in between was the acknowledgement the retries never had."
}

# Five seconds of silence is final too, and it is not the same as a power cycle: the machine chirps
# through it.
test "3.23 five seconds of silence is a final refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, host.chirp, stimulus.service_button

  note "Create the four door and change lamp exceptions. Establish communication, then general poll, identification poll, general poll: the two general polls draw slot door opened and slot door closed. Send the general poll twice more: the first draws slot door closed again and the second either repeats it or is unanswered."
  note "Stop polling for more than five seconds - the machine chirps every 200 ms. Establish communication again and send the general poll twice then the identification poll: both general polls draw slot door closed, so the silence did not acknowledge it."
  note "Send the general poll twice: both draw change lamp on. Go silent again for more than five seconds. Then send a long poll addressed to another machine, a general poll, that foreign poll, a general poll: the foreign polls are not answered, and the two general polls draw change lamp on and change lamp off."
}
