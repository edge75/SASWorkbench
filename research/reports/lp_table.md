| Line | Code | Description | Len | Trans | ACK | Type | Status | CRC | #Boxes | Input boxes (prompt@bytes) |
|---|---|---|---|---|---|---|---|---|---|---|
| 316 | $01 | Shutdown (lock out play) | n | n | n | n | n | Y | 0 | - |
| 318 | $02 | Startup (enable play) | n | n | n | n | n | Y | 0 | - |
| 320 | $03 | Sound off (all sounds disabled) | n | n | n | n | n | Y | 0 | - |
| 322 | $04 | Sound on (all sounds enabled) | n | n | n | n | n | Y | 0 | - |
| 324 | $05 | Reel spin sounds disabled | n | n | n | n | n | Y | 0 | - |
| 326 | $06 | Enable bill acceptor | n | n | n | n | n | Y | 0 | - |
| 328 | $07 | Disable bill acceptor | n | n | n | n | n | Y | 0 | - |
| 330 | $08 | Configure bill denominations | n | n | n | n | n | Y | 2 ,b | Enter bill denomination bitmap (FFFFFF00):@4; Enter bill acceptor action flag 00 or 01:@1 |
| 334 | $09 | Enable/disable game n | n | n | n | n | n | Y | 2 | Enter the game number@2; Enter 01-Enable or 00-Disable@1 |
| 338 | $0A | Enter maintenance mode | n | n | n | n | n | Y | 0 | - |
| 340 | $0B | Exit maintenance mode | n | n | n | n | n | Y | 0 | - |
| 342 | $0C | Set sound volume | n | n | n | n | n | Y | 1 | Enter the volume level@4 |
| 345 | $0D | Play sound | n | n | n | n | n | Y | 1 | Enter the sound number@4 |
| 348 | $0E | Enable/disable real time event reporting | n | n | n | n | n | Y | 1 | Enter 01-Enable or 00-Disable@1 |
| 351 | $0F | Send meters $10 through $15 | n | n | n | n | n | n | 0 | - |
| 353 | $10 | Send total cancelled credits meter | n | n | n | n | n | n | 0 | - |
| 355 | $11 | Send total coin in meter | n | n | n | n | n | n | 0 | - |
| 357 | $12 | Send total coin out meter | n | n | n | n | n | n | 0 | - |
| 359 | $13 | Send total drop meter | n | n | n | n | n | n | 0 | - |
| 361 | $14 | Send total jackpot meter | n | n | n | n | n | n | 0 | - |
| 363 | $15 | Send games played meter | n | n | n | n | n | n | 0 | - |
| 365 | $16 | Send games won meter | n | n | n | n | n | n | 0 | - |
| 367 | $17 | Send games lost meter | n | n | n | n | n | n | 0 | - |
| 369 | $18 | Send games since last power up and door closure | n | n | n | n | n | n | 0 | - |
| 371 | $19 | Send Meters $11 through $15 | n | n | n | n | n | n | 0 | - |
| 373 | $1A | Send current credits | n | n | n | n | n | n | 0 | - |
| 375 | $1B | Send hand pay information | n | n | n | n | n | n | 0 | - |
| 377 | $1C | Send meters | n | n | n | n | n | n | 0 | - |
| 379 | $1E | Send bill meters (# of bills) | n | n | n | n | n | n | 0 | - |
| 381 | $1F | Send gaming machine ID & information | n | n | n | n | n | n | 0 | - |
| 383 | $20 | Send dollar value of bills meter | n | n | n | n | n | n | 0 | - |
| 385 | $21 | ROM signature verification | n | n | n | n | n | y | 1 | Enter 2 byte seed@2 |
| 388 | $27 | Send current restricted promotional credits | n | n | n | n | n | n | 0 | - |
| 390 | $2A | Send true coin in meter (coins) | n | n | n | n | n | n | 0 | - |
| 392 | $2B | Send true coin out meter (coins) | n | n | n | n | n | n | 0 | - |
| 394 | $2C | Send current hopper level (coins) | n | n | n | n | n | n | 0 | - |
| 396 | $2D | Send total hand paid cancelled credits | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 399 | $2E | Delay the gaming machine | n | n | n | n | n | y | 1 | Enter BINARY delay time in units of 100ms@2 |
| 402 | $2F | Send selected meters for game n | y | n | n | n | n | y | 2 | Enter the game number (0000 = machine)@2; Enter the meter number (hex)@1 |
| 406 | $31 | Send $1.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 408 | $32 | Send $2.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 410 | $33 | Send $5.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 412 | $34 | Send $10.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 414 | $35 | Send $20.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 416 | $36 | Send $50.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 418 | $37 | Send $100.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 420 | $38 | Send $500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 422 | $39 | Send $1000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 424 | $3A | Send $200.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 426 | $3B | Send $25.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 428 | $3C | Send $2000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 430 | $3D | Send cash out ticket information | n | n | n | n | n | n | 0 | - |
| 432 | $3E | Send $2500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 434 | $3F | Send $5000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 436 | $40 | Send $10000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 438 | $41 | Send $20000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 440 | $42 | Send $25000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 442 | $43 | Send $50000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 444 | $44 | Send $100000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 446 | $45 | Send $250.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 448 | $46 | Send credit amount of all bills accepted | n | n | n | n | n | n | 0 | - |
| 450 | $47 | Send coin amount from an external coin acceptor | n | n | n | n | n | n | 0 | - |
| 452 | $48 | Send last accepted bill information | n | n | n | n | n | n | 0 | - |
| 454 | $49 | Send number of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| 456 | $4A | Send credit amt of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| 458 | $4C | Set enhanced validation ID | n | n | n | n | n | y | 2 | Enter the machine ID (binary)@3; Enter starting sequence number (binary)@3 |
| 462 | $4D | Send enhanced validation info | n | n | n | n | n | y | 1 | Enter buffer index (00=current info)@1 |
| 465 | $4F | Send current hopper status | n | n | n | n | n | n | 0 | - |
| 467 | $50 | Send validation meters | n | n | n | n | n | y | 1 | Enter validation type@1 |
| 482 | $51 | Send total number of games implemented | n | n | n | n | n | n | 0 | - |
| 484 | $52 | Send game n meters | n | n | n | n | n | Y | 1 | Enter the game number (0000 = machine)@2 |
| 487 | $53 | Send game n configuration | n | n | n | n | n | Y | 1 | Enter the game number (0000 = machine)@2 |
| 490 | $54 | Send SAS version and machine serial number | n | n | n | n | n | n | 0 | - |
| 492 | $55 | Send selected game number | n | n | n | n | n | n | 0 | - |
| 494 | $56 | Send enabled game numbers | n | n | n | n | n | n | 0 | - |
| 496 | $57 | Send pending cashout info | n | n | n | n | n | n | 0 | - |
| 498 | $58 | Receive validation number | n | n | n | n | n | y | 2 | Enter validation system ID@1; Enter validation number@8 |
| 502 | $6F | Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | Game number (0000 = machine)@2; Meter code 1 (Table C-7)@2; Meter code 2 (Table C-7)@2 |
| 507 | $6F | Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | Game number (0000 = machine)@2; Meter code (Table C-7)@2 |
| 511 | $70 | Send ticket validation data | n | n | n | n | n | n | 0 | - |
| 513 | $71 | Redeem ticket | y | n | n | n | n | y | 4 | Enter transfer code@1; Enter transfer amount@5; Enter parsing code@1; Enter validation number@9 |
| 519 | $71 | Redeem ticket interrogate | y | n | n | n | n | y | 1 | Enter transfer code@1 |
| 522 | $73 | AFT register gaming machine | y | n | n | n | n | y | 4 | Enter registration code@1; Enter asset number (binary)@4; Enter registration key@20; Enter POS ID@4 |
| 528 | $73 | AFT register interrogate | y | n | n | n | n | y | 1 | Enter registration code@1 |
| 531 | $74 | AFT game lock and status request | n | n | n | n | n | y | 3 | Enter lock code@1; Enter lock condition@1; Enter lock timeout@2 |
| 536 | $76 | Set custom AFT ticket data | y | n | n | n | n | y | 3 | Enter function@1; Enter data code@1; Enter data@0 |
| 541 | $7B | Extended validation status | y | n | n | n | n | y | 4 | Enter control mask (binary)@2; Enter control states (binary)@2; Enter cashable expiration@2; Enter restricted expiration@2 |
| 547 | $7C | Set extended ticket data | y | n | n | n | n | y | 2 | Enter data code@1; Enter data@0 |
| 551 | $7D | Set ticket data | y | n | n | n | n | y | 5 | Enter host ID@2; Enter expiration in number of days@1; Enter location data@0; Enter address1 data@0; Enter address2 data@0 |
| 558 | $7E | Send current date and time | n | n | n | n | n | n | 0 | - |
| 560 | $7F | Set date and time | n | n | n | n | n | Y | 2 | Enter the date (MMDDYYYY)@4; Enter the time (HHMMSS)@3 |
| 564 | $83 | Send cumulative progressive wins | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 567 | $84 | Send progressive win amount | n | n | n | n | n | n | 0 | - |
| 569 | $85 | Send SAS progressive win amount | n | n | n | n | n | n | 0 | - |
| 571 | $87 | Send multiple SAS progressive win amounts | n | n | n | n | n | n | 0 | - |
| 573 | $8A | Initiate a legacy bonus pay | n | n | n | n | n | y | 2 | Enter the amount of the bonus@4; 00 deduct/01 non-deduct/02 wagermatch@1 |
| 577 | $8B | Initiate multiplied jackpot mode | n | n | n | n | n | y | 5 | Enter the minimum win@4; Enter the maximum win@4; Enter the multiplier (hex 01-0A)@1; Enter 00-enable or 01-disable@1; Enter 00-all wagers or 01-max bet only@1 |
| 584 | $8C | Enter/exit tournament mode | n | n | n | n | n | y | 5 | Enter the game number (0000 = machine)@2; Enter time minutes@1; Enter time seconds@1; Enter credits@4; 00-disable or 01-enable tournament pulses@1 |
| 591 | $8E | Send card information | n | n | n | n | n | n | 0 | - |
| 593 | $8F | Send reel stop information | n | n | n | n | n | n | 0 | - |
| 595 | $90 | Send legacy bonus win amount | n | n | n | n | n | n | 0 | - |
| 597 | $94 | Reset hand pay | n | n | n | n | n | y | 0 | - |
| 599 | $95 | Send tournament games played | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 602 | $96 | Send tournament games won | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 605 | $97 | Send tournament credits wagered | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 608 | $98 | Send tournament credits won | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 611 | $99 | Send tournament meters 95 through 98 | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 614 | $9A | Send legacy bonus meters | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 617 | $A0 | Send enabled features | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 620 | $A4 | Send the cash out limit | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 623 | $A8 | Enable jackpot reset method | n | n | n | n | n | Y | 1 | 00-Standard or 01-Reset to credit meter@1 |
| 626 | $AA | Enable/disable auto rebet | n | n | n | n | n | Y | 1 | 00-Disable or 01-Enable@1 |
| 629 | $AF | Alt Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | Game number (0000 = machine)@2; Meter code 1 (Table C-7)@2; Meter code 2 (Table C-7)@2 |
| 634 | $AF | Alt Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | Game number (0000 = machine)@2; Meter code (Table C-7)@2 |
| 638 | $B1 | Send current player denomination | n | n | n | n | n | n | 0 | - |
| 640 | $B2 | Send enabled player denominations | n | n | n | n | n | n | 0 | - |
| 642 | $B3 | Send token denomination | n | n | n | n | n | n | 0 | - |
| 644 | $B4 | Send wager category information | n | n | n | n | n | y | 2 | Enter the game number (0000 = machine)@2; Enter the wager category (0000 = total coin in)@2 |
| 648 | $B5 | Send extended game n information | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 651 | $B6 | Meter collect status | y | n | n | n | n | y | 1 | 00=Ack 01=Ready 80=Unable@1 |
| 654 | $B7 | Set machine numbers | y | n | n | n | n | y | 2 | Enter asset number (binary)@4; Enter floor location (ASCII)@0 |
| 658 | $FD | dummy | n | n | n | n | n | n | 0 | - |
