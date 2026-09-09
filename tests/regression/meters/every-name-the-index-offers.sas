# Every meter a script can ask for by name, read by name. Forty of them, and the point is not
# any single number but that each one resolves to a command the machine answers and to a field
# that reply actually holds: a name added to the index with nothing behind it fails here rather
# than in whatever case first tried to use it.
#
# The comparison is "at least nought" throughout, which is the weakest true thing that can be
# said about a meter and the strongest one that holds for a machine in any state. What each name
# is worth after something has happened to it is the next case.
#
# Which command each name is read from is not arbitrary and is not visible here. Where two
# commands report one meter the library does not always make them answer the same number, so the
# choice is made once, in one place, and printed by `sasctl catalogue readable-meters`.

test "every meter this build names can be read by that name" {
  requires decoder.lp0F
  requires decoder.lp18
  requires decoder.lp1C
  requires decoder.lp1E
  requires decoder.lp2A
  requires decoder.lp2B
  requires decoder.lp46
  requires decoder.lp49
  requires decoder.lp4A

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The nine that were readable before this wave and are counted in credits or currency.
  host expect meter total_coin_in >= 0
  host expect meter total_coin_out >= 0
  host expect meter total_drop >= 0
  host expect meter total_jackpot >= 0
  host expect meter total_cancelled_credits >= 0
  host expect meter current_credits >= 0
  host expect meter total_bill_value >= 0
  host expect meter games_played >= 0
  host expect meter games_won >= 0
  host expect meter games_lost >= 0

  # The six bill counts the bulk reply carries, which is where they are read from.
  host expect meter bills_1 >= 0
  host expect meter bills_5 >= 0
  host expect meter bills_10 >= 0
  host expect meter bills_20 >= 0
  host expect meter bills_50 >= 0
  host expect meter bills_100 >= 0

  # The fourteen no bulk reply carries, each read from the command named for it.
  host expect meter bills_2 >= 0
  host expect meter bills_25 >= 0
  host expect meter bills_200 >= 0
  host expect meter bills_250 >= 0
  host expect meter bills_500 >= 0
  host expect meter bills_1000 >= 0
  host expect meter bills_2000 >= 0
  host expect meter bills_2500 >= 0
  host expect meter bills_5000 >= 0
  host expect meter bills_10000 >= 0
  host expect meter bills_20000 >= 0
  host expect meter bills_25000 >= 0
  host expect meter bills_50000 >= 0
  host expect meter bills_100000 >= 0

  # The money the acceptor, the coin acceptor and the hopper have moved.
  host expect meter bills_accepted_credits >= 0
  host expect meter bills_in_stacker >= 0
  host expect meter stacker_credits >= 0
  host expect meter true_coin_in >= 0
  host expect meter true_coin_out >= 0
  host expect meter external_coin_acceptor_credits >= 0

  # The four counted from an event rather than from the machine's whole life.
  host expect meter games_since_power_up >= 0
  host expect meter games_since_door_closure >= 0
  host expect meter slot_door_opened >= 0
  host expect meter power_reset >= 0
}
