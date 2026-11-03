Attribute VB_Name = "cInterimReport"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

Private Const repCfgKey As String = "{report_config}"
Private Const sysDatKey As String = "{system_data}"

'header names, config values
Private Const colPersCode As Long = 2
Private Const colLastName As Long = 3
Private Const colFirstName As Long = 4
Private Const colTaxFree As Long = 5
Private Const colTaxFree610 As Long = 15
Private Const colTaxFree640 As Long = 16
Private Const colTaxFree650 As Long = 17
Private Const colResidence As Long = 6
Private Const colPension As Long = 7
Private Const colControlDate As Long = 8
Private Const colStartDate As Long = 9
Private Const colWorkType As Long = 10
Private Const colEntryState As Long = 11
Private Const colEndDate As Long = 12
Private Const colBreak As Long = 13
Private Const colSumTotal As Long = 14

'headers default order
Private Const posPersCode As Long = 1
Private Const posLastName As Long = 2
Private Const posFirstName As Long = 3
Private Const posTaxFree As Long = 4
Private Const posResidence As Long = 5
Private Const posPension As Long = 6
Private Const posControlDate As Long = 7
Private Const posStartDate As Long = 8
Private Const posWorkType As Long = 9
Private Const posEntryState As Long = 10
Private Const posEndDate As Long = 11
Private Const posTaxFree610 As Long = 12
Private Const posTaxFree640 As Long = 13
Private Const posTaxFree650 As Long = 14

Private Const dfltPosSumStart As Long = 4
Private posSumStart As Long
Private shftVal As Long

Private fndPersCode As Long
Private fndLastName As Long
Private fndFirstName As Long
Private fndTaxFree As Long
Private fndTaxFree610 As Long
Private fndTaxFree640 As Long
Private fndTaxFree650 As Long
Private fndResidence As Long
Private fndPension As Long
Private fndControlDate As Long
Private fndStartDate As Long
Private fndWorkType As Long
Private fndEntryState As Long
Private fndEndDate As Long
Private fndSumTotal As Long

Private cfgWs As Worksheet
Private repWs As Worksheet
Private cfgRow As Long
Private initTrue As Boolean
Private initRep As Boolean

Public Function InitData(ByRef wsCfg As Worksheet) As Boolean
    Dim i As Integer
    Dim posRange As Range
    
    If wsCfg Is Nothing Then Exit Function
    
    Set cfgWs = wsCfg
    
    Set posRange = cfgWs.Range("A1").EntireColumn.Find(What:=repCfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else cfgRow = posRange.Row + 2
    
    If IsNumeric(cfgWs.Cells(cfgRow, colBreak).Value) Then posSumStart = CLng(cfgWs.Cells(cfgRow, colBreak).Value) Else posSumStart = dfltPosSumStart
    shftVal = 1
    
    initTrue = True
    InitData = True
End Function

Public Function InitReport(ByRef wsRep As Worksheet, ByRef arrNames() As String) As Boolean
    Dim i As Long
    If cfgWs Is Nothing Or wsRep Is Nothing Or Not IsArray(arrNames) Then Exit Function
    
    Set repWs = wsRep
    shftVal = UBound(arrNames) - LBound(arrNames) + 2
    
    i = posSumStart + shftVal - 1
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colSumTotal).Value)
    If posPersCode < posSumStart Then i = posPersCode Else i = posPersCode + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colPersCode).Value)
    If posLastName < posSumStart Then i = posLastName Else i = posLastName + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colLastName).Value)
    If posFirstName < posSumStart Then i = posFirstName Else i = posFirstName + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colFirstName).Value)
    If posTaxFree < posSumStart Then i = posTaxFree Else i = posTaxFree + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colTaxFree).Value)
    If posStartDate < posSumStart Then i = posStartDate Else i = posStartDate + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colStartDate).Value)
    If posEndDate < posSumStart Then i = posEndDate Else i = posEndDate + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colEndDate).Value)
    If posEntryState < posSumStart Then i = posEntryState Else i = posEntryState + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colEntryState).Value)
    If posWorkType < posSumStart Then i = posWorkType Else i = posWorkType + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colWorkType).Value)
    If posResidence < posSumStart Then i = posResidence Else i = posResidence + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colResidence).Value)
    If posPension < posSumStart Then i = posPension Else i = posPension + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colPension).Value)
    If posControlDate < posSumStart Then i = posControlDate Else i = posControlDate + shftVal
    repWs.Cells(1, i).Value = CStr(cfgWs.Cells(cfgRow, colControlDate).Value)
    
    For i = LBound(arrNames) To UBound(arrNames)
        repWs.Cells(1, posSumStart + i - LBound(arrNames)).Value = arrNames(i)
    Next i
    
    initRep = True
    InitReport = True
End Function

Property Get ReportName() As String
    If Not cfgWs Is Nothing Then ReportName = CStr(cfgWs.Cells(cfgRow, 1).Value)
End Property

Property Get PersCodeHeader() As String
    PersCodeHeader = CStr(cfgWs.Cells(cfgRow, colPersCode).Value)
End Property

Property Get LastNameHeader() As String
    LastNameHeader = CStr(cfgWs.Cells(cfgRow, colLastName).Value)
End Property

Property Get FirstNameHeader() As String
    FirstNameHeader = CStr(cfgWs.Cells(cfgRow, colFirstName).Value)
End Property

Property Get TaxFreeHeader() As String
    TaxFreeHeader = CStr(cfgWs.Cells(cfgRow, colTaxFree).Value)
End Property

Property Get TaxFree610Header() As String
    TaxFree610Header = CStr(cfgWs.Cells(cfgRow, colTaxFree610).Value)
End Property

Property Get TaxFree640Header() As String
    TaxFree640Header = CStr(cfgWs.Cells(cfgRow, colTaxFree640).Value)
End Property

Property Get TaxFree650Header() As String
    TaxFree650Header = CStr(cfgWs.Cells(cfgRow, colTaxFree650).Value)
End Property

Property Get ResidenceHeader() As String
    ResidenceHeader = CStr(cfgWs.Cells(cfgRow, colResidence).Value)
End Property

Property Get PensionHeader() As String
    PensionHeader = CStr(cfgWs.Cells(cfgRow, colPension).Value)
End Property

Property Get ControlDateHeader() As String
    ControlDateHeader = CStr(cfgWs.Cells(cfgRow, colControlDate).Value)
End Property

Property Get SumTotalHeader() As String
    SumTotalHeader = CStr(cfgWs.Cells(cfgRow, colSumTotal).Value)
End Property

Property Get StartDateHeader() As String
    StartDateHeader = CStr(cfgWs.Cells(cfgRow, colStartDate).Value)
End Property

Property Get WorkTypeHeader() As String
    WorkTypeHeader = CStr(cfgWs.Cells(cfgRow, colWorkType).Value)
End Property

Property Get EntryStateHeader() As String
    EntryStateHeader = CStr(cfgWs.Cells(cfgRow, colEntryState).Value)
End Property

Property Get EndDateHeader() As String
    EndDateHeader = CStr(cfgWs.Cells(cfgRow, colEndDate).Value)
End Property

Property Get ReportFirstColumn() As Long
    ReportFirstColumn = 1
End Property

Property Get ReportLastColumn() As Long
    If posEndDate < posSumStart Then ReportLastColumn = posEndDate + shftVal - 1 Else ReportLastColumn = posEndDate + shftVal
End Property

Property Get PersCodeColumn() As Long
    If posPersCode < posSumStart Then PersCodeColumn = posPersCode Else PersCodeColumn = posPersCode + shftVal
End Property

Property Get LastNameColumn() As Long
    If posLastName < posSumStart Then LastNameColumn = posLastName Else LastNameColumn = posLastName + shftVal
End Property

Property Get FirstNameColumn() As Long
    If posFirstName < posSumStart Then FirstNameColumn = posFirstName Else FirstNameColumn = posFirstName + shftVal
End Property

Property Get TaxFreeColumn() As Long
    If posTaxFree < posSumStart Then TaxFreeColumn = posTaxFree Else TaxFreeColumn = posTaxFree + shftVal
End Property

Property Get TaxFree610Column() As Long
    If posTaxFree610 < posSumStart Then TaxFree610Column = posTaxFree610 Else TaxFree610Column = posTaxFree610 + shftVal
End Property

Property Get TaxFree640Column() As Long
    If posTaxFree640 < posSumStart Then TaxFree640Column = posTaxFree640 Else TaxFree640Column = posTaxFree640 + shftVal
End Property

Property Get TaxFree650Column() As Long
    If posTaxFree650 < posSumStart Then TaxFree650Column = posTaxFree650 Else TaxFree650Column = posTaxFree650 + shftVal
End Property

Property Get ResidenceColumn() As Long
    If posResidence < posSumStart Then ResidenceColumn = posResidence Else ResidenceColumn = posResidence + shftVal
End Property

Property Get PensionColumn() As Long
    If posPension < posSumStart Then PensionColumn = posPension Else PensionColumn = posPension + shftVal
End Property

Property Get ControlDateColumn() As Long
    If posControlDate < posSumStart Then ControlDateColumn = posControlDate Else ControlDateColumn = posControlDate + shftVal
End Property

Property Get StartDateColumn() As Long
    If posStartDate < posSumStart Then StartDateColumn = posStartDate Else StartDateColumn = posStartDate + shftVal
End Property

Property Get WorkTypeColumn() As Long
    If posWorkType < posSumStart Then WorkTypeColumn = posWorkType Else WorkTypeColumn = posWorkType + shftVal
End Property

Property Get EntryStateColumn() As Long
    If posEntryState < posSumStart Then EntryStateColumn = posEntryState Else EntryStateColumn = posEntryState + shftVal
End Property

Property Get EndDateColumn() As Long
    If posEndDate < posSumStart Then EndDateColumn = posEndDate Else EndDateColumn = posEndDate + shftVal
End Property

Property Get SumTotalColumn() As Long
    SumTotalColumn = posSumStart + shftVal - 1
End Property

Property Get SumStartColumn() As Long
    SumStartColumn = posSumStart
End Property

Property Get PersCodeFoundCol() As Long
    PersCodeFoundCol = fndPersCode
End Property

Property Get LastNameFoundCol() As Long
    LastNameFoundCol = fndLastName
End Property

Property Get FirstNameFoundCol() As Long
    FirstNameFoundCol = fndFirstName
End Property

Property Get TaxFreeFoundCol() As Long
    TaxFreeFoundCol = fndTaxFree
End Property

Property Get TaxFree610FoundCol() As Long
    TaxFree610FoundCol = fndTaxFree610
End Property

Property Get TaxFree640FoundCol() As Long
    TaxFree640FoundCol = fndTaxFree640
End Property

Property Get TaxFree650FoundCol() As Long
    TaxFree650FoundCol = fndTaxFree650
End Property

Property Get ResidenceFoundCol() As Long
    ResidenceFoundCol = fndResidence
End Property

Property Get PensionFoundCol() As Long
    PensionFoundCol = fndPension
End Property

Property Get ControlDateFoundCol() As Long
    ControlDateFoundCol = fndControlDate
End Property

Property Get SumTotalFoundCol() As Long
    SumTotalFoundCol = fndSumTotal
End Property

Property Get StartDateFoundCol() As Long
    StartDateFoundCol = fndStartDate
End Property

Property Get WorkTypeFoundCol() As Long
    WorkTypeFoundCol = fndWorkType
End Property

Property Get EntryStateFoundCol() As Long
    EntryStateFoundCol = fndEntryState
End Property

Property Get EndDateFoundCol() As Long
    EndDateFoundCol = fndEndDate
End Property

Public Function FindHeader(ByRef sRange As Range, ByVal hrName As String, ByRef outCol As Long) As Boolean
    Dim fRange As Range
    outCol = 1
    Set fRange = sRange.Find(What:=hrName, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function
    outCol = fRange.Column
    FindHeader = True
End Function

Public Function CheckHeaders(ByRef sRange As Range) As Boolean
    Dim fRange As Range
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colPersCode).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndPersCode = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colLastName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndLastName = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colFirstName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndFirstName = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colTaxFree).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndTaxFree = fRange.Column
    
    'Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colTaxFree610).Value), LookIn:=xlformulas, LookAt:=xlWhole, MatchCase:=False)
    'If fRange Is Nothing Then Exit Function Else fndTaxFree610 = fRange.Column
    
    'Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colTaxFree640).Value), LookIn:=xlformulas, LookAt:=xlWhole, MatchCase:=False)
    'If fRange Is Nothing Then Exit Function Else fndTaxFree640 = fRange.Column
    
    'Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colTaxFree650).Value), LookIn:=xlformulas, LookAt:=xlWhole, MatchCase:=False)
    'If fRange Is Nothing Then Exit Function Else fndTaxFree650 = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colResidence).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndResidence = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colPension).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndPension = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colControlDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndControlDate = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colStartDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndStartDate = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colWorkType).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndWorkType = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colEntryState).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndEntryState = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colEndDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndEndDate = fRange.Column
    
    Set fRange = sRange.Find(What:=CStr(cfgWs.Cells(cfgRow, colSumTotal).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else fndSumTotal = fRange.Column
    
    CheckHeaders = True

End Function
