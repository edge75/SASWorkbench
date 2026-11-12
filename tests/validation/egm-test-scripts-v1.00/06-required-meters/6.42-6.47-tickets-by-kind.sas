# Chapter 6, sections 6.42 to 6.47 - the ticket meters split by kind, read at rest.
#
# The same shape as 6.27: each code comes back through both polls with a width above zero, whether
# or not tickets are in use. Their movement belongs to the ticketing chapter.

test "6.42 cashable ticket in credits, counting non-restricted" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002800
  host expect field meters[0].code == $28
  host expect field meters[0].size >= 1
  host send lp AF data 0400002800
  host expect field meters[0].code == $28
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 0028 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.43 cashable ticket in credits, not counting non-restricted" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002900
  host expect field meters[0].code == $29
  host expect field meters[0].size >= 1
  host send lp AF data 0400002900
  host expect field meters[0].code == $29
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 0029 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.44 restricted ticket in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002A00
  host expect field meters[0].code == $2A
  host expect field meters[0].size >= 1
  host send lp AF data 0400002A00
  host expect field meters[0].code == $2A
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 002A carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.45 non-restricted ticket in credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002B00
  host expect field meters[0].code == $2B
  host expect field meters[0].size >= 1
  host send lp AF data 0400002B00
  host expect field meters[0].code == $2B
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 002B carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.46 cashable ticket out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002C00
  host expect field meters[0].code == $2C
  host expect field meters[0].size >= 1
  host send lp AF data 0400002C00
  host expect field meters[0].code == $2C
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 002C carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.47 restricted ticket out credits" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002D00
  host expect field meters[0].code == $2D
  host expect field meters[0].size >= 1
  host send lp AF data 0400002D00
  host expect field meters[0].code == $2D
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where tickets are switched off or not built in."
  note "Meter 002D carries the same value in both replies, and it matches the audit screen in accounting units."
}
