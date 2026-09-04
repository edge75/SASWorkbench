object SASAboutBox: TSASAboutBox
  Left = 433
  Top = 180
  BorderIcons = []
  BorderStyle = bsDialog
  Caption = 'IGT SAS Simulator'
  ClientHeight = 312
  ClientWidth = 367
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -11
  Font.Name = 'MS Sans Serif'
  Font.Style = []
  OldCreateOrder = True
  Position = poScreenCenter
  OnActivate = FormActivate
  OnClose = FormClose
  PixelsPerInch = 96
  TextHeight = 13
  object Panel1: TPanel
    Left = 8
    Top = 56
    Width = 353
    Height = 217
    BevelInner = bvRaised
    BevelOuter = bvLowered
    ParentColor = True
    TabOrder = 0
    object VersionLAB: TLabel
      Left = 112
      Top = 24
      Width = 73
      Height = 13
      Caption = 'Version 1.1'
      IsControl = True
    end
    object Copyright: TLabel
      Left = 40
      Top = 56
      Width = 264
      Height = 13
      Caption = 'Copyright (c) 1997-2005 International Game Technology'
      IsControl = True
    end
    object Image1: TImage
      Left = 272
      Top = 0
      Width = 65
      Height = 49
      Picture.Data = {BINARY 69090 bytes}
      Transparent = True
      Visible = False
    end
    object ProductNameLab: TLabel
      Left = 120
      Top = 8
      Width = 67
      Height = 13
      Caption = 'SAS Simulator'
    end
    object SASVersionLabel: TLabel
      Left = 96
      Top = 40
      Width = 109
      Height = 13
      Caption = 'Supports SAS Versions'
    end
    object Memo1: TMemo
      Left = 8
      Top = 72
      Width = 337
      Height = 137
      BorderStyle = bsNone
      Color = clBtnFace
      Lines.Strings = ('This  program uses multiple threads and will support  40 ms polling ', 'cycles and progressive broadcasts. As   a result a 200 mhz Pentium is ', 'required to run properly.  ', '', 'Conditions for use of this program:', 'This is a prototype test program, and should be treated as such. This ', 'program is supplied AS IS, no guarantees of operation,   warranties, or ', 'support is given. The user assumes all responsibility for any damage this ', 'program may cause.  This program is used with the express permission ', 'of IGT.', 'Any unauthorized distribution or duplication is strictly prohibited.')
      TabOrder = 0
    end
  end
  object OKButton: TButton
    Left = 72
    Top = 280
    Width = 75
    Height = 25
    Caption = 'I Agree'
    Default = True
    ModalResult = 1
    TabOrder = 1
  end
  object Button1: TButton
    Left = 200
    Top = 280
    Width = 75
    Height = 25
    Caption = 'I Don''t Agree'
    ModalResult = 2
    TabOrder = 2
  end
  object Timer1: TTimer
    Enabled = False
    Interval = 50
    OnTimer = Timer1Timer
    Left = 16
    Top = 272
  end
end