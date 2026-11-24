# Chapter 19, sections 19.15 and 19.16 - the two authentication exceptions with real-time event
# reporting on.
#
# With reporting on, $8F and $8E each arrive as a message - the address, the marker $FF, the code
# and a check sequence, nothing else - and the machine may put one in the answer to a long poll as
# well as to a general poll. This host always polls with general polls in the loop, so which poll
# carries the event is a person's to read in the frames; the format is what runs.
#
# A machine without real-time reporting refuses long poll $0E with its address ORed with $80, and
# both cases then do not apply.

test "19.15 authentication complete in real-time mode" {
  tag @egm-test-scripts @chapter-19 @wire-only
  machine component authentication on
  requires decoder.lp6E
  requires stimulus.real_time_events, decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 0E data 01

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect exception authentication_complete within 60s

  # Unread, it comes again, still in the event format.
  host expect exception authentication_complete within 20s

  # Read the result so the exception stops, then turn reporting off.
  host send lp 6E data 0103
  host forget exceptions within 2s
  host send lp 0E data 00
  host forget exceptions within 2s

  note "The document's set-up names a component that cannot be authenticated and then expects $8F, which only a completed authentication raises; this case authenticates component 1."
  note "The document's loop has no general polls, so for the minute after completion $8F must come back in the answers to long polls. This host cannot poll without general polls; check in the frames that each event is address, $FF, $8F and the check sequence, and nothing more."
}

test "19.16 component list changed in real-time mode" {
  tag @egm-test-scripts @chapter-19 @needs-a-person
  machine component authentication on
  requires stimulus.component_change
  requires stimulus.real_time_events, decoder.lp0E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 0E data 01
  egm component change
  host expect exception component_list_changed within 10s

  # Turn reporting off again.
  host send lp 0E data 00
  host forget exceptions within 2s

  note "Check in the frames that the event is address, $FF, $8E and the check sequence, and nothing more; the document lets it arrive on a long poll as readily as on a general poll. Applies only to a machine that passes 19.4 and supports real-time reporting (11.1)."
}
