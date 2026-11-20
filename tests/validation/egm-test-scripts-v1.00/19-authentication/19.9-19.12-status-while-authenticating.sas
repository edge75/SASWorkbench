# Chapter 19, sections 19.9 to 19.12 - what the machine says about an authentication while it runs,
# after it ends, and when a second one is started over the first.
#
# Two questions reach the same answer. Action $01 asks about one component and action $03 asks
# about whichever one is being or was last authenticated. While it runs both say $40 with the
# method and a zero authentication length; once $8F has been raised both say $41 with the result,
# or a failure from $C0 up. The result stays readable until a reply carrying it is acknowledged;
# after that action $03 answers $FF. Only one authentication runs at a time, so starting another
# abandons the first, and the first then has nothing to report.
#
# The requests are as in 19.7: CRC-16, a two-byte seed of zero, a one-byte offset of zero.
#
# From the gaming machine library's source, read but not measured, three things these cases are
# expected to catch on a machine built on it - UNKNOWN until one is:
#   - a status $40 reply leaves out the authentication length byte (see 19.7);
#   - a component's result is cleared the moment either action reports it, acknowledged or not,
#     so action $03 after a read of the result answers $FF where 19.9 and 19.11 expect the result;
#   - an abandoned component reports $C1 on the next read rather than the $FF 19.12 expects.

test "19.9 reading the status of the component being authenticated" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, decoder.lp6E, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0
  host remember field name of lp 6E as started

  # Action $01 on the same component before $8F: the same answer.
  host send lp 6E data 0401000101
  host expect field status of lp 6E == $40
  host expect field name of lp 6E == saved.started
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0

  # After $8F, the result.
  host expect exception authentication_complete within 60s
  host send lp 6E data 0401000101
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF

  # Action $03 gives the same result, and action $01 after it once more.
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0401000101
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF

  note "With status $41 every one of the last three replies carries the method sent ($00000001), a non-zero authentication length and that many bytes of result, the same bytes each time. Statuses from $80 to $BF are refused by the document here and the steps above let them through: check the status is $41 or $C0 and up."
}

test "19.10 asking how the authentication went" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, decoder.lp6E, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0
  host remember field name of lp 6E as started

  # Action $03 before $8F: in progress, about the component just started.
  host send lp 6E data 0103
  host expect field status of lp 6E == $40
  host expect field name of lp 6E == saved.started
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0

  # After $8F, the result; asked again after that reply was acknowledged, nothing.
  host expect exception authentication_complete within 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  note "With status $41 the result reply carries the method sent, a non-zero authentication length and that many bytes of result. The last reply is $FF with nothing after the status but the check sequence. The document asks for component 1 to be enabled before the case starts."
}

# Asking about each component in turn until one is no longer in progress, then reading its result.
test "19.11 the result is cleared once it has been read" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, decoder.lp6E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40

  host send lp 6E data 0401000101
  host expect field status of lp 6E == $40
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0

  # Once it has finished, action $01 reports how it went.
  host expect exception authentication_complete within 60s
  host send lp 6E data 0401000101
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF

  # Action $03 then reports it once more if it was a success, and after that nothing.
  host send lp 6E data 0103
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  note "The document lets the component be any index 19.2 reported and walks the indexes with action $01 until one answers other than $40; with only index 1 started, index 1 is that one once $8F has been raised. Walking the others needs the count from 19.2 (host.request_from_reply)."
  note "The first action $03 is not asserted because the document branches on the read before it: after a $41 it repeats the result - status, the component's name, size and methods, the method sent, a non-zero length and the data - and after anything else it is already $FF with no method, length or data. Check which in the frame."
}

# A second authentication started over the first: the first is abandoned and has nothing to say.
test "19.12 starting another authentication abandons the first" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, machine.multiple_components, decoder.lp6E
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect field method of lp 6E == 1

  # Component 2 before component 1 has finished.
  host send lp 6E data 0D02000102010000000200000100
  host expect field status of lp 6E == $40
  host expect field method of lp 6E == 1
  host remember field name of lp 6E as second

  # Component 1 has nothing to report; component 2 is still running.
  host send lp 6E data 0401000101
  host expect field status of lp 6E == $FF
  host send lp 6E data 0401000102
  host expect field status of lp 6E == $40
  host expect field name of lp 6E == saved.second

  host expect exception authentication_complete within 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0401000102
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF

  note "Each request must reach the machine before it raises $8F for the one before, so a component that authenticates in a fraction of a second makes this case unreliable; pick two large ones."
  note "The $FF for the abandoned component carries no method, length or data. With status $41 the final reply carries the method of the second request and its result."
  note "The document lists the final pair as action $03 then the read, and asks for status $41 or above $C0; $C0 itself is a failure the document allows elsewhere and is accepted here."
}
