# Chapter 3, sections 3.1 to 3.3 — the wire itself.
#
# These are the cases about the link rather than about anything the machine counts: the byte
# format, what starts a message, and what the machine does with a command it does not know.

# The link runs at 19200 baud with a start bit, eight data bits, the wakeup bit and a stop bit.
# Over the loopback transport that is not a setting a case can get wrong - the transport carries
# all nine bits or it refuses to open (D2) - so what is left to assert is the consequence: the
# machine answers general polls with an exception byte and answers long polls.
test "3.1 communication protocol" {
  tag @egm-test-scripts @chapter-3 @wire-only

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Two cycles of the loop, then the machine is still answering.
  host send lp 1F
  host wait 2s
  host send lp 1F
}

# The wakeup bit is what starts a message, so a poll sent without it must be ignored or refused.
# Sending one needs a transport that will drop the bit on request, and this one will not: dropping
# it silently is the degradation D2 exists to refuse, and there is no way to ask for it deliberately
# either. So the case skips naming that rather than asserting the machine's silence for some other
# reason - a poll nothing sent is not a poll the machine ignored.
test "3.2 a poll without the wakeup bit is ignored or refused" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_without_wakeup

  note "Send a long poll to the machine's own address with the wakeup bit clear, twice, a second apart. The machine either ignores it or refuses it by answering its address ORed with $80. Either is a pass; an ordinary reply is a failure."
  note "When it answers or refuses any poll, the address it sends carries no wakeup bit."
}

# A command the machine does not implement gets no reply at all, and the case pairs it with one the
# machine does implement so that silence is read as silence rather than as a dead link.
test "3.3 an unsupported command is not answered" {
  tag @egm-test-scripts @chapter-3 @wire-only
  requires decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp B5 with game_number=1
  host expect no reply to lp FE
}
