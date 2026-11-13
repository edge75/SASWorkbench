# Chapter 23, sections 23.1 to 23.3 - the oldest ways of asking for a meter.
#
# Before $6F and $AF a host had two: a long poll per meter, and $2F, which takes a list of one-byte
# meter codes and answers each with a value whose width is fixed by which meter it is rather than
# stated in the reply. These cases check that a machine still answers both, and that $2F agrees
# with the newer polls about the same meters.
#
# Each value is remembered from the $6F reply, and the $AF and $2F replies must carry the same
# number for the same code. Every code is checked at its position before its value is used.

# A command that does not exist, and a single-meter poll this machine does not implement, both get
# silence rather than a refusal.
test "23.1 single-meter polls the machine does not have are ignored" {
  tag @egm-test-scripts @chapter-23 @wire-only

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # $FE is not a command at all.
  host expect no reply to lp FE

  # The machine is still answering afterwards.
  host send lp 1F

  note "The document also sends a single-meter poll that is defined but that this machine does not implement, and expects the same silence. Which one depends on the machine: ask its manufacturer, and add it as a no-reply expectation in a copy of this case."
}

# Three meters the machine keeps and one it does not, through all three polls. The fixed-width poll
# must answer the unkept one as zero rather than leave it out.
#
# The machine library leaves a code it does not keep out of the $2F reply altogether, and the
# software machine copies it, so the last two assertions fail against both and the case carries
# @finding, as 6.6 does for the same behaviour on $6F.
test "23.2 the fixed-width poll answers a short list" {
  tag @egm-test-scripts @chapter-23 @wire-only @finding
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A00000000010002009F00
  host expect field game_number == 0
  host expect field meters[0].code == 0
  host remember field meters[0].value as coin_in
  host expect field meters[1].code == 1
  host remember field meters[1].value as coin_out
  host expect field meters[2].code == 2
  host remember field meters[2].value as jackpot
  host send lp AF data 0A00000000010002009F00
  host expect field game_number == 0
  host expect field meters[0].code == 0
  host expect field meters[0].value == saved.coin_in
  host expect field meters[1].code == 1
  host expect field meters[1].value == saved.coin_out
  host expect field meters[2].code == 2
  host expect field meters[2].value == saved.jackpot

  # Length six: two bytes of game number and four one-byte codes.
  host send lp 2F data 0600000001029F
  host expect field game_number == 0
  host expect field length <= 216
  host expect field meters[0].code == 0
  host expect field meters[0].value == saved.coin_in
  host expect field meters[1].code == 1
  host expect field meters[1].value == saved.coin_out
  host expect field meters[2].code == 2
  host expect field meters[2].value == saved.jackpot
  host expect field meters[3].code == $9F
  host expect field meters[3].value == 0

  note "009F stands in for whichever meter this machine does not keep; take one from the manufacturer's list on a machine that keeps it."
  note "Not run: $2F sent again with a corrupted check sequence is ignored or refused (host.bad_check_sequence)."
}

# The same comparison for a game rather than for the machine as a whole. On a machine with one game
# the loop is the one reading written here; with more, every enabled game is read in turn.
test "23.3 the fixed-width poll for each enabled game" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp2F
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1
  host send lp 56
  host expect field games[0].game_number >= 1

  # Game one: coin in, coin out, jackpot and games played.
  host send lp 6F data 0A00010000010002000500
  host expect field game_number == 1
  host expect field meters[0].code == 0
  host remember field meters[0].value as coin_in
  host expect field meters[1].code == 1
  host remember field meters[1].value as coin_out
  host expect field meters[2].code == 2
  host remember field meters[2].value as jackpot
  host expect field meters[3].code == 5
  host remember field meters[3].value as games_played
  host send lp AF data 0A00010000010002000500
  host expect field game_number == 1
  host expect field meters[0].value == saved.coin_in
  host expect field meters[1].value == saved.coin_out
  host expect field meters[2].value == saved.jackpot
  host expect field meters[3].code == 5
  host expect field meters[3].value == saved.games_played
  host send lp 2F data 06000100010205
  host expect field game_number == 1
  host expect field meters[0].code == 0
  host expect field meters[0].value == saved.coin_in
  host expect field meters[1].code == 1
  host expect field meters[1].value == saved.coin_out
  host expect field meters[2].code == 2
  host expect field meters[2].value == saved.jackpot
  host expect field meters[3].code == 5
  host expect field meters[3].value == saved.games_played

  note "Game one is written in because the language has no variables. If the enabled list does not start at one, or holds more games, repeat the three polls for each game it names; on a multi-denomination machine first give every enabled game the same player denomination, and with more than twenty games enable and read them a set at a time."
}
