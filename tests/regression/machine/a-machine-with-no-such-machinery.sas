# What a machine answers about equipment it has not got. A zero here is a real answer and not a
# placeholder: this machine has no coin acceptor, no hopper and no handpay, so the meters those
# would move have never moved. The replies that would need machinery it does not have at all -
# progressives, a legacy bonus, reels, cards - are refused rather than answered with zeroes, and
# that refusal is pinned in the unit tests rather than here, because a refusal is not something a
# script asserts on.

test "the meters of equipment this machine has not got read zero" {
  tag @wire-only
  requires decoder.lp2A
  requires decoder.lp2B
  requires decoder.lp47

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 2A
  host expect field true_coin_in == 0

  host send lp 2B
  host expect field true_coin_out == 0

  # The same library meter as the poll above, so on this machine the two can never disagree.
  host send lp 47
  host expect field external_coin_acceptor_credits == 0
}

test "a machine holding no handpay says so field by field" {
  tag @wire-only
  requires decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1B
  host expect field progressive_group == 0
  host expect field level == 0
  host expect field amount == 0
  host expect field partial_pay == 0
  host expect field reset_id == 0
  host expect field session_game_win == 0
}

test "a machine with no hopper says it cannot measure one" {
  tag @wire-only
  requires decoder.lp4F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The level is in this reply only when the machine can detect it, and the byte before it is how
  # the machine says it cannot. So the reply is five bytes rather than nine, and its length byte
  # is what says which.
  host send lp 4F
  host expect field length == 2
  host expect field percent_full == $FF
}

test "nothing is waiting to be told how to pay out" {
  tag @wire-only
  requires decoder.lp57

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # This machine performs standard validation and prints the moment it is asked, so no cashout is
  # ever held waiting for a host. The reply has a code for exactly that.
  host send lp 57
  host expect field cashout_type == $80
  host expect field amount == 0
}

test "a printed ticket shows up in the validation meters" {
  tag @needs-stimulus
  # The enhanced styles keep the printed record waiting for $4D, and this case never reads it.
  requires egm.validation == standard
  requires stimulus.bill
  requires stimulus.button.collect
  requires decoder.lp50

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s

  egm press collect
  host expect exception cashout_ticket_printed within 5s

  # One cashable ticket from a cashout, and its value in cents rather than in accounting units -
  # this is the pair of meters that does not follow the denomination.
  host send lp 50 with validation_type=0
  host expect field validation_type == 0
  host expect field quantity == 1
  host expect field amount == 2000

  # A type this machine has never issued, answered rather than refused, which is what the
  # library's own handler does.
  host send lp 50 with validation_type=$04
  host expect field validation_type == 4
  host expect field quantity == 0
}
