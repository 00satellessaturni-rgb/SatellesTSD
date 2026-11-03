Attribute VB_Name = "cTsdTable"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

Private Const defParamYes As String = "Y"
Private Const defParamNo As String = "N"

Private Const errHeaderName As String = "Hoiatused, vead"

Private Const tsdL1CfgKey As String = "{tsdl1_config}"
Private Const tsdL2CfgKey As String = "{tsdl2_config}"
Private Const taxCfgKey As String = "{taxes_config}"
Private Const tsdL1MtrxKey As String = "{tsdl1-tor-cat_matrix}"
Private Const tsdL2MtrxKey As String = "{tsdl2-tor-cat_matrix}"

Private Const frmlaStart As String = "=IF(LEN(OFFSET('*s'!R#rC#c,0,0))>0,"
Private Const frmlaEnd As String = ","""")"
Private Const frmlaEmpt As String = """"""""""""""
Private Const frmlaTxt As String = """""""""&OFFSET('*s'!R#rC#c,0,0)&"""""""""
Private Const frmlaSum As String = """""""""&TEXT(OFFSET('*s'!R#rC#c,0,0),""0,00"")&"""""""""
Private Const frmlaSmc As String = "&"";""&"

Private Const tor_entryStateValid As String = "2"
Private Const tor_entryStatePaused As String = "3"
Private Const tor_entryStateFinished As String = "4"

Private Const lisa1_rowStart As Long = 4
Private Const lisa1_colStart As Long = 1

Private Const col_1000 As Long = 1 'pers code
Private Const col_1010 As Long = 2 'name
Private Const col_1020 As Long = 3 'payout code
Private Const col_1030 As Long = 4 'gross sum
Private Const col_1050 As Long = 5 'A1/ E101
Private Const col_1060 As Long = 6 'soc taxed sum
Private Const col_1070 As Long = 7 'soctax free sum 1
Private Const col_1080 As Long = 8 'soctax free sum 2
Private Const col_1090 As Long = 9 'soc taxed sum gap
Private Const col_1100 As Long = 10 'soc tax
Private Const col_1110 As Long = 11 'pens payment
Private Const col_1120 As Long = 12 'unemp ins taxed sum
Private Const col_1130 As Long = 13 'unemp ins pay person
Private Const col_1140 As Long = 14 'unemp ins pay company
Private Const col_1170 As Long = 15 'income tax
Private Const col_1160_610 As Long = 16 'tax free 610
Private Const col_1160_640 As Long = 17 'tax free 640
Private Const col_1160_650 As Long = 18 'tax free 650

Private Const lisa2_rowStart As Long = 4
Private Const lisa2_colStart As Long = 1

Private Const col_2000 As Long = 1 'pers code
Private Const col_2010 As Long = 2 'name
Private Const col_2020 As Long = 3 'country code
Private Const col_2030 As Long = 4 'payout code
Private Const col_2040 As Long = 5 'gross sum
Private Const col_2060 As Long = 6 'A1/ E101
Private Const col_2070 As Long = 7 'soc taxed sum
Private Const col_2080 As Long = 8 'soctax free sum 1
Private Const col_2090 As Long = 9 'soctax free sum 2
Private Const col_2100 As Long = 10 'soc taxed sum gap
Private Const col_2110 As Long = 11 'soc tax
Private Const col_2120 As Long = 12 'unemp ins taxed sum
Private Const col_2130 As Long = 13 'unemp ins pay person
Private Const col_2140 As Long = 14 'unemp ins pay company
Private Const col_2150 As Long = 15 'income taxed sum
Private Const col_2160 As Long = 16 'income tax rate
Private Const col_2170 As Long = 17 'income tax
Private Const col_2154_610 As Long = 18 'tax free 610
Private Const col_2154_650 As Long = 19 'tax free 650

Private Const colIncTaxRate As Long = 2
Private Const colPensRate As Long = 7
Private Const colUnEmInsRatePers As Long = 3
Private Const colUnEmInsRateComp As Long = 4
Private Const colTaxFreeSumYearCommon As Long = 8
Private Const colTaxFreeSumYearRetired As Long = 12
Private Const colTaxFreeTier1Year As Long = 9
Private Const colTaxFreeTier2Year As Long = 10
Private Const colTaxFreeT2Divider As Long = 11
Private Const colSocialTaxRate As Long = 5
Private Const colSocialMinSumMonth As Long = 6

Private Const colMtrxMin As Long = 2

Private Const indxMtrxOutCode As Long = 0
Private Const indxMtrxIncTax As Long = 1
Private Const indxMtrxInsPers As Long = 2
Private Const indxMtrxInsComp As Long = 3
Private Const indxMtrxSocTax As Long = 4
Private Const indxMtrxSocMin As Long = 5
Private Const indxMtrxPens As Long = 6
Private Const indxMtrxTaxFree As Long = 7

'config vars
Private rowCfgL1 As Long
Private rowCodeL1 As Long
Private colCodeL1 As Long
Private rowDescCodeL1 As Long
Private colDescCodeL1 As Long
Private rowIsSumL1 As Long
Private rowIsCalcL1 As Long
Private rowCfgL2 As Long
Private rowCodeL2 As Long
Private colCodeL2 As Long
Private rowDescCodeL2 As Long
Private colDescCodeL2 As Long
Private rowIsSumL2 As Long
Private rowIsCalcL2 As Long
Private rowDescRate As Long
Private rowRate As Long
Private rowL1MtrxInCode As Long
Private rowL1MtrxDesc As Long
Private rowL1MtrxCfg As Long
Private rowL2MtrxInCode As Long
Private rowL2MtrxDesc As Long
Private rowL2MtrxCfg As Long

Private lisa1_headerCount As Long
Private lisa2_headerCount As Long

Private cfgWs As Worksheet
Private rep1Ws As Worksheet
Private out1Ws As Worksheet
Private rep2Ws As Worksheet
Private out2Ws As Worksheet

'inputs
Private curEntryTypeId As String
Private curEntryStateId As String
Private curPeriod As String
Private curStartDate As String
Private curEndDate As String
Private curResident As Boolean
Private curPension As Boolean
Private curPensionRate As Double
Private curNetSum As Double
Private curExtraIncomeFlatSum As Double
Private curFreeData As String
Private curFree610Data As String
Private curFree640Data As String
Private curFree650Data As String
Private curRetired As Boolean
Private torValid As Boolean
Private rdntCheck As Boolean
Private pnsnCheck As Boolean

'outputs
Private curPersCode As String '1:1000,2:2000
Private curFullName As String '1:1010,2:2010
Private curCountryCode As String '2:2020
Private curPayCode As String '1:1020,2:2030
Private curGrossSum As Double '1:1030,2:2040
'1:1050,2:2060 A1/E101 (not used)
Private curSocSum As Double '1:1060,2:2070
Private curSocFreeSum1 As Double '1:1070,2:2080
Private curSocFreeSum2 As Double '1:1080,2:2090
Private curSocGapSum As Double '1:1090,2:2100
Private curSocPay As Double '1:1100,2:2110
Private curPensPay As Double '1:1110
Private curUnEmInsSum As Double '1:1120,2:2120
Private curUnEmInsPersPay As Double '1:1130,2:2130
Private curUnEmInsCompPay As Double '1:1140,2:2140
Private curIncTaxRate As Double '2:2160
Private curIncTaxPay As Double '1:1170,2:2170
Private curFree610Sum As Double '1:1160_610,2:2154_610
Private curFree640Sum As Double '1:1160_640
Private curFree650Sum As Double '1:1160_650,2:2154_650

'process vars
Private mtrxOutCode As String
Private mtrxIncTax As Boolean
Private mtrxSocTax As Boolean
Private mtrxSocMin As Boolean
Private mtrxPens As Boolean
Private mtrxInsPers As Boolean
Private mtrxInsComp As Boolean
Private mtrxTaxFree As Boolean

Private initTrue As Boolean
Private curFrmla1 As String
Private curFrmla2 As String
Private errString As String
Private curRowL1 As Long
Private curRowL2 As Long
Private curMtrxCol As Long
Private oCalc As cCalc2026

Public Function InitData(ByRef wsCfg As Worksheet, ByRef wbRep As Workbook, Optional ByVal extraTsdCount As Integer) As Boolean
    Dim fRange As Range
    Dim i As Long, j As Long
    
    If wsCfg Is Nothing Or wbRep Is Nothing Then Exit Function
    
    Set cfgWs = wsCfg
    
    Set oCalc = New cCalc2026
    If Not oCalc.InitData(cfgWs) Then Exit Function
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=tsdL1CfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then
        Exit Function
    Else
        rowCfgL1 = fRange.Row
        rowCodeL1 = rowCfgL1 + 1
        rowDescCodeL1 = rowCfgL1 + 2
        rowIsSumL1 = rowCfgL1 + 3
        rowIsCalcL1 = rowCfgL1 + 4
        colCodeL1 = cfgWs.Cells(rowCodeL1, 2).End(xlToRight).Column
        colDescCodeL1 = cfgWs.Cells(rowDescCodeL1, 2).End(xlToRight).Column
        lisa1_headerCount = colCodeL1 - 1
    End If
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=tsdL2CfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then
        Exit Function
    Else
        rowCfgL2 = fRange.Row
        rowCodeL2 = rowCfgL2 + 1
        rowDescCodeL2 = rowCfgL2 + 2
        rowIsSumL2 = rowCfgL2 + 3
        rowIsCalcL2 = rowCfgL2 + 4
        colCodeL2 = cfgWs.Cells(rowCodeL2, 2).End(xlToRight).Column
        colDescCodeL2 = cfgWs.Cells(rowDescCodeL2, 2).End(xlToRight).Column
        lisa2_headerCount = colCodeL2 - 1
    End If
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=taxCfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then
        Exit Function
    Else
        rowDescRate = fRange.Row + 1
        rowRate = fRange.Row + 2
    End If
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=tsdL1MtrxKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then
        Exit Function
    Else
        rowL1MtrxInCode = fRange.Row + 1
        rowL1MtrxDesc = fRange.Row + 2
        rowL1MtrxCfg = fRange.Row + 3
    End If
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=tsdL2MtrxKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then
        Exit Function
    Else
        rowL2MtrxInCode = fRange.Row + 1
        rowL2MtrxDesc = fRange.Row + 2
        rowL2MtrxCfg = fRange.Row + 3
    End If
    
    If wbRep.Worksheets.Count > 0 Then
        Set rep1Ws = wbRep.Worksheets.Add(Before:=wbRep.Worksheets(1))
    Else
        Set rep1Ws = wbRep.Worksheets.Add
    End If
    If rep1Ws Is Nothing Then Exit Function
    rep1Ws.Name = "TSD Lisa 1 kontroll"
    
    Set rep2Ws = wbRep.Worksheets.Add(After:=wbRep.Worksheets(1))
    If rep2Ws Is Nothing Then Exit Function
    rep2Ws.Name = "TSD Lisa 2 kontroll"
    
    Set out1Ws = wbRep.Worksheets.Add(After:=wbRep.Worksheets(2))
    If out1Ws Is Nothing Then Exit Function
    out1Ws.Name = "TSD Lisa 1 eksport"
    
    Set out2Ws = wbRep.Worksheets.Add(After:=wbRep.Worksheets(3))
    If out2Ws Is Nothing Then Exit Function
    out2Ws.Name = "TSD Lisa 2 eksport"
    
    For i = 2 To cfgWs.Cells(rowDescRate, 2).End(xlToRight).Column
        rep1Ws.Cells(1, 2 * (i - 1) - 1).Value = CStr(cfgWs.Cells(rowDescRate, i).Value)
        rep1Ws.Cells(1, 2 * (i - 1)).Value = CStr(cfgWs.Cells(rowRate, i).Value)
    Next i
    
    For i = 2 To colCodeL1
        j = i - 2
        rep1Ws.Cells(2, lisa1_colStart + j).Value = CStr(cfgWs.Cells(rowDescCodeL1, i).Value)
        rep1Ws.Cells(3, lisa1_colStart + j).Value = CStr(cfgWs.Cells(rowCodeL1, i).Value)
        If i = 2 Then curFrmla1 = Replace(Replace(frmlaStart, "#c", lisa1_colStart), "*s", rep1Ws.Name) Else curFrmla1 = curFrmla1 & frmlaSmc
        If UCase(CStr(cfgWs.Cells(rowIsCalcL1, i).Value)) = defParamYes Then
            curFrmla1 = curFrmla1 & frmlaEmpt
        Else
            If UCase(CStr(cfgWs.Cells(rowIsSumL1, i).Value)) = defParamYes Then
                curFrmla1 = curFrmla1 & Replace(Replace(frmlaSum, "#c", lisa1_colStart + j), "*s", rep1Ws.Name)
            Else
                curFrmla1 = curFrmla1 & Replace(Replace(frmlaTxt, "#c", lisa1_colStart + j), "*s", rep1Ws.Name)
            End If
        End If
    Next i
    
    rep1Ws.Cells(3, lisa1_colStart + lisa1_headerCount).Value = errHeaderName
    curFrmla1 = curFrmla1 & frmlaEnd
    
    'For i = 1 To 1000
    '    out1Ws.Cells(i, 1).Value = Replace(curFrmla, "#r", i + 2)
    'Next i
    
    For i = 2 To cfgWs.Cells(rowDescRate, 2).End(xlToRight).Column
        rep2Ws.Cells(1, 2 * (i - 1) - 1).Value = CStr(cfgWs.Cells(rowDescRate, i).Value)
        rep2Ws.Cells(1, 2 * (i - 1)).Value = CStr(cfgWs.Cells(rowRate, i).Value)
    Next i
    
    For i = 2 To colCodeL2
        j = i - 2
        rep2Ws.Cells(2, lisa2_colStart + j).Value = CStr(cfgWs.Cells(rowDescCodeL2, i).Value)
        rep2Ws.Cells(3, lisa2_colStart + j).Value = CStr(cfgWs.Cells(rowCodeL2, i).Value)
        If i = 2 Then curFrmla2 = Replace(Replace(frmlaStart, "#c", lisa2_colStart), "*s", rep2Ws.Name) Else curFrmla2 = curFrmla2 & frmlaSmc
        If UCase(CStr(cfgWs.Cells(rowIsCalcL2, i).Value)) = defParamYes Then
            curFrmla2 = curFrmla2 & frmlaEmpt
        Else
            If UCase(CStr(cfgWs.Cells(rowIsSumL2, i).Value)) = defParamYes Then
                curFrmla2 = curFrmla2 & Replace(Replace(frmlaTxt, "#c", lisa2_colStart + j), "*s", rep2Ws.Name)
            Else
                curFrmla2 = curFrmla2 & Replace(Replace(frmlaTxt, "#c", lisa2_colStart + j), "*s", rep2Ws.Name)
            End If
        End If
    Next i
    
    rep2Ws.Cells(3, lisa2_colStart + lisa2_headerCount).Value = errHeaderName
    curFrmla2 = curFrmla2 & frmlaEnd
    
    'For i = 1 To 1000
    '    out2Ws.Cells(i, 1).Value = Replace(curFrmla, "#r", i + 2)
    'Next i
    
    curRowL1 = lisa1_rowStart
    curRowL2 = lisa2_rowStart
    initTrue = True
    InitData = True
End Function

Property Let DatePeriod(ByVal data As String)
    curPeriod = data
End Property

Property Get DatePeriod() As String
    DatePeriod = curPeriod
End Property

Property Let PersonalCode(ByVal data As String)
    curPersCode = data
End Property

Property Get PersonalCode() As String
    PersonalCode = curPersCode
End Property

Property Get FirstLastName() As String
    FirstLastName = curFullName
End Property

Property Let FirstLastName(ByVal data As String)
    curFullName = data
End Property

Property Let IsResident(ByVal data As Boolean)
    curResident = data
End Property

Property Let HasPension(ByVal data As Boolean)
    curPension = data
End Property

Property Let HasPensionRate(ByVal data As Double)
    curPensionRate = data
End Property

Property Let CountryCode(ByVal data As String)
    curCountryCode = data
End Property

Property Let EntryTypeId(ByVal data As String)
    curEntryTypeId = data
End Property

Property Let EntryStateId(ByVal data As String)
    curEntryStateId = data
End Property

Property Let StartDate(ByVal data As String)
    curStartDate = data
End Property

Property Let EndDate(ByVal data As String)
    curEndDate = data
End Property

Property Let NetSum(ByVal data As Double)
    curNetSum = data
End Property

Property Get ExtraIncomeFlatSum() As Double
    ExtraIncomeFlatSum = curExtraIncomeFlatSum
End Property

Property Let ExtraIncomeFlatSum(ByVal data As Double)
    curExtraIncomeFlatSum = data
End Property

Property Let TaxFreeData(ByVal data As String)
    curFreeData = data
End Property

Property Let TaxFree610Data(ByVal data As String)
    curFree610Data = data
End Property

Property Let TaxFree640Data(ByVal data As String)
    curFree640Data = data
End Property

Property Let TaxFree650Data(ByVal data As String)
    curFree650Data = data
End Property

Property Let IsRetired(ByVal data As Boolean)
    curRetired = data
End Property

Property Get TorControlValid() As Boolean
    TorControlValid = torValid
End Property

Property Let TorControlValid(ByVal data As Boolean)
    torValid = data
End Property

Property Get ResidenceCheck() As Boolean
    ResidenceCheck = rdntCheck
End Property

Property Let ResidenceCheck(ByVal data As Boolean)
    rdntCheck = data
End Property

Property Get PensionCheck() As Boolean
    PensionCheck = pnsnCheck
End Property

Property Let PensionCheck(ByVal data As Boolean)
    pnsnCheck = data
End Property

Property Get ErrorList() As String
    ErrorList = errString
End Property

Property Let ErrorList(ByVal data As String)
    errString = mdlFunc.AddText(errString, data)
End Property

Public Function GetWorkIdByName(ByVal wName As String, ByRef wId As String) As Boolean
    Dim sRange As Range, fRange As Range
    Dim wRow As Long
    
    wId = vbNullString
    If Len(wName) = 0 Then Exit Function
    
    Set sRange = cfgWs.Range(cfgWs.Cells(rowL1MtrxDesc, colMtrxMin), cfgWs.Cells(rowL1MtrxDesc, cfgWs.Columns.Count))
    Set fRange = sRange.Find(What:=wName, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    
    If fRange Is Nothing Then Exit Function
    
    wId = CStr(cfgWs.Cells(rowL1MtrxInCode, fRange.Column).Value)
    GetWorkIdByName = True
    
End Function

Public Function ErrorsCheck(ByVal lisaIndx As Integer) As Boolean
    Dim repWsChk As Worksheet
    Dim colChk As Long
    
    Select Case lisaIndx
    Case 1
        Set repWsChk = rep1Ws
        colChk = lisa1_colStart + lisa1_headerCount
    Case 2
        Set repWsChk = rep2Ws
        colChk = lisa2_colStart + lisa2_headerCount
    Case Else
        Exit Function
    End Select
    
    If CStr(repWsChk.Cells(repWsChk.Rows.Count, colChk).End(xlUp).Value) = errHeaderName Then ErrorsCheck = True
    
End Function

Property Get EntryStateValidId() As String
    EntryStateValidId = tor_entryStateValid
End Property

Public Sub GetLisa1String(ByRef dataString As String)
    Dim i As Long
    dataString = vbNullString
    
    For i = 1 To out1Ws.Cells(out1Ws.Rows.Count, 1).End(xlUp).Row
        If Len(out1Ws.Cells(i, 1).Value) > 0 Then
            If Len(dataString) > 0 Then dataString = dataString & vbNewLine
            dataString = dataString & out1Ws.Cells(i, 1).Value
        End If
    Next i
End Sub

Public Sub GetLisa2String(ByRef dataString As String)
    Dim i As Long
    dataString = vbNullString
    
    For i = 1 To out2Ws.Cells(out2Ws.Rows.Count, 1).End(xlUp).Row
        If Len(out2Ws.Cells(i, 1).Value) > 0 Then
            If Len(dataString) > 0 Then dataString = dataString & vbNewLine
            dataString = dataString & out2Ws.Cells(i, 1).Value
        End If
    Next i
End Sub

Private Function getMatrixColumn(ByVal tsdl As Integer) As Boolean
    Dim sRange As Range, fRange As Range
    If Len(curEntryTypeId) = 0 Then Exit Function

    Select Case tsdl
    Case 1
        Set sRange = cfgWs.Range(cfgWs.Cells(rowL1MtrxInCode, colMtrxMin), cfgWs.Cells(rowL1MtrxInCode, cfgWs.Columns.Count))
        If sRange Is Nothing Then Exit Function
        Set fRange = sRange.Find(What:=curEntryTypeId, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If fRange Is Nothing Then Exit Function Else curMtrxCol = fRange.Column
        
        mtrxOutCode = CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxOutCode, curMtrxCol).Value)
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxIncTax, curMtrxCol).Value)) = defParamYes Then mtrxIncTax = True Else mtrxIncTax = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxSocTax, curMtrxCol).Value)) = defParamYes Then mtrxSocTax = True Else mtrxSocTax = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxSocMin, curMtrxCol).Value)) = defParamYes Then mtrxSocMin = True Else mtrxSocMin = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxPens, curMtrxCol).Value)) = defParamYes Then mtrxPens = True Else mtrxPens = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxInsPers, curMtrxCol).Value)) = defParamYes Then mtrxInsPers = True Else mtrxInsPers = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxInsComp, curMtrxCol).Value)) = defParamYes Then mtrxInsComp = True Else mtrxInsComp = False
        If UCase(CStr(cfgWs.Cells(rowL1MtrxCfg + indxMtrxTaxFree, curMtrxCol).Value)) = defParamYes Then mtrxTaxFree = True Else mtrxTaxFree = False
    Case 2
        Set sRange = cfgWs.Range(cfgWs.Cells(rowL2MtrxInCode, colMtrxMin), cfgWs.Cells(rowL2MtrxInCode, cfgWs.Columns.Count))
        If sRange Is Nothing Then Exit Function
        Set fRange = sRange.Find(What:=curEntryTypeId, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If fRange Is Nothing Then Exit Function Else curMtrxCol = fRange.Column
        
        mtrxOutCode = CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxOutCode, curMtrxCol).Value)
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxIncTax, curMtrxCol).Value)) = defParamYes Then mtrxIncTax = True Else mtrxIncTax = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxSocTax, curMtrxCol).Value)) = defParamYes Then mtrxSocTax = True Else mtrxSocTax = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxSocMin, curMtrxCol).Value)) = defParamYes Then mtrxSocMin = True Else mtrxSocMin = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxPens, curMtrxCol).Value)) = defParamYes Then mtrxPens = True Else mtrxPens = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxInsPers, curMtrxCol).Value)) = defParamYes Then mtrxInsPers = True Else mtrxInsPers = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxInsComp, curMtrxCol).Value)) = defParamYes Then mtrxInsComp = True Else mtrxInsComp = False
        If UCase(CStr(cfgWs.Cells(rowL2MtrxCfg + indxMtrxTaxFree, curMtrxCol).Value)) = defParamYes Then mtrxTaxFree = True Else mtrxTaxFree = False
    Case Else
        Exit Function
    End Select

    getMatrixColumn = True
End Function

Public Function CreateRecord(Optional ByVal tsdPayOutCode As String) As Boolean
    Dim tsdl As Integer
    
    errString = vbNullString
    tsdl = 1
    
    If Not torValid Then errString = mdlFunc.AddText(errString, "TOR tuvastamine ebaonnestus")
    If Not rdntCheck Then errString = mdlFunc.AddText(errString, "residentsus ei ole kontrollitud")
    If Not pnsnCheck And rdntCheck And curResident Then errString = mdlFunc.AddText(errString, "pension ei ole kontrollitud")
    If Not mdlFunc.IsPersonCode(curPersCode) Then errString = mdlFunc.AddText(errString, "palun sisestage isikukood")
    
    If Len(errString) > 0 Then GoTo CreateRecordEntryError
    
    If Not curResident Then tsdl = 2
    
    If Not getMatrixColumn(tsdl) Then
        errString = mdlFunc.AddText(errString, "tootamise liigi tuvastamine ebaonnestus")
        GoTo CreateRecordEntryError
    End If
    
    Select Case curEntryStateId
        Case tor_entryStateValid
            'proceed
        Case tor_entryStatePaused
            If curNetSum = 0 Then GoTo CreateRecordEnd
        Case tor_entryStateFinished
            If curNetSum = 0 Then GoTo CreateRecordEnd
        Case Else
            errString = mdlFunc.AddText(errString, "kande oleku tuvastamine ebaonnestus")
            GoTo CreateRecordEntryError
    End Select
    
    oCalc.ResetData
    oCalc.NetSum = curNetSum
    oCalc.ExtraIncomeFlatSum = curExtraIncomeFlatSum
    oCalc.IncomeTaxApplied = mtrxIncTax
    oCalc.SocialTaxApplied = mtrxSocTax
    oCalc.SocialTaxUseMin = mtrxSocMin
    oCalc.PensionApplied = mtrxPens And curPension
    oCalc.PensionRate = curPensionRate
    oCalc.UnEmInsCompApplied = mtrxInsComp
    oCalc.UnEmInsPersApplied = mtrxInsPers
    oCalc.TaxFreeApplied = mtrxTaxFree
    If Len(curFreeData) > 0 And IsNumeric(curFreeData) Then
        oCalc.CustomTaxFree True, CDbl(curFreeData)
    End If
    
    If Not oCalc.CalculateValues Then
        errString = mdlFunc.AddText(errString, "vaartuste arvutamine ebaonnestus")
        GoTo CreateRecordEntryError
    End If
    
    If tsdl = 2 Then GoTo CreateRecordTsdLisa2
    
CreateRecordTsdLisa1:

    If oCalc.GrossSum + oCalc.SocialTaxAppliedToSum + oCalc.SocialTaxComplimSum + oCalc.SocialTaxSum + oCalc.PensionSum + oCalc.UnEmInsPersSum + oCalc.UnEmInsCompSum + oCalc.IncomeTaxSum = 0 Then
        GoTo CreateRecordEnd
    End If

    rep1Ws.Cells(curRowL1, col_1000).Value = curPersCode 'pers code
    rep1Ws.Cells(curRowL1, col_1010).Value = curFullName 'name
    rep1Ws.Cells(curRowL1, col_1020).Value = mtrxOutCode 'payout code
    rep1Ws.Cells(curRowL1, col_1030).Value = Format(oCalc.GrossSum, "0.00") '4 'gross sum
    rep1Ws.Cells(curRowL1, col_1050).Value = vbNullString '5 'A1/ E101
    rep1Ws.Cells(curRowL1, col_1060).Value = Format(oCalc.SocialTaxAppliedToSum, "0.00") '6 'soc taxed sum
    rep1Ws.Cells(curRowL1, col_1070).Value = "0,00" '7 'soctax free sum 1
    rep1Ws.Cells(curRowL1, col_1080).Value = "0,00" '8 'soctax free sum 2
    rep1Ws.Cells(curRowL1, col_1090).Value = Format(oCalc.SocialTaxComplimSum, "0.00") '9 'soc taxed sum gap
    rep1Ws.Cells(curRowL1, col_1100).Value = Format(oCalc.SocialTaxSum, "0.00") '10 'soc tax
    rep1Ws.Cells(curRowL1, col_1110).Value = Format(oCalc.PensionSum, "0.00") '11 'pens payment
    rep1Ws.Cells(curRowL1, col_1120).Value = Format(oCalc.GrossSum, "0.00") '12 'unemp ins taxed sum
    rep1Ws.Cells(curRowL1, col_1130).Value = Format(oCalc.UnEmInsPersSum, "0.00") '13 'unemp ins pay person
    rep1Ws.Cells(curRowL1, col_1140).Value = Format(oCalc.UnEmInsCompSum, "0.00") '14 'unemp ins pay company
    rep1Ws.Cells(curRowL1, col_1170).Value = Format(oCalc.IncomeTaxSum, "0.00") '15 'income tax
    rep1Ws.Cells(curRowL1, col_1160_610).Value = Format(oCalc.TaxFreeSum, "0.00") '16 'tax free 610
    rep1Ws.Cells(curRowL1, col_1160_640).Value = "0,00" '17 'tax free 640
    rep1Ws.Cells(curRowL1, col_1160_650).Value = "0,00" '18 'tax free 650
    
    out1Ws.Cells(curRowL1 - lisa1_rowStart + 1, 1).Value = Replace(curFrmla1, "#r", curRowL1)
    
    'rep1Ws.Cells(curRowL1, lisa1_headerCount + 1).Value = errString 'errors
    
    curRowL1 = curRowL1 + 1
    GoTo CreateRecordEnd
    
CreateRecordTsdLisa2:
    
    If oCalc.GrossSum + oCalc.SocialTaxAppliedToSum + oCalc.SocialTaxComplimSum + oCalc.SocialTaxSum + oCalc.UnEmInsPersSum + oCalc.UnEmInsCompSum + oCalc.IncomeTaxAppliedToSum + oCalc.IncomeTaxSum = 0 Then
        GoTo CreateRecordEnd
    End If
    
    rep2Ws.Cells(curRowL2, col_2000).Value = curPersCode 'pers code
    rep2Ws.Cells(curRowL2, col_2010).Value = curFullName 'name
    rep2Ws.Cells(curRowL2, col_2020).Value = vbNullString 'curCountryCode 'country code
    rep2Ws.Cells(curRowL2, col_2030).Value = mtrxOutCode 'payout code
    rep2Ws.Cells(curRowL2, col_2040).Value = Format(oCalc.GrossSum, "0.00") 'gross sum
    rep2Ws.Cells(curRowL2, col_2060).Value = vbNullString 'A1/ E101
    rep2Ws.Cells(curRowL2, col_2070).Value = Format(oCalc.SocialTaxAppliedToSum, "0.00") 'soc taxed sum
    rep2Ws.Cells(curRowL2, col_2080).Value = "0,00" 'soctax free sum 1
    rep2Ws.Cells(curRowL2, col_2090).Value = "0,00" 'soctax free sum 2
    rep2Ws.Cells(curRowL2, col_2100).Value = Format(oCalc.SocialTaxComplimSum, "0.00") 'soc taxed sum gap
    rep2Ws.Cells(curRowL2, col_2110).Value = Format(oCalc.SocialTaxSum, "0.00") 'soc tax
    rep2Ws.Cells(curRowL2, col_2120).Value = Format(oCalc.GrossSum, "0.00") 'unemp ins taxed sum
    rep2Ws.Cells(curRowL2, col_2130).Value = Format(oCalc.UnEmInsPersSum, "0.00") 'unemp ins pay person
    rep2Ws.Cells(curRowL2, col_2140).Value = Format(oCalc.UnEmInsCompSum, "0.00") 'unemp ins pay company
    rep2Ws.Cells(curRowL2, col_2150).Value = Format(oCalc.IncomeTaxAppliedToSum, "0.00") 'income taxed sum
    rep2Ws.Cells(curRowL2, col_2160).Value = Format(oCalc.IncomeTaxRate, "0.00") 'income tax rate
    rep2Ws.Cells(curRowL2, col_2170).Value = Format(oCalc.IncomeTaxSum, "0.00") 'income tax
    rep2Ws.Cells(curRowL2, col_2154_610).Value = Format(oCalc.TaxFreeSum, "0.00") 'tax free 610
    rep2Ws.Cells(curRowL2, col_2154_650).Value = "0,00" 'tax free 650
    
    out2Ws.Cells(curRowL2 - lisa2_rowStart + 1, 1).Value = Replace(curFrmla2, "#r", curRowL2)
    
    'rep2Ws.Cells(curRowL2, lisa2_headerCount + 1).Value = errString 'errors
    
    curRowL2 = curRowL2 + 1
    GoTo CreateRecordEnd
    
CreateRecordEntryError:
    Select Case tsdl
    Case 1
        rep1Ws.Cells(curRowL1, col_1000).Value = curPersCode 'pers code
        rep1Ws.Cells(curRowL1, col_1010).Value = curFullName 'name
        rep1Ws.Cells(curRowL1, lisa1_headerCount + 1).Value = errString 'errors
        rep1Ws.Range(rep1Ws.Cells(curRowL1, lisa1_colStart), rep1Ws.Cells(curRowL1, lisa1_headerCount + 1)).Interior.Color = vbYellow
        curRowL1 = curRowL1 + 1

        GoTo CreateRecordEnd
    Case 2
        rep2Ws.Cells(curRowL2, col_2000).Value = curPersCode 'pers code
        rep2Ws.Cells(curRowL2, col_2010).Value = curFullName 'name
        rep2Ws.Cells(curRowL2, col_2020).Value = vbNullString 'curCountryCode 'country code
        rep2Ws.Cells(curRowL2, lisa2_headerCount + 1).Value = errString 'errors
        rep2Ws.Range(rep1Ws.Cells(curRowL2, lisa2_colStart), rep1Ws.Cells(curRowL2, lisa2_headerCount + 1)).Interior.Color = vbYellow
        curRowL2 = curRowL2 + 1

        GoTo CreateRecordEnd
    End Select
    
CreateRecordEnd:
    CreateRecord = True
End Function
