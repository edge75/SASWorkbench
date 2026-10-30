# Two hosts on one machine, and what each of them may ask it.
#
# The whole safety argument for two hosts is that a category belongs to exactly one channel, and
# it is structural rather than a rule the hosts have to police: the machine stores each of seven
# categories as a two-bit field holding one channel number, so a category cannot be on two. What
# the machine then does with that is two things, both read out of its library rather than decided
# here — it gates thirty-five long polls at the top of their handlers, and it filters which
# exceptions reach which channel's buffer.
#
# These cases assert the gate and the filter on a wire. They are the reason 10.1.4 can be believed:
# the conformance case fails on the cashout ticket poll, and a reader is entitled to ask whether
# the mechanism works at all or the case simply cannot see it.

test "a command in a category this channel was not given is met with silence" {
  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect loopback address 01 channel 1
  host poll every 200ms
  host expect link up within 5s

  # Three validation commands, all gated in the library and none of them on this channel. Silence
  # and not a refusal: the handler returns without sending anything, and a refusal would tell the
  # host the command had been understood and declined.
  host expect no reply to lp 70
  host expect no reply to lp 57
}

test "the same commands are answered on the channel that does own the category" {
  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect loopback address 02 channel 2
  host poll every 200ms
  host expect link up within 5s

  # $70 answers on the validating channel, which is what makes the silence above the gate and not
  # a machine that cannot answer this command at all.
  host send lp 70
  host expect field ticket_status of lp 70 == FF
}

test "one machine answers two hosts at two addresses at once" {
  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect loopback address 01 channel 1
  host poll every 200ms
  host expect link up within 5s

  host connect loopback address 02 channel 2
  host expect link up within 5s

  # Both hosts read the same machine's identity, which is what says there is one machine here and
  # not two. The reply echoes the address of the channel it came out on, so the two replies differ
  # in that byte and agree in every other.
  host send lp 1F
  host expect field game_id of lp 1F == QX

  host channel 1
  host send lp 1F
  host expect field game_id of lp 1F == QX
}

test "an exception in a category this channel was not given never reaches it" {
  tag @needs-stimulus
  requires stimulus.ticket
  requires decoder.lp4C

  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect loopback address 02 channel 2
  host poll every 200ms
  host expect link up within 5s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced

  host connect loopback address 01 channel 1
  host expect link up within 5s

  host channel 2
  host forget exceptions
  host channel 1
  host forget exceptions

  # A ticket going in raises ticket_inserted, which the library's own predicate puts in the
  # validation category. So only the validating host is told, and the other host is not - which is
  # the filter, and it is the half of the arrangement that the cashout ticket exception shows the
  # other side of.
  egm ticket insert amount=500 validation=1234567890123456

  host channel 2
  host expect exception ticket_inserted within 5s

  host channel 1
  host expect no exception ticket_inserted within 2s
}
