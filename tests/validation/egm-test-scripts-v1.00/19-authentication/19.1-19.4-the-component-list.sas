# Chapter 19, sections 19.1 to 19.4 - whether the machine takes part in component authentication,
# and the list of components it offers for it.
#
# A machine that authenticates its components says so in bit 4 ($10) of the second feature byte of
# its enabled-features reply, and then answers long poll $6E. Every $6E reply starts with a check
# sequence computed across all the component names, so a host can tell the list moved without
# reading it again; and the machine raises $8E whenever a component is added, removed, updated or
# reordered - never merely because one stopped talking or was restarted.
#
# The requests are written as bytes, length byte first. With action $00 the request is that action
# alone; with actions $01 and $02 an addressing mode, a length and the index or name follow.
#
# Every case after 19.1 declares the machine taking part, which the software machine is built to do
# with two components and any other machine is asked off that feature bit. 19.4 still needs a
# component whose link can be cut (stimulus.component_link).

# Whichever way the bit is set passes; what it must agree with is the machine's configuration.
test "19.1 the enabled-features reply says whether authentication is supported" {
  tag @egm-test-scripts @chapter-19 @wire-only
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number of lp A0 == 0

  note "Bit 4 ($10) of the second feature byte is set on a machine configured for component authentication and clear on one that is not. This build cannot compare one bit of a reply byte on its own (host.field_bits), so read it in the frame and compare it with the machine's configuration. Cases 19.2 to 19.16 apply only when it is set."
}

# The list as a whole: its check sequence, how many components it has, and nothing else.
test "19.2 the number of installed components" {
  tag @egm-test-scripts @chapter-19 @wire-only
  machine component authentication on
  requires decoder.lp6E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Action $00: interrogate the list.
  host send lp 6E data 0100
  host expect field status of lp 6E == 1
  host expect field size_length of lp 6E >= 1
  host expect field size of lp 6E >= 1

  note "The component list check sequence must equal the CRC computed across every component name; this build cannot compute one over names it has not read (host.request_from_reply). Check it against the names 19.5 reads."
  note "A list name, when the machine sends one, is printable ASCII of exactly the length given. The size is the number of components that can be authenticated - compare it with the machine's own component screen."
  note "Not run: the same request with a broken check sequence must be ignored or refused with the address ORed with $80 (host.bad_crc)."
}

# A change to the installed components changes the list's check sequence.
test "19.3 the component list check sequence follows the list" {
  tag @egm-test-scripts @chapter-19 @needs-a-person
  machine component authentication on
  requires decoder.lp6E, host.compare_replies
  requires stimulus.component_change

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6E data 0100
  host expect field status of lp 6E == 1
  host remember field component_list_crc of lp 6E as before

  egm component change

  # Still a well-formed list reply, as in 19.2, with a different check sequence.
  host send lp 6E data 0100
  host expect field status of lp 6E == 1
  host expect field size_length of lp 6E >= 1
  host expect field size of lp 6E >= 1
  host expect field component_list_crc of lp 6E != saved.before

  note "On a real machine the change is a person's: add, remove, update or reorder a component that takes part in authentication, by whatever means the manufacturer provides."
}

# $8E for a real change to the list, and silence for a component that merely drops out.
test "19.4 a change to the list raises component list changed" {
  tag @egm-test-scripts @chapter-19 @needs-a-person
  machine component authentication on
  requires stimulus.component_change, stimulus.component_link

  note "Add, remove, update or reorder a component that takes part in authentication: $8E on the next general poll."
  note "Cut the communication with such a component, and separately cycle its power: neither raises $8E. A peripheral that stops answering is still installed, only unavailable."
}
