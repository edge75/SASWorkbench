# Chapter 16, sections 16.12 to 16.17 - the three registration exceptions, plain and as events.
#
# A machine that lets an attendant start a registration says so with $6C. The host can then ask
# for the attendant's confirmation ($73 with code $40), and the machine raises $6D once the
# attendant has given it. $6E, cancelled, is covered by the cases of 16.8 to 16.11. With real-time
# reporting on, each of the three is a full event message, and may answer a long poll as well as a
# general one.
#
# All of it starts with somebody at the machine's menus, so the first two cases need a person, and
# the three event variants need the same plus a line the script drives, so that a long poll can be
# the first thing the machine hears after the event (host.poll_sequence). The $73 requests are the
# ones written out in
# 16.3-16.11-registration.sas; read with 01 FF, cancel with 01 80, ask for confirmation with 01 40.

test "16.12 the attendant asks to register" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, machine.aft, machine.aft_operator_registration

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 73 data 0180
  host expect field registration_status == $80
  host forget exceptions within 2s

  note "Now ask for registration from the machine's transfer menu. The machine raises $6C on the next general poll; the step below waits for it."
  host expect exception aft_request_to_register within 120s

  host send lp 73 data 01FF
  host expect field registration_status == $80
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01
  host expect field asset_number == 12345
}

test "16.13 the attendant acknowledges the registration" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, machine.aft, machine.aft_operator_registration

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 73 data 0180
  host forget exceptions within 2s

  note "Ask for registration from the machine's transfer menu; the machine raises $6C."
  host expect exception aft_request_to_register within 120s
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host expect field registration_status == $00

  # The host asks for the attendant's confirmation: pending.
  host send lp 73 data 0140
  host expect field registration_status == $40

  note "Confirm at the machine. It raises $6D, and from then on reads back as ready, with the key and point of sale of the initialise step."
  host expect exception aft_registration_acknowledged within 120s
  host send lp 73 data 01FF
  host expect field registration_status == $00

  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01
}

test "16.14 the registration-cancelled exception" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp73, machine.aft

  note "Nothing of its own: $6E is checked by the cases of 16.8, 16.9, 16.10 and 16.11."
}

test "16.15 the request to register as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, decoder.lp0E, machine.aft, machine.aft_operator_registration
  requires stimulus.real_time_events, host.poll_sequence

  note "With real-time reporting on (long poll 0E data 01), repeat 16.12. $6C arrives as an event message - the address, $FF, $6C and a check sequence - and may be the answer to a long poll as well as to a general poll."
}

test "16.16 the acknowledgement as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, decoder.lp0E, machine.aft, machine.aft_operator_registration
  requires stimulus.real_time_events, host.poll_sequence

  note "With real-time reporting on, repeat 16.13. $6D arrives as an event message, on a long poll as well as on a general poll."
}

test "16.17 the cancellation as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp73, decoder.lp0E, machine.aft, machine.memory_fault, machine.aft_operator_registration
  requires stimulus.real_time_events, host.poll_sequence

  note "With real-time reporting on, repeat 16.8 to 16.11. Each cancellation arrives as an event message, on a long poll as well as on a general poll."
  note "The document names the event of this case as $6D; the cancellation it repeats raises $6E, which is the code to look for."
}
