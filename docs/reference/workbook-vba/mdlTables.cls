Attribute VB_Name = "mdlTables"
Option Explicit
Option Compare Text

Private Const fml = "=SUM(#1:OFFSET(#2,0,-1))"
Private Const dummy1 As String = "#1"
Private Const dummy2 As String = "#2"
Private Const dummy As String = "???"
Private Const fnDefRep As String = "\report-details-"
Private Const fnErrRep As String = "\report-details(vead)-"
Private Const fnExtRep As String = ".xlsx"
Private Const fnDefTsd As String = "\tsd-details-"
Private Const fnErrTsd As String = "\tsd-details(vead)-"
Private Const fnExtTsd As String = ".xlsx"
Private Const fnDefExp As String = "tsd-"
Private Const fnErrExp As String = "tsd(vead)-"
Private Const fnExt1Exp As String = "-lisa1.csv"
Private Const fnExt2Exp As String = "-lisa2.csv"

Public Sub GenerateReport(ByVal dateString As String, ByVal compName As String, ByVal useHttp As Boolean)
    Dim wb As Workbook
    Dim ws As Worksheet
    
    Dim wsP As Worksheet 'panga valjavote/kassa
    Dim wsE As Worksheet 'tootajate loend
    Dim wsR As Worksheet 'vahearuanne
    Dim wsC As Worksheet 'kontrollaruanne
    Dim arrWs() As Worksheet 'extra reports
    Dim arrWsRows() As Long
    
    Dim i As Long, j As Long, iC As Long, iR As Long, tmpRow As Long
    Dim catRepCount As Integer
    Dim extVal As Boolean
    Dim extRate As Double
    Dim sum1 As Double, sum2 As Double
    Dim temp As String, frmla As String, extMsg As String, descMsg As String, catMsg As String, custDesc As String
    Dim rSearch As Range, rFound As Range
    Dim arrIndxs() As Long
    Dim arrSheets() As Worksheet
    Dim arrNames() As String
    Dim oEmployee As New cEmployee
    Dim oPayOut As New cPayOut
    Dim oInterimReport As New cInterimReport
    Dim oCategory As New cCategory
    
    If Not IsDate(dateString) Then
        MsgBox "Invalid date/period!", vbCritical, Title:="Generate report"
        Exit Sub
    End If
    
    dateString = Format(CDate(dateString), "yyyy.MM.dd")
    
    If Not oEmployee.InitData(mdlCore.GetConfigWs, mdlCore.GetEmployeeWs, useHttp) Then
        MsgBox "Employee config failed!", vbCritical, Title:="Employee config"
        Exit Sub
    End If
    
    mdlCore.GetSourceIndexes arrIndxs()
    mdlCore.GetSourceSheets arrSheets()
    
    If Not oPayOut.InitData(mdlCore.GetConfigWs, arrIndxs(), arrSheets(), mdlCore.GetEmployeeWSIndex) Then
        MsgBox "PayOuts config failed!", vbCritical, Title:="PayOuts config"
        Exit Sub
    End If
    
    If Not oPayOut.GetPaymentSheets(arrSheets()) Then
        MsgBox "Failed to set po sheet array!", vbCritical, Title:="PayOuts sheets"
        Exit Sub
    End If
    
    If Not oPayOut.GetPaymentListNames(arrNames()) Then
        MsgBox "Failed to set po name array!", vbCritical, Title:="PayOuts names"
        Exit Sub
    End If
    
    If Not oInterimReport.InitData(mdlCore.GetConfigWs) Then
        MsgBox "Interim Report config failed!", vbCritical, Title:="Report config"
        Exit Sub
    End If
    
    If Not oCategory.InitData(mdlCore.GetConfigWs) Then
        MsgBox "Category filters config failed!", vbCritical, Title:="Category config"
        Exit Sub
    End If
    
    ReDim arrWs(oCategory.MinIndex To oCategory.MaxIndex + 1)
    ReDim arrWsRows(oCategory.MinIndex To oCategory.MaxIndex + 1)
    
    Set wsE = mdlCore.GetEmployeeWs
    
    Set wb = Application.Workbooks.Add
    Set wsR = wb.Sheets.Add(Before:=wb.Worksheets(1))
    wsR.Name = oInterimReport.ReportName
    Set wsC = wb.Worksheets.Add(After:=wb.Worksheets(1))
    wsC.Name = "Otsingu kontroll"
    
    If Not oInterimReport.InitReport(wsR, arrNames()) Then
        MsgBox "Interim Report headers failed!", vbCritical, Title:="Report headers"
    End If
    
    temp = Replace(fml, dummy1, mdlFunc.ColumnLabel(oInterimReport.SumStartColumn) & dummy)
    temp = Replace(temp, dummy2, mdlFunc.ColumnLabel(oInterimReport.SumTotalColumn) & dummy)
    iR = 2
    
    wsC.Cells(1, 1).Value = "Isikukood"
    wsC.Cells(1, 2).Value = "Perekonnanimi"
    wsC.Cells(1, 3).Value = "Eesnimi"
    wsC.Cells(1, 4).Value = "Dokument"
    wsC.Cells(1, 5).Value = "Kuupaev"
    wsC.Cells(1, 6).Value = "Nimi valjavote lehel"
    wsC.Cells(1, 7).Value = "Otsing*"
    wsC.Cells(1, 8).Value = "D/C"
    wsC.Cells(1, 9).Value = "Summa"
    wsC.Cells(1, 10).Value = "Selgitus"
    wsC.Cells(1, 11).Value = "Kontroll/Kategooria"
    iC = 2
    
    For i = oCategory.MinIndex To oCategory.MaxIndex + 1
        If oCategory.SetCatIndex(i) Or i = UBound(arrWs) Then
            If oCategory.ParamReport Or i = UBound(arrWs) Then
                If i = UBound(arrWs) Then
                    Set arrWs(i) = wb.Worksheets.Add(After:=wb.Worksheets(2))
                    arrWs(i).Name = "Tundmatu kategooria"
                Else
                    Set arrWs(i) = wb.Worksheets.Add(After:=wb.Worksheets(2 + catRepCount))
                    arrWs(i).Name = oCategory.CatName
                End If
                arrWs(i).Cells(1, 1).Value = "Isikukood"
                arrWs(i).Cells(1, 2).Value = "Perekonnanimi"
                arrWs(i).Cells(1, 3).Value = "Eesnimi"
                arrWs(i).Cells(1, 4).Value = "Dokument"
                arrWs(i).Cells(1, 5).Value = "Kuupaev"
                arrWs(i).Cells(1, 6).Value = "Nimi valjavote lehel"
                arrWs(i).Cells(1, 7).Value = "Otsing*"
                arrWs(i).Cells(1, 8).Value = "D/C"
                arrWs(i).Cells(1, 9).Value = "Summa"
                arrWs(i).Cells(1, 10).Value = "Selgitus"
                arrWs(i).Cells(1, 11).Value = "Kontroll/Kategooria"
                arrWsRows(i) = 2
                catRepCount = catRepCount + 1
            End If
            If i <> UBound(arrWs) And oCategory.ParamTable Then
                tmpRow = wsR.Cells(wsR.Rows.Count, 1).End(xlUp).Row + 5
                wsR.Cells(tmpRow, 1).Value = oCategory.CatName
                tmpRow = tmpRow + 1
                wsR.Cells(tmpRow, oInterimReport.PersCodeColumn).Value = oInterimReport.PersCodeHeader
                wsR.Cells(tmpRow, oInterimReport.LastNameColumn).Value = oInterimReport.LastNameHeader
                wsR.Cells(tmpRow, oInterimReport.FirstNameColumn).Value = oInterimReport.FirstNameHeader
                wsR.Cells(tmpRow, oInterimReport.SumTotalColumn).Value = oCategory.HeaderNameTotal
                For j = LBound(arrNames) To UBound(arrNames)
                    wsR.Cells(tmpRow, oInterimReport.SumStartColumn + j - LBound(arrNames)).Value = arrNames(j)
                Next j
            End If
        End If
    Next i
    
    oCategory.SetEmpBasedFilter = True
    
    Do While oEmployee.NextEmployee
        
        descMsg = vbNullString
        
        'insert new row to move additional tables down
        wsR.Cells(iR, 1).EntireRow.Insert Shift:=xlDown
        wsR.Cells(iR, 1).EntireRow.ClearFormats
        
        wsR.Cells(iR, oInterimReport.PersCodeColumn).Value = oEmployee.PersonalCode
        wsR.Cells(iR, oInterimReport.LastNameColumn).Value = oEmployee.LastName
        wsR.Cells(iR, oInterimReport.FirstNameColumn).Value = oEmployee.FirstName
        frmla = Replace(temp, dummy, CStr(iR))
        wsR.Cells(iR, oInterimReport.SumTotalColumn).Value = frmla
        wsR.Cells(iR, oInterimReport.StartDateColumn).Value = oEmployee.StartDate
        wsR.Cells(iR, oInterimReport.EndDateColumn).Value = oEmployee.EndDate
        wsR.Cells(iR, oInterimReport.EntryStateColumn).Value = oEmployee.EntryState
        wsR.Cells(iR, oInterimReport.WorkTypeColumn).Value = oEmployee.WorkType
        
        If oEmployee.IsDuplicateEntry Then
            wsR.Range(wsR.Cells(iR, oInterimReport.ReportFirstColumn), wsR.Cells(iR, oInterimReport.ReportLastColumn)).Interior.Color = vbYellow
            GoTo GenerateReportNextEmployee
        End If
        
        If useHttp Then
            oEmployee.IsResident dateString, extVal, extMsg
            wsR.Cells(iR, oInterimReport.ResidenceColumn).Value = extMsg
            oEmployee.HasPension dateString, extVal, extMsg, extRate
            wsR.Cells(iR, oInterimReport.PensionColumn).Value = extMsg
            wsR.Cells(iR, oInterimReport.ControlDateColumn).Value = dateString
        End If
        
        Do While oPayOut.SearchData(oEmployee.FirstName, oEmployee.LastName)
            
            If oPayOut.NameMaskUsed Then
                'MsgBox oPayOut.NameFullMask & vbNewLine & oEmployee.IsUniqueResult(oPayOut.NameLastMask, oPayOut.NameFirstMask)
                If Not oEmployee.IsUniqueResult(oPayOut.NameLastMask, oPayOut.NameFirstMask) Then
                    GoTo GenerateReportPayOutNextSearch
                End If
            End If
            
            If IsDate(oPayOut.TransDate) Then
                If Year(CDate(oPayOut.TransDate)) <> Year(CDate(dateString)) Or Month(CDate(oPayOut.TransDate)) <> Month(CDate(dateString)) Then
                    catMsg = "kuupaev ei kuulu perioodi [" & Year(CDate(dateString)) & "-" & Month(CDate(dateString)) & "]"
                    GoTo GenerateReportPayOutReportData
                End If
            Else
                catMsg = "viga: kehtetu kuupaev [" & oPayOut.TransDate & "]"
                GoTo GenerateReportPayOutReportData
            End If
            
            'define result category
            If oCategory.DefineCategory(oPayOut.ListConfigIndex, oPayOut.Description, oPayOut.NameFieldText, oPayOut.IsDebit, oPayOut.IsCredit, catMsg) Then
                'save found data for employee report
                If Len(descMsg) > 0 Then descMsg = descMsg & " " & vbNewLine
                descMsg = descMsg & "[" & oPayOut.ListDataRow & "][" & oPayOut.NameFieldText & "][" & oCategory.CatName & "]" & oPayOut.Description
                
                If oCategory.ParamSalary Then
                    mdlFunc.DoubleFromString CStr(wsR.Cells(iR, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value), sum1
                    mdlFunc.DoubleFromString oPayOut.Amount, sum2
                    wsR.Cells(iR, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value = Round(Abs(sum1) + Abs(sum2), 2)
                End If
                
                If oCategory.ParamReport Then
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 1).Value = oEmployee.PersonalCode
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 2).Value = oEmployee.LastName
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 3).Value = oEmployee.FirstName
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 4).Value = oPayOut.FoundDocLineText
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 5).Value = oPayOut.TransDate
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 6).Value = oPayOut.NameFieldText
                    If oPayOut.NameMaskUsed Then arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 7).Value = oPayOut.NameFullMask
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 8).Value = oPayOut.DebitCredit
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 9).Value = oPayOut.Amount
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 10).Value = oPayOut.Description
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 11).Value = catMsg
                    'If useHttp And oCategory.ParamSalary Then
                    '    oEmployee.IsResident oPayOut.TransDate, extVal, extMsg
                    '    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 12).Value = extMsg
                    '    oEmployee.HasPension oPayOut.TransDate, extVal, extMsg, extRate
                    '    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 13).Value = extMsg
                    'End If
                    arrWsRows(oCategory.CatIndex) = arrWsRows(oCategory.CatIndex) + 1
                End If
                
                If oCategory.ParamTable Then
                    Set rFound = wsR.Cells(1, 1).EntireColumn.Find(What:=oCategory.CatName, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
                    If Not rFound Is Nothing Then
                        i = 2
                        Do While Len(CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value)) > 0
                            If CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value) = oEmployee.LastName And CStr(wsR.Cells(rFound.Row + i, oInterimReport.FirstNameColumn).Value) = oEmployee.FirstName Then Exit Do
                            i = i + 1
                        Loop
                        If Len(CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value)) = 0 Then wsR.Cells(rFound.Row + i, 1).EntireRow.Insert Shift:=xlDown
                        wsR.Cells(rFound.Row + i, oInterimReport.PersCodeColumn).Value = oEmployee.PersonalCode
                        wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value = oEmployee.LastName
                        wsR.Cells(rFound.Row + i, oInterimReport.FirstNameColumn).Value = oEmployee.FirstName
                        frmla = Replace(temp, dummy, CStr(rFound.Row + i))
                        wsR.Cells(rFound.Row + i, oInterimReport.SumTotalColumn).Value = frmla
                        mdlFunc.DoubleFromString CStr(wsR.Cells(rFound.Row + i, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value), sum1
                        mdlFunc.DoubleFromString oPayOut.Amount, sum2
                        wsR.Cells(rFound.Row + i, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value = Round(Abs(sum1) + Abs(sum2), 2)
                    End If
                End If
            Else
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 1).Value = oEmployee.PersonalCode
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 2).Value = oEmployee.LastName
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 3).Value = oEmployee.FirstName
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 4).Value = oPayOut.FoundDocLineText
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 5).Value = oPayOut.TransDate
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 6).Value = oPayOut.NameFieldText
                If oPayOut.NameMaskUsed Then arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 7).Value = oPayOut.NameFullMask
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 8).Value = oPayOut.DebitCredit
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 9).Value = oPayOut.Amount
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 10).Value = oPayOut.Description
                arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 11).Value = catMsg
                arrWsRows(UBound(arrWsRows)) = arrWsRows(UBound(arrWsRows)) + 1
            End If
            
GenerateReportPayOutReportData:
            'report found data to payout list
            Set wsP = oPayOut.GetCurrentWS
            wsP.Cells(oPayOut.ListDataRow, oPayOut.ListLastColumn + 1).Value = oPayOut.NameFull & " " & catMsg

            'report found data to search report
            wsC.Cells(iC, 1).Value = oEmployee.PersonalCode
            wsC.Cells(iC, 2).Value = oEmployee.LastName
            wsC.Cells(iC, 3).Value = oEmployee.FirstName
            wsC.Cells(iC, 4).Value = oPayOut.FoundDocLineText
            wsC.Cells(iC, 5).Value = oPayOut.TransDate
            wsC.Cells(iC, 6).Value = oPayOut.NameFieldText
            If oPayOut.NameMaskUsed Then wsC.Cells(iC, 7).Value = oPayOut.NameFullMask
            wsC.Cells(iC, 8).Value = oPayOut.DebitCredit
            wsC.Cells(iC, 9).Value = oPayOut.Amount
            wsC.Cells(iC, 10).Value = oPayOut.Description
            wsC.Cells(iC, 11).Value = catMsg
            iC = iC + 1
GenerateReportPayOutNextSearch:
        Loop
        
        'report found data to employee list
        wsE.Cells(oEmployee.ListDataRow, oEmployee.ListLastColumn + 1 + oPayOut.ListSheetIndex - LBound(arrNames)).Value = descMsg
GenerateReportNextEmployee:
        iR = iR + 1
    Loop
    
    'complete search report
    If Not oPayOut.ResetState Then GoTo GenerateReportSaveExit
    Set rSearch = wsC.Range(wsC.Cells(1, 4), wsC.Cells(iC - 1, 4))
    If rSearch Is Nothing Then GoTo GenerateReportSaveExit
    
    oCategory.SetEmpBasedFilter = False
    Do While oPayOut.NextEntry
        Set rFound = rSearch.Find(What:=oPayOut.FoundDocLineText, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
        If rFound Is Nothing Then
            If Len(oPayOut.NameFieldText) = 0 Then
                catMsg = vbNullString
                GoTo GenerateReportFinishSkipCat
            End If
            
            If oCategory.DefineCategory(oPayOut.ListConfigIndex, oPayOut.Description, oPayOut.NameFieldText, oPayOut.IsDebit, oPayOut.IsCredit, catMsg) Then
                
                If oCategory.ParamReport Then
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 4).Value = oPayOut.FoundDocLineText
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 5).Value = oPayOut.TransDate
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 6).Value = oPayOut.NameFieldText
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 8).Value = oPayOut.DebitCredit
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 9).Value = oPayOut.Amount
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 10).Value = oPayOut.Description
                    arrWs(oCategory.CatIndex).Cells(arrWsRows(oCategory.CatIndex), 11).Value = catMsg
                    arrWsRows(oCategory.CatIndex) = arrWsRows(oCategory.CatIndex) + 1
                End If
                If oCategory.ParamTable Then
                    Set rFound = wsR.Cells(1, 1).EntireColumn.Find(What:=oCategory.CatName, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=True)
                    If Not rFound Is Nothing Then
                        i = 2
                        Do While Len(CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value)) > 0
                            If CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value) = oPayOut.NameFieldText Then Exit Do
                            i = i + 1
                        Loop
                        If Len(CStr(wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value)) = 0 Then wsR.Cells(rFound.Row + i, 1).EntireRow.Insert Shift:=xlDown
                        wsR.Cells(rFound.Row + i, oInterimReport.LastNameColumn).Value = oPayOut.NameFieldText
                        frmla = Replace(temp, dummy, CStr(rFound.Row + i))
                        wsR.Cells(rFound.Row + i, oInterimReport.SumTotalColumn).Value = frmla
                        mdlFunc.DoubleFromString CStr(wsR.Cells(rFound.Row + i, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value), sum1
                        mdlFunc.DoubleFromString oPayOut.Amount, sum2
                        wsR.Cells(rFound.Row + i, oInterimReport.SumStartColumn + oPayOut.ListSheetIndex - LBound(arrNames)).Value = Round(Abs(sum1) + Abs(sum2), 2)
                    End If
                End If
            Else
                If Len(catMsg) > 0 Then
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 4).Value = oPayOut.FoundDocLineText
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 5).Value = oPayOut.TransDate
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 6).Value = oPayOut.NameFieldText
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 8).Value = oPayOut.DebitCredit
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 9).Value = oPayOut.Amount
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 10).Value = oPayOut.Description
                    arrWs(UBound(arrWs)).Cells(arrWsRows(UBound(arrWsRows)), 11).Value = catMsg
                    arrWsRows(UBound(arrWsRows)) = arrWsRows(UBound(arrWsRows)) + 1
                End If
            End If
GenerateReportFinishSkipCat:
            Set wsP = oPayOut.GetCurrentWS
            wsP.Cells(oPayOut.ListDataRow, oPayOut.ListLastColumn + 1).Value = catMsg
            
            wsC.Cells(iC, 4).Value = oPayOut.FoundDocLineText
            wsC.Cells(iC, 5).Value = oPayOut.TransDate
            wsC.Cells(iC, 6).Value = oPayOut.NameFieldText
            wsC.Cells(iC, 8).Value = oPayOut.DebitCredit
            wsC.Cells(iC, 9).Value = oPayOut.Amount
            wsC.Cells(iC, 10).Value = oPayOut.Description
            wsC.Cells(iC, 11).Value = catMsg
            iC = iC + 1
        End If
    Loop
    
GenerateReportSaveExit:
    
    If Len(compName) = 0 Then If Len(oEmployee.EmployerName) = 0 Then compName = mdlCore.GetCurCompany Else compName = Replace(oEmployee.EmployerName, " ", "-")
    
    For i = UBound(arrSheets) To LBound(arrSheets) Step -1
        If oPayOut.IsIndexValid(i) Then
            Set ws = wb.Worksheets.Add(After:=wb.Worksheets(2 + catRepCount))
            ws.Name = Replace(arrNames(i), "*", vbNullString)
            arrSheets(i).Cells(1, oPayOut.ListLastColumn + 1).Value = "Vaste leitud"
            arrSheets(i).Cells.Copy
            ws.Cells.PasteSpecial
        End If
    Next i
    
    Set ws = wb.Worksheets.Add(After:=wb.Worksheets(1))
    ws.Name = "TOR kontroll"
    For i = LBound(arrNames) To UBound(arrNames)
        wsE.Cells(1, oEmployee.ListLastColumn + 1 + i - LBound(arrNames)).Value = arrNames(i)
    Next i
    wsE.Cells.Copy
    ws.Cells.PasteSpecial
    ws.Cells.WrapText = False
    
    wb.Worksheets(1).Activate
    wb.SaveAs fileName:=mdlCore.GetSourceFilePath & fnDefRep & LCase(compName) & "-" & Format(DateTime.Now, "yyyy-MM-dd_HH-mm-ss") & fnExtRep
    wb.Close False
    Set wb = Nothing
    
    MsgBox "Report done!", vbOKOnly, Title:="GenerateReport"
    
End Sub

Public Sub GenerateTsd(ByVal dateString As String, ByVal compName As String, Optional ByVal useHttp As Boolean)
    Dim i As Long, j As Long
    Dim k As Integer
    Dim fRange As Range, sRange As Range
    Dim sum1 As Double, sum2 As Double
    Dim val1 As Boolean, val2 As Boolean
    Dim pRate As Double
    Dim curCatTsd As Boolean, curCatTor As Boolean, curCatCode As String, wId As String
    Dim err As String, fnTsd As String, fn1Exp As String, fn2Exp As String
    Dim dataString As String, tempArr() As String
    
    Dim oReport As New cInterimReport
    Dim oCat As New cCategory
    Dim oEmp As New cEmployee
    Dim oTsd As New cTsdTable
    Dim wb As Workbook
    Dim cfgWs As Worksheet, repWs As Worksheet, torWs As Worksheet, repCopyWs As Worksheet
    
    Dim rowMinMain As Long
    Dim rowMaxMain As Long
    Dim rowTemp As Long
    
    If Not IsDate(dateString) Then
        MsgBox "Invalid date/period!", vbCritical, Title:="Generate TSD"
        Exit Sub
    End If
    
    Set wb = Application.Workbooks.Add
    Set cfgWs = mdlCore.GetConfigWs
    Set repWs = mdlCore.GetInterimReportWs
    Set torWs = mdlCore.GetTorControlWs
    
    If cfgWs Is Nothing Or repWs Is Nothing Or torWs Is Nothing Or wb Is Nothing Then
        MsgBox "Worksheets config failed!", vbCritical, Title:="Generate Tsd"
        Exit Sub
    End If
    
    If Not oTsd.InitData(cfgWs, wb) Then
        MsgBox "Tsd config failed!", vbCritical, Title:="Tsd config"
        Exit Sub
    End If
    
    If Not oEmp.InitData(cfgWs, torWs, useHttp) Then
        MsgBox "Employee config failed!", vbCritical, Title:="Employee config"
        Exit Sub
    End If
    
    If Not oCat.InitData(cfgWs) Then
        MsgBox "Category config failed!", vbCritical, Title:="Category config"
        Exit Sub
    End If
    
    If Not oReport.InitData(cfgWs) Then
        MsgBox "Report config failed!", vbCritical, Title:="Report config"
        Exit Sub
    End If
    
    If Not oReport.CheckHeaders(repWs.Cells(1, 1).EntireRow) Then
        MsgBox "Report headers not found!", vbCritical, Title:="Report config"
        Exit Sub
    End If
    
    'find salary report data (main)
    rowMinMain = 2
    rowMaxMain = 1
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.PersCodeFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.LastNameFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.FirstNameFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    
    For i = rowMaxMain To rowMinMain Step -1
        If oCat.SetCatByRange(repWs.Range(repWs.Cells(i, 1), repWs.Cells(i, repWs.Columns.Count))) Then
            rowMaxMain = i - 1
        End If
    Next i
    
    If rowMaxMain < rowMinMain Then
        MsgBox "Report data not found!", vbExclamation, Title:="Generate Tsd"
        Exit Sub
    End If
    
    If rowMaxMain < repWs.Rows.Count Then
        Set sRange = repWs.Range(repWs.Cells(rowMaxMain + 1, oReport.PersCodeFoundCol), repWs.Cells(repWs.Rows.Count, oReport.PersCodeFoundCol))
        Set fRange = repWs.Range(repWs.Cells(rowMaxMain + 1, oReport.SumTotalFoundCol), repWs.Cells(repWs.Rows.Count, oReport.SumTotalFoundCol))
    End If
    
    'process salary report data
    For i = rowMinMain To rowMaxMain
        If Len(Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value))) > 0 Or Len(Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value))) > 0 Or Len(Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value))) > 0 Then
            oTsd.DatePeriod = Trim(CStr(repWs.Cells(i, oReport.ControlDateFoundCol).Value))
            oTsd.PersonalCode = Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value))
            oTsd.FirstLastName = Trim(Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)) & " " & Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value)))
            oTsd.StartDate = Trim(CStr(repWs.Cells(i, oReport.StartDateFoundCol).Value))
            oTsd.EndDate = Trim(CStr(repWs.Cells(i, oReport.EndDateFoundCol).Value))
            mdlFunc.DoubleFromString Trim(CStr(repWs.Cells(i, oReport.SumTotalFoundCol).Value)), sum1
            oTsd.NetSum = sum1
            oTsd.TaxFreeData = Trim(CStr(repWs.Cells(i, oReport.TaxFreeFoundCol).Value))
            
            If InStr(oTsd.FirstLastName, " ") > 0 And (Len(Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)) = 0 Or Len(Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value))) = 0)) Then
                tempArr = Split(oTsd.FirstLastName, " ")
                repWs.Cells(i, oReport.FirstNameFoundCol).Value = tempArr(LBound(tempArr))
                repWs.Cells(i, oReport.LastNameFoundCol).Value = tempArr(UBound(tempArr))
                For k = LBound(tempArr) To UBound(tempArr)
                    If k <> LBound(tempArr) And k <> UBound(tempArr) Then repWs.Cells(i, oReport.FirstNameFoundCol).Value = CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value) & " " & tempArr(k)
                Next k
            End If
            
            oTsd.TorControlValid = oEmp.FindEmployee(Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value)), Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value)), Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)))
            If oTsd.TorControlValid Then
                oTsd.FirstLastName = oEmp.FirstName & " " & oEmp.LastName
                oTsd.CountryCode = oEmp.CountryCode
                oTsd.EntryTypeId = oEmp.WorkTypeId
                oTsd.EntryStateId = oEmp.EntryStateId
            Else
                If oTsd.GetWorkIdByName(CStr(repWs.Cells(i, oReport.WorkTypeFoundCol).Value), wId) Then
                    oTsd.EntryTypeId = wId
                    oTsd.EntryStateId = oTsd.EntryStateValidId
                    oTsd.TorControlValid = True
                End If
            End If
            
            oTsd.ResidenceCheck = oEmp.ResidenceStringCheck(CStr(repWs.Cells(i, oReport.ResidenceFoundCol).Value), val1)
            oTsd.IsResident = val1
            
            If oTsd.ResidenceCheck Then
                oTsd.PensionCheck = oEmp.PensionStringCheck(CStr(repWs.Cells(i, oReport.PensionFoundCol).Value), val2, pRate)
                oTsd.HasPension = val2
                oTsd.HasPensionRate = pRate
            End If
            
            If useHttp And Len(oTsd.PersonalCode) > 0 And Not (oTsd.ResidenceCheck And oTsd.PensionCheck) Then
                oEmp.ResetRequestState
                oTsd.ResidenceCheck = oEmp.IsResident(dateString, val1, err, oTsd.PersonalCode)
                oTsd.IsResident = val1
                repWs.Cells(i, oReport.ResidenceFoundCol).Value = err
                If oTsd.ResidenceCheck Then
                    oTsd.PensionCheck = oEmp.HasPension(dateString, val2, err, pRate, oTsd.PersonalCode)
                    oTsd.HasPension = val2
                    oTsd.HasPensionRate = pRate
                    repWs.Cells(i, oReport.PensionFoundCol).Value = err
                End If
            End If
            
            oTsd.ExtraIncomeFlatSum = collectIncomeData(sRange, fRange, oTsd.PersonalCode, oCat, repWs)
            
            If oTsd.ExtraIncomeFlatSum <> 0 Then
                With repWs.Cells(i, oReport.SumTotalFoundCol).AddComment
                    .Visible = False
                    .Text "lisatulu: " & CStr(oTsd.ExtraIncomeFlatSum)
                End With
            End If
            
            oTsd.CreateRecord
            
        End If
    Next i
    
    'find category report data
    rowMinMain = rowMaxMain + 1
    
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.PersCodeFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.LastNameFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    rowTemp = repWs.Cells(repWs.Rows.Count, oReport.FirstNameFoundCol).End(xlUp).Row
    If rowMaxMain < rowTemp Then rowMaxMain = rowTemp
    
    If rowMaxMain < rowMinMain Then GoTo GenerateTsdSaveExit
    
    'process category report data
    For i = rowMinMain To rowMaxMain
    
        If oCat.SetCatByRange(repWs.Range(repWs.Cells(i, 1), repWs.Cells(i, repWs.Columns.Count))) Then
            curCatTor = oCat.ParamEmpBased
            curCatCode = oCat.TSDCode
            If Len(curCatCode) > 0 Then curCatTsd = True Else curCatTsd = False
            GoTo GenerateTsdNextCatRow
        End If
        
        If Not curCatTsd Then GoTo GenerateTsdNextCatRow
        
        If oReport.FindHeader(repWs.Range(repWs.Cells(i, 1), repWs.Cells(i, repWs.Columns.Count)), oCat.HeaderNameTotal, j) Then GoTo GenerateTsdNextCatRow
        
        If Len(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value)) > 0 Or Len(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)) > 0 Then
            oTsd.DatePeriod = dateString
            oTsd.CountryCode = "EE"
            oTsd.EntryTypeId = curCatCode
            oTsd.EntryStateId = oTsd.EntryStateValidId
            oTsd.PersonalCode = Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value))
            oTsd.FirstLastName = Trim(Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)) & " " & Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value)))
            oTsd.StartDate = dateString
            oTsd.EndDate = dateString
            mdlFunc.DoubleFromString CStr(repWs.Cells(i, oReport.SumTotalFoundCol).Value), sum1
            oTsd.NetSum = sum1
            oTsd.TaxFreeData = CStr(repWs.Cells(i, oReport.TaxFreeFoundCol).Value)
            
            If InStr(oTsd.FirstLastName, " ") > 0 And (Len(Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)) = 0 Or Len(Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value))) = 0)) Then
                tempArr = Split(oTsd.FirstLastName, " ")
                repWs.Cells(i, oReport.FirstNameFoundCol).Value = tempArr(LBound(tempArr))
                repWs.Cells(i, oReport.LastNameFoundCol).Value = tempArr(UBound(tempArr))
                For k = LBound(tempArr) To UBound(tempArr)
                    If k <> LBound(tempArr) And k <> UBound(tempArr) Then repWs.Cells(i, oReport.FirstNameFoundCol).Value = CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value) & " " & tempArr(k)
                Next k
            End If
            
            If oCat.ParamEmpBased Then
                oTsd.TorControlValid = oEmp.FindEmployee(Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value)), Trim(CStr(repWs.Cells(i, oReport.LastNameFoundCol).Value)), Trim(CStr(repWs.Cells(i, oReport.FirstNameFoundCol).Value)))
                If oTsd.TorControlValid Then
                    oTsd.PersonalCode = oEmp.PersonalCode
                    oTsd.FirstLastName = oEmp.FirstName & " " & oEmp.LastName
                    oTsd.CountryCode = oEmp.CountryCode
                Else
                    If oTsd.GetWorkIdByName(CStr(repWs.Cells(i, oReport.WorkTypeFoundCol).Value), wId) And Len(Trim(CStr(repWs.Cells(i, oReport.PersCodeFoundCol).Value))) > 0 Then
                        oTsd.EntryTypeId = wId
                        oTsd.EntryStateId = oTsd.EntryStateValidId
                        oTsd.TorControlValid = True
                    End If
                End If
            Else
                oTsd.TorControlValid = True
            End If
            
            oTsd.ResidenceCheck = oEmp.ResidenceStringCheck(CStr(repWs.Cells(i, oReport.ResidenceFoundCol).Value), val1)
            oTsd.IsResident = val1
            
            If oTsd.ResidenceCheck Then
                oTsd.PensionCheck = oEmp.PensionStringCheck(CStr(repWs.Cells(i, oReport.PensionFoundCol).Value), val2, pRate)
                oTsd.HasPension = val2
                oTsd.HasPensionRate = pRate
            End If
                
            If useHttp And Len(oTsd.PersonalCode) > 0 And Not (oTsd.ResidenceCheck And oTsd.PensionCheck) Then
                oEmp.ResetRequestState
                oTsd.ResidenceCheck = oEmp.IsResident(dateString, val1, err, oTsd.PersonalCode)
                oTsd.IsResident = val1
                repWs.Cells(i, oReport.ResidenceFoundCol).Value = err
                If oTsd.ResidenceCheck Then
                    oTsd.PensionCheck = oEmp.HasPension(dateString, val2, err, pRate, oTsd.PersonalCode)
                    oTsd.HasPension = val2
                    oTsd.HasPensionRate = pRate
                    repWs.Cells(i, oReport.PensionFoundCol).Value = err
                End If
            End If
            
            oTsd.CreateRecord
            
        End If
GenerateTsdNextCatRow:
    Next i
    
GenerateTsdSaveExit:
    fnTsd = fnDefTsd
    fn1Exp = fnDefExp
    fn2Exp = fnDefExp
    If Not oTsd.ErrorsCheck(1) Then fn1Exp = fnErrExp
    If Not oTsd.ErrorsCheck(2) Then fn2Exp = fnErrExp
    If fn1Exp = fnErrExp Or fn2Exp = fnErrExp Then fnTsd = fnErrTsd
    If Len(compName) = 0 Then If Len(oEmp.EmployerName) = 0 Then compName = mdlCore.GetCurCompany Else compName = Replace(oEmp.EmployerName, " ", "-")
    
    Set repCopyWs = wb.Worksheets.Add(Before:=wb.Worksheets(1))
    repWs.Cells.Copy
    repCopyWs.Cells.PasteSpecial
    repCopyWs.Name = repWs.Name
    wb.Worksheets(1).Activate
    wb.SaveAs fileName:=mdlCore.GetReportFilePath & fnTsd & LCase(compName) & "-" & Format(DateTime.Now, "yyyy-MM-dd_HH-mm-ss") & ".xlsx"
    oTsd.GetLisa1String dataString
    If Len(dataString) > 0 Then mdlFunc.SaveToFile mdlCore.GetReportFilePath, fn1Exp & LCase(compName) & "-lisa1.csv", dataString
    oTsd.GetLisa2String dataString
    If Len(dataString) > 0 Then mdlFunc.SaveToFile mdlCore.GetReportFilePath, fn2Exp & LCase(compName) & "-lisa2.csv", dataString
    wb.Close False
    Set wb = Nothing
    
    MsgBox "TSD done!" & vbNewLine & "Lines processed: " & rowMaxMain - 1, vbOKOnly, Title:="GenerateTsd"
    
End Sub

Private Function collectIncomeData(ByRef sRange As Range, ByRef fRange As Range, ByVal persCode As String, ByRef dCat As cCategory, ByRef rWs As Worksheet) As Double
    Dim fRow As Long, curRange As Range, sum1 As Double, j As Long, incTrue As Boolean
    
    If sRange Is Nothing Or fRange Is Nothing Then Exit Function
    
    Set curRange = sRange.Find(What:=persCode, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
    If curRange Is Nothing Then Exit Function Else fRow = curRange.Row
    
    Do
        incTrue = False
        For j = curRange.Row To 1 Step -1
            If dCat.SetCatByRange(rWs.Range(rWs.Cells(j, 1), rWs.Cells(j, 10))) Then
                If Len(dCat.TSDCode) > 0 Then
                    incTrue = True
                End If
                Exit For
            End If
        Next j
        
        If incTrue Then
            mdlFunc.DoubleFromString CStr(fRange.Cells(curRange.Row - fRange.Cells(1, 1).Row + 1, 1).Value), sum1
            collectIncomeData = collectIncomeData + sum1
        End If
        
        Set curRange = sRange.Find(What:=persCode, After:=curRange, LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If curRange.Row = fRow Then Exit Do
    Loop
    
End Function

Public Sub TestData()
    
TestDataSaveExit:
    
    MsgBox "Test done!", vbOKOnly, Title:="TestData"
    
End Sub
