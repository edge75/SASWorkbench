# Six replies that describe the machine rather than what has happened to it, checked against
# each other. The identification reply and the game configuration reply carry the same seven
# values under two commands, and a host that read one and trusted the other would never find out
# if they disagreed.

test "the game configuration reply says what the identification reply says" {
  tag @wire-only
  requires decoder.lp1F
  requires decoder.lp53

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1F
  host expect field game_id == QX
  host expect field denomination == 2
  host expect field max_bet == 255
  host expect field paytable_id == 001
  host expect field base_percentage == 9210

  # The same machine, asked about the machine as a whole rather than about itself.
  host send lp 53 with game_number=0
  host expect field game_number == 0
  host expect field game_id == QX
  host expect field additional_id == 001
  host expect field denomination == 2
  host expect field max_bet == 255
  host expect field base_percentage == 9210
}

test "one game, implemented and enabled, and none selected" {
  tag @wire-only
  requires decoder.lp51
  requires decoder.lp55
  requires decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 51
  host expect field games == 1

  # Nothing on this machine selects a game, so nothing is selected - which is a different
  # statement from "game one", and the reply has a way of saying it.
  host send lp 55
  host expect field selected_game == 0

  host send lp 56
  host expect field count == 1
  host expect field games[0].game_number == 1
}

test "the extended game reply names the game and its paytable" {
  tag @wire-only
  requires decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Two length-prefixed names in one reply, which is why nothing after them sits at a fixed
  # offset: a layout one byte out reads the length of the second name out of the first one's
  # last character and produces a string of the wrong length.
  host send lp B5 with game_number=0
  host expect field game_name == QXFAKE
  host expect field paytable_name == 001
  host expect field max_bet == 255

  # This machine is in no progressive group, so it has no progressive levels and no wager
  # categories to report.
  host expect field progressive_levels == 0
  host expect field wager_categories == 0
}

test "the wager category reply reports the payback as text and the meter as wide as it says" {
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.play
  requires decoder.lpB4
  requires decoder.lpB3

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  egm play games=2 bet=1 win=0

  # Category zero of the machine as a whole, which is the coin-in meter and the machine's own
  # payback percentage. The percentage is four characters of text: read as a number it comes out
  # as nine hundred and forty-three million.
  host send lp B4 with game_number=0 wager_category=0
  host expect field payback_percentage == 9210
  host expect field meter_size == 4
  host expect field coin_in == 40

  # This machine has no coin acceptor and no token, so nothing here ever configured one.
  host send lp B3
  host expect field token_denomination == 0
}
