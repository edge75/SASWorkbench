# The sequenced mode, proven end to end through the script runner rather than only at the engine.
#
# What is on the line between two messages is the protocol: a machine learns the host heard its
# last reply from the next poll it sees, and this host's own synchronisation poll is a global
# broadcast, which section 3.19 makes an acknowledgement in its own right. So a case that is about
# acknowledgement cannot run while the round is going.

test "a case owns every message on the line while it is in a sequence" {
  tag @wire-only
  requires host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host poll sequence

  # Nothing goes out but these. The broadcast is the one section 3.19 turns on, and no machine
  # may answer it.
  host send sync
  host send gp draws nothing
  host send lp 1F
  host send gp draws nothing
}

# The same mode, with something queued: each acknowledged general poll draws one exception, in the
# order the machine queued them, and the poll after them says so rather than going quiet.
test "each general poll in a sequence draws the next exception" {
  tag @needs-stimulus
  requires host.poll_sequence, stimulus.door.slot, stimulus.door.drop

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  host forget exceptions within 2s

  host poll sequence

  # A sync poll of our own first: the round's last cycle has finished before the doors move, and a
  # machine stops answering a third general poll in a row with nothing between them (section 3.3).
  host send sync

  egm door slot open
  egm door drop open

  # A general poll straight after another is an implied NACK, so a poll elsewhere acknowledges each.
  host send gp draws slot_door_opened
  host send gp draws silence to 7F
  host send gp draws drop_door_opened
  host send gp draws silence to 7F
  host send gp draws nothing
}

# And the line is given back.
test "a sequence hands the line back to the round" {
  tag @wire-only
  requires host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host poll sequence
  host send gp draws nothing

  host poll every 200ms
  host expect link up within 5s
}
