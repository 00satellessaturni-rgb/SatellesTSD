Attribute VB_Name = "cPayOut"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

'Private Const rowDefDoc As Long = 11
'Private Const rowDefCol As Long = 27
Private Const docDefKey As String = "{payout_define}"
Private Const docColDefKey As String = "{payout_config}"
Private Const sysDefKey As String = "{system_data}"
Private Const colDocName As Long = 1

Private Const colAccOrDef As Long = 2
Private Const colTransDate As Long = 3
Private Const colFullName As Long = 4
Private Const colAmount As Long = 5
Private Const colDesc As Long = 6
Private Const colDebCre As Long = 7

Private Const colType As Long = 8
Private Const colDebit As Long = 9
Private Const colCredit As Long = 10

Private Const indxAccOrDef As Long = 0
Private Const indxTransDate As Long = 1
Private Const indxFullName As Long = 2
Private Const indxAmount As Long = 3
Private Const indxDesc As Long = 4
Private Const indxDebCre As Long = 5

Private Const colMaxIndx As Long = 5

Private Const indxType As Long = 0
Private Const indxDebit As Long = 1
Private Const indxCredit As Long = 2

Private Const parMaxIndx As Long = 2

Private arrSrcCfgIndxs() As Long
Private arrSrcNames() As String
Private arrSrcSheets() As Worksheet
Private arrSrcParams() As String
Private arrDefCol() As Long
Private arrSubFNamesStr() As String
Private arrCurNames() As String

Private ws As Worksheet
Private curDocName As String
Private curDocIndx As Integer
Private curDocCfgIndx As Long
Private curDataRow As Long
Private curLastCol As Long
Private curMaxRow As Long
Private curDebitDef As String
Private curCreditDef As String

Private curFirstName As String
Private curLastName As String
Private curFirstFound As String
Private curLastFound As String
Private curFirstMask As String
Private curLastMask As String
Private curSearchedName As String
Private curPersonFound As Boolean
Private curFirstMaskUsed As Boolean
Private curLastMaskUsed As Boolean
Private curSrcRange As Range
Private curFndRange As Range
Private curFndRow As Long

Private initTrue As Boolean
Private rowDefDoc As Long
Private rowDefCol As Long

Public Function InitData(ByRef wsCfg As Worksheet, ByRef arrIndxs() As Long, ByRef arrSheets() As Worksheet, ByVal empIndx As Long) As Boolean
    Dim i As Long, j As Long
    Dim fndRange As Range, posRange As Range, corRange As Range
    
    If wsCfg Is Nothing Then Exit Function
    
    If Not IsArray(arrIndxs) Or Not IsArray(arrSheets) Then Exit Function
    If LBound(arrIndxs) <> LBound(arrSheets) Or UBound(arrIndxs) <> UBound(arrSheets) Then Exit Function
    
    'some configs
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=docDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else rowDefDoc = posRange.Row + 1
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=docColDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else rowDefCol = posRange.Row + 2
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=sysDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then
        Exit Function
    Else
        If Len(CStr(wsCfg.Cells(posRange.Row + 4, posRange.Column + 1).Value)) > 0 Then arrSubFNamesStr = Split(CStr(wsCfg.Cells(posRange.Row + 4, posRange.Column + 1).Value), ";")
    End If
    '------------
    
    ReDim arrSrcCfgIndxs(LBound(arrSheets))
    ReDim arrSrcNames(LBound(arrSheets))
    ReDim arrSrcSheets(LBound(arrSheets))
    
    For i = LBound(arrIndxs) To UBound(arrIndxs)
        
        If empIndx <> i Then
            If Len(arrSrcNames(UBound(arrSrcNames))) > 0 And Not arrSrcSheets(UBound(arrSrcSheets)) Is Nothing Then
                ReDim Preserve arrSrcCfgIndxs(UBound(arrSrcCfgIndxs) + 1)
                ReDim Preserve arrSrcNames(UBound(arrSrcNames) + 1)
                ReDim Preserve arrSrcSheets(UBound(arrSrcSheets) + 1)
            End If
            If UBound(arrSrcNames) > LBound(arrSrcNames) Then
                For j = UBound(arrSrcNames) - 1 To LBound(arrSrcNames) Step -1
                    If wsCfg.Cells(rowDefCol + arrIndxs(i), colType).Value < wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(j), colType).Value Then
                        arrSrcCfgIndxs(j + 1) = arrSrcCfgIndxs(j)
                        arrSrcNames(j + 1) = arrSrcNames(j)
                        Set arrSrcSheets(j + 1) = arrSrcSheets(j)
                        arrSrcCfgIndxs(j) = arrIndxs(i)
                        arrSrcNames(j) = wsCfg.Cells(rowDefDoc + arrIndxs(i), colDocName)
                        Set arrSrcSheets(j) = arrSheets(i)
                    Else
                        arrSrcCfgIndxs(j + 1) = arrIndxs(i)
                        arrSrcNames(j + 1) = wsCfg.Cells(rowDefDoc + arrIndxs(i), colDocName)
                        Set arrSrcSheets(j + 1) = arrSheets(i)
                        Exit For
                    End If
                Next j
            Else
                arrSrcCfgIndxs(UBound(arrSrcCfgIndxs)) = arrIndxs(i)
                arrSrcNames(UBound(arrSrcNames)) = wsCfg.Cells(rowDefDoc + arrIndxs(i), colDocName)
                Set arrSrcSheets(UBound(arrSrcSheets)) = arrSheets(i)
            End If
            
        End If
        
    Next i
    
    If Len(arrSrcNames(LBound(arrSrcNames))) = 0 Or arrSrcSheets(LBound(arrSrcSheets)) Is Nothing Then Exit Function
    
    ReDim arrDefCol(LBound(arrSrcSheets) To UBound(arrSrcSheets), 0 To colMaxIndx)
    ReDim arrSrcParams(LBound(arrSrcSheets) To UBound(arrSrcSheets), 0 To parMaxIndx)
    
    For i = LBound(arrSrcSheets) To UBound(arrSrcSheets)
        Set fndRange = arrSrcSheets(i).Range(arrSrcSheets(i).Cells(1, 1), arrSrcSheets(i).Cells(1, arrSrcSheets(i).Columns.Count))
        
        If fndRange Is Nothing Then Exit Function
        
        'accordef
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colAccOrDef).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxAccOrDef) = posRange.Column
        
        'transdate
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colTransDate).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxTransDate) = posRange.Column
        
        'fullname
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colFullName).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxFullName) = posRange.Column
        
        'amount
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colAmount).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxAmount) = posRange.Column
        
        'desc
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colDesc).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxDesc) = posRange.Column
        
        'debit/credit
        Set posRange = fndRange.Find(What:=CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colDebCre).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If posRange Is Nothing Then Exit Function Else arrDefCol(i, indxDebCre) = posRange.Column
        
        
        arrSrcParams(i, indxType) = CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colType).Value)
        arrSrcParams(i, indxDebit) = CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colDebit).Value)
        arrSrcParams(i, indxCredit) = CStr(wsCfg.Cells(rowDefCol + arrSrcCfgIndxs(i), colCredit).Value)
        
        Set corRange = arrSrcSheets(i).Range(arrSrcSheets(i).Cells(2, arrDefCol(i, indxFullName)), arrSrcSheets(i).Cells(arrSrcSheets(i).Rows.Count, arrDefCol(i, indxFullName)).End(xlUp))
        
        If corRange Is Nothing Then Exit Function
        
        mdlFunc.CorrectUtf8SymbolCyrBalt corRange
    Next i
    
    setCurListNames
    resetSearchValues vbNullString, vbNullString
    
    If Not setRowColumnValues(LBound(arrSrcSheets)) Then Exit Function
    
    InitData = True
    initTrue = True
End Function

Property Get TransDate() As String
    If initTrue And Not ws Is Nothing Then
        If Len(CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxTransDate)).Value)) > 0 Then TransDate = Split(CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxTransDate)).Value), " ")(0)
    End If
End Property

Property Get NameFull() As String
    NameFull = curLastName & " " & curFirstName
End Property

Property Get NameFullFound() As String
    NameFullFound = curLastFound & " " & curFirstFound
End Property

Property Get NameFieldText() As String
    If initTrue And Not ws Is Nothing Then
        NameFieldText = CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxFullName)).Value)
    End If
End Property

Property Get NameSearched() As String
    NameSearched = curSearchedName
End Property

Property Get NameMaskUsed() As Boolean
    NameMaskUsed = curFirstMaskUsed Or curLastMaskUsed
End Property

Property Get NameFirstMask() As String
    NameFirstMask = curFirstMask
End Property

Property Get NameLastMask() As String
    NameLastMask = curLastMask
End Property

Property Get NameFullMask() As String
    NameFullMask = curLastMask & " " & curFirstMask
End Property

Property Get Amount() As String
    If initTrue And Not ws Is Nothing Then
        Amount = CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxAmount)).Value)
    End If
End Property

Property Get DebitCredit() As String
    If initTrue And Not ws Is Nothing Then
        DebitCredit = CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxDebCre)).Value)
    End If
End Property

Property Get Description() As String
    If initTrue And Not ws Is Nothing Then
        Description = CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxDesc)).Value)
    End If
End Property

Property Get IsDebit() As Boolean
    If initTrue And Not ws Is Nothing Then
        If CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxDebCre)).Value) = arrSrcParams(curDocIndx, indxDebit) Then IsDebit = True Else IsDebit = False
    End If
End Property

Property Get IsCredit() As Boolean
    If initTrue And Not ws Is Nothing Then
        If CStr(ws.Cells(curDataRow, arrDefCol(curDocIndx, indxDebCre)).Value) = arrSrcParams(curDocIndx, indxCredit) Then IsCredit = True Else IsCredit = False
    End If
End Property

Property Get ListName() As String
    If initTrue And Not ws Is Nothing Then ListName = curDocName
End Property

Property Get ListSheetName() As String
    If initTrue And Not ws Is Nothing Then ListSheet = ws.Name
End Property

Property Get ListSheetIndex() As String
    If initTrue And Not ws Is Nothing Then ListSheetIndex = curDocIndx
End Property

Property Get ListDataRow() As Long
    If initTrue And Not ws Is Nothing Then ListDataRow = curDataRow
End Property

Property Get ListLastColumn() As Long
    If initTrue And Not ws Is Nothing Then ListLastColumn = curLastCol
End Property

Property Get ListConfigIndex() As Long
    If initTrue Then ListConfigIndex = curDocCfgIndx
End Property

Property Get FoundDocLineText() As String
    If initTrue Then FoundDocLineText = curDocName & " [" & CStr(curDataRow) & "]"
End Property

Public Function GetCurrentWS() As Worksheet
    If initTrue And Not ws Is Nothing Then Set GetCurrentWS = ws
End Function

Public Function NextEntry() As Boolean
    If ws Is Nothing Then Exit Function
    curDataRow = curDataRow + 1
    Do While curDataRow > curMaxRow
        If Not setRowColumnValues(curDocIndx + 1) Then Exit Function
        curDataRow = curDataRow + 1
    Loop
    NextEntry = True
End Function

Public Function ResetState() As Boolean
    ResetState = setRowColumnValues(LBound(arrSrcSheets))
    resetSearchValues vbNullString, vbNullString
End Function

Public Function SearchData(ByVal fName As String, ByVal lName As String) As Boolean
    Dim i As Integer, j As Integer
    Dim tempF As String, tempL As String, tempData As String
    Dim tempPart As Variant, tempSub As Variant
    Dim initialSearch As Boolean, subF As Boolean
    
    'start the cycle from the beginning if person's name has changed, or if the document is not set
    If ws Is Nothing Or LCase(fName) <> LCase(curFirstName) Or LCase(lName) <> LCase(curLastName) Then
        If Not setRowColumnValues(LBound(arrSrcSheets)) Then Exit Function
        resetSearchValues fName, lName
    End If
    
    'cycle through documents
    For i = curDocIndx To UBound(arrSrcSheets)
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "For"
        tempF = fName
        tempL = lName
        'feature1:disabled//tempStrL = vbNullString
        initialSearch = False
        subF = False
        'reset search values for next document, otherwise search current document
        If i <> curDocIndx Then
            If Not setRowColumnValues(i) Then Exit Function
        End If
        
        'initial search for person's name
SearchDataFindPerson:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataFindPerson"
        'if initial search is completed, search next data
        If curPersonFound Then GoTo SearchDataFindNext
        'set up search data or make a search for name
        If curSrcRange Is Nothing Then Set curSrcRange = ws.Range(ws.Cells(2, arrDefCol(curDocIndx, indxFullName)), ws.Cells(curMaxRow, arrDefCol(curDocIndx, indxFullName)))
        If curSrcRange Is Nothing Then GoTo SearchDataNextSheet
        If curFndRange Is Nothing Then Set curFndRange = curSrcRange.Find(What:=tempL, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
        If curFndRange Is Nothing Then GoTo SearchDataExtendedLast Else curFndRow = curFndRange.Row
        'if something is found, make detailed search
SearchDataFindPersonDetailed:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataFindPersonDetailed"
        tempData = vbNullString
        curLastFound = vbNullString
        For Each tempPart In Split(mdlFunc.RemoveSymbols(CStr(curFndRange.Value), ",.;:()"), " ")
'MsgBox "temppart= " & tempPart & vbNewLine & "curfndrange.value= " & CStr(curFndRange.Value)
            If Len(CStr(tempPart)) > 0 Then
                If UCase(CStr(tempPart)) Like UCase(tempL) And Len(curLastFound) = 0 Then
                    'feature1:disabled//If InStr(UCase(tempStrL), UCase(CStr(temppart))) > 0 Then Exit For
                    curLastFound = CStr(tempPart)
                Else
                    If Len(tempData) > 0 Then tempData = tempData & " "
                    tempData = tempData & CStr(tempPart)
                End If
            End If
        Next
        'if lastame is not found then find next data row
SearchDataFindPersonNextTry:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataFindPersonNextTry"
        If Len(curLastFound) = 0 Then
            Set curFndRange = curSrcRange.Find(What:=tempL, After:=curFndRange, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
            If curFndRange Is Nothing Then GoTo SearchDataExtendedLast
            If curFndRange.Row = curFndRow Then GoTo SearchDataExtendedLast
            GoTo SearchDataFindPersonDetailed
        End If
        'if lastname is found then start firstname compare
SearchDataFindPersonFirst:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataFindPersonFirst"
        For Each tempPart In Split(tempData, " ")
            If subF Then
                For Each tempSub In Split(UCase(arrSubFNamesStr(j)), ",")
                    If UCase(CStr(tempPart)) = UCase(CStr(tempSub)) Then
                        initialSearch = True
                        curFirstMask = CStr(tempSub)
                        Exit For
                    End If
                Next
            Else
                If UCase(CStr(tempPart)) Like UCase(tempF) Then initialSearch = True
            End If
            'initial search successful: lastname and firstname are found
            If initialSearch Then
                curFirstFound = CStr(tempPart)
                curSearchedName = CStr(curFndRange.Value)
                curFndRow = curFndRange.Row
                curDataRow = curFndRange.Row
                If Not subF Then curFirstMask = tempF
                curLastMask = tempL
                curPersonFound = True
                SearchData = True
                Exit Function
            End If
        Next
        'if firstname is not found then find next data row
        Set curFndRange = curSrcRange.Find(What:=tempL, After:=curFndRange, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
        If curFndRange Is Nothing Then GoTo SearchDataExtendedLast
        If curFndRange.Row = curFndRow Then GoTo SearchDataExtendedFirst
        GoTo SearchDataFindPersonDetailed
        
        'mask lastname
SearchDataExtendedLast:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataExtendedLast"
        If mdlFunc.NextMaskSD(curLastName, tempL) Then
            curLastMaskUsed = True
            curFndRow = 0
            If Not curFndRange Is Nothing Then Set curFndRange = Nothing
            GoTo SearchDataFindPerson
        Else
            GoTo SearchDataNextSheet
        End If
        
        'mask firstname
SearchDataExtendedFirst:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataExtendedFirst"
        If curFirstMaskUsed Then
            subF = False
        Else
            If Not subF And IsArray(arrSubFNamesStr) Then
                For j = LBound(arrSubFNamesStr) To UBound(arrSubFNamesStr)
                    For Each tempSub In Split(UCase(arrSubFNamesStr(j)), ",")
                        If UCase(fName) = UCase(CStr(tempSub)) Then
                            subF = True
                            Exit For
                        End If
                    Next
                    If subF Then Exit For
                Next j
            End If
        End If
        If subF Then
            curFirstMaskUsed = True
            GoTo SearchDataFindPersonDetailed
        End If
        If mdlFunc.NextMaskSD(curFirstName, tempF) Then
            curFirstMaskUsed = True
            GoTo SearchDataFindPersonDetailed
        Else
            'feature1:disabled//tempF = curFirstName
            'feature1:disabled//curFirstMaskUsed = False
            'feature1:disabled//If InStr(UCase(tempStrL), UCase(curLastFound)) = 0 And Len(curLastFound) > 0 Then
            'feature1:disabled//    If Len(tempStrL) > 0 And Len(curLastFound) > 0 Then tempStrL = tempStrL & ","
            'feature1:disabled//    tempStrL = tempStrL & curLastFound
            'feature1:disabled//End If
            'feature1:disabled//GoTo SearchDataExtendedLast
            GoTo SearchDataNextSheet
        End If
        
        'if person is found then find next record
SearchDataFindNext:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataFindNext"
        Set curFndRange = curSrcRange.Find(What:=curSearchedName, After:=curFndRange, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If curFndRange Is Nothing Then GoTo SearchDataNextSheet
        If curFndRange.Row = curFndRow Then GoTo SearchDataNextSheet
        curDataRow = curFndRange.Row
        SearchData = True
        Exit Function
        
        'next document
SearchDataNextSheet:
'MsgBox tempL & vbNewLine & tempF & vbNewLine & "SearchDataNextSheet"
        If i = UBound(arrSrcSheets) Then resetSearchValues vbNullString, vbNullString Else resetSearchValues fName, lName
    Next i
    
End Function

Public Function IsIndexValid(ByVal indx As Integer) As Boolean
    IsIndexValid = setRowColumnValues(indx)
End Function

Public Function GetPaymentListNames(ByRef arrDocNames() As String) As Boolean
    Dim i As Integer
    If Not initTrue Or Not IsArray(arrCurNames) Then Exit Function
    If Len(arrCurNames(LBound(arrCurNames))) = 0 Then Exit Function
    ReDim arrDocNames(LBound(arrCurNames) To UBound(arrCurNames))
    For i = LBound(arrCurNames) To UBound(arrCurNames)
        arrDocNames(i) = arrCurNames(i)
    Next i
    GetPaymentListNames = True
End Function

Public Function GetPaymentSheets(ByRef arrPSheets() As Worksheet) As Boolean
    Dim i As Integer
    If initTrue And Not arrSrcSheets(LBound(arrSrcSheets)) Is Nothing Then
        ReDim arrPSheets(LBound(arrSrcSheets) To UBound(arrSrcSheets))
        For i = LBound(arrSrcSheets) To UBound(arrSrcSheets)
            Set arrPSheets(i) = arrSrcSheets(i)
        Next i
        GetPaymentSheets = True
    End If
End Function

Private Function getTypeSpecificName(ByVal indx As Integer) As String
    Dim dTp As String, rTp As Range
    
    If indx < LBound(arrSrcNames) Or indx > UBound(arrSrcNames) Or arrSrcSheets(indx) Is Nothing Then Exit Function
    
    getTypeSpecificName = arrSrcNames(indx)
    dTp = arrSrcParams(indx, indxType)
    
    Select Case dTp
    
    Case "1"
        Set rTp = arrSrcSheets(indx).Cells(arrSrcSheets(indx).Rows.Count, arrDefCol(indx, indxAccOrDef)).End(xlUp)
        If rTp.Row > 1 And Len(rTp.Value) >= 4 Then If IsNumeric(Right(rTp.Value, 4)) Then getTypeSpecificName = arrSrcNames(indx) & " *" & Right(rTp.Value, 4)
    Case Else
    
    End Select
    
End Function

Private Sub setCurListNames()
    Dim i As Integer, j As Integer, k As Integer
    Dim arrTempNames() As String
    
    ReDim arrCurNames(LBound(arrSrcNames) To UBound(arrSrcNames))
    ReDim arrTempNames(LBound(arrSrcNames) To UBound(arrSrcNames))
    
    For i = LBound(arrSrcNames) To UBound(arrSrcNames)
        arrTempNames(i) = getTypeSpecificName(i)
    Next i
    
    For i = UBound(arrTempNames) To LBound(arrTempNames) Step -1
        k = 1
        If i > LBound(arrTempNames) Then
            For j = i - 1 To LBound(arrTempNames) Step -1
                If arrTempNames(i) = arrTempNames(j) Then k = k + 1
            Next j
        End If
        arrCurNames(i) = arrTempNames(i)
        If k > 1 Then arrCurNames(i) = arrCurNames(i) & " #" & k
    Next i
End Sub

Private Sub resetSearchValues(ByVal fName As String, ByVal lName As String)
    Set curSrcRange = Nothing
    Set curFndRange = Nothing
    curFndRow = 0
    curFirstName = fName
    curLastName = lName
    curFirstFound = vbNullString
    curLastFound = vbNullString
    curFirstMask = vbNullString
    curLastMask = vbNullString
    curSearchedName = vbNullString
    curPersonFound = False
    curFirstMaskUsed = False
    curLastMaskUsed = False
End Sub

Private Function setRowColumnValues(ByVal indx As Integer) As Boolean
    If Not IsArray(arrSrcSheets) Then Exit Function
    If indx < LBound(arrSrcSheets) Or indx > UBound(arrSrcSheets) Then Exit Function
    If arrSrcSheets(indx) Is Nothing Then Exit Function
    Set ws = arrSrcSheets(indx)
    curDocIndx = indx
    curDocCfgIndx = arrSrcCfgIndxs(indx)
    curDocName = arrCurNames(indx)
    curDataRow = 1
    curLastCol = ws.Cells(1, ws.Columns.Count).End(xlToLeft).Column
    curMaxRow = ws.Cells(ws.Rows.Count, arrDefCol(indx, indxFullName)).End(xlUp).Row
    setRowColumnValues = True
End Function
