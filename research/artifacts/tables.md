## Poll+Response combined (137 poll, 116 resp, union 137)

| cmd | poll name | P crc | P len | P mg | P #direct | P #total | resp name | R crc | R len | R #direct | R #total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 01 | Shutdown | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 02 | Startup | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 03 | Sound off | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 04 | Sound on | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 05 | Reel spin or game play sounds disabled | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 06 | Enable bill acceptor | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 07 | Disable bill acceptor | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 08 | Configure bill denominations | yes | no | no | 2 | 52 | **(none)** | - | - | - | - |
| 09 | Enable/disable game N | yes | no | yes | 2 | 2 | **(none)** | - | - | - | - |
| 0A | Enter maintenance mode | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 0B | Exit maintenance mode | yes | no | no | 0 | 0 | **(none)** | - | - | - | - |
| 0E | Enable/disable Real Time Event reporting | yes | no | no | 1 | 1 | **(none)** | - | - | - | - |
| 0F | Send meters 10 through 15 | no | no | no | 0 | 0 | Send meters 10 through 15 | yes | no | 6 | 6 |
| 10 | Send total cancelled credit meter | no | no | no | 0 | 0 | Send total cancelled credit meter | yes | no | 1 | 1 |
| 11 | Send total coin in meter | no | no | no | 0 | 0 | Send total coin in meter | yes | no | 1 | 1 |
| 12 | Send total coin out meter | no | no | no | 0 | 0 | Send total coin out meter | yes | no | 1 | 1 |
| 13 | Send total drop meter | no | no | no | 0 | 0 | Send total drop meter | yes | no | 1 | 1 |
| 14 | Send total jackpot meter | no | no | no | 0 | 0 | Send total jackpot meter | yes | no | 1 | 1 |
| 15 | Send games played meter | no | no | no | 0 | 0 | Send games played meter | yes | no | 1 | 1 |
| 16 | Send games won meter | no | no | no | 0 | 0 | Send games won meter | yes | no | 1 | 1 |
| 17 | Send games lost meter | no | no | no | 0 | 0 | Send games lost meter | yes | no | 1 | 1 |
| 18 | Send games since last power up and slot door closure | no | no | no | 0 | 0 | Send games since last power up and slot door closure | yes | no | 2 | 2 |
| 19 | Send meters 11 through 15 | no | no | no | 0 | 0 | Send meters 11 through 15 | yes | no | 5 | 5 |
| 1A | Send current credits | no | no | no | 0 | 0 | Send current credits | yes | no | 1 | 1 |
| 1B | Send handpay information | no | no | no | 0 | 0 | Send handpay information | yes | no | 6 | 6 |
| 1C | Send meters | no | no | no | 0 | 0 | Send meters | yes | no | 8 | 8 |
| 1D | Send EFT Cumulative meters | no | no | no | 0 | 0 | Send EFT Cumulative meters | yes | no | 4 | 4 |
| 1E | Send bill meters (counts) | no | no | no | 0 | 0 | Send bill meters (counts) | yes | no | 6 | 6 |
| 1F | Send gaming machine ID and information | no | no | no | 0 | 0 | Send gaming machine ID and information | yes | no | 8 | 36 |
| 20 | Send dollar value of bills meter | no | no | no | 0 | 0 | Send dollar value of bills meter | yes | no | 1 | 1 |
| 21 | ROM signature verification | yes | no | no | 1 | 1 | ROM signature verification | yes | no | 1 | 1 |
| 22 | Transfer non-cashable amount to GM | yes | no | no | 3 | 3 | Transfer non-cashable amount to GM | yes | no | 4 | 17 |
| 23 | Transfer promotional amount to GM | yes | no | no | 3 | 3 | Transfer promotional amount to GM | yes | no | 4 | 17 |
| 24 | Transfer dollar amount to host | yes | no | no | 2 | 2 | Transfer cash amount to host | yes | no | 4 | 17 |
| 25 | Transfer cash amount out of GM | yes | no | no | 2 | 2 | Force cash out of GM | yes | no | 4 | 17 |
| 26 | Host request for cashout out dollar amount | yes | no | no | 1 | 1 | Host request for cashout out dollar amount | yes | no | 4 | 17 |
| 27 | Send current promotional credits | no | no | no | 0 | 0 | Send current promotional credits | yes | no | 1 | 1 |
| 28 | Send tranfer log | no | no | no | 0 | 0 | Send tranfer log | yes | yes | 5 | 160 |
| 29 | Transfer cashable amount to GM | yes | no | no | 3 | 3 | Transfer cash amount to GM | yes | no | 4 | 17 |
| 2A | Send true coin in | no | no | no | 0 | 0 | Send true coin in | yes | no | 1 | 1 |
| 2B | Send true coin out | no | no | no | 0 | 0 | Send true coin out | yes | no | 1 | 1 |
| 2C | Send current hopper level | no | no | no | 0 | 0 | Send current hopper level | yes | no | 1 | 1 |
| 2D | Send total hand paid cancelled credits | yes | no | yes | 1 | 1 | Send total hand paid cancelled credits | yes | no | 2 | 2 |
| 2E | Game delay | yes | no | no | 1 | 1 | **(none)** | - | - | - | - |
| 2F | Send selected meters for game N | yes | yes | yes | 14 | 26 | Send selected meters for game N | yes | yes | 11 | 20 |
| 31 | Send $1.00 bills in meter | no | no | no | 0 | 0 | Send $1.00 bills in meter | yes | no | 1 | 1 |
| 32 | Send $2.00 bills in meter | no | no | no | 0 | 0 | Send $2.00 bills in meter | yes | no | 1 | 1 |
| 33 | Send $5.00 bills in meter | no | no | no | 0 | 0 | Send $5.00 bills in meter | yes | no | 1 | 1 |
| 34 | Send $10.00 bills in meter | no | no | no | 0 | 0 | Send $10.00 bills in meter | yes | no | 1 | 1 |
| 35 | Send $20.00 bills in meter | no | no | no | 0 | 0 | Send $20.00 bills in meter | yes | no | 1 | 1 |
| 36 | Send $50.00 bills in meter | no | no | no | 0 | 0 | Send $50.00 bills in meter | yes | no | 1 | 1 |
| 37 | Send $100.00 bills in meter | no | no | no | 0 | 0 | Send $100.00 bills in meter | yes | no | 1 | 1 |
| 38 | Send $500.00 bills in meter | no | no | no | 0 | 0 | Send $500.00 bills in meter | yes | no | 1 | 1 |
| 39 | Send $1,000.00 bills in meter | no | no | no | 0 | 0 | Send $1,000.00 bills in meter | yes | no | 1 | 1 |
| 3A | Send $200.00 bills in meter | no | no | no | 0 | 0 | Send $200.00 bills in meter | yes | no | 1 | 1 |
| 3B | Send $25.00 bills in meter | no | no | no | 0 | 0 | Send $25.00 bills in meter | yes | no | 1 | 1 |
| 3C | Send $2,000.00 bills in meter | no | no | no | 0 | 0 | Send $2,000.00 bills in meter | yes | no | 1 | 1 |
| 3D | Send cash-out ticket information | no | no | no | 0 | 0 | Send cash out ticket information | no | no | 2 | 2 |
| 3E | Send $2,500.00 bills in meter | no | no | no | 0 | 0 | Send $2,500.00 bills in meter | yes | no | 1 | 1 |
| 3F | Send $5,000.00 bills in meter | no | no | no | 0 | 0 | Send $5,000.00 bills in meter | yes | no | 1 | 1 |
| 40 | Send $10,000.00 bills in meter | no | no | no | 0 | 0 | Send $10,000.00 bills in meter | yes | no | 1 | 1 |
| 41 | Send $20,000.00 bills in meter | no | no | no | 0 | 0 | Send $20,000.00 bills in meter | yes | no | 1 | 1 |
| 42 | Send $25,000.00 bills in meter | no | no | no | 0 | 0 | Send $25,000.00 bills in meter | yes | no | 1 | 1 |
| 43 | Send $50,000.00 bills in meter | no | no | no | 0 | 0 | Send $50,000.00 bills in meter | yes | no | 1 | 1 |
| 44 | Send $100,000.00 bills in meter | no | no | no | 0 | 0 | Send $100,000.00 bills in meter | yes | no | 1 | 1 |
| 45 | Send $250.00 bills in meter | no | no | no | 0 | 0 | Send $250.00 bills in meter | yes | no | 1 | 1 |
| 46 | Send credit amount of all bills accepted | no | no | no | 0 | 0 | Send credit amount of all bills accepted | yes | no | 1 | 1 |
| 47 | Send coin amount accepted from external coin acceptor | no | no | no | 0 | 0 | Send coin amount accepted from external coin acceptor | yes | no | 1 | 1 |
| 48 | Send last accepted bill information | no | no | no | 0 | 0 | Send last accepted bill information | yes | no | 3 | 69 |
| 49 | Send number of bills currently in stacker | no | no | no | 0 | 0 | Send number of bills currently in stacker | yes | no | 1 | 1 |
| 4A | Send credit amount of all bills currently in stacker | no | no | no | 0 | 0 | Send credit amount of all bills currently in stacker | yes | no | 1 | 1 |
| 4C | Set secure enhanced validation ID | yes | no | no | 2 | 2 | Set secure enhanced validation ID | yes | no | 2 | 2 |
| 4D | Send enhanced validation information | yes | no | no | 1 | 1 | Send enhanced validation information | yes | no | 10 | 23 |
| 4F | Send current hopper status | no | no | no | 0 | 0 | Send current hopper status | yes | yes | 3 | 14 |
| 50 | Send validation meters | yes | no | no | 1 | 14 | Send validation meters | yes | no | 3 | 16 |
| 51 | Send total number of games implemented | no | no | no | 0 | 0 | Send total number of games implemented | yes | no | 1 | 1 |
| 52 | Send game N meters | yes | no | yes | 1 | 1 | Send game N meters | yes | no | 5 | 5 |
| 53 | Send game N configuration | yes | no | yes | 1 | 1 | Send game N configuration | yes | no | 9 | 37 |
| 54 | Send SAS version ID and EGM serial number | no | no | no | 0 | 0 | Send SAS version ID and EGM serial number | yes | yes | 2 | 2 |
| 55 | Send selected game number | no | no | no | 0 | 0 | Send selected game number | yes | no | 1 | 1 |
| 56 | Send enabled game numbers | no | no | no | 0 | 0 | Send implemented game numbers | yes | yes | 11 | 21 |
| 57 | Send pending cashout information | no | no | no | 0 | 0 | Send pending cashout information | yes | no | 2 | 6 |
| 58 | Receive validation number | yes | no | no | 2 | 2 | Receive validation number | yes | no | 1 | 5 |
| 62 | Transfer non-cashable credits to GM | yes | no | no | 3 | 3 | Transfer non-cashable credits to GM | yes | no | 4 | 17 |
| 63 | Transfer promotional credits to GM | yes | no | no | 3 | 3 | Transfer promotional credits to GM | yes | no | 4 | 17 |
| 64 | Transfer credits to host | yes | no | no | 2 | 2 | Transfer credits to host | yes | no | 3 | 16 |
| 65 | Force cashable credits out of GM | yes | no | no | 2 | 2 | Force cash credits out of GM | yes | no | 3 | 16 |
| 66 | Send last cashout amount | yes | no | no | 1 | 1 | Send last cashout amount | yes | no | 3 | 16 |
| 67 | Transfer win credits to host | yes | no | no | 2 | 2 | Transfer win credits to host | yes | no | 3 | 16 |
| 69 | Transfer cashable credits to GM | yes | no | no | 3 | 3 | Transfer cashable credits to GM | yes | no | 4 | 17 |
| 6A | Send available EFT Transfers | no | no | no | 0 | 0 | Send available EFT meters | no | no | 2 | 6 |
| 6B | Transfer promotional credits to host | yes | no | no | 2 | 2 | Transfer promotional credits to host | yes | no | 3 | 16 |
| 6E | Send authentication info | yes | yes | no | 3 | 29 | Send authentication info | yes | yes | 4 | 48 |
| 6F | Send extended meters for game N | yes | yes | yes | 2 | 2 | Send extended meters for game N | yes | yes | 2 | 2 |
| 70 | Send ticket validation data | no | no | no | 0 | 0 | Send ticket validation data | yes | yes | 2 | 9 |
| 71 | Redeem ticket | yes | yes | no | 2 | 24 | Redeem ticket | yes | yes | 2 | 26 |
| 72 | AFT transfer funds | yes | yes | no | 2 | 47 | AFT transfer funds | yes | yes | 3 | 80 |
| 73 | AFT register gaming machine | yes | yes | no | 2 | 11 | AFT register gaming machine | yes | yes | 4 | 9 |
| 74 | AFT lock and status request | yes | no | no | 3 | 15 |  AFT lock and status request | yes | yes | 12 | 50 |
| 75 | Set AFT receipt data | yes | yes | no | 11 | 149 | **(none)** | - | - | - | - |
| 76 | Set custom AFT ticket data | yes | yes | no | 6 | 48 | Set custom AFT ticket data | yes | yes | 5 | 40 |
| 7B | Extended validation status | yes | yes | no | 4 | 32 | Extended validation status | yes | yes | 4 | 18 |
| 7C | Set extended ticket data | yes | yes | no | 5 | 45 | Set extended ticket data | yes | no | 1 | 1 |
| 7D | Set ticket data | yes | yes | no | 1 | 7 | Set ticket data | yes | no | 1 | 1 |
| 7E | Send current date and time | no | no | no | 0 | 0 | Send current date and time | yes | no | 2 | 2 |
| 7F | Receive date and time | yes | no | no | 2 | 2 | **(none)** | - | - | - | - |
| 80 | Send progressive amount | yes | no | no | 3 | 3 | **(none)** | - | - | - | - |
| 83 | Send cumulative progressive wins | yes | no | yes | 1 | 1 | Send cumulative progressive wins | yes | no | 2 | 2 |
| 84 | Send progressive win amount | no | no | no | 0 | 0 | Send progressive win amount | yes | no | 3 | 3 |
| 85 | Send SAS progressive win amount | no | no | no | 0 | 0 | Send SAS progressive win amount | yes | no | 3 | 3 |
| 86 | Receive multiple progressive levels | yes | yes | no | 22 | 60 | **(none)** | - | - | - | - |
| 87 | Send multiple SAS progressive win amounts | no | no | no | 0 | 0 | Send multiple SAS progressive win amounts | yes | yes | 12 | 32 |
| 8A | Initiate legacy bonus | yes | no | no | 2 | 6 | **(none)** | - | - | - | - |
| 8B | Initiate multiplied jackpot mode | yes | no | no | 5 | 22 | **(none)** | - | - | - | - |
| 8C | Enter/exit tournament mode | yes | no | yes | 5 | 5 | **(none)** | - | - | - | - |
| 8E | Send card information | no | no | no | 0 | 0 | Send card information | yes | no | 6 | 6 |
| 8F | Send physical reel stop information | no | no | no | 0 | 0 | Send physical reel stop information | yes | no | 9 | 9 |
| 90 | Send legacy bonus win amount | no | no | no | 0 | 0 | Send legacy bonus win amount | yes | no | 4 | 8 |
| 94 | Remote handpay reset | yes | no | no | 0 | 0 | Remote handpay reset | yes | no | 1 | 5 |
| 95 | Send tournament games played | yes | no | yes | 1 | 1 | Send tournament games played | yes | no | 2 | 2 |
| 96 | Send tournament games won | yes | no | yes | 1 | 1 | Send tournament games won | yes | no | 2 | 2 |
| 97 | Send tournament credits wagered | yes | no | yes | 1 | 1 | Send tournament credits wagered | yes | no | 2 | 2 |
| 98 | Send tournament credits won | yes | no | yes | 1 | 1 | Send tournament credits won | yes | no | 2 | 2 |
| 99 | Send tournament meters | yes | no | yes | 1 | 1 | Send meters 95 through 98 | yes | no | 5 | 5 |
| 9A | Send legacy bonus meters | yes | no | yes | 1 | 1 | Send legacy bonus meters | yes | no | 4 | 4 |
| A0 | Send enabled features | yes | no | yes | 1 | 1 | Send enabled features | yes | no | 5 | 45 |
| A4 | Send cash out limit | yes | no | yes | 1 | 1 | Send cash out limit | yes | no | 2 | 2 |
| A8 | Enable jackpot handpay reset method | yes | no | no | 1 | 4 | Enable jackpot handpay reset method | yes | no | 1 | 5 |
| AA | Enable/disable game auto rebet | yes | no | no | 1 | 1 | **(none)** | - | - | - | - |
| AF | Send extended meters for game N (alt) | yes | yes | yes | 2 | 2 | Send extended meters for game N (alt) | yes | yes | 2 | 2 |
| B1 | Send current player denomination | no | no | no | 0 | 0 | Send current player denomination | yes | no | 1 | 29 |
| B2 | Send enabled player denominations | no | no | no | 0 | 0 | Send enabled player denominations | yes | yes | 11 | 301 |
| B3 | Send token denomination | no | no | no | 0 | 0 | Send token denomination | yes | no | 1 | 29 |
| B4 | Send wager category information | yes | no | yes | 2 | 2 | Send wager category information | yes | yes | 4 | 4 |
| B5 | Send extended game N information | yes | no | yes | 1 | 1 | Send extended game N information | yes | yes | 8 | 376 |
| B6 | Meter Collection Status | yes | yes | no | 1 | 4 | Meter Collection Status | yes | yes | 1 | 4 |
| B7 | Set machine numbers | yes | yes | no | 2 | 2 | Set machine numbers | yes | yes | 3 | 7 |

## poll-only cmds (21): 01, 02, 03, 04, 05, 06, 07, 08, 09, 0A, 0B, 0E, 2E, 75, 7F, 80, 86, 8A, 8B, 8C, AA

## response-only cmds (0): 

## Events (116)

| code | name | #direct | #total |
|---|---|---|---|
| 0 | No Activity | 0 | 0 |
| 11 | Slot door was opened | 0 | 0 |
| 12 | Slot door was closed | 0 | 0 |
| 13 | Drop door was opened | 0 | 0 |
| 14 | Drop door was closed | 0 | 0 |
| 15 | Card cage was opened | 0 | 0 |
| 16 | Card cage was closed | 0 | 0 |
| 17 | AC power was applied to EGM | 0 | 0 |
| 18 | AC power was lost from EGM | 0 | 0 |
| 19 | Cashbox door was opened | 0 | 0 |
| 1A | Cashbox door was closed | 0 | 0 |
| 1B | Cashbox was removed | 0 | 0 |
| 1C | Cashbox was installed | 0 | 0 |
| 1D | Belly door was opened | 0 | 0 |
| 1E | Belly door was closed | 0 | 0 |
| 1F | No activity, waiting for user input | 0 | 0 |
| 20 | General tilt | 0 | 0 |
| 21 | Coin in tilt | 0 | 0 |
| 22 | Coin out tilt | 0 | 0 |
| 23 | Hopper empty detected | 0 | 0 |
| 24 | Extra coin paid | 0 | 0 |
| 25 | Diverter malfunction | 0 | 0 |
| 27 | Cashbox full detected | 0 | 0 |
| 28 | Bill jam | 0 | 0 |
| 29 | Bill acceptor hardware failure | 0 | 0 |
| 2A | Reverse bill detected | 0 | 0 |
| 2B | Bill rejected | 0 | 0 |
| 2C | Counterfeit bill detected | 0 | 0 |
| 2D | Reverse coin in detected | 0 | 0 |
| 31 | CMOS RAM error (data recovered from EEPROM) | 0 | 0 |
| 32 | CMOS RAM error (no data recovered from EEPROM) | 0 | 0 |
| 33 | CMOS RAM error (bad device) | 0 | 0 |
| 34 | EEPROM error (data error) | 0 | 0 |
| 35 | EEPROM error (bad device) | 0 | 0 |
| 36 | EEPROM error (different checksum - version changed) | 0 | 0 |
| 37 | EEPROM error (bad checksum compare) | 0 | 0 |
| 38 | Partitioned EEPROM error (checksum - version changed) | 0 | 0 |
| 39 | Partitioned EEPROM error (bad checksum compare) | 0 | 0 |
| 3A | Memory error reset | 0 | 0 |
| 3B | Low backup battery detected | 0 | 0 |
| 3C | Operator changed options | 0 | 0 |
| 3D | A cashout ticket has been printed | 0 | 0 |
| 3E | A handpay has been validated | 0 | 0 |
| 3F | Validation ID not configured | 0 | 0 |
| 40 | Reel Tilt (which reel not specified) | 0 | 0 |
| 41 | Reel 1 tilt | 0 | 0 |
| 42 | Reel 2 tilt | 0 | 0 |
| 43 | Reel 3 tilt | 0 | 0 |
| 44 | Reel 4 tilt | 0 | 0 |
| 45 | Reel 5 tilt | 0 | 0 |
| 46 | Reel mechanism disconnected | 0 | 0 |
| 47 | $1.00 bill accepted | 0 | 0 |
| 48 | $5.00 bill accepted | 0 | 0 |
| 49 | $10.00 bill accepted | 0 | 0 |
| 4A | $20.00 bill accepted | 0 | 0 |
| 4B | $50.00 bill accepted | 0 | 0 |
| 4C | $100.00 bill accepted | 0 | 0 |
| 4D | $2.00 bill accepted | 0 | 0 |
| 4E | $500.00 bill accepted | 0 | 0 |
| 4F | Bill Accepted | 3 | 69 |
| 50 | $200.00 bill accepted | 0 | 0 |
| 51 | Handpay is pending | 0 | 0 |
| 52 | Handpay was reset | 0 | 0 |
| 53 | No progressive information has been received for 5 seconds | 0 | 0 |
| 54 | Progressive win | 0 | 0 |
| 55 | Player has cancelled the handpay request | 0 | 0 |
| 56 | SAS progressive level hit | 0 | 0 |
| 57 | System validation request | 0 | 0 |
| 60 | Printer communication error | 0 | 0 |
| 61 | Printer paper out error | 0 | 0 |
| 66 | Cash out button pressed | 0 | 0 |
| 67 | Ticket has been inserted | 0 | 0 |
| 68 | Ticket transfer complete | 0 | 0 |
| 69 | AFT transfer complete | 0 | 0 |
| 6A | AFT request for host cashout | 0 | 0 |
| 6B | AFT request for host to cash out win | 0 | 0 |
| 6C | AFT request to register | 0 | 0 |
| 6D | AFT registration acknowledged | 0 | 0 |
| 6E | AFT registration cancelled | 0 | 0 |
| 6F | Game locked | 0 | 0 |
| 70 | Exception buffer overflow | 0 | 0 |
| 71 | Change lamp on | 0 | 0 |
| 72 | Change lamp off | 0 | 0 |
| 74 | Printer paper low | 0 | 0 |
| 75 | Printer power off | 0 | 0 |
| 76 | Printer power on | 0 | 0 |
| 77 | Replace printer ribbon | 0 | 0 |
| 78 | Printer carriage jammed | 0 | 0 |
| 79 | Coin in lockout malfunction | 0 | 0 |
| 7A | Gaming machine soft meters reset to zero | 0 | 0 |
| 7B | Bill validator (period) total have been reset by an attendant/operator | 0 | 0 |
| 7C | Legacy Bonus Awarded | 4 | 4 |
| 7E | Game Start | 4 | 38 |
| 7F | Game End | 1 | 1 |
| 80 | Hopper full detected | 0 | 0 |
| 81 | Hopper level low detected | 0 | 0 |
| 82 | Display meters or attendant menu has been entered | 0 | 0 |
| 83 | Display meters or attendant menu has been exited | 0 | 0 |
| 84 | Self test or operator menu has been entered | 0 | 0 |
| 85 | Self test or operator menu has been exited | 0 | 0 |
| 86 | Gaming machine is out of service | 0 | 0 |
| 87 | Player has requested draw cards | 0 | 0 |
| 88 | Reel N Stopped | 2 | 2 |
| 89 | Coin/credit wagered | 0 | 0 |
| 8A | Game Recall Entry Displayed | 2 | 2 |
| 8B | Card Held/Not Held | 1 | 1 |
| 8C | Game Selected | 1 | 1 |
| 8E | Component list changed | 0 | 0 |
| 8F | Authentication complete | 0 | 0 |
| 98 | Power off card cage access | 0 | 0 |
| 99 | Power off slot door access | 0 | 0 |
| 9A | Power off cashbox door access | 0 | 0 |
| 9B | Power off drop door access | 0 | 0 |
| A0 | Meter change pending | 0 | 0 |
| A1 | Meter change cancelled | 0 | 0 |
| A2 | Enabled games/denoms changed | 0 | 0 |
