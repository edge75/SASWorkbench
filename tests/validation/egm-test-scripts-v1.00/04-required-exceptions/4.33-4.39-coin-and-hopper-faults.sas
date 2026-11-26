# Chapter 4, sections 4.33 to 4.39 - coins going the wrong way.
#
# Seven cases about a coin acceptor, a hopper and a diverter. Every one of them is produced by
# interfering with a mechanism - a coin on a string, a blocked optical switch, a disconnected
# hopper - and a machine without that mechanism owes nothing. The platform has neither, so each case
# declares the devices it needs: the software machine is built with them, and any other machine
# skips until a person at a cabinet that has them runs it (D58).

# The catch-all. Anything the machine faults on that has no code of its own comes out as $20.
test "4.33 general tilt" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.general_tilt

  note "Create a fault the machine has no other SAS exception for, if it can be made to: one $20 on a general poll. It is the code for everything else, so what produces it is the manufacturer's answer."
}

test "4.34 coin in and coin out tilts" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.coin, stimulus.hopper, stimulus.settle
  machine coin acceptor on
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm coin fault tilt
  host expect exception coin_in_tilt within 8s
  egm coin fault cleared

  egm hopper fault jam
  host expect exception coin_out_tilt within 8s
  egm hopper fault cleared

  note "Only for machines with a hopper. At the cabinet a coin-in tilt is tokens with the acceptor disconnected or several in fast succession, and a coin-out tilt is a payout from a disconnected hopper or a blocked counter."
}

test "4.35 a coin pulled back out" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.coin, stimulus.settle
  machine coin acceptor on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm coin fault reversed
  host expect exception reverse_coin_in within 8s
  host expect no exception reverse_coin_in within 3s

  note "Only for machines with a coin acceptor. At the cabinet: a token on a string pulled back, or one inserted very slowly."
}

# The lockout is what stops coins being taken when the machine is not ready for them, and a machine
# must notice one getting past it in every state, not just during a game.
test "4.36 a coin taken while the acceptor was locked out" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.coin, stimulus.settle, stimulus.game_in_progress, stimulus.handpay.jackpot
  requires stimulus.handpay.reset, machine.audit_menu, stimulus.door.main, stimulus.door.cashbox
  requires stimulus.door.drop, stimulus.door.logic
  machine coin acceptor on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # During a game.
  egm bill insert 2000
  egm game start bet=1
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm game end
  host forget exceptions within 2s

  # In a hand pay lockup.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm handpay reset
  host forget exceptions within 2s

  # In an audit display.
  egm menu attendant enter
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm menu attendant leave
  host forget exceptions within 2s

  # With each security door open.
  egm door main open
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm door main close
  egm door cashbox open
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm door cashbox close
  egm door drop open
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm door drop close
  egm door logic open
  egm coin fault lockout
  host expect exception coin_in_lockout_malfunction within 8s
  egm door logic close

  note "Only for machines with a coin acceptor. At the cabinet: force the lockout into the accept position and insert a coin in each state."
}

test "4.37.1 an empty hopper on a cash out" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.hopper, stimulus.bill, stimulus.button.collect, stimulus.settle
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm hopper level 0
  egm set hopper limit 1000000
  egm bill insert 2000
  egm press collect
  host expect exception hopper_empty within 8s
  egm hopper fault cleared

  note "Only for machines with a hopper. At the cabinet: empty it, set the hopper limit to its maximum, put credits on with one note and press cash out."
}

test "4.37.2 an empty hopper on a win" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.hopper, stimulus.bill, stimulus.play, stimulus.settle
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm hopper level 0
  egm set hopper limit 1000000
  egm set win payout hopper
  egm bill insert 2000
  egm play games=1 bet=1 win=50
  host expect exception hopper_empty within 8s
  egm hopper fault cleared

  note "Only for machines configured to pay wins straight from the hopper rather than to the credit meter. At the cabinet: empty it and play until a win."
}

test "4.38 an extra coin paid" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.hopper, stimulus.settle
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm set hopper limit 1000000
  egm hopper fault runaway
  host expect exception extra_coin_paid within 8s
  egm hopper fault cleared

  note "Only for machines with a hopper, filled, with the hopper limit at its maximum. At the cabinet: tape a coin over the exit, block the hopper's switch, or trigger the release by hand."
}

test "4.39 a diverter malfunction" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.coin, stimulus.hopper, stimulus.settle
  machine coin acceptor on
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A full hopper sends coins to the drop through the diverter.
  egm hopper level 1000
  egm coin insert 2
  host expect meter true_coin_in += 2 within 3s

  egm coin fault diverter
  host expect exception diverter_malfunction within 8s
  egm coin fault cleared

  note "Only for machines with a coin acceptor and a diverter. At the cabinet: jumper out the hopper-full detector, insert two tokens, then block the diverter shut and insert one more."
}
