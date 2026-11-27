# Chapter 20, section 20.1 - clearing a hand pay from the host with long poll $94.
#
# A machine configured for it lets the host clear a jackpot or cancelled-credit hand pay as if the
# attendant had keyed it off, and the reply's single code says what happened: cleared, refused, or
# nothing to clear. The meters must move exactly as they would for an attendant reset - a jackpot
# lands on the jackpot meter, cancelled credits on the hand-paid cancelled credits meter, and coin
# out moves for neither.
#
# @finding (20.1): with the main door open the machine must refuse the reset with code 01. The library
# answers 01 only when the application has said it is busy for $94, and the platform never says so
# (QSP SasNetwork_AftTransfer.hpp:1525, libsas lp.cpp:9102), so the hand pay is reset with the door open.

test "20.1 a remote hand pay reset" {
  tag @egm-test-scripts @chapter-20 @needs-stimulus @needs-configuration @destructive @finding
  requires decoder.lp94
  requires machine.remote_handpay_reset
  requires stimulus.handpay.jackpot, stimulus.handpay, stimulus.handpay.reset, stimulus.door.main, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # With nothing to clear, the machine says so rather than refusing the command.
  host send lp 94
  host expect field reset_code of lp 94 == 2

  # A jackpot with the main door open cannot be cleared from the host.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  egm door main open
  host send lp 94
  host expect field reset_code of lp 94 == 1

  # Close the door, read the hand pay, and let the attendant clear it.
  egm door main close
  host send lp 1B
  host expect field amount of lp 1B != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host forget exceptions within 2s

  # A jackpot cleared from the host with no $A8 is a standard reset, and it goes to the jackpot
  # meter. Fifteen hundred dollars is thirty thousand five-cent units.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount of lp 1B != 0
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter jackpot += 30000 within 5s
  host expect meter coin_out unchanged within 2s
  host forget exceptions within 2s

  # Two jackpots, each cleared from the host before it is read. Each reading afterwards releases
  # one $52, oldest first.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 94
  host expect field reset_code of lp 94 == 0
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host send lp 1B
  host expect field amount of lp 1B != 0
  host expect exception handpay_was_reset within 15s
  host send lp 1B
  host expect field amount of lp 1B != 0
  host expect exception handpay_was_reset within 15s
  host forget exceptions within 2s

  # A cancelled-credit hand pay cleared from the host: neither coin out nor jackpot moves.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level of lp 1B == $80
  host expect field reset_id of lp 1B == 0
  host send lp 94
  host expect field reset_code of lp 94 == 0
  host expect meter coin_out unchanged within 2s
  host expect meter jackpot unchanged within 2s

  note "Before the run, configure the machine for remote hand pay reset and for host-controlled reset to the credit meter, with a jackpot limit below fifteen hundred dollars and a high credit limit. The amounts are in cents."
  note "After each reset from the host the machine is out of the hand pay and playable again; a person confirms that at the machine. The cancelled-credit reset must also add the hand pay to meter 0003, hand-paid cancelled credits, which this build cannot read by name, so check it in a selected meter reply."
  note "The document's step 7 gives this jackpot's reset ID as 00, while 20.3 gives 01 for the same jackpot on the same set-up, with reset to the credit meter configured and a high credit limit. Both cannot hold, so the reset ID is asserted in 20.3, where the round is about it, and not here."
  note "Not done: the document then turns remote reset off at the machine and shows $94 being ignored, both with and without a hand pay, and the attendant's key still clearing it onto meter 0003. Changing the machine's configuration mid-case is a person's job and has no step in the language, so that part is not exercised."
}
