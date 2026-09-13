# Two hosts on QSP_DemoOpenGL, and what each of them may ask it.
#
# The software machine can be shown to divide itself between hosts the way the library says, but
# both ends of that are our own code. This suite is the same questions put to a real machine, and
# it deliberately asks none that need money: the machine under test has no licence and returns
# every note before its money handler sees one (BLOCKERS.md Trap D), so anything needing credits
# on it fails for a reason that has nothing to do with channels. That is why GSA 10.1.4 itself is
# not here - it prints a ticket, and printing one needs credits.
#
# What has to be true of the machine before any of this runs, and none of it is reachable from a
# host:
#
#   * two network serial ports in its own configuration. This build declares both.
#   * a poll address on the second channel, and validation pointed at it. Set through the machine's
#     own audit menu, or written into a saved memory with
#     'sasctl nvram channels "1@01 2@02 validation=2"' and restored.
#   * the two ports reaching two different hosts. Over the TCP transport that means a per-port
#     endpoint each: SASWB_QXCOM_ENDPOINT__DEV_TTYS5 for the first and
#     SASWB_QXCOM_ENDPOINT__DEV_TTYS2 for the second.
#
# The endpoints are written into the file rather than overridden on the command line, because one
# override cannot say two different things and both channels pointed at one socket would be one
# host answering itself.

test "VC-01 a machine configured for two channels answers on both" {
  tag @channel2 @needs-configuration

  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect tcp 127.0.0.1:9781 address 01 channel 1
  host poll every 200ms
  host expect link up within 15s

  host connect tcp 127.0.0.1:9782 address 02 channel 2
  host expect link up within 15s

  # The same machine, read from both hosts. Everything after the echoed address must agree, because
  # there is one machine here.
  host send lp 1F
  host channel 1
  host send lp 1F
}

test "VC-02 a command in a category this channel was not given is met with silence" {
  tag @channel2 @needs-configuration

  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect tcp 127.0.0.1:9781 address 01 channel 1
  host poll every 200ms
  host expect link up within 15s

  # Two validation commands, gated in the library on a category this channel does not own. The
  # library's gate returns without sending anything, so silence and not a refusal.
  #
  # Framed at two bytes, both of them, which is what makes their silence the gate: a command the
  # receiver frames longer is dropped as incomplete before any handler sees it, and its silence
  # would say nothing at all. The runner now refuses to assert one of those.
  host expect no reply to lp 57
  host expect no reply to lp 70
}

test "VC-03 the cashout ticket information poll is answered on a channel that owns no validation" {
  tag @channel2 @needs-configuration

  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  host connect tcp 127.0.0.1:9781 address 01 channel 1
  host poll every 200ms
  host expect link up within 15s

  # The finding, put to a real machine. This poll carries no channel gate in the library at all, so
  # a host that owns no validation is answered - and on a machine that has printed a ticket it is
  # answered with that ticket's real number and amount. GSA 10.1.4 step 5 requires the opposite.
  #
  # Nothing here has printed a ticket, because printing one needs money this machine will not take,
  # so what is asserted is only that the poll is answered rather than gated. That is the half of
  # 10.1.4 step 5 a machine with no licence can still establish.
  host send lp 3D
}

test "VC-04 what a host cannot read for itself" {
  tag @channel2 @needs-configuration

  # Nothing on a wire settles either of these, so the case is a pair of notes and skips with the
  # capability naming what it would take: a person at the machine's own menu.
  requires machine.audit_menu

  note "No long poll reports which channel owns which category, so a host cannot read the arrangement off the wire and the map on a run's record is what the case declared. Check it against the machine: audit menu, SAS configuration, Ports."
  note "A machine with one serial port cannot open a second channel whatever its memory says. Its audit page lists a port per channel with a poll address, and only the ports the cabinet has."
}
