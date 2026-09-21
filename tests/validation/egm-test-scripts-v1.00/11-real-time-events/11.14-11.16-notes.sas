# Chapter 11, sections 11.14 to 11.16 - a note taken, in both formats.
#
# A note is the clearest case of what the mode changes. With reporting off the machine sends the
# exception for that denomination and nothing else; with it on it sends one $4F carrying the
# country the note came from, which denomination it was, and how many of that denomination the
# machine has taken. The event is queued the same way either way and the code is chosen as it
# leaves, which is why turning the mode on cannot lose a note.

test "11.14 notes follow the mode" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill
  requires decoder.lp0E, decoder.event4F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # On: one $4F per note, carrying what it was.
  host send lp 0E data 01
  host forget exceptions within 2s

  egm bill insert 500
  host expect field denomination_code of event bill_accepted == 2 within 8s

  host forget exceptions within 2s
  egm bill insert 2000
  host expect field denomination_code of event bill_accepted == 4 within 8s

  # Off: the same two notes come back as the exception for their own denomination.
  host send lp 0E data 00
  host forget exceptions within 2s

  egm bill insert 500
  host expect exception usd_5_bill_accepted within 8s

  egm bill insert 2000
  host expect exception usd_20_bill_accepted within 8s

  note "The document asks for every denomination the acceptor takes. This machine's takes 1, 2, 5, 10, 20, 50 and 100; a machine that takes 200 or 500 owes $50 and $4E with reporting off, and a machine that takes a denomination with no code of its own owes $4F even with reporting off."
  note "$4F is the preferred answer with reporting on, and a machine that sends the per-denomination code instead is not failing."
}

# The same thing across a power cycle, which is where the mode is lost and the queue is not.
test "11.15 a queue of notes changes format with the mode" {
  tag @egm-test-scripts @chapter-11 @needs-a-person @destructive
  requires machine.power_cycle

  note "Play off or cash out all credits and set a high credit limit. With reporting off, insert several notes between $1 and $500 including one with no code of its own: each comes back as its own denomination's exception on a general poll, and the odd one as $4F."
  note "Turn reporting on and insert the same set: each comes back as an event, $4F preferred. Stop polling, insert them again, start polling: still events, so the queue kept them."
  note "Stop polling, insert them again, and power cycle before polling. Reporting is off again, so the queued notes come back as their own denomination codes with $4F for the odd one - the format followed the mode at the moment they left, not the mode when they were taken."
}

# What the $4F event carries, field by field.
test "11.16 what a note event carries" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires stimulus.real_time_events, stimulus.bill
  requires decoder.lp0E, decoder.event4F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01
  host forget exceptions within 2s

  egm bill insert 2000

  # Country from Table C5 - 37 is the United States - denomination from Table C6, and the count
  # of notes of that denomination this machine has taken, this one included.
  host expect field country_code of event bill_accepted == 37 within 8s
  host expect field denomination_code of event bill_accepted == 4 within 8s
  host expect field bill_meter of event bill_accepted == 1 within 8s

  # The same note again: the count for that denomination goes up by one and nothing else moves.
  host forget exceptions within 2s
  egm bill insert 2000
  host expect field country_code of event bill_accepted == 37 within 8s
  host expect field denomination_code of event bill_accepted == 4 within 8s
  host expect field bill_meter of event bill_accepted == 2 within 8s

  note "The count in the event must equal that denomination's own note meter on the machine's own display. Nothing here can read a machine's screen, so somebody has to open the meter display and compare the two (D40)."
  note "The document asks for every denomination the acceptor takes, each with its own count."
}
