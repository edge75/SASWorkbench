# Chapter 5, sections 5.11 and 5.12 - the hopper status poll.
#
# One reply with two lengths: two bytes when the machine can only say what the hopper is doing and
# how full it is, six when it can also count the coins. Both cases need a real hopper in a known
# state - emptied, jammed, half full - and the software machine has none, so they skip until a
# hopper stimulus exists. What a machine without a hopper answers is pinned in tests/regression.

test "5.11 each hopper fault has its own status" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires stimulus.hopper, stimulus.hopper_fault
  requires decoder.lp4F

  note "Empty the hopper, put credits on the machine and cash out: the hopper-empty exception on a general poll, and the hopper status reply has length 02 or 06 and status 07."
  note "Repeat with the hopper optics flooded (status 01), a coin going in backwards (02), a coin too short (03), a coin jam (04), a runaway (05), and the coin optics disconnected (06). The reply carries the coin count only when its length is 06."
}

test "5.12 how full the hopper is" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires stimulus.hopper
  requires decoder.lp4F

  note "Fill the hopper and configure the machine to know it is full, then read the hopper status: the percentage is about $64 (100) if the machine measures it and $FF if not, and a six-byte reply carries roughly the number of coins in it."
  note "With no credits and an empty hopper, add coins to half the configured capacity and read again: the status is between 00 and 07 if supported, the percentage about $32 (50) or $FF, and the count again roughly the coins present."
}
