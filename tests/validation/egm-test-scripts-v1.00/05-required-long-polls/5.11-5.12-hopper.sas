# Chapter 5, sections 5.11 and 5.12 - the hopper status poll.
#
# One reply with two lengths: two bytes when the machine can only say what the hopper is doing and
# how full it is, six when it can also count the coins. Both cases need a hopper in a known state -
# emptied, jammed, half full - so each declares one (D58). What a machine without a hopper answers
# is pinned in tests/regression.

test "5.11 each hopper fault has its own status" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.hopper, stimulus.hopper_fault, stimulus.bill, stimulus.button.collect, stimulus.settle
  requires decoder.lp4F
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # An empty hopper on a cash out.
  egm hopper level 0
  egm set hopper limit 1000000
  egm bill insert 2000
  egm press collect
  host expect exception hopper_empty within 8s
  host send lp 4F
  host expect field length of lp 4F >= 2
  host expect field status of lp 4F == 7
  egm hopper fault cleared

  egm hopper fault flooded_optics
  host send lp 4F
  host expect field status of lp 4F == 1
  egm hopper fault cleared

  egm hopper fault reverse_coin
  host send lp 4F
  host expect field status of lp 4F == 2
  egm hopper fault cleared

  egm hopper fault coin_too_short
  host send lp 4F
  host expect field status of lp 4F == 3
  egm hopper fault cleared

  egm hopper fault jam
  host send lp 4F
  host expect field status of lp 4F == 4
  egm hopper fault cleared

  egm hopper fault runaway
  host send lp 4F
  host expect field status of lp 4F == 5
  egm hopper fault cleared

  egm hopper fault disconnected
  host send lp 4F
  host expect field status of lp 4F == 6
  egm hopper fault cleared

  note "At the cabinet: flood the optics, feed a coin backwards, use a short coin, jam it, make it run away and disconnect the optics. The reply carries the coin count only when its length is 06."
}

test "5.12 how full the hopper is" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus @finding
  requires stimulus.hopper, stimulus.settle
  requires decoder.lp4F
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Full.
  egm hopper level 1000
  host send lp 4F
  host expect field status of lp 4F == 0
  host expect field percent_full of lp 4F == 100
  # @finding: the library keeps the coin count only when the hopper cannot measure it, the opposite of
  # when its reply carries one, so a six-byte reply always counts zero (libsas.cpp:1325, lp.cpp:2777).
  host expect field hopper_level of lp 4F == 1000

  # Half full.
  egm hopper level 500
  host send lp 4F
  host expect field status of lp 4F <= 7
  host expect field percent_full of lp 4F == 50
  host expect field hopper_level of lp 4F == 500

  note "At the cabinet: fill the hopper and configure the machine to know it is full, then empty it and add coins to half its capacity. A machine that does not measure the level answers $FF and a two-byte reply."
}
