Attribute VB_Name = "formMain"
Attribute VB_Base = "0{02D3BCC5-06FF-417F-9069-5756BD2EFDF3}{F7F41A02-994E-4C20-A6DE-22F9C962E904}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

Private Sub btnClose_Click()
    mdlCore.ClearWs
    mdlCore.ClearReportWb
    Unload Me
End Sub

Private Sub btnHide_Click()
    Me.Hide
End Sub

Private Sub btnOpen_Click()
    Dim docList() As String
    
    If mdlCore.SetSourceMult Then
        If mdlCore.GetDocumentList(docList) Then
            formMain.lstFileList.List = docList
        Else
            formMain.lstFileList.Clear
        End If
    Else
        If mdlCore.GetReportSheetList(docList) Then formMain.lstFileList.List = docList Else formMain.lstFileList.Clear
    End If
    
    Me.lblCompanyName.Caption = mdlCore.GetDefaultName
End Sub

Private Sub btnOpenReport_Click()
    Dim docList() As String
    
    If mdlCore.SetSourceReport Then
        If mdlCore.GetReportSheetList(docList) Then
            formMain.lstFileList.List = docList
        Else
            formMain.lstFileList.Clear
        End If
    Else
        If mdlCore.GetDocumentList(docList) Then formMain.lstFileList.List = docList Else formMain.lstFileList.Clear
    End If
    
    Me.lblCompanyName.Caption = mdlCore.GetDefaultName
End Sub

Private Sub btnRemoveFile_Click()
'    With formMain.lstFileList
'        If .ListIndex >= 0 And .ListCount > 0 Then
'            Dim temp As String
'            Dim result As String
'            Dim i As Integer
'            temp = .List(.ListIndex, 1)
'            For i = 0 To .ListCount - 1
'                If .List(i, 1) = temp Then
'                    result = result & vbNewLine & .List(i, 0)
'                End If
'            Next i
'            If Len(result) = 0 Then Exit Sub
'            If MsgBox("This will remove items:" & result, vbOKCancel, "Remove file") = vbOK Then
'                Dim docList() As String
'                If mdlCore.RemoveFile(.List(.ListIndex, 1)) Then
'                    If mdlCore.GetDocumentList(docList) Then .List = docList Else .Clear
'                End If
'            End If
'        End If
'    End With
End Sub

Private Sub btnRemoveItem_Click()
'    Dim docList() As String
'    With formMain.lstFileList
'        If .ListIndex >= 0 And .ListCount > 0 Then
'            If mdlCore.RemoveItem(.List(.ListIndex, 0)) Then
'                If mdlCore.GetDocumentList(docList) Then .List = docList Else .Clear
'            End If
'        End If
'    End With
End Sub

Private Sub btnStart_Click()
    Dim dateString As String
    
    With formMain
        If .chbManFilter.Value Then
            dateString = .txtManFilterYear & "-" & .cbxManFilterMonth.Text & "-01"
        Else
            dateString = .cbxFilterPeriod.Text & "-01"
        End If
        If Not IsDate(dateString) Then
            MsgBox "Invalid date!", vbExclamation, Title:="Period"
        End If
        dateString = Format(DateAdd("d", -1, DateAdd("m", 1, CDate(dateString))), "dd.MM.yyyy")
        If mdlCore.IsStateReport Then
            If mdlCore.DocumentsAreSet Then mdlTables.GenerateReport dateString, .txtCompanyName.Text, .chbUseHttp.Value Else MsgBox "Documents are not loaded!", vbExclamation, Title:="Start"
        End If
        If mdlCore.IsStateTsd Then
            If mdlCore.InterimReportSet Then mdlTables.GenerateTsd dateString, .txtCompanyName.Text, .chbUseHttp.Value Else MsgBox "Documents are not loaded!", vbExclamation, Title:="Start"
        End If
    End With
    
End Sub

Private Sub UserForm_Initialize()
    Dim curDate As Date
    Dim i As Integer
    Dim arrPeriods() As String
    
    With formMain
        .lstFileList.Clear
        mdlCore.ClearWs
        mdlCore.ClearReportWb
        If Not mdlCore.InitData Then MsgBox "Initial config failed!", vbCritical, Title:="Initial config"
        
        curDate = DateTime.Now
        
        .cbxFilterPeriod.Clear
        .cbxManFilterMonth.Clear
        .txtManFilterYear.Text = Year(curDate)
        
        ReDim arrPeriods(0 To 3)
        
        For i = 0 To 3
            arrPeriods(i) = Format(curDate, "yyyy-MM")
            curDate = DateAdd("m", -1, curDate)
        Next i
        
        .cbxFilterPeriod.List = arrPeriods
        .cbxFilterPeriod.ListIndex = 1
        
        ReDim arrPeriods(0 To 11)
        
        For i = 0 To 11
            arrPeriods(i) = Format(i + 1, "00")
        Next i
        
        .cbxManFilterMonth.List = arrPeriods
        .cbxManFilterMonth.ListIndex = 0
        
    End With
End Sub

Private Sub UserForm_Terminate()
    formMain.lstFileList.Clear
    mdlCore.ClearWs
    mdlCore.ClearReportWb
End Sub
