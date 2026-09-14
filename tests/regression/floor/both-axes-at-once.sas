# Both axes at once: several machines, each divided between two hosts.
#
# A round splits one host between machines; a channel splits one machine between hosts. Until now
# a case could have either and was refused if it asked for both. They meet as a line per channel,
# and that is read out of the machine rather than chosen: the platform opens channel one on its
# first network serial port and each further channel on the next, so two hosts on one line is not
# an arrangement any machine has — and on a real pair it would be two hosts transmitting at once
# with nothing to arbitrate between them.
#
# The consequence a test can see is that the same address appears on both lines and means the same
# machine's two ports. An address is unique on a line, not on a floor. (It is also what makes a
# hundred machines on two channels fit at all: two hundred ports, and SAS has 127 addresses.)
#
# The small floor is here; the hundred-machine case D29 asks for is next door and carries a tag,
# because it costs a round of a hundred to run.

test "the same machine answers two hosts on two lines, and the floor tells them apart" {
  machine channel 1 addresses 01-04 owns aft legacy_bonus
  machine channel 2 addresses 01-04 owns validation information

  host connect loopback addresses 01-04 channel 1
  host expect link up within 10s

  host connect loopback addresses 01-04 channel 2
  host expect link up within 10s

  # Machine $03 on the funds host and machine $03 on the validation host are one machine. Both
  # read its identity, and the reply echoes the address of the port it came out on — so the two
  # agree in every byte that describes the machine.
  host channel 1
  host machine 03
  host send lp 1F
  host expect field game_id of lp 1F == QX

  host channel 2
  host machine 03
  host send lp 1F
  host expect field game_id of lp 1F == QX
}

test "a category answers on the host that owns it and nowhere else, on every machine of the floor" {
  machine channel 1 addresses 01-04 owns aft legacy_bonus
  machine channel 2 addresses 01-04 owns validation information

  host connect loopback addresses 01-04 channel 1
  host expect link up within 10s

  host connect loopback addresses 01-04 channel 2
  host expect link up within 10s

  # The gate is the machine's, so it holds for every machine on the line and not only the first.
  # A validation command on the funds host is met with silence on $02 and on $04 alike.
  host channel 1
  host machine 02
  host expect no reply to lp 70

  host machine 04
  host expect no reply to lp 70

  # The same command, the same machines, on the host that was given validation.
  host channel 2
  host machine 02
  host send lp 70
  host expect field ticket_status of lp 70 == FF

  host machine 04
  host send lp 70
  host expect field ticket_status of lp 70 == FF
}

test "a floor can be driven, and only the machine the case named moves" {
  tag @needs-stimulus
  requires stimulus.bill

  machine channel 1 addresses 01-04 owns aft legacy_bonus
  machine channel 2 addresses 01-04 owns validation information

  host connect loopback addresses 01-04 channel 1
  host expect link up within 10s

  host channel 1
  host machine 02
  egm settle
  host forget exceptions

  # The note goes into machine $02 because the case said so, not because $02 happens to be where
  # the round had got to. The meters that follow are read from $02 by a long poll addressed to it.
  egm bill insert 2000
  host expect meter total_bill_in += 20 within 20s

  # $04 is on the same line and was never touched, so its own bill meter did not move. This is the
  # assertion that a floor driven by name is actually driven by name: a stimulus applied to the
  # head of the round would pass the step above and fail this one.
  host machine 04
  host expect meter total_bill_in += 0 within 20s
}
