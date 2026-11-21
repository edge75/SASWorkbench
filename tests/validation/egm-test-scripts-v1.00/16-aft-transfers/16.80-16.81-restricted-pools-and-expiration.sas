# Chapter 16, sections 16.80 and 16.81 - which pool restricted credit belongs to, and when it dies.
#
# Restricted credit is promotional money tied to a pool and an expiry date. The machine can only
# hold one pool at a time, so a transfer from a different pool must be refused while any of the
# first remains; and the expiry the host sends must come back in the transfer record, or be
# replaced by the machine's own default when the host sent none, or refuse a ticket that would be
# born already expired.
#
# The software machine keeps the pool and the expiration as the library does, so both run; the ticket
# half of 16.81 is a note, as the platform it mirrors performs no transfer to a ticket.

# @finding: a transfer to the host reports the pool it named, not the pool the credit came from, and
# naming the other pool is a full success. The library stores the request's pool in the record
# (lp.cpp:5612) and checks the pool only for transfers to the machine (lp.cpp:5459).
test "16.80 restricted credit from two pools" {
  tag @egm-test-scripts @chapter-16 @finding
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Two full transfers of five restricted dollars in pool 1.
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field restricted_amount == 500
  host expect field pool_id == 1
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000132000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field restricted_amount == 500
  host expect field pool_id == 1

  # Pool 2 while pool 1 credit is there: refused, nothing moved, the record naming pool 2.
  host send lp 72 data 36000000000000000000000005000000000000013930000000000000000000000000000000000000000000000133000000000200000000
  host expect field transfer_status == $8F
  host send lp 72 data 02FF00
  host expect field transfer_status == $8F
  host expect field restricted_amount == 0
  host expect field pool_id == 2

  # Three dollars back with no pool named, then three naming pool 2: both records show pool 1.
  host send lp 72 data 36000080000000000000000003000000000000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field pool_id == 1
  host send lp 72 data 36000080000000000000000003000000000000003930000000000000000000000000000000000000000000000135000000000200000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $01
  host expect field pool_id == 1

  # Emptied, five pool-1 dollars on again, and four back naming pool 1.
  host send lp 72 data 36010080000000000099999999990000000000003930000000000000000000000000000000000000000000000136000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000137000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36000080000000000000000004000000000000003930000000000000000000000000000000000000000000000138000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
}

test "16.81 the expiry date of restricted credit" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # In and out with a date long past: both succeed and both records carry the date sent.
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000131010120200100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 01012020
  host send lp 72 data 36000080000000000000000005000000000000003930000000000000000000000000000000000000000000000132010120200000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 01012020

  # A date far ahead.
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000133123120990100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 12312099
  host send lp 72 data 36000080000000000000000005000000000000003930000000000000000000000000000000000000000000000134123120990000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 12312099

  # Zero, and one day.
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000135000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 0
  host send lp 72 data 36000080000000000000000005000000000000003930000000000000000000000000000000000000000000000136000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 72 data 36000000000000000000000005000000000000003930000000000000000000000000000000000000000000000137000000010100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 1
  host send lp 72 data 36000080000000000000000005000000000000003930000000000000000000000000000000000000000000000138000000010000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field expiration == 1

  note "The document also asks for an expiry of today, which a script cannot write; it behaves as the dates either side of it."
  note "A transfer of five restricted dollars to a ticket with today's expiry prints the ticket, succeeds, and records that date. With an expiry before today it is refused as already expired ($8D) and moves nothing. With zero or one it prints and records the machine's default expiry. The platform the software machine mirrors performs no transfer to a ticket, so the library refuses one $82 (see 16.63)."
}
