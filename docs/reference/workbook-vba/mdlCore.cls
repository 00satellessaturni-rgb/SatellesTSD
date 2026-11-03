Attribute VB_Name = "mdlCore"
Option Explicit

Private configWs As Worksheet
Private arrSrcBooks() As Workbook
Private arrSrcSheets() As Worksheet
Private arrSrcNames() As String
Private arrSrcNameIndxs() As Long
'Private arrPayBooks() As Worksheet
'Private arrPaySheets() As Worksheet
'Private arrPayNames() As String
'Private arrPayNameIndxs() As Long

Private arrDocRowColIndx() As Long
Private arrFilePath() As String

Private reportWb As Workbook
Private reportIndx As Integer
Private torIndx As Integer

Private curDocState As Integer
Private Const docStateReport As Integer = 1
Private Const docStateTsd As Integer = 2

Private initTrue As Boolean
Private rowDocEmp As Long
Private rowDocStt As Long
Private curDfltCompName As String
'Private rowDocRep As Long

Private Const empSrcIndx As Long = 0

Private Const sttArrMaxIndx As Long = 19
Private Const colDocName As Long = 1
Private Const colDocCheck As Long = 2

Private Const empDefKey As String = "{employee_define}"
Private Const sttDefKey As String = "{payout_define}"
'Private Const repDefKey As String = "{report_config}"
'Private Const empArrMaxIndx As Long = 0
'Private Const rowDocEmp As Long = 9
'Private Const rowDocStt As Long = 11


Public Function InitData() As Boolean
    Dim i As Long
    Dim cfgRange As Range
    
    initTrue = False
    
    On Error GoTo InitDataErr
    
    Set configWs = Sheet1
    ReDim arrSrcBooks(0)
    ReDim arrSrcSheets(0)
    ReDim arrSrcNames(0)
    ReDim arrSrcNameIndxs(0)
    ReDim arrDocRowColIndx(0 To 1, 0 To 1)
    
    Set cfgRange = configWs.Range("A1").EntireColumn.Find(What:=empDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If cfgRange Is Nothing Then Exit Function Else rowDocEmp = cfgRange.Row + 1
    
    Set cfgRange = configWs.Range("A1").EntireColumn.Find(What:=sttDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If cfgRange Is Nothing Then Exit Function Else rowDocStt = cfgRange.Row + 1
    
    If Len(CStr(configWs.Cells(rowDocEmp, colDocName).Value)) > 0 And Len(CStr(configWs.Cells(rowDocEmp, colDocCheck).Value)) > 0 Then
        arrDocRowColIndx(0, 0) = rowDocEmp
        arrDocRowColIndx(1, 0) = configWs.Cells(rowDocEmp, configWs.Columns.Count).End(xlToLeft).Column + 1 - colDocCheck
    End If
    
    For i = 0 To sttArrMaxIndx
        If Len(CStr(configWs.Cells(rowDocStt + i, colDocName).Value)) > 0 And Len(CStr(configWs.Cells(rowDocStt + i, colDocCheck).Value)) > 0 Then
            If arrDocRowColIndx(0, UBound(arrDocRowColIndx, 2)) > 0 Then ReDim Preserve arrDocRowColIndx(1, UBound(arrDocRowColIndx, 2) + 1)
            arrDocRowColIndx(0, UBound(arrDocRowColIndx, 2)) = rowDocStt + i
            arrDocRowColIndx(1, UBound(arrDocRowColIndx, 2)) = configWs.Cells(rowDocStt + i, configWs.Columns.Count).End(xlToLeft).Column + 1 - colDocCheck
        End If
    Next i
    
    For i = LBound(arrDocRowColIndx, 2) To UBound(arrDocRowColIndx, 2)
        If arrDocRowColIndx(0, i) = 0 Or arrDocRowColIndx(1, i) = 0 Then Exit Function
    Next i
    
    InitData = True
    initTrue = True
    Exit Function
InitDataErr:
End Function

Public Function IsStateReport() As Boolean
    If curDocState = docStateReport Then IsStateReport = True
End Function

Public Function IsStateTsd() As Boolean
    If curDocState = docStateTsd Then IsStateTsd = True
End Function

Public Function GetEmployeeWSIndex() As Long
    GetEmployeeWSIndex = empSrcIndx
End Function

Public Function GetEmployeeListName() As String
    If initTrue Then GetEmployeeListName = arrSrcNames(empSrcIndx)
End Function

Public Function GetConfigWs() As Worksheet
    If initTrue Then Set GetConfigWs = configWs
End Function

Public Function GetEmployeeWs() As Worksheet
    If initTrue Then Set GetEmployeeWs = arrSrcSheets(empSrcIndx)
End Function

Public Function GetInterimReportWs() As Worksheet
    If Not reportWb Is Nothing Then If reportIndx >= 1 And reportIndx <= reportWb.Worksheets.Count Then Set GetInterimReportWs = reportWb.Worksheets(reportIndx)
End Function

Public Function GetTorControlWs() As Worksheet
    If Not reportWb Is Nothing Then If torIndx >= 1 And torIndx <= reportWb.Worksheets.Count Then Set GetTorControlWs = reportWb.Worksheets(torIndx)
End Function

Public Function GetSourceFilePath() As String
    If initTrue And Not arrSrcBooks(empSrcIndx) Is Nothing Then GetSourceFilePath = arrSrcBooks(empSrcIndx).Path
End Function

Public Function GetReportFilePath() As String
    If Not reportWb Is Nothing Then GetReportFilePath = reportWb.Path
End Function

Public Function GetDefaultName() As String
    GetDefaultName = curDfltCompName
End Function

Public Sub GetSourceIndexes(ByRef arrIndxs() As Long)
    Dim i As Integer
    ReDim arrIndxs(LBound(arrSrcNameIndxs) To UBound(arrSrcNameIndxs))
    For i = LBound(arrSrcNameIndxs) To UBound(arrSrcNameIndxs)
        arrIndxs(i) = arrSrcNameIndxs(i)
    Next i
End Sub

Public Sub GetSourceSheets(ByRef arrSheets() As Worksheet)
    Dim i As Integer
    ReDim arrSheets(LBound(arrSrcSheets) To UBound(arrSrcSheets))
    For i = LBound(arrSrcSheets) To UBound(arrSrcSheets)
        Set arrSheets(i) = arrSrcSheets(i)
    Next i
End Sub

Public Function GetCurCompany() As String
    If initTrue Then GetCurCompany = CStr(configWs.Range("A2").Value)
End Function

Private Function checkDocument(ByRef ws As Worksheet, ByRef docRow As Long) As Boolean
    Dim i As Long, j As Long
    
    docRow = 0
    
    If ws Is Nothing Or configWs Is Nothing Then Exit Function
    
    For i = LBound(arrDocRowColIndx, 2) To UBound(arrDocRowColIndx, 2)
        If arrDocRowColIndx(1, i) = ws.Cells(1, ws.Columns.Count).End(xlToLeft).Column Then
            docRow = arrDocRowColIndx(0, i)
            For j = 1 To arrDocRowColIndx(1, i)
                If CStr(configWs.Cells(arrDocRowColIndx(0, i), j + colDocCheck - 1).Value) <> CStr(ws.Cells(1, j).Value) Then
                    docRow = 0
                    Exit For
                End If
            Next j
        End If
        If docRow > 0 Then
            If ws.Cells(2, ws.Columns.Count).End(xlToLeft).Column = 1 And Len(CStr(ws.Cells(2, ws.Columns.Count).End(xlToLeft).Value)) = 0 Then Exit Function
            checkDocument = True
            Exit Function
        End If
    Next i
    
End Function

Private Function getFilePath(ByRef filePath As String) As Boolean
    Dim fd As FileDialog
    Dim sItem As Variant
    getFilePath = False
    filePath = vbNullString
    Set fd = Application.FileDialog(msoFileDialogFilePicker)
    With fd
        .Title = "Please select the file."
        .AllowMultiSelect = False
        .InitialFileName = "C:\Users\" & Environ$("Username") & "\Downloads\"
        .Filters.Add "Custom Excel Files", "*.xlsx, *.xls"
        .FilterIndex = 1
        If .Show = False Then MsgBox "Please select Excel file.", vbExclamation: Exit Function
        
        For Each sItem In .SelectedItems
            filePath = sItem
        Next
        
    End With
    If Len(filePath) > 0 Then getFilePath = True
    Set fd = Nothing
End Function

Private Function getFilePathMult(Optional ByVal subFd As String) As Boolean
    Dim fd As FileDialog
    Dim sItem As Variant
    Dim fso As New FileSystemObject
    getFilePathMult = False
    ReDim arrFilePath(0)
    If Len(subFd) > 0 Then
        subFd = Trim(mdlFunc.RemoveSymbols(subFd, ":;$#%@!&*~"))
        If Len(Replace(subFd, "\", vbNullString)) > 0 Then
            subFd = subFd & "\"
            Do While InStr(subFd, "\\") > 0
                subFd = Replace(subFd, "\\", "\")
            Loop
            If Left(subFd, 1) = "\" Then subFd = Right(subFd, Len(subFd) - 1)
            If Not fso.FolderExists("C:\Users\" & Environ$("Username") & "\Downloads\" & subFd) Then
                fso.CreateFolder "C:\Users\" & Environ$("Username") & "\Downloads\" & subFd
            End If
        Else
            subFd = vbNullString
        End If
    End If
    Set fd = Application.FileDialog(msoFileDialogFilePicker)
    With fd
        .Title = "Please select the file."
        .AllowMultiSelect = True
        .InitialFileName = "C:\Users\" & Environ$("Username") & "\Downloads\" & subFd
        .Filters.Add "Custom Excel Files", "*.xlsx, *.xls"
        .FilterIndex = 1
        If .Show = False Then MsgBox "Please select Excel file.", vbExclamation: Exit Function
        
        For Each sItem In .SelectedItems
            If Len(arrFilePath(UBound(arrFilePath))) > 0 Then ReDim Preserve arrFilePath(UBound(arrFilePath) + 1)
            arrFilePath(UBound(arrFilePath)) = sItem
        Next
        
    End With
    
    If Len(arrFilePath(UBound(arrFilePath))) > 0 Then getFilePathMult = True
    Set fd = Nothing
End Function

Public Function SetSourceReport() As Boolean
    Dim filePath As String
    Dim i As Integer
    Dim showNotFoundMsg As Boolean
    Dim oReport As New cInterimReport
    Dim oEmp As New cEmployee
    
    On Error GoTo SetSourceReportError
    
    If Not getFilePath(filePath) Then Exit Function
    
    If Not oReport.InitData(configWs) Then Exit Function
    
    ClearWs
    
    ClearReportWb
    
    curDfltCompName = vbNullString
    
    Set reportWb = Application.Workbooks.Open(filePath)
    
    If reportWb Is Nothing Then Exit Function
    
    showNotFoundMsg = True
    
    If reportWb.Worksheets.Count = 0 Then GoTo SetSourceReportMsg
    
    For i = 1 To reportWb.Worksheets.Count
        If reportIndx = 0 Then If oReport.CheckHeaders(reportWb.Worksheets(i).Cells(1, 1).EntireRow) Then reportIndx = i
        If torIndx = 0 Then
            If oEmp.InitData(configWs, reportWb.Worksheets(i), False) Then
                torIndx = i
                curDfltCompName = oEmp.EmployerName
            End If
        End If
        If reportIndx > 0 And torIndx > 0 Then
            showNotFoundMsg = False
            Exit For
        End If
    Next

SetSourceReportMsg:
    Set oEmp = Nothing
    If showNotFoundMsg Then
        MsgBox "Aruande andmed puuduvad." & vbNewLine & filePath, vbExclamation, "Set source report"
        ClearReportWb
    Else
        curDocState = docStateTsd
        SetSourceReport = True
    End If
    
    Exit Function
SetSourceReportError:
    MsgBox "Tundmatu viga!", vbCritical, "Set source report"
End Function

Public Function SetSourceMult() As Boolean
    Dim wb As Workbook
    Dim ws As Worksheet
    Dim wItem As Variant, fItem As Variant
    Dim filePath As String
    Dim testItem() As String
    Dim i As Integer
    Dim inUseIndex As Integer
    Dim showNotFoundMsg As Boolean
    Dim curDocIndx As Long
    Dim oEmp As New cEmployee
    
    On Error GoTo SetSourceMultError
    
    If Not getFilePathMult("Palgad") Then Exit Function
    
    For Each fItem In arrFilePath
    
        filePath = fItem
        If Len(filePath) = 0 Then GoTo SetSourceMultNextFile
        
        testItem = Split(filePath, "\")
        For i = LBound(arrSrcBooks) To UBound(arrSrcBooks)
            If Not arrSrcBooks(i) Is Nothing Then
                If arrSrcBooks(i).Name = testItem(UBound(testItem)) And filePath <> arrSrcBooks(i).FullName Then
                    MsgBox "A file with the same name is already opened." & vbNewLine & "Please close " & arrSrcBooks(i).Name & " or rename this file before opening.", vbInformation, "Set source"
                    Exit Function
                End If
            End If
        Next i
        inUseIndex = fileInUseFirstIndex(-1, filePath)
        If inUseIndex >= 0 Then Set wb = arrSrcBooks(inUseIndex) Else Set wb = Workbooks.Open(filePath)
        showNotFoundMsg = True
        For Each wItem In wb.Worksheets
            Set ws = wItem
            If checkDocument(ws, curDocIndx) Then
                SetSourceMult = True
                showNotFoundMsg = False
                If curDocIndx = rowDocEmp Then
                    curDfltCompName = vbNullString
                    If Not arrSrcBooks(empSrcIndx) Is Nothing And Not arrSrcSheets(empSrcIndx) Is Nothing Then
                        If arrSrcBooks(empSrcIndx).FullName = filePath And arrSrcSheets(empSrcIndx).Name = ws.Name Then GoTo SetSourceMultNextSheet
                        Select Case MsgBox("Found sheet: " & ws.Name & vbNewLine & "Existing sheet: " & arrSrcSheets(empSrcIndx).Name & vbNewLine & CStr(configWs.Cells(rowDocEmp, colDocName).Value) & ": " & "this type of document is already added." & vbNewLine & "Replace?", vbYesNoCancel, "Set source")
                            Case vbYes
                                If arrSrcBooks(empSrcIndx).FullName <> wb.FullName Then
                                    If fileInUseFirstIndex(empSrcIndx, arrSrcBooks(empSrcIndx).FullName) < 0 Then
                                        arrSrcBooks(empSrcIndx).Close False
                                    End If
                                End If
                            Case vbNo
                                GoTo SetSourceMultNextSheet
                            Case vbCancel
                                GoTo SetSourceMultEnd
                        End Select
                    End If
                    Set arrSrcBooks(empSrcIndx) = wb
                    Set arrSrcSheets(empSrcIndx) = ws
                    arrSrcNames(empSrcIndx) = CStr(configWs.Cells(rowDocEmp, colDocName).Value)
                    If Not arrSrcSheets(empSrcIndx) Is Nothing Then
                        If oEmp.InitData(configWs, arrSrcSheets(empSrcIndx), False) Then
                            curDfltCompName = oEmp.EmployerName
                        End If
                        Set oEmp = Nothing
                    End If
                Else
                    For i = LBound(arrSrcBooks) To UBound(arrSrcBooks)
                        If Not arrSrcBooks(i) Is Nothing And Not arrSrcSheets(i) Is Nothing Then
                            If arrSrcBooks(i).FullName = wb.FullName And arrSrcSheets(i).Name = ws.Name Then GoTo SetSourceMultNextSheet
                        End If
                    Next i
                    ReDim Preserve arrSrcBooks(UBound(arrSrcBooks) + 1)
                    ReDim Preserve arrSrcSheets(UBound(arrSrcSheets) + 1)
                    ReDim Preserve arrSrcNames(UBound(arrSrcNames) + 1)
                    ReDim Preserve arrSrcNameIndxs(UBound(arrSrcNameIndxs) + 1)
                    Set arrSrcBooks(UBound(arrSrcBooks)) = wb
                    Set arrSrcSheets(UBound(arrSrcSheets)) = ws
                    arrSrcNames(UBound(arrSrcNames)) = CStr(configWs.Cells(curDocIndx, colDocName).Value)
                    arrSrcNameIndxs(UBound(arrSrcNameIndxs)) = curDocIndx - rowDocStt
                End If
            End If
SetSourceMultNextSheet:
        Next
        If showNotFoundMsg Then
            MsgBox "Andmed puuduvad." & vbNewLine & filePath, vbExclamation, "Set source"
            wb.Close False
        ElseIf fileInUseFirstIndex(-1, wb.FullName) < 0 Then
            wb.Close False
        End If
    
SetSourceMultNextFile:
        Set ws = Nothing
        Set wb = Nothing
    Next

SetSourceMultEnd:
    Set ws = Nothing
    Set wb = Nothing
    curDocState = docStateReport
    Exit Function
    
SetSourceMultError:
    Set ws = Nothing
    Set wb = Nothing
    MsgBox "Tundmatu viga!", vbCritical, "Set source"
End Function

Public Function GetDocumentList(ByRef docList() As String) As Boolean
    Dim i As Integer, j As Integer
    Dim temp() As String
    
    For i = LBound(arrSrcNames) To UBound(arrSrcNames)
        If UBound(arrSrcBooks) < i Or UBound(arrSrcSheets) < i Then Exit For
        If Len(arrSrcNames(i)) > 0 And Not arrSrcBooks(i) Is Nothing And Not arrSrcSheets(i) Is Nothing Then j = j + 1
    Next i
    
    If j = 0 Then Exit Function
    
    ReDim docList(0 To j - 1, 0 To 2)
    
    j = 0
    
    For i = LBound(arrSrcNames) To UBound(arrSrcNames)
        If Len(arrSrcNames(i)) > 0 And Not arrSrcBooks(i) Is Nothing And Not arrSrcSheets(i) Is Nothing Then
            docList(j, 0) = arrSrcNames(i)
            docList(j, 1) = arrSrcBooks(i).FullName
            docList(j, 2) = arrSrcSheets(i).Name
            j = j + 1
        End If
    Next i
    
    'If Len(ettevNimi) = 0 Then
    '    temp = Split(arrSrcBooks(LBound(arrSrcBooks)).Path, "\")
    '    If temp(UBound(temp)) Like "*-palgad*" Then ettevNimi = Split(temp(UBound(temp)), "-palgad")(0)
    'End If
    
    GetDocumentList = True
End Function

Public Function GetReportSheetList(ByRef shtList() As String) As Boolean
    Dim i As Integer, j As Integer
    Dim temp() As String
    
    If reportWb Is Nothing Then Exit Function
    If reportWb.Worksheets.Count = 0 Then Exit Function

    If reportIndx < 1 Or reportIndx > reportWb.Worksheets.Count Then Exit Function
    
    ReDim shtList(0 To reportWb.Worksheets.Count - 1, 0 To 2)
    
    j = 0
    shtList(j, 0) = reportWb.Worksheets(reportIndx).Name
    shtList(j, 1) = reportWb.FullName
    shtList(j, 2) = reportIndx
    
    j = 1
    shtList(j, 0) = reportWb.Worksheets(torIndx).Name
    shtList(j, 1) = reportWb.FullName
    shtList(j, 2) = torIndx
    
    For i = 1 To reportWb.Worksheets.Count
        If i <> reportIndx And i <> torIndx Then
            j = j + 1
            shtList(j, 0) = reportWb.Worksheets(i).Name
            shtList(j, 1) = reportWb.FullName
            shtList(j, 2) = i
        End If
    Next i
    
    'If Len(ettevNimi) = 0 Then
    '    temp = Split(reportWb.FullName, "\")
    '    If temp(UBound(temp)) Like "report-details-*-20[0-9][0-9]*" Then ettevNimi = Split(Split(temp(UBound(temp)), "report-details-")(1), "-20")(0)
    'End If
    
    GetReportSheetList = True
End Function

Public Function RemoveItem(ByVal itemName As String) As Boolean
'    Dim i As Integer
'    Dim indx As Integer
'    indx = -1
'    On Error GoTo RemoveItemError
'    For i = arrLbnd To arrUbnd
'        If arrNames(i) = itemName Then
'            indx = i
'            Exit For
'        End If
'    Next i
'    If indx <> -1 Then
'        Set arrWSheets(i) = Nothing
'        If fileInUseFirstIndex(i, arrWBooks(i).FullName) < 0 Then arrWBooks(i).Close False
'        Set arrWBooks(i) = Nothing
'        RemoveItem = True
'    End If
RemoveItemError:
End Function

Public Function RemoveFile(ByVal filePath As String) As Boolean
'    If Len(filepath) > 0 Then
'        Dim arrIndexes() As Integer
'        Dim i As Integer
'        On Error GoTo RemoveFileError
'        If validIndexes(arrIndexes, filepath) Then
'            For i = 0 To UBound(arrIndexes)
'                Set arrWSheets(arrIndexes(i)) = Nothing
'                If i = UBound(arrIndexes) Then arrWBooks(arrIndexes(i)).Close False
'                Set arrWBooks(arrIndexes(i)) = Nothing
'            Next i
'            RemoveFile = True
'        End If
'    End If
RemoveFileError:
End Function

Private Function fileInUseFirstIndex(ByVal excludeIndex As Integer, ByVal filePath As String) As Integer
    Dim i As Integer
    fileInUseFirstIndex = -1
    For i = LBound(arrSrcBooks) To UBound(arrSrcBooks)
        If i <> excludeIndex And Not arrSrcBooks(i) Is Nothing Then
            If arrSrcBooks(i).FullName = filePath Then
                fileInUseFirstIndex = i
                Exit For
            End If
        End If
    Next i
End Function

Private Function validIndexes(ByRef arrIndexes() As Integer, Optional ByVal filePath As String) As Boolean
'    Dim i As Integer
'    ReDim arrIndexes(0)
'    arrIndexes(0) = -1
'    For i = arrLbnd To arrUbnd
'        If Not arrWBooks(i) Is Nothing And Not arrWSheets(i) Is Nothing Then
'            If Len(filepath) = 0 Or Len(filepath) > 0 And arrWBooks(i).FullName = filepath Then
'                If arrIndexes(0) <> -1 Then ReDim Preserve arrIndexes(UBound(arrIndexes) + 1)
'                arrIndexes(UBound(arrIndexes)) = i
'            End If
'        End If
'    Next i
'    If arrIndexes(0) <> -1 Then validIndexes = True
End Function

Public Function DocumentsAreSet() As Boolean
    Dim i As Integer, j As Integer
    j = 0
    For i = LBound(arrSrcNames) To UBound(arrSrcNames)
        If UBound(arrSrcBooks) < i Or UBound(arrSrcSheets) < i Then Exit For
        If Len(arrSrcNames(i)) > 0 And Not arrSrcBooks(i) Is Nothing And Not arrSrcSheets(i) Is Nothing Then j = j + 1
    Next i
    If j > 1 And Len(arrSrcNames(empSrcIndx)) > 0 And Not arrSrcSheets(empSrcIndx) Is Nothing Then DocumentsAreSet = True
End Function

Public Function InterimReportSet() As Boolean
    If Not reportWb Is Nothing Then If reportIndx >= 1 And reportIndx <= reportWb.Worksheets.Count And torIndx >= 1 And torIndx <= reportWb.Worksheets.Count Then InterimReportSet = True
End Function

Public Sub ClearWs()
    On Error GoTo ClearWsError
    Dim i As Integer, j As Integer
    curDocState = 0
    For i = LBound(arrSrcBooks) To UBound(arrSrcBooks)
        Set arrSrcSheets(i) = Nothing
        If Not arrSrcBooks(i) Is Nothing Then
            If i < UBound(arrSrcBooks) Then
                For j = i + 1 To UBound(arrSrcBooks)
                    If Not arrSrcBooks(j) Is Nothing Then
                        If arrSrcBooks(j).FullName = arrSrcBooks(i).FullName Then Set arrSrcBooks(j) = Nothing
                    End If
                Next j
            End If
            arrSrcBooks(i).Close False
            Set arrSrcBooks(i) = Nothing
        End If
    Next i
    ReDim arrSrcBooks(0)
    ReDim arrSrcSheets(0)
    ReDim arrSrcNames(0)
    ReDim arrSrcNameIndxs(0)
ClearWsError:
End Sub

Public Sub ClearReportWb()
    curDocState = 0
    reportIndx = 0
    torIndx = 0
    If Not reportWb Is Nothing Then reportWb.Close False
    Set reportWb = Nothing
End Sub

Sub ImportCSVFile(filePath As String, ByRef ws As Worksheet)
'    Dim line As String
'    Dim arrayOfElements
'    Dim linenumber As Integer
'    Dim elementnumber As Integer
'    Dim element As Variant
'
'    linenumber = 0
'    elementnumber = 0
'
'    Open filepath For Input As #1 ' Open file for input
'        Do While Not EOF(1) ' Loop until end of file
'            linenumber = linenumber + 1
'            Line Input #1, line
'            arrayOfElements = Split(line, ";")
'
'            elementnumber = 0
'            For Each element In arrayOfElements
'                elementnumber = elementnumber + 1
'                ws.Cells(linenumber, elementnumber).Value = element
'            Next
'        Loop
'    Close #1 ' Close file.
End Sub

