# The five-meter, eight-meter and per-game replies carry meters the single-meter polls also
# carry, so the interesting question is whether they agree - a field one byte out in a
# twenty-byte reply decodes into a plausible number rather than failing, and only a comparison
# against the same meter read another way finds it.
#
# Everything here is in accounting units. This machine publishes code $02, five cents: a
# twenty-dollar note is four hundred units, and a wager of one game credit is a hundred cents,
# which is twenty units.

test "the bulk meter replies agree with the single-meter polls" {
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.play
  requires decoder.lp19
  requires decoder.lp1C
  requires decoder.lp52

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  egm play games=2 bet=1 win=0

  host send lp 19
  host expect field total_coin_in == 40
  host expect field total_coin_out == 0
  host expect field total_drop == 400
  host expect field total_jackpot == 0
  host expect field games_played == 2

  host send lp 1C
  host expect field total_coin_in == 40
  host expect field total_drop == 400
  host expect field games_played == 2
  host expect field games_won == 0

  # Nothing power-cycles this machine, so this one is zero for want of the event rather than for
  # want of a counter.
  host expect field power_reset == 0

  # The same four meters again, for the machine as a whole.
  host send lp 52 with game_number=0
  host expect field game_number == 0
  host expect field total_coin_in == 40
  host expect field total_jackpot == 0
  host expect field games_played == 2
}

test "the door count and the two game counts move with what moves them" {
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.play
  requires stimulus.door.slot
  requires decoder.lp18
  requires decoder.lp1C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 10000
  egm play games=2 bet=1 win=0

  egm door slot open
  host expect exception slot_door_opened within 5s
  egm door slot close
  host expect exception slot_door_closed within 5s

  egm play games=1 bet=1 win=0

  # Two counts that would be indistinguishable without a door between them: shutting the slot
  # door starts the second one again and leaves the first alone.
  host send lp 18
  host expect field games_since_power_up == 3
  host expect field games_since_door_closure == 1

  # And the one door the protocol keeps a meter for.
  host send lp 1C
  host expect field slot_door_opened == 1
}

test "a meter asked for by code comes back as wide as that meter is" {
  tag @needs-stimulus
  requires stimulus.bill
  requires decoder.lp2F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000

  # A length of three, game zero, and one meter code: the credits dropped in. This reply states
  # no width per meter, so where the value ends is decided by which meter it is and by nothing in
  # the message - which is what makes it worth a test of its own.
  host send lp 2F data 03000024
  host expect field game_number == 0
  host expect field meters[0].code == 36
  host expect field meters[0].value == 400
}
