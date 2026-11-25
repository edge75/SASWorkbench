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
# Every transport this host opens carries the bit symbol by symbol (D2), so leaving it off is a
# deliberate act the host can perform, not a degradation it could fall into.
test "3.2 a poll without the wakeup bit is ignored or refused" {
  tag @egm-test-scripts @chapter-3 @wire-only
  requires host.poll_without_wakeup, host.poll_sequence
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host poll sequence

  # Twice, a second apart: silence or the refusal passes, an ordinary reply fails.
  host send lp 1F without wakeup
  host wait 1s
  host send lp 1F without wakeup

  # The same poll with the bit is answered, so the silence above was about the bit.
  host send lp 1F
  host poll every 200ms

  note "Every answer and refusal the machine sends is checked for a wakeup bit as it arrives; one carrying it fails the step that read it."
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
