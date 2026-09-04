object Form1: TForm1
  Left = 426
  Top = 264
  Width = 808
  Height = 646
  Caption = 'SAS Test Program'
  Color = clBtnFace
  Constraints.MinHeight = 480
  Constraints.MinWidth = 640
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -11
  Font.Name = 'MS Sans Serif'
  Font.Style = []
  Menu = MainMenu1
  OldCreateOrder = True
  Position = poScreenCenter
  ShowHint = True
  OnClose = FormClose
  OnCreate = FormCreate
  DesignSize = (800, 592)
  PixelsPerInch = 96
  TextHeight = 13
  object ResendStructButton: TButton
    Left = 344
    Top = 64
    Width = 121
    Height = 25
    Caption = 'Resend Last Message'
    TabOrder = 6
    OnClick = ResendStructButtonClick
  end
  object SendCC: TButton
    Left = 344
    Top = 96
    Width = 121
    Height = 25
    Caption = 'Send Custom Command'
    Enabled = False
    TabOrder = 7
    OnClick = SendCCClick
  end
  object ComboBox1: TComboBox
    Left = 0
    Top = 0
    Width = 345
    Height = 21
    ItemHeight = 13
    TabOrder = 0
    Text = 'Long Poll Commands'
    OnChange = ComboBox1Change
  end
  object ComboBox2: TComboBox
    Left = 0
    Top = 24
    Width = 345
    Height = 21
    ItemHeight = 13
    TabOrder = 1
    Text = 'Electronic Funds Transfer'
    OnChange = ComboBox2Change
  end
  object Edit1: TEdit
    Left = 0
    Top = 104
    Width = 281
    Height = 21
    TabOrder = 4
    Text = 'Custom Command'
    OnChange = Edit1Change
    OnKeyPress = Edit1KeyPress
  end
  object CRCBox: TCheckBox
    Left = 288
    Top = 104
    Width = 41
    Height = 17
    Caption = 'CRC'
    TabOrder = 5
  end
  object ReSend: TButton
    Left = 352
    Top = 0
    Width = 57
    Height = 17
    Caption = '<- Resend'
    Enabled = False
    TabOrder = 2
    OnClick = ReSendClick
  end
  object Resend2: TButton
    Left = 352
    Top = 24
    Width = 57
    Height = 17
    Caption = '<-Resend'
    Enabled = False
    TabOrder = 3
    OnClick = Resend2Click
  end
  object Button2: TButton
    Left = 0
    Top = 568
    Width = 89
    Height = 25
    Anchors = [akLeft, akBottom]
    Caption = 'Clear Msg. Disp'
    TabOrder = 9
    OnClick = Button2Click
  end
  object Button3: TButton
    Left = 88
    Top = 568
    Width = 97
    Height = 25
    Anchors = [akLeft, akBottom]
    Caption = 'Clear Tx/Rx Disp'
    TabOrder = 10
    OnClick = Button3Click
  end
  object Button6: TButton
    Left = 304
    Top = 568
    Width = 81
    Height = 25
    Anchors = [akLeft, akBottom]
    Caption = 'Stop Display'
    TabOrder = 13
    OnClick = Button6Click
  end
  object PageControl2: TPageControl
    Left = 0
    Top = 48
    Width = 289
    Height = 49
    ActivePage = TabSheet6
    TabIndex = 1
    TabOrder = 8
    object TabSheet5: TTabSheet
      Caption = 'Command Sets'
      object ComboBox5: TComboBox
        Left = 0
        Top = 0
        Width = 281
        Height = 21
        ItemHeight = 0
        TabOrder = 0
        Text = 'Command Sets'
        OnChange = ComboBox5Change
      end
    end
    object TabSheet6: TTabSheet
      Caption = 'Command Set Structure Editor'
      object ComboBox6: TComboBox
        Left = 0
        Top = 0
        Width = 273
        Height = 21
        ItemHeight = 13
        TabOrder = 0
        Text = 'Structures'
        OnChange = ComboBox6Change
      end
    end
  end
  object PageControl1: TPageControl
    Left = 501
    Top = 0
    Width = 296
    Height = 577
    ActivePage = TabSheet10
    BiDiMode = bdLeftToRight
    MultiLine = True
    ParentBiDiMode = False
    TabIndex = 8
    TabOrder = 14
    object TabSheet2: TTabSheet
      Caption = 'General Poll/Address'
      object : TLabel
        Left = 45
        Top = 192
        Width = 20
        Height = 13
        Caption = 'GPs'
      end
      object Label3: TLabel
        Left = 40
        Top = 8
        Width = 25
        Height = 13
        Caption = '40ms'
      end
      object Label4: TLabel
        Left = 40
        Top = 224
        Width = 14
        Height = 13
        Caption = 'Off'
      end
      object Label5: TLabel
        Left = 40
        Top = 208
        Width = 49
        Height = 13
        Caption = '2 seconds'
      end
      object Label16: TLabel
        Left = 96
        Top = 432
        Width = 6
        Height = 13
        Caption = '2'
      end
      object Label17: TLabel
        Left = 48
        Top = 432
        Width = 47
        Height = 13
        Caption = 'Poll up to '
      end
      object GPlab: TLabel
        Left = 10
        Top = 248
        Width = 57
        Height = 13
        Caption = 'Auto GP Off'
      end
      object TrackBar1: TTrackBar
        Left = 4
        Top = 0
        Width = 37
        Height = 241
        Hint = 'Move to Change General  poll rate'
        LineSize = 2
        Max = 2100
        Min = 20
        Orientation = trVertical
        ParentShowHint = False
        PageSize = 10
        Frequency = 100
        Position = 2100
        SelEnd = 0
        SelStart = 40
        ShowHint = False
        TabOrder = 0
        TickMarks = tmBottomRight
        TickStyle = tsAuto
        OnChange = TrackBar1Change
      end
      object Button9: TButton
        Left = 48
        Top = 132
        Width = 65
        Height = 20
        Caption = '200ms GPs'
        TabOrder = 1
        OnClick = Button9Click
      end
      object ms40: TButton
        Left = 48
        Top = 54
        Width = 65
        Height = 20
        Caption = '40 ms GPs'
        TabOrder = 2
        OnClick = ms40Click
      end
      object Button10: TButton
        Left = 48
        Top = 80
        Width = 65
        Height = 20
        Caption = '80 ms GPs'
        TabOrder = 3
        OnClick = Button10Click
      end
      object UpDown1: TUpDown
        Left = 46
        Top = 164
        Width = 64
        Height = 21
        Min = 40
        Max = 2000
        Increment = 5
        Orientation = udHorizontal
        Position = 2000
        TabOrder = 4
        Wrap = False
        OnClick = UpDown1Click
      end
      object Button11: TButton
        Left = 48
        Top = 28
        Width = 65
        Height = 20
        Caption = 'GPs OFF'
        TabOrder = 5
        OnClick = Button11Click
      end
      object GP100: TButton
        Left = 48
        Top = 106
        Width = 65
        Height = 20
        Caption = '150ms GP''s'
        TabOrder = 6
        OnClick = GP100Click
      end
      object SASAddress: TRadioGroup
        Left = 160
        Top = 8
        Width = 97
        Height = 441
        Caption = 'SAS Address'
        Columns = 2
        Items.Strings = ('1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20', '21', '22', '23', '24', '25', '26', '27', '28', '29', '30', '31', '32', '33', '34', '35', '36', '37', '38', '39', '40', '41', '42', '43', '44', '45', '46', '47', '48', '49', '50', '51', '52', '53', '54', '55', '56', '57', '58', '58', '60', '61', '62', '63', '64')
        TabOrder = 7
        OnClick = SASAddressClick
      end
      object CycleAddress: TCheckBox
        Left = 48
        Top = 448
        Width = 97
        Height = 17
        Caption = 'Cycle Address'
        TabOrder = 8
      end
      object UpDown3: TUpDown
        Left = 8
        Top = 432
        Width = 25
        Height = 33
        Hint = 'Sets the Max Address for Cycling'
        Min = 1
        Max = 64
        Position = 2
        TabOrder = 9
        Wrap = False
        OnClick = UpDown3Click
      end
      object ShowPhantoms: TCheckBox
        Left = 8
        Top = 280
        Width = 129
        Height = 17
        Hint = 'Display the General Polls and the Phantom Polls'
        Caption = 'Show Syncs / GPs'
        ParentShowHint = False
        ShowHint = True
        TabOrder = 10
      end
      object NoPhantom: TCheckBox
        Left = 8
        Top = 296
        Width = 81
        Height = 17
        Caption = 'No Sync (80)'
        Checked = True
        State = cbChecked
        TabOrder = 11
      end
      object NoSync: TCheckBox
        Left = 8
        Top = 400
        Width = 137
        Height = 17
        Caption = 'No Sync (with time slot)'
        TabOrder = 12
      end
      object WakeUpCheckBox: TCheckBox
        Left = 8
        Top = 312
        Width = 97
        Height = 17
        Hint = 'Unchecking this box disables wakeup bits '
        Caption = 'Wake Up Bits'
        Checked = True
        State = cbChecked
        TabOrder = 13
        OnClick = WakeUpCheckBoxClick
      end
      object GroupBox13: TGroupBox
        Left = 8
        Top = 336
        Width = 113
        Height = 57
        Caption = 'Comm Info'
        TabOrder = 14
        object Label7: TLabel
          Left = 5
          Top = 36
          Width = 54
          Height = 13
          Caption = 'Time Outs='
        end
        object Label6: TLabel
          Left = 64
          Top = 36
          Width = 6
          Height = 13
          Caption = '0'
        end
        object Label51: TLabel
          Left = 8
          Top = 20
          Width = 64
          Height = 13
          Caption = 'Bytes Read ='
        end
        object Label15: TLabel
          Left = 77
          Top = 20
          Width = 6
          Height = 13
          Caption = '0'
        end
      end
    end
    object TabSheet3: TTabSheet
      Caption = 'Quick Cmd'
      object ComboBox3: TListBox
        Left = 8
        Top = 8
        Width = 217
        Height = 457
        Style = lbOwnerDrawVariable
        ItemHeight = 13
        TabOrder = 0
        OnClick = ComboBox3Change
      end
    end
    object ScriptTab: TTabSheet
      Caption = 'Misc'
      object Label25: TLabel
        Left = 8
        Top = 424
        Width = 112
        Height = 13
        Caption = 'WU Delay Milliseconds '
      end
      object Button12: TButton
        Left = 0
        Top = 8
        Width = 137
        Height = 25
        Caption = 'Send Multiple Wakeup Bits'
        TabOrder = 0
        OnClick = Button12Click
      end
      object MWB0111: TButton
        Left = 0
        Top = 32
        Width = 137
        Height = 25
        Caption = 'Test MWB0111'
        TabOrder = 1
        OnClick = MWB0111Click
      end
      object GroupBox1: TGroupBox
        Left = 0
        Top = 64
        Width = 145
        Height = 105
        Caption = 'Last Command'
        TabOrder = 2
        object EditResend: TEdit
          Left = 0
          Top = 20
          Width = 137
          Height = 21
          TabOrder = 0
        end
        object RepeatButton: TButton
          Left = 8
          Top = 48
          Width = 121
          Height = 25
          Caption = 'Send Command'
          Enabled = False
          TabOrder = 1
          OnClick = RepeatButtonClick
        end
        object AddCRC: TCheckBox
          Left = 8
          Top = 80
          Width = 65
          Height = 17
          Caption = 'Add CRC'
          TabOrder = 2
        end
      end
      object ForceEFTACK: TCheckBox
        Left = 8
        Top = 264
        Width = 97
        Height = 17
        Caption = 'Force EFT ACK'
        TabOrder = 3
      end
      object GPButton: TButton
        Left = 82
        Top = 298
        Width = 71
        Height = 17
        Caption = 'Send GP'
        TabOrder = 4
        OnClick = GPButtonClick
      end
      object SendPhantom: TButton
        Left = 10
        Top = 298
        Width = 65
        Height = 17
        Caption = 'Re-Sync'
        TabOrder = 5
        OnClick = SendPhantomClick
      end
      object Disp1F: TCheckBox
        Left = 8
        Top = 336
        Width = 121
        Height = 17
        Hint = 'Display the $00(no activiy) or the $!F(waiting for user input)'
        Caption = 'Display Idle Activity'
        ParentShowHint = False
        ShowHint = True
        TabOrder = 6
      end
      object CRCErrors: TCheckBox
        Left = 8
        Top = 352
        Width = 121
        Height = 17
        Caption = 'Report CRC Errors'
        TabOrder = 7
      end
      object Respond51: TCheckBox
        Left = 8
        Top = 400
        Width = 225
        Height = 17
        Hint = 'Respond with $1B (Send Hand Pay Information) to a  $51 or $66 '
        Caption = 'Auto respond to req. for handpay info'
        Checked = True
        ParentShowHint = False
        ShowHint = True
        State = cbChecked
        TabOrder = 8
      end
      object Trans0: TCheckBox
        Left = 8
        Top = 368
        Width = 145
        Height = 17
        Hint = 'Place a "0" in the transaction field'
        Caption = '"0" the  Transaction field'
        TabOrder = 9
      end
      object EFTRespond: TCheckBox
        Left = 8
        Top = 384
        Width = 137
        Height = 13
        Hint = 'Respond with athe selected EFT transaction'
        Caption = 'Respond Selected EFT'
        ParentShowHint = False
        ShowHint = True
        TabOrder = 10
      end
      object CRCError: TCheckBox
        Left = 8
        Top = 248
        Width = 97
        Height = 17
        Caption = 'Force CRC Error'
        TabOrder = 11
      end
      object ComboBox7: TComboBox
        Left = 128
        Top = 424
        Width = 41
        Height = 21
        ItemHeight = 13
        TabOrder = 12
        Text = '1'
        OnChange = ComboBox7Change
        Items.Strings = ('1', '2', '3', '4', '5', '6', '7', '8', '9', '10')
      end
      object ComboBox4: TComboBox
        Left = 8
        Top = 456
        Width = 185
        Height = 21
        ItemHeight = 0
        TabOrder = 13
        Text = '$A5 ASDC Commands'
        OnChange = ComboBox4Change
      end
      object test: TButton
        Left = 200
        Top = 448
        Width = 65
        Height = 25
        Caption = 'test'
        TabOrder = 14
        OnClick = testClick
      end
      object NoEFTACK: TCheckBox
        Left = 8
        Top = 280
        Width = 97
        Height = 17
        Caption = 'No EFT ACK'
        TabOrder = 15
      end
      object GroupBox10: TGroupBox
        Left = 152
        Top = 8
        Width = 113
        Height = 89
        Caption = 'Priority'
        TabOrder = 16
        object Priority0: TButton
          Left = 8
          Top = 16
          Width = 97
          Height = 25
          Caption = 'Priority Normal'
          TabOrder = 0
          OnClick = Priority0Click
        end
        object Priority1: TButton
          Left = 8
          Top = 48
          Width = 97
          Height = 25
          Caption = 'Priority High'
          TabOrder = 1
          OnClick = Priority1Click
        end
      end
      object TimingCB: TCheckBox
        Left = 8
        Top = 320
        Width = 97
        Height = 17
        Caption = 'Monitor Timing'
        TabOrder = 17
      end
      object Button1: TButton
        Left = 200
        Top = 424
        Width = 65
        Height = 17
        Caption = 'Test Button'
        TabOrder = 18
        OnClick = Button1Click
      end
      object RTERGB: TGroupBox
        Left = 152
        Top = 104
        Width = 113
        Height = 65
        Caption = 'Real Time Event Rep'
        TabOrder = 19
        object SetRTER: TButton
          Left = 8
          Top = 32
          Width = 41
          Height = 25
          Caption = 'Set '
          TabOrder = 0
          OnClick = SetRTERClick
        end
        object ClrRTER: TButton
          Left = 64
          Top = 32
          Width = 41
          Height = 25
          Caption = 'Clear'
          TabOrder = 1
          OnClick = ClrRTERClick
        end
      end
      object GroupBox11: TGroupBox
        Left = 152
        Top = 176
        Width = 113
        Height = 65
        Caption = 'DTR for Fiber Optic'
        TabOrder = 20
        object SetDTRButton: TButton
          Left = 16
          Top = 32
          Width = 41
          Height = 25
          Hint = 'Sets the DTR and RTS lines high'
          Caption = 'Set'
          TabOrder = 0
          OnClick = SetDTRButtonClick
        end
        object ClearDTRButton: TButton
          Left = 64
          Top = 32
          Width = 41
          Height = 25
          Hint = 'Sets the DTR and RTS lines low'
          Caption = 'Clear'
          TabOrder = 1
          OnClick = ClearDTRButtonClick
        end
      end
      object GroupBox12: TGroupBox
        Left = 0
        Top = 176
        Width = 145
        Height = 65
        Caption = 'Comm Loopback'
        TabOrder = 21
        object EchoingCB: TCheckBox
          Left = 8
          Top = 16
          Width = 105
          Height = 17
          Caption = 'Echoing Enabled'
          TabOrder = 0
        end
        object EchoIndexCB: TCheckBox
          Left = 8
          Top = 32
          Width = 97
          Height = 17
          Hint = 'Check this box if first byte of recieved messages is cut off'
          Caption = 'Use Echo Index'
          TabOrder = 1
        end
      end
      object LabeledEdit1: TLabeledEdit
        Left = 168
        Top = 264
        Width = 65
        Height = 21
        EditLabel.Width = 60
        EditLabel.Height = 13
        EditLabel.Caption = 'Length Byte '
        LabelPosition = lpAbove
        LabelSpacing = 3
        TabOrder = 22
      end
    end
    object TabSheet7: TTabSheet
      Caption = 'Multi-Denom'
      ImageIndex = 4
      object Preamble: TCheckBox
        Left = 16
        Top = 16
        Width = 153
        Height = 17
        Caption = 'Add preamble to long polls'
        TabOrder = 0
      end
      object SASDenomComboBox: TComboBox
        Left = 16
        Top = 48
        Width = 193
        Height = 21
        ItemHeight = 0
        TabOrder = 1
        Text = 'SASDenomComboBox'
        OnChange = SASDenomComboBoxChange
      end
      object CheckListBox1: TCheckListBox
        Left = 16
        Top = 88
        Width = 193
        Height = 217
        ItemHeight = 13
        TabOrder = 2
      end
    end
    object TabSheet8: TTabSheet
      Caption = 'Validation'
      ImageIndex = 5
      object RadioGroup1: TRadioGroup
        Left = 0
        Top = 16
        Width = 233
        Height = 73
        Caption = 'Validation Type'
        ItemIndex = 0
        Items.Strings = ('No Automatic processing', 'Standard', 'Secure Enhanced', 'System')
        TabOrder = 0
      end
      object GroupBox2: TGroupBox
        Left = 0
        Top = 96
        Width = 233
        Height = 145
        Caption = 'System Validation'
        TabOrder = 1
        object Label19: TLabel
          Left = 8
          Top = 20
          Width = 126
          Height = 13
          Caption = 'System Validation Number:'
        end
        object Label21: TLabel
          Left = 8
          Top = 112
          Width = 100
          Height = 13
          Caption = 'Validation System ID:'
        end
        object SysValidationEdit: TEdit
          Left = 8
          Top = 36
          Width = 113
          Height = 21
          MaxLength = 16
          TabOrder = 0
          Text = '0123456789098765'
        end
        object UseValidation: TCheckBox
          Left = 8
          Top = 64
          Width = 137
          Height = 17
          Caption = 'Change to this Number'
          TabOrder = 1
          OnClick = UseValidationClick
        end
        object Edit2: TEdit
          Left = 112
          Top = 112
          Width = 25
          Height = 21
          MaxLength = 3
          TabOrder = 2
          Text = '01'
        end
        object AutoIncVal: TCheckBox
          Left = 8
          Top = 96
          Width = 209
          Height = 17
          Caption = 'Auto Increment Validation Num.'
          TabOrder = 3
        end
        object RandSysValButton: TButton
          Left = 136
          Top = 32
          Width = 89
          Height = 25
          Caption = 'Randomize SVN'
          TabOrder = 4
          OnClick = RandSysValButtonClick
        end
      end
      object GroupBox4: TGroupBox
        Left = 0
        Top = 248
        Width = 233
        Height = 137
        Caption = 'Secure Enhanced Validation'
        TabOrder = 2
        object Label32: TLabel
          Left = 8
          Top = 12
          Width = 61
          Height = 13
          Caption = 'Machine  ID:'
        end
        object Label33: TLabel
          Left = 8
          Top = 52
          Width = 62
          Height = 13
          Caption = 'Sequence #:'
        end
        object EnhMachineIDEdit: TEdit
          Left = 8
          Top = 28
          Width = 65
          Height = 21
          MaxLength = 6
          TabOrder = 0
          Text = '000001'
        end
        object EnhSequenceEdit: TEdit
          Left = 8
          Top = 68
          Width = 65
          Height = 21
          MaxLength = 6
          TabOrder = 1
          Text = '000000'
        end
        object InitEnhVal: TButton
          Left = 8
          Top = 96
          Width = 161
          Height = 25
          Caption = 'Initialize Enhanced Validation'
          TabOrder = 2
          OnClick = InitEnhValClick
        end
        object AutoInitCB: TCheckBox
          Left = 88
          Top = 72
          Width = 137
          Height = 17
          Caption = 'Auto Initialize Validation'
          TabOrder = 3
        end
      end
      object BadValNum: TCheckBox
        Left = 8
        Top = 392
        Width = 121
        Height = 17
        Caption = 'Bad Validation Num'
        TabOrder = 3
      end
      object BadTransCode: TCheckBox
        Left = 8
        Top = 408
        Width = 113
        Height = 17
        Caption = 'Bad Transfer Code'
        TabOrder = 4
      end
      object InsertGP: TCheckBox
        Left = 8
        Top = 424
        Width = 97
        Height = 17
        Caption = 'InsertGP'
        TabOrder = 5
      end
    end
    object TabSheet9: TTabSheet
      Caption = 'Redemption'
      ImageIndex = 6
      object Label22: TLabel
        Left = 8
        Top = 136
        Width = 75
        Height = 13
        Caption = 'Validation Data:'
      end
      object GroupBox3: TGroupBox
        Left = 0
        Top = 8
        Width = 233
        Height = 113
        Caption = 'Redemption'
        TabOrder = 0
        object Label18: TLabel
          Left = 8
          Top = 60
          Width = 72
          Height = 13
          Caption = 'Transfer Codes'
        end
        object Label20: TLabel
          Left = 8
          Top = 16
          Width = 72
          Height = 13
          Caption = 'Ticket Amount:'
        end
        object TxfrAmt: TEdit
          Left = 8
          Top = 32
          Width = 113
          Height = 21
          MaxLength = 10
          TabOrder = 0
          Text = '2500'
        end
        object UseTrans: TCheckBox
          Left = 128
          Top = 32
          Width = 97
          Height = 17
          Caption = 'Use this amount'
          TabOrder = 1
        end
        object InsertHex: TCheckBox
          Left = 128
          Top = 48
          Width = 97
          Height = 17
          Caption = 'Insert hex value'
          TabOrder = 2
        end
        object TransferCodesCB: TComboBox
          Left = 8
          Top = 76
          Width = 217
          Height = 21
          ItemHeight = 0
          TabOrder = 3
          Text = 'Transfer Codes'
          OnChange = TransferCodesCBChange
        end
      end
      object EnhValidationEdit: TEdit
        Left = 8
        Top = 152
        Width = 129
        Height = 21
        MaxLength = 18
        TabOrder = 1
        Text = '000000000000000000'
      end
      object EnhValNumCheckBox: TCheckBox
        Left = 138
        Top = 152
        Width = 97
        Height = 17
        Caption = 'Use this #'
        TabOrder = 2
      end
      object AutoValReplyBox: TCheckBox
        Left = 8
        Top = 296
        Width = 121
        Height = 17
        Caption = 'Auto Redemption'
        Checked = True
        State = cbChecked
        TabOrder = 3
      end
      object Send70: TButton
        Left = 8
        Top = 200
        Width = 129
        Height = 25
        Caption = 'Send 70 - Interrogate'
        TabOrder = 4
        OnClick = Send70Click
      end
      object SetValidationFlags: TButton
        Left = 8
        Top = 232
        Width = 129
        Height = 25
        Caption = 'Send 71 - Redeem Tkt'
        TabOrder = 5
        OnClick = SetValidationFlagsClick
      end
      object GetStatus: TButton
        Left = 8
        Top = 264
        Width = 129
        Height = 25
        Caption = 'Send 71 - Get Status'
        TabOrder = 6
        OnClick = GetStatusClick
      end
      object GroupBox5: TGroupBox
        Left = 0
        Top = 328
        Width = 233
        Height = 105
        Caption = 'Restricted Redemption'
        TabOrder = 7
        object Label23: TLabel
          Left = 8
          Top = 16
          Width = 97
          Height = 13
          Caption = 'Restricted Expiration'
        end
        object Label24: TLabel
          Left = 8
          Top = 40
          Width = 35
          Height = 13
          Caption = 'Pool ID'
        end
        object RestrictedExpirationEdit: TEdit
          Left = 120
          Top = 16
          Width = 57
          Height = 21
          MaxLength = 8
          TabOrder = 0
          Text = '00000000'
        end
        object PoolIDEdit: TEdit
          Left = 120
          Top = 40
          Width = 57
          Height = 21
          MaxLength = 4
          TabOrder = 1
          Text = '0000'
        end
        object UseAFT: TCheckBox
          Left = 8
          Top = 64
          Width = 201
          Height = 17
          Caption = 'Use Restricted Redemption'
          TabOrder = 2
        end
      end
    end
    object TabSheet4: TTabSheet
      Caption = 'Scripting'
      ImageIndex = 7
      object PageControl4: TPageControl
        Left = 8
        Top = 16
        Width = 273
        Height = 457
        ActivePage = TabSheet16
        TabIndex = 0
        TabOrder = 0
        object TabSheet16: TTabSheet
          Caption = 'Script'
          object Scripting: TCheckBox
            Left = 8
            Top = 8
            Width = 97
            Height = 17
            Caption = 'Scripting'
            TabOrder = 0
            OnClick = ScriptingClick
          end
          object ListBox1: TListBox
            Left = 16
            Top = 248
            Width = 201
            Height = 161
            ItemHeight = 13
            TabOrder = 1
          end
          object ListBox2: TListBox
            Left = 16
            Top = 40
            Width = 201
            Height = 185
            ItemHeight = 13
            TabOrder = 2
          end
        end
        object TabSheet17: TTabSheet
          Caption = 'Meter Test'
          ImageIndex = 1
          object GroupBox6: TGroupBox
            Left = 8
            Top = 40
            Width = 217
            Height = 145
            Caption = 'Individual Meter Test'
            TabOrder = 0
            object Label26: TLabel
              Left = 8
              Top = 16
              Width = 27
              Height = 13
              Caption = 'Meter'
            end
            object Label27: TLabel
              Left = 56
              Top = 16
              Width = 37
              Height = 13
              Caption = 'Position'
            end
            object Label28: TLabel
              Left = 112
              Top = 16
              Width = 80
              Height = 13
              Caption = 'How Many Bytes'
            end
            object Label29: TLabel
              Left = 8
              Top = 96
              Width = 116
              Height = 13
              Caption = 'Times Meter Changed = '
            end
            object Label30: TLabel
              Left = 144
              Top = 96
              Width = 6
              Height = 13
              Caption = '0'
            end
            object Label31: TLabel
              Left = 8
              Top = 120
              Width = 173
              Height = 13
              Caption = 'This feature only works with scripting'
            end
            object MonitoredMeterCmd: TComboBox
              Left = 8
              Top = 32
              Width = 41
              Height = 21
              ItemHeight = 13
              TabOrder = 0
              Text = 'MonitoredMeterCmd'
              Items.Strings = ('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '1A', '1B', '1C', '1D', '1E', '1F', '20', '21', '22', '23', '24', '25', '26', '27', '28', '29', '2A', '2B', '2C', '2D', '2E', '2F', '30', '31', '32', '33', '34', '35', '36', '37', '38', '39', '3A', '3B', '3C', '3D', '3E', '3F', '40', '41', '42', '43', '44', '45', '46', '47', '48', '49', '4A', '4B', '4C', '4D', '4E', '4F', '50', '51', '52', '53', '54', '55', '56', '57', '58', '59', '5A', '5B', '5C', '5D', '5E', '5F', '60', '61', '62', '63', '64', '65', '66', '67', '68', '69', '6A', '6B', '6C', '6D', '6E', '6F', '70', '71', '72', '73', '74', '75', '76', '77', '78', '79', '7A', '7B', '7C', '7D', '7E', '7F', '80', '81', '82', '83', '84', '85', '86', '87', '88', '89', '8A', '8B', '8C', '8D', '8E', '8F', '90', '91', '92', '93', '94', '95', '96', '97', '98', '99', '9A', '9B', '9C', '9D', '9E', '9F', 'A0', 'A1', 'A2', 'A3', 'A4', 'A5', 'A6', 'A7', 'A8', 'A9', 'AA', 'AB', 'AC', 'AD', 'AE', 'AF', 'B0', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'B7', 'B8', 'B9', 'BA', 'BB', 'BC', 'BD', 'BE', 'BF', 'C0', 'C1', 'C2', 'C3', 'C4', 'C5', 'C6', 'C7', 'C8', 'C9', 'CA', 'CB', 'CC', 'CD', 'CE', 'CF', 'D0', 'D1', 'D2', 'D3', 'D4', 'D5', 'D6', 'D7', 'D8', 'D9', 'DA', 'DB', 'DC', 'DD', 'DE', 'DF', 'E0', 'E1', 'E2', 'E3', 'E4', 'E5', 'E6', 'E7', 'E8', 'E9', 'EA', 'EB', 'EC', 'ED', 'EE', 'EF', 'F0', 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'FA', 'FB', 'FC', 'FD', 'FE', 'FF')
            end
            object MonitoredMeterPos: TComboBox
              Left = 56
              Top = 32
              Width = 49
              Height = 21
              ItemHeight = 13
              TabOrder = 1
              Text = 'MonitoredMeterPos'
              Items.Strings = ('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20', '21', '22', '23', '24', '25', '26', '27', '28', '29', '30', '31', '32', '33', '34', '35', '36', '37', '38', '39', '40')
            end
            object MonitoredMeterBytes: TComboBox
              Left = 112
              Top = 32
              Width = 57
              Height = 21
              ItemHeight = 13
              TabOrder = 2
              Text = 'MonitoredMeterBytes'
              Items.Strings = ('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20')
            end
            object MonitorMeter: TCheckBox
              Left = 8
              Top = 56
              Width = 97
              Height = 17
              Caption = 'Monitor Meter'
              TabOrder = 3
              OnClick = MonitorMeterClick
            end
          end
          object MeterTest: TButton
            Left = 16
            Top = 200
            Width = 137
            Height = 33
            Caption = 'Meter Test'
            TabOrder = 1
            OnClick = MeterTestClick
          end
        end
      end
    end
    object Progressive: TTabSheet
      Caption = 'Progressive'
      ImageIndex = 8
      object Label36: TLabel
        Left = 8
        Top = 432
        Width = 44
        Height = 13
        Caption = 'Group= 1'
      end
      object Label34: TLabel
        Left = 8
        Top = 296
        Width = 86
        Height = 13
        Caption = 'Broadcast Interval'
      end
      object Label1: TLabel
        Left = 8
        Top = 312
        Width = 55
        Height = 13
        Caption = '3 second(s)'
      end
      object Label14: TLabel
        Left = 229
        Top = 1
        Width = 31
        Height = 13
        Caption = 'Levels'
      end
      object In: TLabel
        Left = 8
        Top = 0
        Width = 70
        Height = 13
        Caption = 'Increment amt.'
      end
      object Do86: TCheckBox
        Left = 8
        Top = 376
        Width = 217
        Height = 14
        Caption = 'Use Multiple Level Progressive Broadcast'
        TabOrder = 0
        OnClick = Do86Click
      end
      object ProgRepeat: TCheckBox
        Left = 8
        Top = 360
        Width = 185
        Height = 17
        Caption = 'Enable Progressive Broadcasts'
        TabOrder = 1
        OnClick = ProgRepeatClick
      end
      object DispBroad: TCheckBox
        Left = 8
        Top = 408
        Width = 121
        Height = 17
        Hint = 'Turns on the display of progressive messages'
        Caption = 'Display Broadcasts'
        TabOrder = 2
      end
      object UpDown2: TUpDown
        Left = 69
        Top = 432
        Width = 28
        Height = 36
        Min = 0
        Max = 255
        Position = 1
        TabOrder = 3
        Wrap = False
        OnClick = UpDown2Click
      end
      object Button4: TButton
        Left = 144
        Top = 296
        Width = 121
        Height = 25
        Caption = 'Broadcast'
        TabOrder = 4
        OnClick = Button4Click
      end
      object Do80: TCheckBox
        Left = 8
        Top = 392
        Width = 225
        Height = 17
        Caption = 'Use Single Level Progressive Broadcast'
        Checked = True
        State = cbChecked
        TabOrder = 5
        OnClick = Do80Click
      end
      object UpDown6: TUpDown
        Left = 104
        Top = 296
        Width = 25
        Height = 33
        Min = 1
        Max = 10
        Position = 3
        TabOrder = 6
        Wrap = False
        OnClick = UpDown6Click
      end
      object IncProgCB: TCheckBox
        Left = 8
        Top = 344
        Width = 201
        Height = 17
        Caption = 'Enable Progressive Incrementing'
        TabOrder = 7
        OnClick = IncProgCBClick
      end
    end
    object TabSheet1: TTabSheet
      Caption = 'EFT Test'
      ImageIndex = 8
      TabVisible = False
      object EFTStat: TLabel
        Left = 8
        Top = 256
        Width = 39
        Height = 13
        Caption = 'EFTStat'
      end
      object BreakEFT: TCheckBox
        Left = 8
        Top = 40
        Width = 225
        Height = 17
        Hint = 'Breaks line after first ack'
        Caption = 'Break TXRX line on final ACK from game'
        TabOrder = 0
      end
      object RestoreRXLineButton: TButton
        Left = 8
        Top = 8
        Width = 121
        Height = 25
        Caption = 'Restore TXRX Line'
        TabOrder = 1
        OnClick = RestoreRXLineButtonClick
      end
      object Garble: TCheckBox
        Left = 8
        Top = 56
        Width = 97
        Height = 17
        Caption = 'Garble'
        TabOrder = 2
      end
      object EFTButton: TButton
        Left = 40
        Top = 328
        Width = 145
        Height = 33
        Caption = 'EFTButton'
        TabOrder = 3
        OnClick = EFTButtonClick
      end
      object EFTEdit: TEdit
        Left = 8
        Top = 272
        Width = 225
        Height = 21
        TabOrder = 4
        Text = 'EFTEdit'
      end
      object ComboBox8: TComboBox
        Left = 8
        Top = 296
        Width = 225
        Height = 21
        ItemHeight = 0
        TabOrder = 5
        Text = 'ComboBox8'
      end
      object ComboBox9: TComboBox
        Left = 8
        Top = 376
        Width = 145
        Height = 21
        ItemHeight = 13
        TabOrder = 6
        Text = 'Denoms'
        Items.Strings = ('.01', '.05', '.10', '.25', '.50', '1.00', '2.00', '5.00', '10.00', '20.00', '50.00', '100.00', '500.00', '1000.00', '10000.00')
      end
      object ACKCB: TCheckBox
        Left = 8
        Top = 224
        Width = 97
        Height = 17
        Caption = 'ACKCB'
        TabOrder = 7
      end
      object SetACK: TButton
        Left = 10
        Top = 96
        Width = 87
        Height = 25
        Caption = 'Set ACK'
        TabOrder = 8
        OnClick = SetACKClick
      end
      object Button7: TButton
        Left = 82
        Top = 136
        Width = 65
        Height = 25
        Caption = 'Inc Trans.'
        TabOrder = 9
        OnClick = Button7Click
      end
      object DecTrans: TButton
        Left = 10
        Top = 136
        Width = 65
        Height = 25
        Caption = 'Dec Trans.'
        TabOrder = 10
        OnClick = DecTransClick
      end
    end
    object TabSheet10: TTabSheet
      Caption = 'AFT'
      ImageIndex = 9
      object PageControl3: TPageControl
        Left = 0
        Top = 8
        Width = 281
        Height = 505
        ActivePage = TabSheet12
        TabIndex = 1
        TabOrder = 0
        object TabSheet11: TTabSheet
          Caption = 'Registration / Lock'
          object GroupBox8: TGroupBox
            Left = 0
            Top = 200
            Width = 249
            Height = 169
            Caption = 'AFT Lock'
            TabOrder = 0
            object LockCodeLabel: TLabel
              Left = 8
              Top = 16
              Width = 52
              Height = 13
              Caption = 'Lock Code'
            end
            object LockTimeoutLabel: TLabel
              Left = 8
              Top = 120
              Width = 65
              Height = 13
              Caption = 'Lock Timeout'
            end
            object LockCodeCB: TComboBox
              Left = 80
              Top = 16
              Width = 145
              Height = 21
              ItemHeight = 13
              TabOrder = 0
              Text = 'Lock Code'
              Items.Strings = ('00= Request Lock', '80= Cancel lock or pending lock request', 'FF= Interrogate current status only')
            end
            object LockCondition1: TCheckBox
              Left = 80
              Top = 40
              Width = 121
              Height = 17
              Caption = 'TXFR to Machine OK'
              TabOrder = 1
            end
            object LockCondition2: TCheckBox
              Left = 80
              Top = 56
              Width = 145
              Height = 17
              Caption = 'TXFR  from Machine OK'
              TabOrder = 2
            end
            object LockCondition3: TCheckBox
              Left = 80
              Top = 72
              Width = 137
              Height = 17
              Caption = 'TXFR to Printer OK'
              TabOrder = 3
            end
            object LockCondition4: TCheckBox
              Left = 80
              Top = 88
              Width = 121
              Height = 17
              Caption = 'Bonus to Machine OK'
              TabOrder = 4
            end
            object LockTimeoutEdit: TEdit
              Left = 80
              Top = 112
              Width = 121
              Height = 21
              TabOrder = 5
              Text = '500'
            end
            object AFTLockButton: TButton
              Left = 8
              Top = 136
              Width = 185
              Height = 25
              Caption = 'AFT Lock'
              TabOrder = 6
              OnClick = AFTLockButtonClick
            end
          end
          object GroupBox7: TGroupBox
            Left = 0
            Top = 8
            Width = 249
            Height = 185
            Caption = 'AFT Registration'
            TabOrder = 1
            object RegCodeLabel: TLabel
              Left = 8
              Top = 32
              Width = 51
              Height = 13
              Caption = 'Reg. Code'
            end
            object AssetLabel: TLabel
              Left = 8
              Top = 56
              Width = 36
              Height = 13
              Caption = 'Asset #'
            end
            object RegKeyLabel: TLabel
              Left = 8
              Top = 80
              Width = 44
              Height = 13
              Caption = 'Reg. Key'
            end
            object POSIDLabel: TLabel
              Left = 8
              Top = 104
              Width = 33
              Height = 13
              Caption = 'POSID'
            end
            object AFTAssetEdit: TEdit
              Left = 80
              Top = 48
              Width = 160
              Height = 21
              TabOrder = 0
              Text = '3041AB00'
            end
            object AFTRegKeyEdit: TEdit
              Left = 80
              Top = 72
              Width = 160
              Height = 21
              Hint = 'Enter Value in BCD'
              MaxLength = 40
              TabOrder = 1
              Text = '9876543210123456'
            end
            object AFTRegCodeComboBox: TComboBox
              Left = 80
              Top = 24
              Width = 160
              Height = 21
              ItemHeight = 13
              TabOrder = 2
              Text = 'AFT Registration Code'
              OnChange = AFTRegCodeComboBoxChange
              Items.Strings = ('00 = Initialize Registration', '01 = Register Gaming Machine', '40 = Request Operator Acknowledgement', '80 = Unregister Gaming Machine', 'FF = Read Current Registration')
            end
            object RegGamingMach: TButton
              Left = 80
              Top = 128
              Width = 161
              Height = 25
              Caption = 'Register Gaming Machine'
              TabOrder = 3
              OnClick = RegGamingMachClick
            end
            object AFTPOSIDEdit: TEdit
              Left = 80
              Top = 96
              Width = 160
              Height = 21
              TabOrder = 4
              Text = '11223344'
            end
            object AutoRegisterCB: TCheckBox
              Left = 8
              Top = 160
              Width = 97
              Height = 17
              Caption = 'Auto Register'
              Checked = True
              State = cbChecked
              TabOrder = 5
            end
          end
        end
        object TabSheet12: TTabSheet
          Caption = 'Transfer Funds'
          ImageIndex = 1
          object PoolIDLabel: TLabel
            Left = 112
            Top = 304
            Width = 35
            Height = 13
            Caption = 'Pool ID'
          end
          object ExpirationLabel: TLabel
            Left = 112
            Top = 288
            Width = 46
            Height = 13
            Caption = 'Expiration'
          end
          object CashableLabel: TLabel
            Left = 88
            Top = 48
            Width = 83
            Height = 13
            Caption = 'Cashable Amount'
          end
          object RestrictedLabel: TLabel
            Left = 88
            Top = 80
            Width = 87
            Height = 13
            Caption = 'Restricted Amount'
          end
          object NonRestrictedLabel: TLabel
            Left = 88
            Top = 104
            Width = 110
            Height = 13
            Caption = 'Non Restricted Amount'
          end
          object TransactionIDLabel: TLabel
            Left = 128
            Top = 248
            Width = 70
            Height = 13
            Caption = 'Transaction ID'
          end
          object TransIndexLabel: TLabel
            Left = 192
            Top = 64
            Width = 56
            Height = 13
            Caption = 'Trans Index'
          end
          object Label49: TLabel
            Left = 128
            Top = 320
            Width = 87
            Height = 13
            Caption = 'Edit Reciept Data:'
          end
          object TransferCodeCB: TComboBox
            Left = 0
            Top = 0
            Width = 265
            Height = 21
            ItemHeight = 13
            TabOrder = 0
            Text = 'Transfer Codes'
            Items.Strings = ('00 = Transfer request, full transfer only', '01 = Transfer request, partial transfer allowed', '80 = Cancel transfer request', 'FF = Interrogate')
          end
          object TransferTypeCB: TComboBox
            Left = 0
            Top = 24
            Width = 265
            Height = 21
            ItemHeight = 13
            TabOrder = 1
            Text = 'Transfer Types'
            Items.Strings = ('00 = Txfr in-house amount from host to GM', '10 = Txfr bonus coin out win amount from host to GM', '11 = Txfrr bonus jackpot win amount from host to GM', '20 = Txfr in-house amount from host to ticket ', '40 = Txfrr debit amount from host to GM', '60 = Txfr debit amount from host to ticket', '80 = Txfr in-house amount from GM to host', '90 = Txfr win amount from GM to host')
          end
          object CashableEdit: TEdit
            Left = 8
            Top = 48
            Width = 73
            Height = 21
            TabOrder = 2
            Text = '0000000000'
          end
          object RestrictedEdit: TEdit
            Left = 8
            Top = 72
            Width = 73
            Height = 21
            TabOrder = 3
            Text = '0000000000'
          end
          object NonRestrictedEdit: TEdit
            Left = 8
            Top = 96
            Width = 73
            Height = 21
            TabOrder = 4
            Text = '0000000000'
          end
          object TransferFlag0: TCheckBox
            Left = 8
            Top = 120
            Width = 169
            Height = 17
            Caption = 'Host Cashout Enable Control'
            TabOrder = 5
          end
          object TransferFlag1: TCheckBox
            Left = 8
            Top = 136
            Width = 137
            Height = 17
            Caption = 'Host Cashout Enable'
            TabOrder = 6
          end
          object TransferFlag2: TCheckBox
            Left = 8
            Top = 152
            Width = 193
            Height = 17
            Caption = 'Host Cashout Mode  0=soft 1=hard'
            TabOrder = 7
          end
          object TransferFlag6: TCheckBox
            Left = 8
            Top = 200
            Width = 145
            Height = 17
            Caption = 'Accept only if locked'
            TabOrder = 8
          end
          object TransferFlag7: TCheckBox
            Left = 8
            Top = 216
            Width = 161
            Height = 17
            Caption = 'Transaction Receipt request'
            TabOrder = 9
          end
          object TransactionIDEdit: TEdit
            Left = 0
            Top = 240
            Width = 121
            Height = 21
            TabOrder = 10
            Text = 'TransactionIDEdit'
          end
          object ExpirationEdit: TEdit
            Left = 0
            Top = 288
            Width = 105
            Height = 21
            TabOrder = 11
            Text = 'ExpirationEdit'
          end
          object AFTPoolIDEdit: TEdit
            Left = 0
            Top = 312
            Width = 105
            Height = 21
            TabOrder = 12
            Text = 'AFTPoolIDEdit'
          end
          object RecieptDataCB: TComboBox
            Left = 112
            Top = 336
            Width = 145
            Height = 21
            ItemHeight = 13
            TabOrder = 13
            Text = 'Receipt Data'
            OnChange = RecieptDataCBChange
            Items.Strings = ('00 = Transfer source/destination', '01 = Date and time', '10 = Patron name', '11 = Patron acct#', '13 = Account Balance', '41 = ATM card#', '42 = Transaction fee', '43 = Total debit amount')
          end
          object AFTTransactionButton: TButton
            Left = 120
            Top = 384
            Width = 129
            Height = 25
            Caption = 'Do AFT Transaction'
            TabOrder = 14
            OnClick = AFTTransactionButtonClick
          end
          object TransSDCB: TCheckBox
            Left = 0
            Top = 336
            Width = 97
            Height = 17
            Caption = 'Trans Src/Dest'
            TabOrder = 15
          end
          object DateTimeCB: TCheckBox
            Left = 0
            Top = 352
            Width = 97
            Height = 17
            Caption = 'Date/Time'
            TabOrder = 16
          end
          object PatronNameCB: TCheckBox
            Left = 0
            Top = 368
            Width = 97
            Height = 17
            Caption = 'Patron Name'
            TabOrder = 17
          end
          object PatronAcctCB: TCheckBox
            Left = 0
            Top = 384
            Width = 97
            Height = 17
            Caption = 'Patron Acct'
            TabOrder = 18
          end
          object AccountBalanceCB: TCheckBox
            Left = 0
            Top = 400
            Width = 113
            Height = 17
            Caption = 'Account Balance'
            TabOrder = 19
          end
          object ATMCardCB: TCheckBox
            Left = 0
            Top = 416
            Width = 113
            Height = 17
            Caption = 'ATM Card Number'
            TabOrder = 20
          end
          object TransactionFeeCB: TCheckBox
            Left = 0
            Top = 432
            Width = 113
            Height = 17
            Caption = 'Transaction Fee'
            TabOrder = 21
          end
          object TotalDebitCB: TCheckBox
            Left = 0
            Top = 448
            Width = 97
            Height = 17
            Caption = 'Total Debit'
            TabOrder = 22
          end
          object TransIndexEdit: TEdit
            Left = 192
            Top = 80
            Width = 41
            Height = 21
            TabOrder = 23
            Text = '00'
          end
          object InterrogateButton: TButton
            Left = 120
            Top = 416
            Width = 129
            Height = 25
            Caption = 'Interrogate AFT'
            TabOrder = 24
            OnClick = InterrogateButtonClick
          end
          object AutoInterrogateCB: TCheckBox
            Left = 112
            Top = 360
            Width = 97
            Height = 17
            Hint = 'Check this box to auto interrogate when 69 is recieved'
            Caption = 'Auto Interrogate'
            Checked = True
            State = cbChecked
            TabOrder = 25
          end
          object TransIDAppend: TEdit
            Left = 0
            Top = 264
            Width = 25
            Height = 21
            Hint = 'Append Transaction ID Auto increments when checked'
            ReadOnly = True
            TabOrder = 26
            Text = '0'
          end
          object AppendTRID: TCheckBox
            Left = 32
            Top = 264
            Width = 145
            Height = 17
            Hint = 'Check to append Tansaction ID for unique values'
            Caption = 'Append Transaction ID'
            TabOrder = 27
          end
          object UpDown4: TUpDown
            Left = 168
            Top = 264
            Width = 13
            Height = 27
            Min = 0
            Max = 99
            Position = 0
            TabOrder = 28
            Wrap = False
            OnClick = UpDown4Click
          end
          object ZeroRegKey: TCheckBox
            Left = 168
            Top = 296
            Width = 97
            Height = 17
            Caption = 'Zero Reg Key'
            TabOrder = 29
          end
          object AutoIncTID: TCheckBox
            Left = 184
            Top = 264
            Width = 97
            Height = 17
            Caption = 'Auto Inc TID'
            TabOrder = 30
            OnClick = AutoIncTIDClick
          end
          object TransIDError: TCheckBox
            Left = 128
            Top = 232
            Width = 97
            Height = 17
            Caption = 'Trans ID Error'
            TabOrder = 31
          end
          object CancelAFTButton: TButton
            Left = 120
            Top = 448
            Width = 129
            Height = 25
            Caption = 'Cancel AFT Transaction'
            TabOrder = 32
            OnClick = CancelAFTButtonClick
          end
          object UpDown5: TUpDown
            Left = 232
            Top = 232
            Width = 13
            Height = 19
            Min = 0
            Position = 0
            TabOrder = 33
            Wrap = False
          end
          object TransferFlag3: TCheckBox
            Left = 8
            Top = 168
            Width = 217
            Height = 17
            Caption = 'Cashout from Gaming Machine Request.'
            TabOrder = 34
          end
          object TransferFlag5: TCheckBox
            Left = 8
            Top = 184
            Width = 169
            Height = 17
            Caption = 'Use custon ticket data'
            TabOrder = 35
          end
        end
        object TabSheet15: TTabSheet
          Caption = 'Receipt'
          ImageIndex = 2
          object Label50: TLabel
            Left = 48
            Top = 16
            Width = 173
            Height = 13
            Caption = 'Edit Transaction Receipt Data Fields'
          end
          object TransReceiptCB: TComboBox
            Left = 48
            Top = 32
            Width = 145
            Height = 21
            ItemHeight = 13
            TabOrder = 0
            Text = 'TransReceiptCB'
            OnChange = TransReceiptCBChange
            Items.Strings = ('00 = Location', '01 =Address 1', '02 = Address 2', '10 = In House Line 1', '11 = In House Line 2', '12 = In House Line 3', '13 = In House Line 4', '20 = Debit Line 1', '21 = Debit Line 2', '22 = Debit Line 3', '23 = Debit Line 4')
          end
          object LocationCB: TCheckBox
            Left = 48
            Top = 64
            Width = 97
            Height = 17
            Caption = 'Location'
            TabOrder = 1
          end
          object Address1CB: TCheckBox
            Left = 48
            Top = 80
            Width = 97
            Height = 17
            Caption = 'Address 1'
            TabOrder = 2
          end
          object Address2CB: TCheckBox
            Left = 48
            Top = 96
            Width = 97
            Height = 17
            Caption = 'Address 2'
            TabOrder = 3
          end
          object InHouse1CB: TCheckBox
            Left = 48
            Top = 112
            Width = 97
            Height = 17
            Caption = 'In House 1'
            TabOrder = 4
          end
          object InHouse2CB: TCheckBox
            Left = 48
            Top = 128
            Width = 97
            Height = 17
            Caption = 'In House 2'
            TabOrder = 5
          end
          object InHouse3CB: TCheckBox
            Left = 48
            Top = 144
            Width = 97
            Height = 17
            Caption = 'In House 3'
            TabOrder = 6
          end
          object InHouse4CB: TCheckBox
            Left = 48
            Top = 160
            Width = 97
            Height = 17
            Caption = 'In House 4'
            TabOrder = 7
          end
          object DebitLine1CB: TCheckBox
            Left = 48
            Top = 176
            Width = 97
            Height = 17
            Caption = 'Debit Line 1'
            TabOrder = 8
          end
          object DebitLine2CB: TCheckBox
            Left = 48
            Top = 192
            Width = 97
            Height = 17
            Caption = 'Debit Line 2'
            TabOrder = 9
          end
          object DebitLine3CB: TCheckBox
            Left = 48
            Top = 208
            Width = 97
            Height = 17
            Caption = 'Debit Line 3'
            TabOrder = 10
          end
          object DebitLine4CB: TCheckBox
            Left = 48
            Top = 224
            Width = 97
            Height = 17
            Caption = 'Debit Line 4'
            TabOrder = 11
          end
          object UpdateReceiptButton: TButton
            Left = 48
            Top = 248
            Width = 161
            Height = 33
            Caption = 'Update Receipt Data (0x75)'
            TabOrder = 12
            OnClick = UpdateReceiptButtonClick
          end
          object SaveAFTDefaults: TButton
            Left = 48
            Top = 312
            Width = 121
            Height = 25
            Caption = 'Save AFT Defaults'
            TabOrder = 13
          end
          object RestoreAFTDefaults: TButton
            Left = 48
            Top = 344
            Width = 121
            Height = 25
            Caption = 'Restore AFT Defaults'
            TabOrder = 14
          end
        end
      end
    end
    object TabSheet13: TTabSheet
      Caption = 'Pulltab'
      ImageIndex = 10
      TabVisible = False
      object PayLabel: TLabel
        Left = 24
        Top = 144
        Width = 111
        Height = 13
        Caption = 'Pay for this sequence ='
      end
      object RNGPayLabel: TLabel
        Left = 149
        Top = 144
        Width = 5
        Height = 16
        Font.Charset = DEFAULT_CHARSET
        Font.Color = clWindowText
        Font.Height = -13
        Font.Name = 'MS Sans Serif'
        Font.Style = [fsBold]
        ParentFont = False
      end
      object PullTabLabel0: TLabel
        Left = 24
        Top = 200
        Width = 64
        Height = 13
        Caption = '1 Line 1 coin '
      end
      object Label2: TLabel
        Left = 24
        Top = 216
        Width = 69
        Height = 13
        Caption = '3 Lines 1 coin '
      end
      object Label8: TLabel
        Left = 24
        Top = 232
        Width = 69
        Height = 13
        Caption = '5 Lines 1 coin '
      end
      object Label9: TLabel
        Left = 24
        Top = 248
        Width = 69
        Height = 13
        Caption = '7 Lines 1 coin '
      end
      object Label10: TLabel
        Left = 24
        Top = 264
        Width = 69
        Height = 13
        Caption = '9 Lines 1 coin '
      end
      object Label11: TLabel
        Left = 24
        Top = 280
        Width = 71
        Height = 13
        Caption = '9 Lines 2 coins'
      end
      object Label12: TLabel
        Left = 24
        Top = 296
        Width = 74
        Height = 13
        Caption = '9 Lines 3 coins '
      end
      object Label13: TLabel
        Left = 24
        Top = 312
        Width = 74
        Height = 13
        Caption = '9 Lines 4 coins '
      end
      object Label35: TLabel
        Left = 24
        Top = 328
        Width = 74
        Height = 13
        Caption = '9 Lines 5 coins '
      end
      object Label37: TLabel
        Left = 136
        Top = 200
        Width = 3
        Height = 13
      end
      object Label38: TLabel
        Left = 136
        Top = 216
        Width = 3
        Height = 13
      end
      object Label39: TLabel
        Left = 136
        Top = 232
        Width = 3
        Height = 13
      end
      object Label40: TLabel
        Left = 136
        Top = 248
        Width = 3
        Height = 13
      end
      object Label41: TLabel
        Left = 136
        Top = 264
        Width = 3
        Height = 13
      end
      object Label42: TLabel
        Left = 136
        Top = 280
        Width = 3
        Height = 13
      end
      object Label43: TLabel
        Left = 136
        Top = 296
        Width = 3
        Height = 13
      end
      object Label44: TLabel
        Left = 136
        Top = 312
        Width = 3
        Height = 13
      end
      object Label45: TLabel
        Left = 136
        Top = 328
        Width = 3
        Height = 13
      end
      object Label46: TLabel
        Left = 128
        Top = 184
        Width = 24
        Height = 13
        Caption = 'Wins'
      end
      object Label48: TLabel
        Left = 144
        Top = 120
        Width = 37
        Height = 13
        Caption = 'Game 1'
      end
      object RNGPayLabel1: TLabel
        Left = 208
        Top = 144
        Width = 5
        Height = 16
        Font.Charset = DEFAULT_CHARSET
        Font.Color = clWindowText
        Font.Height = -13
        Font.Name = 'MS Sans Serif'
        Font.Style = [fsBold]
        ParentFont = False
      end
      object Label47: TLabel
        Left = 200
        Top = 120
        Width = 37
        Height = 13
        Caption = 'Game 2'
      end
      object Label52: TLabel
        Left = 200
        Top = 200
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label53: TLabel
        Left = 200
        Top = 216
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label54: TLabel
        Left = 200
        Top = 232
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label55: TLabel
        Left = 200
        Top = 248
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label56: TLabel
        Left = 200
        Top = 264
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label57: TLabel
        Left = 200
        Top = 280
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label58: TLabel
        Left = 200
        Top = 296
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label59: TLabel
        Left = 200
        Top = 312
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label60: TLabel
        Left = 200
        Top = 328
        Width = 9
        Height = 13
        Caption = '   '
      end
      object Label61: TLabel
        Left = 192
        Top = 184
        Width = 51
        Height = 13
        Caption = 'Total Tabs'
      end
      object GroupBox9: TGroupBox
        Left = 48
        Top = 16
        Width = 201
        Height = 89
        Caption = 'Pulltab'
        TabOrder = 0
        object TestLab: TLabel
          Left = 144
          Top = 48
          Width = 39
          Height = 13
          Caption = 'TestLab'
        end
        object RNGSeedCB: TCheckBox
          Left = 8
          Top = 16
          Width = 153
          Height = 17
          Caption = 'RNG Seed Mode Enabled'
          TabOrder = 0
          OnClick = RNGSeedCBClick
        end
        object ForcePulltab: TButton
          Left = 8
          Top = 48
          Width = 97
          Height = 25
          Caption = 'Force a Pulltab'
          TabOrder = 1
          OnClick = ForcePulltabClick
        end
      end
    end
    object TabSheet14: TTabSheet
      Caption = 'Report'
      ImageIndex = 11
      object Memo3: TMemo
        Left = 16
        Top = 24
        Width = 233
        Height = 417
        ScrollBars = ssBoth
        TabOrder = 0
      end
      object AddReportEntryButton: TButton
        Left = 16
        Top = 448
        Width = 97
        Height = 25
        Caption = 'Add Report Entry'
        TabOrder = 1
        OnClick = AddReportEntryButtonClick
      end
      object ClrRptButton: TButton
        Left = 120
        Top = 448
        Width = 81
        Height = 25
        Caption = 'Clear'
        TabOrder = 2
        OnClick = ClrRptButtonClick
      end
    end
    object TabSheet18: TTabSheet
      Caption = 'Authentication'
      ImageIndex = 12
      object Label62: TLabel
        Left = 16
        Top = 8
        Width = 30
        Height = 13
        Caption = 'Action'
      end
      object ActionCB: TComboBox
        Left = 16
        Top = 24
        Width = 257
        Height = 21
        ItemHeight = 13
        ItemIndex = 0
        TabOrder = 0
        Text = '00 = Interrogate number of installed components'
        Items.Strings = ('00 = Interrogate number of installed components', '01 = Read status of component', '02 = Authenticate component', '03 = Interrogate authentication status')
      end
      object Button5: TButton
        Left = 8
        Top = 408
        Width = 153
        Height = 33
        Caption = 'Send Authentication Info'
        TabOrder = 1
        OnClick = Button5Click
      end
      object Button8: TButton
        Left = 8
        Top = 448
        Width = 153
        Height = 33
        Caption = 'Interrogate'
        TabOrder = 2
        OnClick = Button8Click
      end
      object GroupBox14: TGroupBox
        Left = 8
        Top = 248
        Width = 257
        Height = 145
        Caption = 'Authentication Data'
        TabOrder = 3
        object Label67: TLabel
          Left = 16
          Top = 116
          Width = 28
          Height = 13
          Caption = 'Offset'
        end
        object Label66: TLabel
          Left = 16
          Top = 92
          Width = 25
          Height = 13
          Caption = 'Seed'
        end
        object Label65: TLabel
          Left = 8
          Top = 44
          Width = 107
          Height = 13
          Caption = 'Authentication Method'
        end
        object OffsetEdit: TEdit
          Left = 48
          Top = 116
          Width = 177
          Height = 21
          TabOrder = 0
          Text = '66778899'
        end
        object SeedEdit: TEdit
          Left = 48
          Top = 92
          Width = 177
          Height = 21
          TabOrder = 1
          Text = '1122334455'
        end
        object AuthenticationMethodCB: TComboBox
          Left = 48
          Top = 60
          Width = 177
          Height = 21
          ItemHeight = 13
          ItemIndex = 0
          TabOrder = 2
          Text = '00000000  = None'
          Items.Strings = ('00000000  = None', '00000001 = CRC 16', '00000002 = CRC 32', '00000004 = MD5', '00000008 = Kobetron I', '00000010 = Kobetron II', '00000020 = SHA1')
        end
        object IncludeAuthCB: TCheckBox
          Left = 8
          Top = 16
          Width = 177
          Height = 17
          Caption = 'Include Authentication Data'
          TabOrder = 3
        end
      end
      object GroupBox15: TGroupBox
        Left = 8
        Top = 56
        Width = 257
        Height = 185
        Caption = 'Address Specification'
        TabOrder = 4
        object Label64: TLabel
          Left = 8
          Top = 140
          Width = 79
          Height = 13
          Caption = 'Compnent Name'
        end
        object Label63: TLabel
          Left = 8
          Top = 92
          Width = 83
          Height = 13
          Caption = 'Component Index'
        end
        object ComponentNameEdit: TEdit
          Left = 8
          Top = 156
          Width = 241
          Height = 21
          TabOrder = 0
          Text = '0123456789'
        end
        object ComponentIndexEdit: TEdit
          Left = 8
          Top = 108
          Width = 241
          Height = 21
          TabOrder = 1
          Text = '0123456789'
        end
        object AddressingModeRB: TRadioGroup
          Left = 8
          Top = 32
          Width = 241
          Height = 57
          Caption = 'Addressing Mode'
          ItemIndex = 0
          Items.Strings = ('00 = addressing  by component index', '01 = addressing by component name')
          TabOrder = 2
        end
        object IncludeAddressCB: TCheckBox
          Left = 8
          Top = 16
          Width = 169
          Height = 17
          Caption = 'Include Address Specification'
          TabOrder = 3
        end
      end
      object AuthAutoInterCB: TCheckBox
        Left = 168
        Top = 456
        Width = 97
        Height = 17
        Caption = 'Auto Interrogate'
        Checked = True
        State = cbChecked
        TabOrder = 5
      end
    end
  end
  object ClrDisps: TButton
    Left = 184
    Top = 568
    Width = 105
    Height = 25
    Anchors = [akLeft, akBottom]
    Caption = 'Clear Both Displays'
    TabOrder = 12
    OnClick = ClrDispsClick
  end
  object Panel1: TPanel
    Left = 0
    Top = 128
    Width = 489
    Height = 387
    Anchors = [akLeft, akTop, akBottom]
    AutoSize = True
    Caption = 'Panel1'
    TabOrder = 11
    object Splitter1: TSplitter
      Left = 1
      Top = 193
      Width = 487
      Height = 8
      Cursor = crVSplit
      Align = alTop
      AutoSnap = False
    end
    object Memo2: TMemo
      Left = 1
      Top = 1
      Width = 487
      Height = 192
      Align = alTop
      Anchors = [akTop]
      Font.Charset = ANSI_CHARSET
      Font.Color = clBlack
      Font.Height = -15
      Font.Name = 'Courier'
      Font.Style = []
      Lines.Strings = ('')
      ParentFont = False
      ScrollBars = ssVertical
      TabOrder = 0
      OnMouseDown = Memo1MouseDown
      OnMouseUp = Memo1MouseUp
    end
    object Memo1: TMemo
      Left = 1
      Top = 201
      Width = 487
      Height = 185
      Align = alClient
      Anchors = [akTop]
      Lines.Strings = ('')
      ScrollBars = ssVertical
      TabOrder = 1
      OnMouseDown = Memo1MouseDown
      OnMouseUp = Memo1MouseUp
    end
  end
  object MainMenu1: TMainMenu
    Left = 392
    Top = 560
    object File1: TMenuItem
      Caption = 'File'
      object ReloadINIfile1: TMenuItem
        Caption = 'Reload INI file'
        OnClick = ReloadINIfile1Click
      end
      object SaveData1: TMenuItem
        Caption = 'Save Data'
        Enabled = False
        OnClick = SaveData1Click
      end
      object SaveDefaultData1: TMenuItem
        Caption = 'Save Default Data'
        OnClick = SaveDefaultData1Click
      end
      object RestoreDF: TMenuItem
        Caption = 'Restore Default Data'
        OnClick = RestoreDFClick
      end
      object SaveReportFile1: TMenuItem
        Caption = 'Save Report File'
        OnClick = SaveReportFile1Click
      end
      object Exit1: TMenuItem
        Caption = 'Exit'
        OnClick = EXIT
      end
    end
    object Log1: TMenuItem
      Caption = 'Log'
      object CaptureData1: TMenuItem
        Caption = 'Capture Data'
        Hint = 'Saves Data in TXRXData.Dat'
        OnClick = CaptureData1Click
      end
    end
    object AFT1: TMenuItem
      Caption = 'AFT'
      object SaveAFTDefaults1: TMenuItem
        Caption = 'Save AFT Defaults'
        OnClick = SaveAFTDefaults1Click
      end
      object RestoreAFTdefaults1: TMenuItem
        Caption = 'Restore AFT defaults'
        OnClick = RestoreAFTdefaults1Click
      end
    end
    object Help1: TMenuItem
      Caption = 'Help'
      object About1: TMenuItem
        Caption = 'About'
        OnClick = About1Click
      end
      object Help2: TMenuItem
        Caption = 'Help'
        OnClick = Help2Click
      end
      object ActiveHelp1: TMenuItem
        Caption = 'Active Help'
        OnClick = ActiveHelp1Click
      end
    end
  end
  object SaveDialog1: TSaveDialog
    DefaultExt = 'dat'
    FileName = 'TxRxData.Dat'
    Filter = 'Dat File (*.dat)|*.dat|Any File (*.*)|*.*'
    Left = 488
    Top = 560
  end
  object Timer1: TTimer
    Enabled = False
    Interval = 50
    OnTimer = Timer1Timer
    Left = 424
    Top = 560
  end
  object ProgRateTimer: TTimer
    Enabled = False
    OnTimer = ProgRateTimerTimer
    Left = 456
    Top = 560
  end
  object SaveDefaults: TSaveDialog
    DefaultExt = '*.set'
    Filter = 'Setup File (*.set)|*.set|Any File|*.*'
    Title = 'Save SAS Defaults'
    Left = 520
    Top = 560
  end
  object RestoreDefaults: TOpenDialog
    DefaultExt = '*.set'
    Filter = 'Set Files (*.set)|*.set|Any File (*.*)|*.*'
    Title = 'Restore Defaults'
    Left = 552
    Top = 560
  end
end