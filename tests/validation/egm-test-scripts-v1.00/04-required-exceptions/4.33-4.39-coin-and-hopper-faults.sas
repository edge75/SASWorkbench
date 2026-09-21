# Chapter 4, sections 4.33 to 4.39 - coins going the wrong way.
#
# Seven cases about a coin acceptor, a hopper and a diverter. Every one of them is produced by
# interfering with a mechanism - a coin on a string, a blocked optical switch, a disconnected
# hopper - and a machine without that mechanism owes nothing. Nothing in this build has a coin
# path at all, so all seven wait on hardware.

# The catch-all. Anything the machine faults on that has no code of its own comes out as $20.
test "4.33 general tilt" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.general_tilt

  note "Create a fault the machine has no other SAS exception for, if it can be made to: one $20 on a general poll. It is the code for everything else, so what produces it is the manufacturer's answer."
}

test "4.34 coin in and coin out tilts" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.coin, stimulus.hopper

  note "Only for machines with a hopper. Create a coin-in tilt - tokens with the acceptor disconnected or its wiring loosened, or several tokens in fast succession: one $21 on a general poll."
  note "Create a coin-out tilt - pay from a disconnected hopper, trigger the feed by hand, block its counter: one $22."
}

test "4.35 a coin pulled back out" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.coin

  note "Only for machines with a coin acceptor. Insert a token on a string and pull it back, or insert one very slowly: one $2D on a general poll."
}

# The lockout is what stops coins being taken when the machine is not ready for them, and a machine
# must notice one getting past it in every state, not just during a game.
test "4.36 a coin taken while the acceptor was locked out" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.coin

  note "Only for machines with a coin acceptor. Force the lockout mechanically into the accept position, then insert a coin during a game and clear the fault: one $79 on a general poll."
  note "Repeat with the machine in a hand pay lockup, with it in an audit display, and with a security door open - main, cash box, drop or logic. Each draws one $79."
}

test "4.37.1 an empty hopper on a cash out" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.hopper

  note "Only for machines with a hopper. Empty it, set the hopper limit to its maximum, put credits on the machine with one note, and press cash out. When the machine fails to pay: one $23 on a general poll."
}

test "4.37.2 an empty hopper on a win" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.hopper

  note "Only for machines configured to pay wins straight from the hopper rather than to the credit meter. Empty it, set the hopper limit to its maximum, and play until a win. When the machine fails to pay: one $23 on a general poll."
}

test "4.38 an extra coin paid" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.hopper

  note "Only for machines with a hopper, filled, with the hopper limit at its maximum. Create an extra-coin tilt - tape a coin over the exit, block or clear the hopper's optical or mechanical switch, trigger the release by hand: one $24 on a general poll."
}

test "4.39 a diverter malfunction" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.coin, stimulus.hopper

  note "Only for machines with a coin acceptor and a diverter. Force coins to the drop - jumper out the hopper-full detector - and insert two tokens, checking the second reaches the diverter area."
  note "Then create a diverter tilt - block the diverter shut, or block its mechanical or optical switch - and insert a token: one $25 on a general poll."
}
