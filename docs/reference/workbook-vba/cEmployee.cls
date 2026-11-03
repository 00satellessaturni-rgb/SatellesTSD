Attribute VB_Name = "cEmployee"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

#If VBA7 Then
    Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As LongPtr)
    'For 64-Bit versions of Excel
#Else
    Private Declare Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)
    'For 32-Bit versions of Excel
#End If

Private Const empCfgKey As String = "{employee_config}"
Private Const sysDatKey As String = "{system_data}"
'Private Const cfgRow As Long = 23
Private Const colFirstName As Long = 2
Private Const colLastName As Long = 3
Private Const colPersCode As Long = 4
Private Const colCountryCode As Long = 5
Private Const colStartDate As Long = 6
Private Const colEndDate As Long = 7
Private Const colWorkTypeId As Long = 8
Private Const colWorkType As Long = 9
Private Const colEntryStateId As Long = 10
Private Const colEntryState As Long = 11
Private Const colTerminationId As Long = 12
Private Const colTermination As Long = 13
Private Const colEmployerName As Long = 14

Private Const rdntTrue As String = "EE"
Private Const rdntFalse As String = "mitteresident"

Private Const urlGetR As String = "https://apps.emta.ee/saqu/public/nonresident/"
Private Const urlPostR As String = "https://apps.emta.ee/saqu/public/nonresident/query"
Private Const hdrCookieR As String = "set-cookie"
Private Const badReqTextR As String = "Bad or missing CSRF value"
Private Const rdntContentId As String = "#content-inner"
Private Const rdntMsgElem As String = "p"
Private Const rdntTblClass As String = ".alt-block"
Private Const rdntRsltFound As String = "mitteresidentide registrist leitud isikud seisuga"
Private Const rdntRsltNotFound As String = "otsingutulemustele vastavaid isikuid ei leitud"

Private Const pnsnTrue As String = "II sammas"
Private Const pnsnFalse As String = "ei ole liitunud"
'Private Const pnsnFinished As String = "" 'value from config
Private pnsnFinished As String

Private Const urlGetP As String = "https://www.pensionikeskus.ee/"
Private Const urlGetDataP As String = "https://www.pensionikeskus.ee/raamatupidajale/ii-sammas/liitumiskontroll/"
Private Const varNameP As String = "?personCode="
Private Const hdrCookieP As String = "set-cookie"
Private Const sesCookieP As String = "incap_ses"
Private Const sesCookiePtail As String = "; pll_language=et; _ga=GA1.2.707191290.1678357770; _gid=GA1.2.1910928541.1678357770"
Private Const pnsnContentClass = ".custom-main-col"
Private Const pnsnDetailsClass = ".table"
Private Const pnsnRsltFound As String = "*Isik on kohustusliku kogumispensioniga liitunud*"
Private Const pnsnRsltFound2 As String = "*Isik on s?nniaasta j?rgi kohustatud*sambaga liituma*"
Private Const pnsnRsltFoundDate As String = "*Maksete kinnipidamise algus:*.*.*"
Private Const pnsnRsltFinished As String = "*Alates*.*.*on isiku kogumispensioni sissemaksed l?ppenud*"
Private Const pnsnRsltNotFound As String = "*Isik ei ole liitunud kohustusliku kogumispensioniga*"

Private ws As Worksheet
Private cfgRow As Long
Private initTrue As Boolean
Private curRow As Long
Private maxRow As Long
Private lastCol As Long

Private posFirstName As Long
Private posLastName As Long
Private posPersCode As Long
Private posCountryCode As Long
Private posStartDate As Long
Private posEndDate As Long
Private posWorkTypeId As Long
Private posWorkType As Long
Private posEntryStateId As Long
Private posEntryState As Long
Private posTerminationId As Long
Private posTermination As Long
Private posEmployerName As Long

Private rdntStatus As Boolean
Private rdntStatusDate As String
Private rdntReqDone As Boolean
Private rdntReqSuccess As Boolean
Private arrRdntPeriods() As String

Private pnsnStatus As Boolean
Private pnsnStatusDate As String
Private pnsnReqDone As Boolean
Private pnsnReqSuccess As Boolean
Private pnsnStartDate As String
Private pnsnFinishedDate As String

Private curCookieR As String
Private curTokenR As String
Private curCookieP As String
Private curTokenP As String

Public Function InitData(ByRef wsCfg As Worksheet, ByRef wsData As Worksheet, ByVal useHttp As Boolean) As Boolean
    Dim fndRange As Range, posRange As Range
    
    initTrue = False
    If wsCfg Is Nothing Or wsData Is Nothing Then Exit Function
    
    Set ws = wsData
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=empCfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else cfgRow = posRange.Row + 2
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=sysDatKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else pnsnFinished = CStr(wsCfg.Cells(posRange.Row + 2, posRange.Column + 1).Value)
    
    Set fndRange = ws.Range(ws.Cells(1, 1), ws.Cells(1, ws.Columns.Count))
    If fndRange Is Nothing Then Exit Function
    
    'firstname
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colFirstName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posFirstName = posRange.Column
    
    'lastname
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colLastName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posLastName = posRange.Column
    
    'perscode
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colPersCode).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posPersCode = posRange.Column
    
    'countrycode
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colCountryCode).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posCountryCode = posRange.Column
    
    'startdate
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colStartDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posStartDate = posRange.Column
    
    'enddate
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colEndDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posEndDate = posRange.Column
    
    'worktypeid
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colWorkTypeId).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posWorkTypeId = posRange.Column
    
    'worktype
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colWorkType).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posWorkType = posRange.Column
    
    'entrystateid
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colEntryStateId).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posEntryStateId = posRange.Column
    
    'entrystate
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colEntryState).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posEntryState = posRange.Column
    
    'terminationid
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colTerminationId).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posTerminationId = posRange.Column
    
    'termination
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colTermination).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posTermination = posRange.Column
    
    'employer
    Set posRange = fndRange.Find(CStr(wsCfg.Cells(cfgRow, colEmployerName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
    If posRange Is Nothing Then Exit Function Else posEmployerName = posRange.Column
    
    If useHttp Then
        If Not setResidentCookie Then MsgBox "MTA address" & vbNewLine & urlGetR & vbNewLine & "failed to resolve", vbExclamation
        If Not setPensionCookie Then MsgBox "Pensionikeskus address" & vbNewLine & urlGetP & vbNewLine & "failed to resolve", vbExclamation
    End If
    
    curRow = 1
    maxRow = ws.Cells(ws.Rows.Count, posFirstName).End(xlUp).Row
    lastCol = ws.Cells(1, ws.Columns.Count).End(xlToLeft).Column
    InitData = True
    initTrue = True

End Function

Property Get FirstName() As String
    If initTrue Then FirstName = CStr(ws.Cells(curRow, posFirstName).Value)
End Property

Property Get LastName() As String
    If initTrue Then LastName = CStr(ws.Cells(curRow, posLastName).Value)
End Property

Property Get FullName() As String
    If initTrue Then FullName = CStr(ws.Cells(curRow, posFirstName).Value) & CStr(ws.Cells(curRow, posLastName).Value)
End Property

Property Get PersonalCode() As String
    If initTrue Then PersonalCode = CStr(ws.Cells(curRow, posPersCode).Value)
End Property

Property Get CountryCode() As String
    If initTrue Then CountryCode = CStr(ws.Cells(curRow, posCountryCode).Value)
End Property

Property Get StartDate() As String
    If initTrue Then StartDate = CStr(ws.Cells(curRow, posStartDate).Value)
End Property

Property Get EndDate() As String
    If initTrue Then EndDate = CStr(ws.Cells(curRow, posEndDate).Value)
End Property

Property Get WorkTypeId() As String
    If initTrue Then WorkTypeId = CStr(ws.Cells(curRow, posWorkTypeId).Value)
End Property

Property Get WorkType() As String
    If initTrue Then WorkType = CStr(ws.Cells(curRow, posWorkType).Value)
End Property

Property Get EntryStateId() As String
    If initTrue Then EntryStateId = CStr(ws.Cells(curRow, posEntryStateId).Value)
End Property

Property Get EntryState() As String
    If initTrue Then EntryState = CStr(ws.Cells(curRow, posEntryState).Value)
End Property

Property Get TerminationId() As String
    If initTrue Then TerminationId = CStr(ws.Cells(curRow, posTerminationId).Value)
End Property

Property Get Termination() As String
    If initTrue Then Termination = CStr(ws.Cells(curRow, posTermination).Value)
End Property

Property Get EmployerName() As String
    If initTrue Then EmployerName = CStr(ws.Cells(ws.Cells(ws.Rows.Count, posEmployerName).End(xlUp).Row, posEmployerName).Value)
End Property

Property Get ListDataRow() As Long
    If initTrue Then ListDataRow = curRow
End Property

Property Get ListLastColumn() As Long
    If initTrue Then ListLastColumn = lastCol
End Property

Public Sub ResetRequestState()
    clearCheckedHttpValues
End Sub

Public Function ResidenceStringCheck(ByVal data As String, ByRef val As Boolean) As Boolean
    val = False
    If LCase(data) = LCase(rdntTrue) Then
        val = True
        ResidenceStringCheck = True
    End If
    If LCase(data) = LCase(rdntFalse) Then
        val = False
        ResidenceStringCheck = True
    End If
End Function

Public Function PensionStringCheck(ByVal data As String, ByRef val As Boolean, ByRef pRate As Double) As Boolean
    val = False
    pRate = 0
    If LCase(data) Like "*%" Then
        If IsNumeric(Replace(data, "%", vbNullString)) Then
            val = True
            pRate = CDbl(Replace(data, "%", vbNullString)) * 0.01
            PensionStringCheck = True
            Exit Function
        End If
    End If
    If IsNumeric(data) Then
        val = True
        pRate = CDbl(data)
        PensionStringCheck = True
        Exit Function
    End If
    If LCase(data) = LCase(pnsnFalse) Then
        val = False
        PensionStringCheck = True
        Exit Function
    End If
    If LCase(data) = LCase(pnsnFinished) Then
        val = False
        PensionStringCheck = True
    End If
End Function

Public Function FindEmployee(ByVal pCode As String, ByVal lName As String, ByVal fName As String) As Boolean
    Dim fRange As Range, sRange As Range, frstRow As Long, temp1 As String, temp2 As String, pos1 As Long, pos2 As Long
    
    clearCheckedHttpValues
    
    curRow = 1
    
    If Len(pCode) > 0 Then
        temp1 = pCode
        temp2 = temp1
        pos1 = posPersCode
        pos2 = pos1
    ElseIf Len(lName) > 0 And Len(fName) > 0 Then
        temp1 = lName
        temp2 = fName
        pos1 = posLastName
        pos2 = posFirstName
    Else
        Exit Function
    End If
    
    Set sRange = ws.Range(ws.Cells(2, pos1), ws.Cells(maxRow, pos1))
    If sRange Is Nothing Then Exit Function
    Set fRange = sRange.Find(What:=temp1, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If fRange Is Nothing Then Exit Function
    frstRow = fRange.Row
    Do
        If LCase(CStr(ws.Cells(fRange.Row, pos2).Value)) = LCase(temp2) Then
            curRow = fRange.Row
            FindEmployee = True
            Exit Do
        End If
        Set fRange = sRange.FindNext(fRange)
        If fRange Is Nothing Then Exit Do
    Loop Until fRange.Row = frstRow
End Function

Public Function NextEmployee() As Boolean
    
    'rdntStatus = False
    'rdntStatusDate = vbNullString
    'rdntReqDone = False
    'rdntReqSuccess = False
    'Erase arrRdntPeriods
    
    'pnsnStatus = False
    'pnsnStatusDate = vbNullString
    'pnsnStartDate = vbNullString
    'pnsnFinishedDate = vbNullString
    'pnsnReqDone = False
    'pnsnReqSuccess = False
    
    clearCheckedHttpValues
    
    curRow = curRow + 1
    
    If curRow > 1 And curRow <= maxRow Then
        NextEmployee = True
    End If
End Function

Public Function IsDuplicateEntry() As Boolean
    Dim sRange As Range, fRange As Range, fRow As Long
    
    If curRow <= 2 Then Exit Function
    
    If Len(CStr(ws.Cells(curRow, posPersCode).Value)) > 0 Then
        Set sRange = ws.Range(ws.Cells(2, posPersCode), ws.Cells(curRow - 1, posPersCode))
        Set fRange = sRange.Find(What:=CStr(ws.Cells(curRow, posPersCode).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If fRange Is Nothing Then Exit Function
    Else
        Set sRange = ws.Range(ws.Cells(2, posLastName), ws.Cells(curRow - 1, posLastName))
        Set fRange = sRange.Find(What:=CStr(ws.Cells(curRow, posLastName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If fRange Is Nothing Then Exit Function
        fRow = fRange.Row
        Do
            If CStr(ws.Cells(fRange.Row, posFirstName).Value) = CStr(ws.Cells(curRow, posFirstName).Value) Then Exit Do
            Set fRange = sRange.FindNext(fRange)
            If fRange Is Nothing Then Exit Function
            If fRange.Row = fRow Then Exit Function
        Loop Until fRange.Row = fRow
    End If
    
    IsDuplicateEntry = True
End Function

Public Function IsUniqueResult(ByVal lastMask As String, ByVal firstMask As String) As Boolean
    Dim rSearch As Range, rFound As Range, fRow As Long
    If ws Is Nothing Or Not initTrue Or curRow < 2 Or curRow > maxRow Then Exit Function
    Set rSearch = ws.Range(ws.Cells(2, posLastName), ws.Cells(maxRow, posLastName))
    If rSearch Is Nothing Then Exit Function
    Set rFound = rSearch.Find(What:=lastMask, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If rFound Is Nothing Then Exit Function
    fRow = rFound.Row
    Do
        If UCase(CStr(ws.Cells(rFound.Row, posFirstName).Value)) Like UCase(firstMask) And CStr(ws.Cells(rFound.Row, posPersCode).Value) <> CStr(ws.Cells(curRow, posPersCode).Value) Then Exit Function
        Set rFound = rSearch.FindNext(rFound)
        If rFound Is Nothing Then Exit Function
    Loop Until rFound.Row = fRow

IsUniqueResultEnd:
    IsUniqueResult = True
End Function

Public Function IsResident(ByVal dateString As String, ByRef val As Boolean, ByRef err As String, Optional ByVal tempPersCode As String) As Boolean
    Dim i As Integer
    Dim s As Boolean, e As Boolean, d As Boolean
    Dim oHtml As MSHTML.HTMLDocument
    Dim elem As MSHTML.IHTMLElement
    Dim temp1 As Variant, temp2 As Variant
    
    err = vbNullString
    If Len(tempPersCode) = 0 Then tempPersCode = CStr(ws.Cells(curRow, posPersCode).Value)
    
    If Not mdlFunc.IsPersonCode(tempPersCode) Then
        err = "palun sisestage isikukood"
        Exit Function
    End If
    
    dateString = Split(dateString, " ")(0)
    
    If Not IsDate(dateString) Then
        err = "vale kuupaev"
        Exit Function
    End If
    
    If rdntReqDone And rdntReqSuccess Then
        If rdntStatusDate = dateString Or Len(rdntStatusDate) = 0 Then
            IsResident = True
            If rdntStatus Then err = rdntTrue Else err = rdntFalse
            val = rdntStatus
            Exit Function
        Else
            GoTo IsResidentPeriodCheck
        End If
    End If
    
    rdntReqDone = getResidentHtml(oHtml, tempPersCode)
    
    If Not rdntReqDone Then
        err = "request timeout or access denied"
        Exit Function
    End If
    
    Set elem = oHtml.querySelector(rdntContentId)
        
    If elem Is Nothing Then
        err = "data not found"
        Exit Function
    End If
    
    oHtml.body.innerHTML = elem.innerHTML
    temp1 = oHtml.querySelector(rdntMsgElem).innerText
    
    If InStr(LCase(temp1), rdntRsltNotFound) > 0 Then
        IsResident = True
        rdntReqSuccess = True
        rdntStatus = True
        err = rdntTrue
        val = True
        Exit Function
    End If
    
    If InStr(LCase(temp1), rdntRsltFound) > 0 Then
        Set elem = oHtml.querySelector(rdntTblClass)
        If elem Is Nothing Then
            err = "content error"
            Exit Function
        End If
        ReDim arrRdntPeriods(0 To 1, 0)
        For Each temp1 In elem.Children
            d = False
            i = 0
            For Each temp2 In temp1.Children
                i = i + 1
                If d And i = 2 Then
                    If Not IsDate(Trim(temp2.innerText)) Then
                        err = "content error"
                        Exit Function
                    End If
                    If Len(arrRdntPeriods(0, UBound(arrRdntPeriods, 2))) > 0 Then ReDim Preserve arrRdntPeriods(0 To 1, 0 To UBound(arrRdntPeriods, 2) + 1)
                    arrRdntPeriods(0, UBound(arrRdntPeriods, 2)) = Trim(temp2.innerText)
                End If
                If d And i = 3 Then
                    If Len(Trim(temp2.innerText)) > 0 And Not IsDate(Trim(temp2.innerText)) Then
                        err = "content error"
                        Exit Function
                    End If
                    arrRdntPeriods(1, UBound(arrRdntPeriods, 2)) = Trim(temp2.innerText)
                End If
                If i = 1 And Len(Trim(temp2.innerText)) = 0 Then d = True
            Next
        Next
        If i > 3 Then
            err = "table size mismatch"
            Exit Function
        End If
        rdntReqSuccess = True
    End If
    
IsResidentPeriodCheck:
    
    IsResident = True
    rdntStatusDate = dateString
    rdntStatus = True
    err = rdntTrue
    val = True
    
    For i = LBound(arrRdntPeriods, 2) To UBound(arrRdntPeriods, 2)
        If CDate(rdntStatusDate) >= CDate(arrRdntPeriods(0, i)) Then s = True
        If Len(arrRdntPeriods(1, i)) > 0 Then
            If CDate(rdntStatusDate) <= CDate(arrRdntPeriods(1, i)) Then e = True
        Else
            e = s
        End If
        If s And e Then
            rdntStatus = False
            err = rdntFalse
            val = False
            Exit Function
        End If
    Next i
    
End Function

Public Function HasPension(ByVal dateString As String, ByRef val As Boolean, ByRef err As String, ByRef pRate As Double, Optional ByVal tempPersCode As String) As Boolean
    Dim i As Integer
    Dim d As Boolean
    Dim temp As String, data() As String
    Dim oHtml As MSHTML.HTMLDocument, oHtmlDtls As MSHTML.HTMLDocument
    Dim elem As MSHTML.IHTMLElement, elemDtls As MSHTML.IHTMLElement, elemDate As MSHTML.IHTMLElement
    Dim node As MSHTML.IHTMLDOMNode
    Dim part As Variant
    Dim date1 As String, date2 As String
    
    err = vbNullString
    pRate = 0
    If Len(tempPersCode) = 0 Then tempPersCode = CStr(ws.Cells(curRow, posPersCode).Value)
    
    If Not mdlFunc.IsPersonCode(tempPersCode) Then
        err = "palun sisestage isikukood"
        Exit Function
    End If
    
    dateString = Split(dateString, " ")(0)
    
    If Not IsDate(dateString) Then
        err = "vale kuupaev"
        Exit Function
    End If
    
    If (pnsnReqDone And pnsnReqSuccess And (pnsnStatusDate = dateString)) Or (rdntReqDone And rdntReqSuccess And (Not rdntStatus) And (dateString = rdntStatusDate)) Then
        HasPension = True
        If pnsnStatus Then err = pnsnTrue Else If rdntStatus Then err = pnsnFalse Else err = rdntFalse
        val = pnsnStatus
        Exit Function
    End If
    
    If (Not rdntReqDone) Or (Not rdntReqSuccess) Or (dateString <> rdntStatusDate) Then
        If Not IsResident(dateString, d, temp, tempPersCode) Then
            err = "residency check failed: " & temp
            Exit Function
        End If
    End If
    
    If rdntReqDone And rdntReqSuccess And (Not rdntStatus) Then
        HasPension = True
        val = False
        err = rdntFalse 'value specifically shows that person is not a resident, without checking pensionikeskus
        pnsnStatus = False
        'pnsnStatusDate = dateString
        'pnsnReqDone = True
        'pnsnReqSuccess = True
        Exit Function
    End If
    
    If pnsnReqDone And pnsnReqSuccess Then GoTo HasPensionPeriodCheck
    
    pnsnReqDone = getPensionHtml(oHtml, tempPersCode)
    
    If Not pnsnReqDone Then
        err = "request timeout or access denied"
        Exit Function
    End If
    
    Set elem = oHtml.querySelector(pnsnContentClass)
    If elem Is Nothing Then
        err = "data not found"
        Exit Function
    End If
    
    Set oHtmlDtls = New MSHTML.HTMLDocument
    oHtmlDtls.body.innerHTML = elem.innerHTML
    Set node = oHtmlDtls.querySelector(pnsnDetailsClass)
    
    d = False
    data = Split(elem.innerText, vbNewLine)
    
    For i = LBound(data) To UBound(data)
        If data(i) Like pnsnRsltNotFound Then
            d = True
            pnsnStatus = False
            Exit For
        End If
        If (data(i) Like pnsnRsltFound) Or (data(i) Like pnsnRsltFound2) Then
            d = True
            GoTo HasPensionNextContentRow
        End If
        If d And (data(i) Like pnsnRsltFoundDate) Then
            For Each part In Split(data(i), " ")
                If IsDate(part) Then
                    pnsnStartDate = part
                    Exit For
                End If
            Next
            If Not IsDate(pnsnStartDate) Then d = False
            Exit For
        End If
        If d And (data(i) Like pnsnRsltFinished) Then
            For Each part In Split(data(i), " ")
                If IsDate(part) Then
                    pnsnFinishedDate = part
                    Exit For
                End If
            Next
            If Not IsDate(pnsnFinishedDate) Then d = False
            Exit For
        End If
HasPensionNextContentRow:
    Next i
    
    If d Then
        pnsnReqSuccess = True
    Else
        err = "content error"
        Exit Function
    End If
    
HasPensionPeriodCheck:
    HasPension = True
    
    If IsDate(pnsnStartDate) Then
        If CDate(dateString) >= CDate(pnsnStartDate) Then pnsnStatus = True Else pnsnStatus = False
    End If
    If IsDate(pnsnFinishedDate) Then
        If CDate(dateString) >= CDate(pnsnFinishedDate) Then pnsnStatus = False
    End If
    
    If pnsnStatus Then
        If node Is Nothing Then
            err = "content error"
            HasPension = False
            GoTo HasPensionSetValues
        End If
        
        Set node = node.LastChild.FirstChild
        
        Do
            Set elemDate = node.FirstChild
            date1 = vbNullString
            date2 = vbNullString
            
            For Each part In Split(elemDate.innerText, " ")
                If IsDate(part) Then
                    If Len(date1) = 0 Then date1 = CStr(part) Else date2 = CStr(part)
                End If
            Next
            
            If Len(date2) = 0 Then date2 = dateString
            'MsgBox elemDate.innerText & vbNewLine & date1 & vbNewLine & date2
            If CDate(date1) <= CDate(dateString) And CDate(date2) >= CDate(dateString) Then
                Set elemDtls = node.LastChild
                err = elemDtls.innerText
                'MsgBox err
                If err Like "*%" Then
                    If IsNumeric(Replace(err, "%", vbNullString)) Then pRate = CDbl(Replace(err, "%", vbNullString)) * 0.01
                ElseIf IsNumeric(err) Then
                    pRate = CDbl(err)
                End If
                Exit Do
            End If
            
            Set node = node.NextSibling
            If node Is Nothing Then Exit Do
        Loop
            
    ElseIf IsDate(pnsnFinishedDate) Then
        err = pnsnFinished
    Else
        err = pnsnFalse
    End If
    
HasPensionSetValues:
    val = pnsnStatus
    pnsnStatusDate = dateString
    
End Function

Private Function getResidentHtml(ByRef oHtml As MSHTML.HTMLDocument, ByVal personCode As String) As Boolean
    Dim oHttp As New MSXML2.ServerXMLHTTP60
    Dim retry As Integer
    
    Set oHtml = New MSHTML.HTMLDocument
    retry = 0
    
    With oHttp
    
    Do
        
        .Open "POST", urlPostR, False
        .setRequestHeader "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0"
        .setRequestHeader "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
        .setRequestHeader "Accept-Language", "et,ru;q=0.8,en;q=0.5,en-US;q=0.3"
        .setRequestHeader "Accept-Encoding", "identity, deflate"
        .setRequestHeader "Referer", "https://apps.emta.ee/saqu/public/nonresident/"
        .setRequestHeader "Content-Type", "application/x-www-form-urlencoded"
        .setRequestHeader "Content-Length", "99"
        .setRequestHeader "Origin", "https://apps.emta.ee"
        .setRequestHeader "Connection", "keep-alive"
        .setRequestHeader "Cookie", curCookieR
        .setRequestHeader "Upgrade-Insecure-Requests", "1"
        .setRequestHeader "Sec-Fetch-Dest", "document"
        .setRequestHeader "Sec-Fetch-Mode", "navigate"
        .setRequestHeader "Sec-Fetch-Site", "none"
        .setRequestHeader "Sec-Fetch-User", "?1"
        .setRequestHeader "Pragma", "no-cache"
        .setRequestHeader "Cache-Control", "no-cache"
        
        .send "personCode=" & personCode & "&regCode=&vatNr=&p_submit=Otsi&CSRFToken=" & curTokenR
        
        If .status >= 400 Then Exit Function
        
        Do
            DoEvents
            Sleep (1)
        Loop Until .readyState = 4
        
        If InStr(.responseText, badReqTextR) > 0 Then
            setResidentCookie
        Else
            oHtml.body.innerHTML = .responseText
            getResidentHtml = True
            Exit Function
        End If
        
        Sleep (1000)
        
        retry = retry + 1
        
    Loop While retry < 5
    
    End With
    
End Function

Private Function setResidentCookie() As Boolean
    Dim oHttp As New MSXML2.ServerXMLHTTP60, oHtml As New MSHTML.HTMLDocument
    
    With oHttp
    
        .Open "GET", urlGetR, False
        .setRequestHeader "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0"
        .setRequestHeader "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
        .setRequestHeader "Accept-Language", "et,ru;q=0.8,en;q=0.5,en-US;q=0.3"
        .setRequestHeader "Accept-Encoding", "identity, deflate"
        .setRequestHeader "Connection", "keep-alive"
        .setRequestHeader "Upgrade-Insecure-Requests", "1"
        .setRequestHeader "Sec-Fetch-Dest", "document"
        .setRequestHeader "Sec-Fetch-Mode", "navigate"
        .setRequestHeader "Sec-Fetch-Site", "none"
        .setRequestHeader "Sec-Fetch-User", "?1"
        .setRequestHeader "Pragma", "no-cache"
        .setRequestHeader "Cache-Control", "no-cache"
        
        .send
        
        If .status >= 400 Then Exit Function
        
        Do
            DoEvents
            Sleep (1)
        Loop Until .readyState = 4
        
        oHtml.body.innerHTML = .responseText
        
        curCookieR = Split(.getResponseHeader(hdrCookieR), ";")(0)
        curTokenR = oHtml.querySelector("input[name=""CSRFToken""]").Value
        
        If Len(curCookieR) > 0 And Len(curTokenR) > 0 Then setResidentCookie = True
    End With
End Function

Private Function getPensionHtml(ByRef oHtml As MSHTML.HTMLDocument, ByVal personCode As String) As Boolean
    Dim oHttp As New MSXML2.ServerXMLHTTP60
    Dim retry As Integer
    
    Set oHtml = New MSHTML.HTMLDocument
    retry = 0
    
    With oHttp
    
    Do
        
        .Open "GET", urlGetDataP & varNameP & personCode, False
        .setRequestHeader "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0"
        .setRequestHeader "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
        .setRequestHeader "Accept-Language", "et,ru;q=0.8,en;q=0.5,en-US;q=0.3"
        .setRequestHeader "Accept-Encoding", "identity, deflate"
        .setRequestHeader "Connection", "keep-alive"
        .setRequestHeader "Referer", "https://www.pensionikeskus.ee/"
        .setRequestHeader "Cookie", curCookieP
        .setRequestHeader "Upgrade-Insecure-Requests", "1"
        .setRequestHeader "Sec-Fetch-Dest", "document"
        .setRequestHeader "Sec-Fetch-Mode", "navigate"
        .setRequestHeader "Sec-Fetch-Site", "none"
        .setRequestHeader "Sec-Fetch-User", "?1"
        .setRequestHeader "Pragma", "no-cache"
        .setRequestHeader "Cache-Control", "no-cache"
        
        .send
        
        If .status >= 300 Then Exit Function
        
        Do
            DoEvents
            Sleep (1)
        Loop Until .readyState = 4
        
        If Len(.responseText) = 0 Then
            setPensionCookie
        Else
            oHtml.body.innerHTML = .responseText
            getPensionHtml = True
            Exit Function
        End If
        
        Sleep (1000)
        
        retry = retry + 1
        
    Loop While retry < 5
    
    End With
    
End Function

Private Function setPensionCookie() As Boolean
    Dim oHttp As New MSXML2.ServerXMLHTTP60
    Dim hdrs() As String, myCookie As String, temp As String
    Dim i As Integer
    
    With oHttp
    
        .Open "GET", urlGetP, False
        .setRequestHeader "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/110.0"
        .setRequestHeader "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
        .setRequestHeader "Accept-Language", "et,ru;q=0.8,en;q=0.5,en-US;q=0.3"
        .setRequestHeader "Accept-Encoding", "identity, deflate"
        .setRequestHeader "Connection", "keep-alive"
        .setRequestHeader "Upgrade-Insecure-Requests", "1"
        .setRequestHeader "Sec-Fetch-Dest", "document"
        .setRequestHeader "Sec-Fetch-Mode", "navigate"
        .setRequestHeader "Sec-Fetch-Site", "none"
        .setRequestHeader "Sec-Fetch-User", "?1"
        .setRequestHeader "Pragma", "no-cache"
        .setRequestHeader "Cache-Control", "no-cache"
        
        .send
        
        If .status >= 400 Then Exit Function
        
        Do
            DoEvents
            Sleep (1)
        Loop Until .readyState = 4
        
        hdrs = Split(.getAllResponseHeaders, vbNewLine)
        
        For i = LBound(hdrs) To UBound(hdrs)
            If InStr(LCase(hdrs(i)), LCase(hdrCookieP)) = 1 Then
                temp = Split(Trim(Split(hdrs(i), ":")(1)), ";")(0)
                If InStr(temp, "incap_ses") = 1 Then temp = temp & "; pll_language=et; _ga=GA1.2.707191290.1678357770; _gid=GA1.2.1910928541.1678357770"
                If Len(myCookie) > 0 Then myCookie = myCookie & "; "
                myCookie = myCookie & temp
            End If
        Next i
        
        curCookieP = myCookie
        
        If Len(curCookieP) > 0 Then setPensionCookie = True
        
    End With
    
End Function

Private Sub clearCheckedHttpValues()
    rdntStatus = False
    rdntStatusDate = vbNullString
    rdntReqDone = False
    rdntReqSuccess = False
    Erase arrRdntPeriods
    
    pnsnStatus = False
    pnsnStatusDate = vbNullString
    pnsnStartDate = vbNullString
    pnsnFinishedDate = vbNullString
    pnsnReqDone = False
    pnsnReqSuccess = False
End Sub
