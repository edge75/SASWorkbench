# Chapter 6, sections 6.48 to 6.56 - electronic transfer meters, read at rest.
#
# Required whether or not the machine takes transfers at all, so the wire half is the same nine
# times: the code comes back through both polls with a width above zero. Moving them needs a
# transfer, which belongs to the cashless chapter.

test "6.48 total transfer in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001700
  host expect field meters[0].code == $17
  host expect field meters[0].size >= 1
  host send lp AF data 0400001700
  host expect field meters[0].code == $17
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0017 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.49 total transfer out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001800
  host expect field meters[0].code == $18
  host expect field meters[0].size >= 1
  host send lp AF data 0400001800
  host expect field meters[0].code == $18
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0018 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.50 cashable transfer in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002E00
  host expect field meters[0].code == $2E
  host expect field meters[0].size >= 1
  host send lp AF data 0400002E00
  host expect field meters[0].code == $2E
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 002E carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.51 restricted transfer in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002F00
  host expect field meters[0].code == $2F
  host expect field meters[0].size >= 1
  host send lp AF data 0400002F00
  host expect field meters[0].code == $2F
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 002F carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.52 non-restricted transfer in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003000
  host expect field meters[0].code == $30
  host expect field meters[0].size >= 1
  host send lp AF data 0400003000
  host expect field meters[0].code == $30
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0030 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.53 debit transfer in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003100
  host expect field meters[0].code == $31
  host expect field meters[0].size >= 1
  host send lp AF data 0400003100
  host expect field meters[0].code == $31
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0031 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.54 cashable transfer out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003200
  host expect field meters[0].code == $32
  host expect field meters[0].size >= 1
  host send lp AF data 0400003200
  host expect field meters[0].code == $32
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0032 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.55 restricted transfer out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003300
  host expect field meters[0].code == $33
  host expect field meters[0].size >= 1
  host send lp AF data 0400003300
  host expect field meters[0].code == $33
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0033 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.56 non-restricted transfer out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400003400
  host expect field meters[0].code == $34
  host expect field meters[0].size >= 1
  host send lp AF data 0400003400
  host expect field meters[0].code == $34
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where electronic transfers are switched off or not built in."
  note "Meter 0034 carries the same value in both replies, and it matches the audit screen in accounting units."
}
