# Chapter 23, section 23.4 - every single-meter poll against the selected-meter polls.
#
# Thirty long polls each carry one meter, and every one of those meters can also be asked for
# by code through $6F, $AF and $2F. The document reads them pair by pair; this case reads all the
# codes in three lists through each selected-meter poll and then every single-meter poll, which puts
# the same values on the line in fewer exchanges.
#
# Which code stands behind which poll, as the document pairs them:
#   $10 0004   $11 0000   $12 0001   $13 000A+000B   $14 0002   $15 0005   $16 0006   $17 0007
#   $18 0025 and 0026     $1A 000C   $20 000B, in whole currency units   $2A 0008   $2B 0009
#   $38 004A   $39 004B   $3A 0048   $3B 0045   $3C 004C   $3E 004D   $3F 004E   $40 004F
#   $41 0050   $42 0051   $43 0052   $44 0053   $45 0049   $46 000B   $47 0027   $49 003E
#   $4A 003F
# The bill counts $31 to $37 are left to 23.11, as the document leaves them.
#
# Whether each pair agrees is one reply compared with another (host.compare_replies), so every
# reply is asserted for its shape and the pairing is a note.

test "23.4 each single-meter poll and its selected-meter code" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The first twelve codes, 0000 to 000C without 0003.
  host send lp 6F data 1A00000000010002000400050006000700080009000A000B000C00
  host expect field meters[0].code <= $0C
  host expect field meters[11].code <= $0C
  host send lp AF data 1A00000000010002000400050006000700080009000A000B000C00
  host expect field meters[11].code <= $0C

  # The next twelve, 0025 to 004D.
  host send lp 6F data 1A00002500260027003E003F004500480049004A004B004C004D00
  host expect field meters[0].code >= $25
  host expect field meters[11].code >= $25
  host expect field meters[11].code <= $4D
  host send lp AF data 1A00002500260027003E003F004500480049004A004B004C004D00
  host expect field meters[11].code <= $4D

  # The last six, 004E to 0053.
  host send lp 6F data 0E00004E004F005000510052005300
  host expect field meters[0].code >= $4E
  host expect field meters[5].code <= $53
  host send lp AF data 0E00004E004F005000510052005300
  host expect field meters[5].code <= $53

  # The same thirty codes through $2F, ten at a time: the machine library answers no more than
  # ten in one request and ignores a longer one.
  host send lp 2F data 0C00000001020405060708090A
  host expect field meters[0].code <= $0A
  host expect field meters[9].code <= $0A
  host send lp 2F data 0C00000B0C2526273E3F454849
  host expect field meters[0].code >= $0B
  host expect field meters[9].code >= $0B
  host expect field meters[9].code <= $49
  host send lp 2F data 0C00004A4B4C4D4E4F50515253
  host expect field meters[0].code >= $4A
  host expect field meters[9].code >= $4A
  host expect field meters[9].code <= $53

  # And the thirty single-meter polls, each of which must be answered.
  host send lp 10
  host send lp 11
  host send lp 12
  host send lp 13
  host send lp 14
  host send lp 15
  host send lp 16
  host send lp 17
  host send lp 18
  host send lp 1A
  host send lp 20
  host send lp 2A
  host send lp 2B
  host send lp 38
  host send lp 39
  host send lp 3A
  host send lp 3B
  host send lp 3C
  host send lp 3E
  host send lp 3F
  host send lp 40
  host send lp 41
  host send lp 42
  host send lp 43
  host send lp 44
  host send lp 45
  host send lp 46
  host send lp 47
  host send lp 49
  host send lp 4A

  note "Each single-meter reply carries the same value as its code in the $6F, $AF and $2F replies, using the pairing in this file's header (host.compare_replies). A meter the machine does not support reads zero in all four."
  note "Three pairings need arithmetic rather than equality (host.meter_arithmetic): $13 is 000A plus 000B, $20 is 000B converted from accounting credits to whole currency units, and $18 carries 0025 and 0026 side by side."
  note "Several single-meter polls are divided by the accounting denomination under some of the library's meter modes and the selected-meter polls are not (see sasctl catalogue readable-meters). A pair that differs by exactly that factor is the meter mode, not a fault; ask how the machine is configured before calling it one."
}
