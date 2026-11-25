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

  # Off: the same two notes come back as the exception for their own denomination. The queue is
  # drained first because a long poll sent with one still waiting is answered with the event.
  host forget exceptions within 2s
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
  requires machine.power_cycle, host.poll_sequence, stimulus.real_time_events, stimulus.bill
  requires stimulus.credit_limit, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  egm set credit limit 100000
  host send lp 0E data 00
  host forget exceptions within 2s

  # Reporting off: each note comes back as its own denomination's exception, one byte each.
  host poll sequence
  host send sync
  egm bill insert 100
  egm bill insert 500
  egm bill insert 2000
  host send sync
  host send gp draws bare usd_1_bill_accepted
  host send lp 1F
  host send gp draws bare usd_5_bill_accepted
  host send lp 1F
  host send gp draws bare usd_20_bill_accepted
  host send lp 1F
  host send gp draws bare nothing

  # Reporting on, the same notes taken while nobody polls: each comes back as an event.
  host send lp 0E data 01
  egm bill insert 100
  egm bill insert 500
  egm bill insert 2000
  host send sync
  host send gp draws event bill_accepted
  host send sync
  host send gp draws event bill_accepted
  host send sync
  host send gp draws event bill_accepted
  host send sync
  host send gp draws event nothing

  # Taken again and the power cycled before polling: reporting is off, so they leave as their own codes.
  egm bill insert 100
  egm bill insert 500
  egm bill insert 2000
  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync
  host send gp draws bare usd_1_bill_accepted
  host send lp 1F
  host send gp draws bare usd_5_bill_accepted
  host send lp 1F
  host send gp draws bare usd_20_bill_accepted
  host send lp 1F
  host poll every 200ms

  note "The document also asks for a note with no code of its own, which comes back as $4F in both modes. This machine's acceptor takes 1, 2, 5, 10, 20, 50 and 100 only, each of which has a code, so that note is checked on a machine whose acceptor takes one."
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
