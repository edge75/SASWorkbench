# Chapter 7, sections 7.5 to 7.8 - what the machine says about one game at a time.
#
# Three commands describe a game by number: the enabled features, the identification, and the game
# configuration. Game zero is the machine as a whole and every other number is one game. The rule
# common to all of them is that the reply names the game it answers for, so a machine that
# answered every request with game zero's details would be caught by the echo alone.
#
# The software machine holds one game, number one. A machine with several games is asked the same
# questions for each number in its enabled list (long poll 56); the language has no loop, so the
# steps below name game one and the notes say what repeats.

# The features reply for game zero is the one chapter 6 already checks; what this section adds is
# that every other game number gets its own reply, carrying its own number.
test "7.5 the features reply is given for each game" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lpA0, decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 with game_number=0
  host expect field game_number == 0

  host send lp 56
  host expect field count >= 1

  host send lp A0 with game_number=1
  host expect field game_number == 1

  note "The game zero reply must also meet the criteria of the chapter 6 features case, which are checked there and not repeated here."
  note "Ask for every game number in the enabled list in turn; each reply carries the number it was asked for, three feature bytes and three reserved bytes. On the demo machine that is games one to four."
}

# The identification reply on a multi-game machine summarises its games: the largest maximum bet
# among them and the average of their theoretical paybacks. Proving that needs two configurations
# of the same machine with games of different maximum bets and paybacks, which only a person can
# set up, and the expected values are read off the machine's own screens.
test "7.6 the identification reply summarises every enabled game" {
  tag @egm-test-scripts @chapter-7 @needs-a-person
  requires decoder.lp1F, machine.multiple_games, machine.enable_games

  note "Enable two games whose maximum bets and theoretical paybacks differ. Read long poll 1F: the game identifier is the manufacturer's assigned code, the denomination is the code for the accounting denomination, the progressive group is zero unless progressives are configured, and the paytable matches the audit display."
  note "The maximum bet is the larger of the two games' maximum bets, or 255 when it exceeds 255. The payback is the average of the two games' theoretical paybacks."
  note "Enable every game and read 1F again: the payback is now the average over all of them and the maximum bet is the largest any of them allows."
}

# Game zero's configuration, the machine as a whole. Only the echo and the progressive group can be
# asserted as written: the rest of the reply is either the manufacturer's to know (its identifier,
# its paytable) or a comparison with the identification reply, which 7.8 makes.
test "7.7 the configuration of game zero" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp53

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 53 with game_number=0
  host expect field game_number == 0

  # The case assumes a machine with no progressives configured.
  host expect field progressive_group == 0

  note "The game identifier is the manufacturer's assigned code, the denomination is the code for the accounting denomination, and the paytable matches the one in the machine's audit display; a person checks these (D40)."
  note "The maximum bet is the largest bet the machine can take, or 255 when that is larger. The payback is four characters of hundredths of a percent with no decimal point, and a payback above 100% keeps only its last four digits."
  note "Not done: the document then sends the same poll with a corrupted check sequence and expects it ignored or refused. This build cannot corrupt a check sequence on request (host.bad_crc), so that half of the case is not exercised."
}

# Each game's configuration in turn. Game zero repeats the identification reply field for field;
# every other game has its own denomination, maximum bet, paytable and payback.
test "7.8 the configuration of each game" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp1F, decoder.lp53, decoder.lp56, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero repeats the identification reply field for field. The payback is taken from game
  # zero's reply first, because there it is text, and text is compared only with remembered text.
  host send lp 53 with game_number=0
  host expect field game_number == 0
  host remember field base_percentage as payback

  host send lp 1F
  host remember field game_id as game_id
  host remember field additional_id as additional_id
  host remember field denomination as denomination
  host remember field max_bet as max_bet
  host remember field progressive_group as progressive_group
  host remember field game_options as game_options
  host remember field paytable_id as paytable_id
  host expect field base_percentage == saved.payback

  host send lp 53 with game_number=0
  host expect field game_id == saved.game_id
  host expect field additional_id == saved.additional_id
  host expect field denomination == saved.denomination
  host expect field max_bet == saved.max_bet
  host expect field progressive_group == saved.progressive_group
  host expect field game_options == saved.game_options
  host expect field paytable_id == saved.paytable_id

  host send lp 56
  host expect field count >= 1

  host send lp 53 with game_number=1
  host expect field game_number == 1

  note "For each enabled game, its denomination code, its maximum bet in game credits, its paytable and its theoretical payback must match what the audit display shows for that game, and each paytable is that game's own (D40). On the demo machine that is games one to four."
}
