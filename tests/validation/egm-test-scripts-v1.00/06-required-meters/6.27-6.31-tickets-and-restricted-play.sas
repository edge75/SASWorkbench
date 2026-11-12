# Chapter 6, sections 6.27 to 6.31 - tickets and restricted play, read at rest.
#
# Every one of these is a required meter whether or not the machine takes or prints tickets, so the
# wire half is the same five times: the code comes back through both polls with a width above zero.
# How these meters move is the subject of the ticketing chapter, so here they are only read.

test "6.27 total ticket in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001500
  host expect field meters[0].code == $15
  host expect field meters[0].size >= 1
  host send lp AF data 0400001500
  host expect field meters[0].code == $15
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 0015 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.28 total ticket out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001600
  host expect field meters[0].code == $16
  host expect field meters[0].size >= 1
  host send lp AF data 0400001600
  host expect field meters[0].code == $16
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 0016 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.29 total restricted credits played" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001900
  host expect field meters[0].code == $19
  host expect field meters[0].size >= 1
  host send lp AF data 0400001900
  host expect field meters[0].code == $19
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 0019 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.30 total non-restricted credits played" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001A00
  host expect field meters[0].code == $1A
  host expect field meters[0].size >= 1
  host send lp AF data 0400001A00
  host expect field meters[0].code == $1A
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 001A carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.31 current restricted credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001B00
  host expect field meters[0].code == $1B
  host expect field meters[0].size >= 1
  host send lp AF data 0400001B00
  host expect field meters[0].code == $1B
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 001B carries the same value in both replies, and it matches the audit screen in accounting units."
}
