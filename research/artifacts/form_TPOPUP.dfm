object POPUP: TPOPUP
  Left = 797
  Top = 196
  BorderStyle = bsDialog
  Caption = 'Key Pad'
  ClientHeight = 270
  ClientWidth = 156
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -11
  Font.Name = 'MS Sans Serif'
  Font.Style = []
  FormStyle = fsStayOnTop
  OldCreateOrder = True
  PixelsPerInch = 96
  TextHeight = 13
  object Keys: TGroupBox
    Left = 0
    Top = 8
    Width = 153
    Height = 257
    Caption = 'Keys'
    TabOrder = 0
    object Button1: TButton
      Left = 8
      Top = 16
      Width = 41
      Height = 33
      Caption = '1'
      TabOrder = 0
      OnClick = Button1Click
    end
    object Button2: TButton
      Left = 56
      Top = 16
      Width = 41
      Height = 33
      Caption = '2'
      TabOrder = 1
      OnClick = Button1Click
    end
    object Button3: TButton
      Left = 104
      Top = 16
      Width = 41
      Height = 33
      Caption = '3'
      TabOrder = 2
      OnClick = Button1Click
    end
    object Button5: TButton
      Left = 56
      Top = 56
      Width = 41
      Height = 33
      Caption = '5'
      TabOrder = 3
      OnClick = Button1Click
    end
    object Button6: TButton
      Left = 104
      Top = 56
      Width = 41
      Height = 33
      Caption = '6'
      TabOrder = 4
      OnClick = Button1Click
    end
    object Button7: TButton
      Left = 8
      Top = 96
      Width = 41
      Height = 33
      Caption = '7'
      TabOrder = 5
      OnClick = Button1Click
    end
    object Button8: TButton
      Left = 56
      Top = 96
      Width = 41
      Height = 33
      Caption = '8'
      TabOrder = 6
      OnClick = Button1Click
    end
    object Button9: TButton
      Left = 104
      Top = 96
      Width = 41
      Height = 33
      Caption = '9'
      TabOrder = 7
      OnClick = Button1Click
    end
    object Button10: TButton
      Left = 56
      Top = 136
      Width = 41
      Height = 33
      Caption = '0'
      TabOrder = 8
      OnClick = Button1Click
    end
    object Button4: TButton
      Left = 8
      Top = 56
      Width = 41
      Height = 33
      Caption = '4'
      TabOrder = 9
      OnClick = Button1Click
    end
    object Button11: TButton
      Left = 8
      Top = 136
      Width = 41
      Height = 33
      Caption = 'S A'
      TabOrder = 10
      Visible = False
    end
    object Button12: TButton
      Left = 8
      Top = 176
      Width = 41
      Height = 33
      Caption = 'A'
      TabOrder = 11
      OnClick = Button1Click
    end
    object Button13: TButton
      Left = 56
      Top = 176
      Width = 41
      Height = 33
      Caption = 'B'
      TabOrder = 12
      OnClick = Button1Click
    end
    object Button14: TButton
      Left = 104
      Top = 176
      Width = 41
      Height = 33
      Caption = 'C'
      TabOrder = 13
      OnClick = Button1Click
    end
    object Button15: TButton
      Left = 8
      Top = 216
      Width = 41
      Height = 33
      Caption = 'D'
      TabOrder = 14
      OnClick = Button1Click
    end
    object Button16: TButton
      Left = 56
      Top = 216
      Width = 41
      Height = 33
      Caption = 'E'
      TabOrder = 15
      OnClick = Button1Click
    end
    object Button17: TButton
      Left = 104
      Top = 216
      Width = 41
      Height = 33
      Caption = 'F'
      TabOrder = 16
      OnClick = Button1Click
    end
  end
end