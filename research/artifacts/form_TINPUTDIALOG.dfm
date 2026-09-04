object InputDialog: TInputDialog
  Left = 517
  Top = 216
  ActiveControl = Edit1
  BorderIcons = []
  BorderStyle = bsDialog
  Caption = 'Dialog'
  ClientHeight = 214
  ClientWidth = 313
  Color = clBtnFace
  Font.Charset = DEFAULT_CHARSET
  Font.Color = clWindowText
  Font.Height = -11
  Font.Name = 'MS Sans Serif'
  Font.Style = []
  OldCreateOrder = True
  Position = poScreenCenter
  OnClose = FormClose
  PixelsPerInch = 96
  TextHeight = 13
  object Bevel1: TBevel
    Left = 8
    Top = 8
    Width = 297
    Height = 161
    Shape = bsFrame
  end
  object Message: TLabel
    Left = 40
    Top = 64
    Width = 43
    Height = 13
    Caption = 'Message'
  end
  object Image1: TImage
    Left = 24
    Top = 16
    Width = 49
    Height = 41
    Picture.Data = {BINARY 5842 bytes}
  end
  object OKBtn: TButton
    Left = 112
    Top = 180
    Width = 90
    Height = 25
    Caption = 'OK'
    Default = True
    ModalResult = 1
    TabOrder = 1
  end
  object CancelBtn: TButton
    Left = 223
    Top = 180
    Width = 82
    Height = 25
    Cancel = True
    Caption = 'Cancel'
    ModalResult = 2
    TabOrder = 2
    Visible = False
  end
  object Edit1: TEdit
    Left = 40
    Top = 88
    Width = 209
    Height = 21
    Hint = 'Enter a value'
    AutoSelect = False
    PopupMenu = PopupMenu1
    TabOrder = 0
    Text = 'Edit1'
    OnChange = Edit1Change
  end
  object ComboBox1: TComboBox
    Left = 40
    Top = 112
    Width = 209
    Height = 21
    ItemHeight = 13
    TabOrder = 3
    Text = 'ComboBox1'
  end
  object PopupMenu1: TPopupMenu
    Left = 216
    Top = 24
    object Keypad1: TMenuItem
      Caption = 'Keypad'
      OnClick = Keypad1Click
    end
  end
end