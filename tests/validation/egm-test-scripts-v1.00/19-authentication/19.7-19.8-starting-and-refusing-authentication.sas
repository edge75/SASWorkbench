# Chapter 19, sections 19.7 and 19.8 - asking for an authentication, and asking wrongly.
#
# Action $02 starts one. After the addressing come the method as four bytes, least significant
# first, then the seed and the offset, each behind its own length byte. Every request here uses
# CRC-16 (method $00000001) with a two-byte seed of zero and a one-byte offset of zero, which
# assumes component 1 offers CRC-16; on a machine whose first component does not, swap in a method
# it lists in its available methods. Action $03 then asks how the authentication went.
#
# The machine accepts with status $40 and the authentication length byte at zero, raises $8F when
# it has finished, and answers action $03 with the result - $41 with the data, or a failure from
# $C0 up. Once the result has been read and acknowledged, action $03 answers $FF: nothing to report.
#
# A refused request may be refused at once (status $80 and up) or accepted and then fail. A case
# cannot branch on which, so 19.8 waits long enough for either and then asks: both paths end in a
# status of $80 or more, and the next question answers $FF.
#
# From the gaming machine library's source, read but not measured: a status $40 reply is built
# with the authentication length byte written and then left out of the length, so the reply ends
# after the method. SAS 6.02 Table 17.1b includes that byte, so the authentication_length steps
# are expected to fail on a machine built on that library - UNKNOWN until one is measured.

test "19.7 starting an authentication" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, decoder.lp6E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Component 1, CRC-16, seed 0000, offset 00.
  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect field name_length of lp 6E > 0
  host expect field size_length of lp 6E > 0
  host expect field method of lp 6E == 1
  host expect field authentication_length of lp 6E == 0
  host expect exception authentication_complete within 60s

  # How it went: neither of the list statuses, not in progress, and not empty-handed.
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF

  note "With status $41 the reply carries the component's name, size and available methods again, the method used, a non-zero authentication length and that many bytes of result; with any other status only the check sequence follows it. A failure on a healthy component is worth a second look."
  note "Not run: the same for every index up to the count 19.2 returned, and again for each addressed by name (host.request_from_reply). Each starts at $40, raises $8F and reports a result."
}

test "19.8 authentication requests the machine must refuse" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires machine.component_authentication, decoder.lp6E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Index 0 names no component.
  host send lp 6E data 0D02000100010000000200000100
  host expect field status of lp 6E >= $40
  host expect field status of lp 6E != $41
  host wait 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $80
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  # A name no component has, addressed by name: "SWB-NO-SUCH".
  host send lp 6E data 1702010B5357422D4E4F2D53554348010000000200000100
  host expect field status of lp 6E >= $40
  host expect field status of lp 6E != $41
  host wait 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $80
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  # Component 1 with a method bit no component can offer, $00000080.
  host send lp 6E data 0D02000101800000000200000100
  host expect field status of lp 6E >= $40
  host expect field status of lp 6E != $41
  host wait 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $80
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  # CRC-16 with a three-byte seed, one more than the method takes.
  host send lp 6E data 0E0200010101000000030000000100
  host expect field status of lp 6E >= $40
  host expect field status of lp 6E != $41
  host wait 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $80
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  # A legal seed with an eight-byte offset of all ones, past the end of any component.
  host send lp 6E data 14020001010100000002000008FFFFFFFFFFFFFFFF
  host expect field status of lp 6E >= $40
  host expect field status of lp 6E != $41
  host wait 60s
  host send lp 6E data 0103
  host expect field status of lp 6E >= $80
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  note "A refusal at once is a status from $80 up with only the check sequence after it. An acceptance is $40, then $8F, then a failure from $C0 up; an acceptance that ends in $41 is the defect this case looks for."
  note "From the library's source, read but not measured: its offset check flags an offset only when both halves of a sixteen-byte comparison exceed the size, so an eight-byte offset is accepted at $40 and whether it then fails is up to the application - UNKNOWN until measured."
}
