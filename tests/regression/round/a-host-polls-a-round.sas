# One host, several machines, one line.
#
# The second axis. A channel splits one machine between two hosts; a round splits one host between
# several machines, and the two are independent. What makes a round mean anything is that every
# machine on the line hears every poll and answers only its own — so these cases ask several
# machines in turn, and assert that a command with no machine named is refused rather than sent to
# whichever one the cycle had reached.
#
# A hundred machines are written as `addresses 01-64`, not as a hundred lines. That is the whole
# point of the notation: a floor a person can read.
#
# "It answered as itself" needs no assertion of its own here: the host checks the address on every
# reply, so a reply from the wrong machine comes back as a wrong-address failure and the send step
# fails. A passing send to $04 is a reply that carried $04.

test "each machine on the line answers its own polls" {
  host connect loopback addresses 01-04
  host expect link up within 5s

  host send lp 1F to 01
  host send lp 1F to 04
  host send lp 1F to 02
}

test "the case can move to a machine instead of naming one on every step" {
  host connect loopback addresses 01-04
  host expect link up within 5s

  host machine 03
  host send lp 1F

  host machine 02
  host send lp 1F
}

# The two refusals a round has to make — a send that names no machine, and a machine outside the
# round — are asserted in the unit suite rather than here. Expressed as cases they would be skips,
# and everything under tests/regression/ runs green with no skips at all: a suite that carries two
# deliberate skips cannot be read at a glance for the accidental one.
