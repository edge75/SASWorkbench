| Code | Description | LEN | TRN | ACK | TYP | STS | CRC | #Box | Input boxes (`prompt` @bytes) |
|---|---|---|---|---|---|---|---|---|---|
| `$01` | Shutdown (lock out play) | n | n | n | n | n | Y | 0 | - |
| `$02` | Startup (enable play) | n | n | n | n | n | Y | 0 | - |
| `$03` | Sound off (all sounds disabled) | n | n | n | n | n | Y | 0 | - |
| `$04` | Sound on (all sounds enabled) | n | n | n | n | n | Y | 0 | - |
| `$05` | Reel spin sounds disabled | n | n | n | n | n | Y | 0 | - |
| `$06` | Enable bill acceptor | n | n | n | n | n | Y | 0 | - |
| `$07` | Disable bill acceptor | n | n | n | n | n | Y | 0 | - |
| `$08` | Configure bill denominations **(extra field `b`)** | n | n | n | n | n | Y | 2 | `Enter bill denomination bitmap (FFFFFF00):` @4 <br> `Enter bill acceptor action flag 00 or 01:` @1 |
| `$09` | Enable/disable game n | n | n | n | n | n | Y | 2 | `Enter the game number` @2 <br> `Enter 01-Enable or 00-Disable` @1 |
| `$0A` | Enter maintenance mode | n | n | n | n | n | Y | 0 | - |
| `$0B` | Exit maintenance mode | n | n | n | n | n | Y | 0 | - |
| `$0C` | Set sound volume | n | n | n | n | n | Y | 1 | `Enter the volume level` @4 |
| `$0D` | Play sound | n | n | n | n | n | Y | 1 | `Enter the sound number` @4 |
| `$0E` | Enable/disable real time event reporting | n | n | n | n | n | Y | 1 | `Enter 01-Enable or 00-Disable` @1 |
| `$0F` | Send meters $10 through $15 | n | n | n | n | n | n | 0 | - |
| `$10` | Send total cancelled credits meter | n | n | n | n | n | n | 0 | - |
| `$11` | Send total coin in meter | n | n | n | n | n | n | 0 | - |
| `$12` | Send total coin out meter | n | n | n | n | n | n | 0 | - |
| `$13` | Send total drop meter | n | n | n | n | n | n | 0 | - |
| `$14` | Send total jackpot meter | n | n | n | n | n | n | 0 | - |
| `$15` | Send games played meter | n | n | n | n | n | n | 0 | - |
| `$16` | Send games won meter | n | n | n | n | n | n | 0 | - |
| `$17` | Send games lost meter | n | n | n | n | n | n | 0 | - |
| `$18` | Send games since last power up and door closure | n | n | n | n | n | n | 0 | - |
| `$19` | Send Meters $11 through $15 | n | n | n | n | n | n | 0 | - |
| `$1A` | Send current credits | n | n | n | n | n | n | 0 | - |
| `$1B` | Send hand pay information | n | n | n | n | n | n | 0 | - |
| `$1C` | Send meters | n | n | n | n | n | n | 0 | - |
| `$1E` | Send bill meters (# of bills) | n | n | n | n | n | n | 0 | - |
| `$1F` | Send gaming machine ID & information | n | n | n | n | n | n | 0 | - |
| `$20` | Send dollar value of bills meter | n | n | n | n | n | n | 0 | - |
| `$21` | ROM signature verification | n | n | n | n | n | y | 1 | `Enter 2 byte seed` @2 |
| `$27` | Send current restricted promotional credits | n | n | n | n | n | n | 0 | - |
| `$2A` | Send true coin in meter (coins) | n | n | n | n | n | n | 0 | - |
| `$2B` | Send true coin out meter (coins) | n | n | n | n | n | n | 0 | - |
| `$2C` | Send current hopper level (coins) | n | n | n | n | n | n | 0 | - |
| `$2D` | Send total hand paid cancelled credits | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$2E` | Delay the gaming machine | n | n | n | n | n | y | 1 | `Enter BINARY delay time in units of 100ms` @2 |
| `$2F` | Send selected meters for game n | y | n | n | n | n | y | 2 | `Enter the game number (0000 = machine)` @2 <br> `Enter the meter number (hex)` @1 |
| `$31` | Send $1.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$32` | Send $2.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$33` | Send $5.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$34` | Send $10.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$35` | Send $20.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$36` | Send $50.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$37` | Send $100.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$38` | Send $500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$39` | Send $1000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$3A` | Send $200.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$3B` | Send $25.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$3C` | Send $2000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$3D` | Send cash out ticket information | n | n | n | n | n | n | 0 | - |
| `$3E` | Send $2500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$3F` | Send $5000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$40` | Send $10000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$41` | Send $20000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$42` | Send $25000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$43` | Send $50000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$44` | Send $100000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$45` | Send $250.00 bills in meter | n | n | n | n | n | n | 0 | - |
| `$46` | Send credit amount of all bills accepted | n | n | n | n | n | n | 0 | - |
| `$47` | Send coin amount from an external coin acceptor | n | n | n | n | n | n | 0 | - |
| `$48` | Send last accepted bill information | n | n | n | n | n | n | 0 | - |
| `$49` | Send number of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| `$4A` | Send credit amt of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| `$4C` | Set enhanced validation ID | n | n | n | n | n | y | 2 | `Enter the machine ID (binary)` @3 <br> `Enter starting sequence number (binary)` @3 |
| `$4D` | Send enhanced validation info | n | n | n | n | n | y | 1 | `Enter buffer index (00=current info)` @1 |
| `$4F` | Send current hopper status | n | n | n | n | n | n | 0 | - |
| `$50` | Send validation meters | n | n | n | n | n | y | 1 | `Enter validation type` @1 <br> _legend:_ 00 = Cashable ticket, no lockup ; 01 = Promotional ticket ; 02 = Cashable ticket from AFT ; 03 = Promotional ticket from AFT ; 04 = Debit ticket from AFT ; 10 = Handpay from cashout (receipt printed) ; 20 = Handpay from win (receipt printed) ; 40 = Handpay from cashout (no receipt) ; 60 = Handpay from win (no receipt) ; 80 = Cashable ticket redeemed ; 81 = Restricted promotional ticket redeemed ; 82 = Nonrestricted promotional ticket redeemed |
| `$51` | Send total number of games implemented | n | n | n | n | n | n | 0 | - |
| `$52` | Send game n meters | n | n | n | n | n | Y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$53` | Send game n configuration | n | n | n | n | n | Y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$54` | Send SAS version and machine serial number | n | n | n | n | n | n | 0 | - |
| `$55` | Send selected game number | n | n | n | n | n | n | 0 | - |
| `$56` | Send enabled game numbers | n | n | n | n | n | n | 0 | - |
| `$57` | Send pending cashout info | n | n | n | n | n | n | 0 | - |
| `$58` | Receive validation number | n | n | n | n | n | y | 2 | `Enter validation system ID` @1 <br> `Enter validation number` @8 |
| `$6F` | Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | `Game number (0000 = machine)` @2 <br> `Meter code 1 (Table C-7)` @2 <br> `Meter code 2 (Table C-7)` @2 |
| `$6F` | Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | `Game number (0000 = machine)` @2 <br> `Meter code (Table C-7)` @2 |
| `$70` | Send ticket validation data | n | n | n | n | n | n | 0 | - |
| `$71` | Redeem ticket | y | n | n | n | n | y | 4 | `Enter transfer code` @1 <br> `Enter transfer amount` @5 <br> `Enter parsing code` @1 <br> `Enter validation number` @9 |
| `$71` | Redeem ticket interrogate | y | n | n | n | n | y | 1 | `Enter transfer code` @1 |
| `$73` | AFT register gaming machine | y | n | n | n | n | y | 4 | `Enter registration code` @1 <br> `Enter asset number (binary)` @4 <br> `Enter registration key` @20 <br> `Enter POS ID` @4 |
| `$73` | AFT register interrogate | y | n | n | n | n | y | 1 | `Enter registration code` @1 |
| `$74` | AFT game lock and status request | n | n | n | n | n | y | 3 | `Enter lock code` @1 <br> `Enter lock condition` @1 <br> `Enter lock timeout` @2 |
| `$76` | Set custom AFT ticket data | y | n | n | n | n | y | 3 | `Enter function` @1 <br> `Enter data code` @1 <br> `Enter data` @0 |
| `$7B` | Extended validation status | y | n | n | n | n | y | 4 | `Enter control mask (binary)` @2 <br> `Enter control states (binary)` @2 <br> `Enter cashable expiration` @2 <br> `Enter restricted expiration` @2 |
| `$7C` | Set extended ticket data | y | n | n | n | n | y | 2 | `Enter data code` @1 <br> `Enter data` @0 |
| `$7D` | Set ticket data | y | n | n | n | n | y | 5 | `Enter host ID` @2 <br> `Enter expiration in number of days` @1 <br> `Enter location data` @0 <br> `Enter address1 data` @0 <br> `Enter address2 data` @0 |
| `$7E` | Send current date and time | n | n | n | n | n | n | 0 | - |
| `$7F` | Set date and time | n | n | n | n | n | Y | 2 | `Enter the date (MMDDYYYY)` @4 <br> `Enter the time (HHMMSS)` @3 |
| `$83` | Send cumulative progressive wins | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$84` | Send progressive win amount | n | n | n | n | n | n | 0 | - |
| `$85` | Send SAS progressive win amount | n | n | n | n | n | n | 0 | - |
| `$87` | Send multiple SAS progressive win amounts | n | n | n | n | n | n | 0 | - |
| `$8A` | Initiate a legacy bonus pay | n | n | n | n | n | y | 2 | `Enter the amount of the bonus` @4 <br> `00 deduct/01 non-deduct/02 wagermatch` @1 |
| `$8B` | Initiate multiplied jackpot mode | n | n | n | n | n | y | 5 | `Enter the minimum win` @4 <br> `Enter the maximum win` @4 <br> `Enter the multiplier (hex 01-0A)` @1 <br> `Enter 00-enable or 01-disable` @1 <br> `Enter 00-all wagers or 01-max bet only` @1 |
| `$8C` | Enter/exit tournament mode | n | n | n | n | n | y | 5 | `Enter the game number (0000 = machine)` @2 <br> `Enter time minutes` @1 <br> `Enter time seconds` @1 <br> `Enter credits` @4 <br> `00-disable or 01-enable tournament pulses` @1 |
| `$8E` | Send card information | n | n | n | n | n | n | 0 | - |
| `$8F` | Send reel stop information | n | n | n | n | n | n | 0 | - |
| `$90` | Send legacy bonus win amount | n | n | n | n | n | n | 0 | - |
| `$94` | Reset hand pay | n | n | n | n | n | y | 0 | - |
| `$95` | Send tournament games played | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$96` | Send tournament games won | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$97` | Send tournament credits wagered | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$98` | Send tournament credits won | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$99` | Send tournament meters 95 through 98 | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$9A` | Send legacy bonus meters | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$A0` | Send enabled features | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$A4` | Send the cash out limit | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$A8` | Enable jackpot reset method | n | n | n | n | n | Y | 1 | `00-Standard or 01-Reset to credit meter` @1 |
| `$AA` | Enable/disable auto rebet | n | n | n | n | n | Y | 1 | `00-Disable or 01-Enable` @1 |
| `$AF` | Alt Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | `Game number (0000 = machine)` @2 <br> `Meter code 1 (Table C-7)` @2 <br> `Meter code 2 (Table C-7)` @2 |
| `$AF` | Alt Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | `Game number (0000 = machine)` @2 <br> `Meter code (Table C-7)` @2 |
| `$B1` | Send current player denomination | n | n | n | n | n | n | 0 | - |
| `$B2` | Send enabled player denominations | n | n | n | n | n | n | 0 | - |
| `$B3` | Send token denomination | n | n | n | n | n | n | 0 | - |
| `$B4` | Send wager category information | n | n | n | n | n | y | 2 | `Enter the game number (0000 = machine)` @2 <br> `Enter the wager category (0000 = total coin in)` @2 |
| `$B5` | Send extended game n information | n | n | n | n | n | y | 1 | `Enter the game number (0000 = machine)` @2 |
| `$B6` | Meter collect status | y | n | n | n | n | y | 1 | `00=Ack 01=Ready 80=Unable` @1 |
| `$B7` | Set machine numbers | y | n | n | n | n | y | 2 | `Enter asset number (binary)` @4 <br> `Enter floor location (ASCII)` @0 |
| `$FD` | dummy | n | n | n | n | n | n | 0 | - |
