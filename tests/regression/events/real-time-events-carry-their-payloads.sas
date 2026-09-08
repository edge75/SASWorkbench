# Real-time event reporting: the machine answers a general poll with a whole message rather
# than one byte, and the exceptions that carry data carry it where the library's own builder
# puts it. Nothing here asserts on a payload the library does not fill in.

test "turning reporting on changes the shape of an answer, not what happened" {
  tag @wire-only @real-time-events
  requires stimulus.real_time_events
  requires decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Acknowledged like any other setter. The machine answers general polls in the event format
  # from here on.
  host send lp 0E data 01
}

test "a note reports what it was and how many of them there are" {
  tag @needs-stimulus @real-time-events
  requires stimulus.real_time_events
  requires stimulus.bill
  requires decoder.event4F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 0E data 01
  host forget exceptions within 1s

  egm bill insert 2000

  # The country code and the denomination are what the machine took; the meter is that
  # denomination's own count, this note included. A host that never turned reporting on sees
  # exception $4A and none of these three.
  host expect field country_code of event bill_accepted == 37 within 5s
  host expect field denomination_code of event bill_accepted == 4 within 5s
  host expect field bill_meter of event bill_accepted >= 1 within 5s
}

test "a game reports the wager in game credits and the meter beside it in accounting units" {
  tag @needs-stimulus @real-time-events
  requires stimulus.real_time_events
  requires stimulus.bill
  requires stimulus.play
  requires decoder.event7E
  requires decoder.event7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 0E data 01

  egm bill insert 2000
  host forget exceptions within 1s

  egm play games=1 bet=2 win=0

  # Two units in one event, which is the whole reason this one is worth reading: two credits
  # staked is two whatever the accounting denomination is, and the coin in meter beside it is
  # in accounting units.
  host expect field credits_wagered of event game_started == 2 within 5s
  host expect field win_amount of event game_ended == 0 within 5s
}

test "an exception that carries nothing is still a whole message" {
  tag @needs-stimulus @real-time-events
  requires stimulus.real_time_events
  requires stimulus.door.slot

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 0E data 01
  host forget exceptions within 1s

  egm door slot open

  # Most exceptions carry no data at all, and those arrive as the address, the marker, the code
  # and a check sequence. The exception is reported the same way it always was.
  host expect exception slot_door_opened within 5s
}
