# Chapter 19, sections 19.5 and 19.6 - reading one component, by index and by name, and a
# component that cannot currently be reached.
#
# Action $01 reads a component's status. It is addressed by index, mode $00, with the index as one
# byte; or by name, mode $01, with the ASCII name. Index 1 is the first component and index 0
# names none. A component that is installed but out of contact answers with status $81 and none of
# the component's own fields.
#
# Two things the language cannot do yet keep half of each case a note. A request cannot carry a
# value read from an earlier reply - a name to address by, or the count to walk the indexes up to -
# so the by-name half and the other indexes are written out for a person (host.request_from_reply).

# Index 1 read twice, the second time compared with the first; the walk and the names are notes.
test "19.5 reading a component by index and by name" {
  tag @egm-test-scripts @chapter-19 @wire-only
  machine component authentication on
  requires decoder.lp6E, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Action $01, by index, index 1.
  host send lp 6E data 0401000101
  host expect field status of lp 6E == 0
  host expect field name_length of lp 6E > 0
  host remember field name of lp 6E as first_name
  host remember field available_methods of lp 6E as first_methods

  # Asked again, the same component answers the same way.
  host send lp 6E data 0401000101
  host expect field status of lp 6E == 0
  host expect field name of lp 6E == saved.first_name
  host expect field available_methods of lp 6E == saved.first_methods

  note "The reply length and check sequence are the decoder's to verify. The list check sequence equals the CRC over all component names, and the name is printable ASCII of the length given; a non-zero size length means the size is exactly that many bytes."
  note "Not run: repeat the read for every index up to the count 19.2 returned. Every name is different (host.request_from_reply)."
  note "Not run: read each component again by name, mode $01 with the name it gave by index. Each answers with status 0 and the same data as by index (host.request_from_reply)."
}

# Out of contact is status $81, and back in contact is status 0 again.
test "19.6 a component that is out of contact" {
  tag @egm-test-scripts @chapter-19 @needs-a-person
  machine component authentication on
  requires decoder.lp6E, stimulus.component_link

  note "Read component 1 by index: status 0. Cut the communication with it, by the manufacturer's means, and read it again: status $81 and nothing after the status but the check sequence."
  note "Restore the communication and read it again: status 0. Do the same for every index the list reports."
  note "Then address the same component by the name 19.5 read for it, mode $01: status 0 while it is in contact, $81 once it is cut off."
}
