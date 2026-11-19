# Chapter 19, sections 19.13 and 19.14 - the authentication complete exception, and a component
# that cannot be authenticated at the moment it is asked.
#
# $8F is not a one-off. Until the host has read the result, the machine raises it again every
# fifteen seconds, through a power cycle too. Reading the result is subject to the same retry rule
# as any other reply: the same request twice is a refusal and draws the same answer, three times
# is final and the third may go unanswered, and once a reply carrying the result is acknowledged -
# here by a general poll between two requests - asking again gives $FF.
#
# From the gaming machine library's source, read but not measured: the fifteen-second repeat is
# kept in working memory with the rest of the component state, so whether it survives a power
# cycle is UNKNOWN; and status $C5 is only ever set by the application when it ends an
# authentication already reported at $40, so a $C5 in the reply to the request itself, as 19.14
# expects, is not something that library produces - also UNKNOWN until a machine is measured.

test "19.13 authentication complete is repeated until the result is read" {
  tag @egm-test-scripts @chapter-19 @needs-a-person @destructive
  requires machine.component_authentication, decoder.lp6E
  requires machine.power_cycle, host.timing, host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 6E data 0D02000101010000000200000100
  host expect field status of lp 6E == $40
  host expect exception authentication_complete within 60s

  # Unread, it comes back every fifteen seconds.
  host expect exception authentication_complete within 20s
  host expect exception authentication_complete within 20s

  # And after a power cycle.
  egm power cycle
  host expect link up within 120s
  host expect exception authentication_complete within 20s
  host expect exception authentication_complete within 20s

  # The line is the case's from here.
  host poll sequence
  host send sync

  # Action $03 three times: the first two answer the same, the third may be silent.
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0103

  # Action $01 on the same component three times: the same rule.
  host send lp 6E data 0401000101
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0401000101
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0401000101

  # Action $03 twice, a general poll to acknowledge it, and once more: nothing left.
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send lp 6E data 0103
  host expect field status of lp 6E >= $41
  host expect field status of lp 6E != $FF
  host send gp
  host send lp 6E data 0103
  host expect field status of lp 6E == $FF

  host poll every 200ms

  note "The document addresses the component by a name 19.2 reported; this case uses index 1 because a request cannot yet carry a name read from a reply (host.request_from_reply). It also waits a full minute before the power cycle; the two repeats above are the same check, shorter."
  note "Check the repeats are fifteen seconds apart, not merely within twenty, and that each repeated answer to action $01 and $03 is byte for byte the first one. The power cycle is a person's to perform on a real machine."
  note "The final $FF carries no method, authentication length or data."
}

test "19.14 a component that cannot be authenticated now" {
  tag @egm-test-scripts @chapter-19 @needs-a-person
  requires machine.component_authentication, decoder.lp6E, stimulus.component_busy

  note "Put a component into a state where it cannot be authenticated for now - the manufacturer says how. Ask to authenticate it with action $02, using a method it lists: the reply is status $C5."
  note "Then read the status of a component that can be authenticated, with action $01: status, name, size and available methods, and those methods are the ones the component reported before. The document asks for them to match the reply to the refused request, which carries no methods; compare them with 19.5's reading instead."
}
