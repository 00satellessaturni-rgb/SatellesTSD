Attribute VB_Name = "cCategory"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit
Option Compare Text

Private Const catDefKey As String = "{category_config}"
Private Const fltDefKey As String = "{category_filters}"
Private Const repDefKey As String = "{category_report}"

Private Const catHeaderTotal As String = "Kokku"

Private Const defDebit As String = "D"
Private Const defCredit As String = "C"
Private Const defParamYes As String = "Y"
Private Const defParamNo As String = "N"

Private Const cfgMaxIndx As Long = 7

Private Const cfgIndxDebCre As Long = 0
Private Const cfgIndxEmp As Long = 1
Private Const cfgIndxSal As Long = 2
Private Const cfgIndxRepSht As Long = 3
Private Const cfgIndxTbl As Long = 4
Private Const cfgIndxRec As Long = 5
Private Const cfgIndxGroup As Long = 6
Private Const cfgIndxCode As Long = 7

Private Const cfgColMin As Long = 2

Private rowCategory As Long
Private rowConfig As Long
Private rowFilter As Long
Private rowReport As Long

Private wsCfg As Worksheet
Private initTrue As Boolean
Private curCatDefined As Boolean
Private curCatIndx As Integer
Private curEmpBasedFilter As String

Private arrCatColumns() As Long

Public Function InitData(ByRef cfgWs As Worksheet) As Boolean
    Dim posRange As Range, i As Long
    
    If cfgWs Is Nothing Then Exit Function
    
    Set wsCfg = cfgWs
    
    curEmpBasedFilter = vbNullString
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=catDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then
        Exit Function
    Else
        rowCategory = posRange.Row + 1
        rowConfig = posRange.Row + 2
    End If
    
    Set posRange = wsCfg.Range("A1").EntireColumn.Find(What:=fltDefKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If posRange Is Nothing Then Exit Function Else rowFilter = posRange.Row + 2
    
    ReDim arrCatColumns(0 To 0)
    
    For i = cfgColMin To wsCfg.Cells(rowFilter - 1, wsCfg.Columns.Count).End(xlToLeft).Column
        If Len(CStr(wsCfg.Cells(rowFilter - 1, i).Value)) > 0 Then
            If arrCatColumns(UBound(arrCatColumns)) > 0 Then ReDim Preserve arrCatColumns(UBound(arrCatColumns) + 1)
            arrCatColumns(UBound(arrCatColumns)) = i
        End If
    Next i
    
    If arrCatColumns(LBound(arrCatColumns)) = 0 Then Exit Function
    
    initTrue = True
    InitData = True
End Function

Property Get IsDefined() As Boolean
    IsDefined = curCatDefined
End Property

Property Get CatName() As String
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then CatName = CStr(wsCfg.Cells(rowCategory, arrCatColumns(curCatIndx)).Value)
End Property

Property Get CatIndex() As Integer
    CatIndex = curCatIndx
End Property

Property Get ParamDebit() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxDebCre, arrCatColumns(curCatIndx)).Value) = defDebit Then ParamDebit = True
    End If
End Property

Property Get ParamCredit() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxDebCre, arrCatColumns(curCatIndx)).Value) = defCredit Then ParamCredit = True
    End If
End Property

Property Get ParamEmpBased() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxEmp, arrCatColumns(curCatIndx)).Value) = defParamYes Then ParamEmpBased = True
    End If
End Property

Property Get ParamSalary() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxSal, arrCatColumns(curCatIndx)).Value) = defParamYes Then ParamSalary = True
    End If
End Property

Property Get ParamReport() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxRepSht, arrCatColumns(curCatIndx)).Value) = defParamYes Then ParamReport = True
    End If
End Property

Property Get ParamTable() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxTbl, arrCatColumns(curCatIndx)).Value) = defParamYes Then ParamTable = True
    End If
End Property

Property Get ParamRecCheck() As Boolean
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        If CStr(wsCfg.Cells(rowConfig + cfgIndxRec, arrCatColumns(curCatIndx)).Value) = defParamYes Then ParamRecCheck = True
    End If
End Property

Property Get TSDCode() As String
    If IsArray(arrCatColumns) And curCatIndx >= LBound(arrCatColumns) And curCatIndx <= UBound(arrCatColumns) Then
        TSDCode = CStr(wsCfg.Cells(rowConfig + cfgIndxCode, arrCatColumns(curCatIndx)).Value)
    End If
End Property

Property Get MinIndex() As Integer
    If IsArray(arrCatColumns) Then MinIndex = LBound(arrCatColumns)
End Property

Property Get MaxIndex() As Integer
    If IsArray(arrCatColumns) Then MaxIndex = UBound(arrCatColumns)
End Property

Property Get HeaderNameTotal() As String
    HeaderNameTotal = catHeaderTotal
End Property

Property Let SetEmpBasedFilter(ByVal data As Boolean)
    If data Then curEmpBasedFilter = defParamYes Else curEmpBasedFilter = defParamNo
End Property

Public Sub ClearEmpBasedFilter()
    curEmpBasedFilter = vbNullString
End Sub

Public Function SetCatIndex(ByVal indx As Integer) As Boolean
    If IsArray(arrCatColumns) And indx >= LBound(arrCatColumns) And indx <= UBound(arrCatColumns) Then
        curCatIndx = indx
        curCatDefined = False
        SetCatIndex = True
    End If
End Function

Public Function SetCatByRange(ByRef sRange As Range) As Boolean
    Dim fRange As Range, i As Integer
    curCatDefined = False
    If sRange Is Nothing Or Not IsArray(arrCatColumns) Then Exit Function
    For i = LBound(arrCatColumns) To UBound(arrCatColumns)
        Set fRange = sRange.Find(What:=CStr(wsCfg.Cells(rowCategory, arrCatColumns(i)).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
        If Not fRange Is Nothing Then
            curCatIndx = i
            SetCatByRange = True
            Exit Function
        End If
    Next i
End Function

Public Function DefineCategory(ByVal docCfgIndx As Long, ByVal data As String, ByVal recData As String, ByVal parDeb As Boolean, ByVal parCre As Boolean, ByRef err As String) As Boolean
    Dim i As Integer, temp As Variant, grp As String, tmpDefStt As Boolean
    curCatDefined = False
    err = vbNullString
    If Not IsArray(arrCatColumns) Then GoTo DefineCategoryNotAvailable
    If arrCatColumns(LBound(arrCatColumns)) = 0 Then GoTo DefineCategoryNotAvailable
    For i = LBound(arrCatColumns) To UBound(arrCatColumns)
        tmpDefStt = False
        If Len(curEmpBasedFilter) > 0 Then
            If Len(CStr(wsCfg.Cells(rowConfig + cfgIndxEmp, arrCatColumns(i)).Value)) > 0 And CStr(wsCfg.Cells(rowConfig + cfgIndxEmp, arrCatColumns(i)).Value) <> curEmpBasedFilter Then GoTo DefineCategoryNextColumn
        End If
        If curCatDefined And Len(grp) > 0 And CStr(wsCfg.Cells(rowConfig + cfgIndxGroup, arrCatColumns(i)).Value) = grp Then GoTo DefineCategoryNextColumn
        If Len(CStr(wsCfg.Cells(rowFilter + docCfgIndx, arrCatColumns(i)).Value)) > 0 Then
            For Each temp In Split(Split(CStr(wsCfg.Cells(rowFilter + docCfgIndx, arrCatColumns(i)).Value), ";")(0), ",")
                If data Like CStr(temp) Then
                    tmpDefStt = True
                    Exit For
                End If
            Next
            If CStr(wsCfg.Cells(rowConfig + cfgIndxRec, arrCatColumns(i)).Value) = defParamYes Then
                For Each temp In Split(Split(CStr(wsCfg.Cells(rowFilter + docCfgIndx, arrCatColumns(i)).Value), ";")(0), ",")
                    If recData Like CStr(temp) Then
                        tmpDefStt = True
                        Exit For
                    End If
                Next
            End If
            If InStr(CStr(wsCfg.Cells(rowFilter + docCfgIndx, arrCatColumns(i)).Value), ";") > 0 Then
                For Each temp In Split(Split(CStr(wsCfg.Cells(rowFilter + docCfgIndx, arrCatColumns(i)).Value), ";")(1), ",")
                    If data Like CStr(temp) Then
                        tmpDefStt = False
                        Exit For
                    End If
                Next
            End If
            If tmpDefStt Then
                If curCatDefined Then
                    If Len(err) = 0 Then err = "viga: kategooria tuvastamine" & " [" & CStr(wsCfg.Cells(rowCategory, arrCatColumns(curCatIndx)).Value) & "]"
                    err = err & " [" & CStr(wsCfg.Cells(rowCategory, arrCatColumns(i)).Value) & "]"
                End If
                curCatDefined = True
                grp = CStr(wsCfg.Cells(rowConfig + cfgIndxGroup, arrCatColumns(i)).Value)
                curCatIndx = i
            End If
        End If
DefineCategoryNextColumn:
    Next i
    If Len(err) = 0 And curCatDefined Then
        If parDeb And CStr(wsCfg.Cells(rowConfig + cfgIndxDebCre, arrCatColumns(curCatIndx)).Value) = defDebit Or parCre And CStr(wsCfg.Cells(rowConfig + cfgIndxDebCre, arrCatColumns(curCatIndx)).Value) = defCredit Then
            err = "[" & CStr(wsCfg.Cells(rowCategory, arrCatColumns(curCatIndx)).Value) & "]"
            DefineCategory = True
        Else
            err = "viga: [" & CStr(wsCfg.Cells(rowCategory, arrCatColumns(curCatIndx)).Value) & "] D/C kontroll"
        End If
    Else
        curCatDefined = False
    End If
    Exit Function
DefineCategoryNotAvailable:
    curCatDefined = False
    err = "viga: pole uhtegi maaratletud kategooriat"
End Function
