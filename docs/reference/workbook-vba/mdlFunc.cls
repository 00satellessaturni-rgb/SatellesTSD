Attribute VB_Name = "mdlFunc"
Option Explicit
Option Compare Text

Public Function Palk2023NetToGross(ByVal netSumMonth As Double, ByVal incTaxRate As Double, ByVal pensRate As Double, ByVal ueInsRate As Double, ByVal tfmYear As Double, ByVal tfk As Double, ByVal tier1Year As Double, ByVal tier2Year As Double) As Double
    Dim netSumYear As Double, grossSumMonth As Double, grossSumYear As Double, temp1Year As Double, tempNet As Double, tfmMonth As Double
    tfmMonth = tfmYear / 12
    netSumYear = netSumMonth * 12
    
    grossSumYear = tier1Year
    tempNet = grossSumYear * (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1)) + incTaxRate * tfmYear
    If netSumYear <= tempNet Then
        grossSumMonth = Round((netSumMonth - incTaxRate * tfmMonth) / (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1)), 2)
        If grossSumMonth <= tfmMonth Then incTaxRate = 0
        Palk2023NetToGross = Round((netSumMonth - incTaxRate * tfmMonth) / (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1)), 2)
        Exit Function
    End If
    
    grossSumYear = tier2Year
    tempNet = grossSumYear * (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1))
    If netSumYear >= tempNet Then
        Palk2023NetToGross = Round((netSumMonth) / (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1)), 2)
        Exit Function
    End If
    
    Palk2023NetToGross = Round((netSumYear - incTaxRate * tfmYear * (1 + tier1Year / tfk)) / (1 - pensRate - ueInsRate + incTaxRate * (pensRate + ueInsRate - 1 - tfmYear / tfk)) / 12, 2)
    
End Function

Public Function DoubleToCorrectString(ByVal val As Double, ByVal decimals As Integer) As String
    Dim temp As String
    Dim i As Integer
    
    temp = CStr(val)
    
    If decimals >= 0 Then
        If decimals = 0 Then temp = "0" Else temp = "0."
        For i = 1 To decimals
            temp = temp & "0"
        Next i
        temp = Format(val, temp)
    End If
    
    DoubleToCorrectString = temp
End Function

Public Function DoubleFromString(ByVal data As String, ByRef val As Double) As Boolean
    val = 0
    If Len(data) = 0 Then Exit Function
    If Not IsNumeric(data) Then
        If InStr(data, ".") > 0 Then
            data = Replace(data, ".", ",")
        ElseIf InStr(data, ",") > 0 Then
            data = Replace(data, ",", ".")
        End If
    End If
    If IsNumeric(data) Then
        val = CDbl(data)
        DoubleFromString = True
    End If
End Function

Public Function SearchExact(ByVal csvString As String, ByVal sVal As String) As Boolean
    If Len(csvString) = 0 Or Len(sVal) = 0 Then Exit Function
    csvString = "," & csvString & ","
    sVal = "," & Replace(sVal, ",", vbNullString) & ","
    If InStr(csvString, sVal) > 0 Then SearchExact = True
End Function

Public Function AddText(ByVal string1 As String, ByVal string2 As String) As String
    If Len(string1) = 0 And Len(string2) = 0 Then Exit Function
    If Len(string1) > 0 And Len(string2) > 0 Then AddText = string1 & "; " & string2 Else AddText = string1 & string2
End Function

Public Function NextMaskSD(ByVal orig As String, ByRef rslt As String) As Boolean
    Dim pos As Integer
    If Len(orig) < 2 Or Len(orig) <> Len(rslt) Then Exit Function
    pos = InStr(rslt, "*")
    If pos = 0 Then
        rslt = "*" & Right(orig, Len(orig) - 1)
        NextMaskSD = True
    ElseIf pos = Len(orig) Then
        rslt = orig
        Exit Function
    Else
        rslt = Left(orig, pos) & "*"
        If Len(orig) - pos - 1 > 0 Then rslt = rslt & Right(orig, Len(orig) - pos - 1)
        NextMaskSD = True
    End If
End Function

Public Function ColumnLabel(ByVal indx As Long) As String
    Dim i As Long
    
    If indx <= 0 Then ColumnLabel = "A"
    
    Do While indx > 0
        i = indx Mod (Asc("Z") - Asc("A") + 1)
        If i = 0 Then i = (Asc("Z") - Asc("A") + 1)
        ColumnLabel = Chr(Asc("A") - 1 + i) & ColumnLabel
        indx = (indx - i) / (Asc("Z") - Asc("A") + 1)
    Loop
End Function

Public Function ColumnIndex(ByVal labl As String, ByVal maxIndx As Long) As Long
    Dim i As Long
    
    If Len(labl) = 0 Then ColumnIndex = 1
    
    i = 1
    labl = UCase(labl)
    
    Do While Len(labl) > 0
        If Asc(Right(labl, 1)) < Asc("A") Or Asc(Right(labl, 1)) > Asc("Z") Then Exit Function
        ColumnIndex = ColumnIndex + i * (Asc(Right(labl, 1)) - Asc("A") + 1)
        i = i * (Asc("Z") - Asc("A") + 1)
        If Len(labl) = 1 Then Exit Do Else labl = Left(labl, Len(labl) - 1)
    Loop
    
    If ColumnIndex > maxIndx Then ColumnIndex = maxIndx
    If ColumnIndex <= 0 Then ColumnIndex = 1
End Function

Public Function IsPersonCode(ByVal data As String) As Boolean
    Dim temp As Integer
    'IsPersonCode = True
    'Exit Function
    If Len(data) = 0 Then Exit Function
    If data Like "[1-6][0-9][0-9][0-1][0-9][0-3][0-9][0-9][0-9][0-9][0-9]" Then
        If Left(data, 1) Like "[1-2]" Then temp = "18"
        If Left(data, 1) Like "[3-4]" Then temp = "19"
        If Left(data, 1) Like "[5-6]" Then temp = "20"
        If IsDate(temp & Left(Right(data, 10), 2) & "-" & Left(Right(data, 8), 2) & "-" & Left(Right(data, 6), 2)) Then
            IsPersonCode = True
        End If
    End If
End Function

Public Function RemoveSymbols(ByVal data As String, ByVal smbl As String) As String
    Dim i As Integer
    
    RemoveSymbols = data
    If Len(data) = 0 Or Len(smbl) = 0 Then Exit Function
    
    For i = 1 To Len(smbl)
        data = Replace(data, Mid(smbl, i, 1), vbNullString)
    Next i
    
    RemoveSymbols = data
End Function

Public Sub CorrectUtf8SymbolCyrBalt(ByRef srcRange As Range)
    Dim i As Integer
    Dim arrCyrillic(0 To 12) As String
    Dim arrBaltic(0 To 12) As String
    
    If srcRange Is Nothing Then Exit Sub
    
    arrCyrillic(0) = ChrW(1044)
    arrCyrillic(1) = ChrW(1056)
    arrCyrillic(2) = ChrW(1061)
    arrCyrillic(3) = ChrW(1062)
    arrCyrillic(4) = ChrW(1068)
    arrCyrillic(5) = ChrW(1070)
    arrCyrillic(6) = ChrW(1076)
    arrCyrillic(7) = ChrW(1088)
    arrCyrillic(8) = ChrW(1093)
    arrCyrillic(9) = ChrW(1094)
    arrCyrillic(10) = ChrW(1100)
    arrCyrillic(11) = ChrW(1102)
    arrCyrillic(12) = ChrW(169)
    
    arrBaltic(0) = ChrW(196)
    arrBaltic(1) = ChrW(352)
    arrBaltic(2) = ChrW(213)
    arrBaltic(3) = ChrW(214)
    arrBaltic(4) = ChrW(220)
    arrBaltic(5) = ChrW(381)
    arrBaltic(6) = ChrW(228)
    arrBaltic(7) = ChrW(353)
    arrBaltic(8) = ChrW(245)
    arrBaltic(9) = ChrW(246)
    arrBaltic(10) = ChrW(252)
    arrBaltic(11) = ChrW(382)
    arrBaltic(12) = ChrW(352)
    
    For i = LBound(arrCyrillic) To UBound(arrCyrillic)
        srcRange.Replace What:=arrCyrillic(i), Replacement:=arrBaltic(i), LookAt:=xlPart, MatchCase:=True
    Next i
    
End Sub

Public Function ConvertUtf8Umlauts(ByVal data As String) As String
    Dim i As Integer
    Dim arrUmlauts(0 To 11) As String
    Dim arrReplace(0 To 11) As String
    
    If Len(data) = 0 Then Exit Function
    
    arrUmlauts(0) = ChrW(1044)
    arrUmlauts(1) = ChrW(1056)
    arrUmlauts(2) = ChrW(1061)
    arrUmlauts(3) = ChrW(1062)
    arrUmlauts(4) = ChrW(1068)
    arrUmlauts(5) = ChrW(1070)
    arrUmlauts(6) = ChrW(1076)
    arrUmlauts(7) = ChrW(1088)
    arrUmlauts(8) = ChrW(1093)
    arrUmlauts(9) = ChrW(1094)
    arrUmlauts(10) = ChrW(1100)
    arrUmlauts(11) = ChrW(1102)
    
    arrReplace(0) = ChrW(196)
    arrReplace(1) = ChrW(352)
    arrReplace(2) = ChrW(213)
    arrReplace(3) = ChrW(214)
    arrReplace(4) = ChrW(220)
    arrReplace(5) = ChrW(381)
    arrReplace(6) = ChrW(228)
    arrReplace(7) = ChrW(353)
    arrReplace(8) = ChrW(245)
    arrReplace(9) = ChrW(246)
    arrReplace(10) = ChrW(252)
    arrReplace(11) = ChrW(382)
    
    For i = LBound(arrUmlauts) To UBound(arrUmlauts)
        data = Replace(data, arrUmlauts(i), arrReplace(i))
    Next i
    
    ConvertUtf8Umlauts = data
    
End Function

Public Function SaveToFile(ByVal filePath, ByVal fileName As String, ByRef data As Variant) As Boolean
    Dim fso As FileSystemObject
    Dim fileStream As TextStream
    
    On Error GoTo SaveToFileErr
    
    SaveToFile = True
    
    fileName = Replace(fileName, "\", vbNullString)
    fileName = mdlFunc.RemoveSymbols(fileName, "!@#$%^&*|=:;<>[]{}""?")
    
    If Len(filePath) = 0 Then filePath = "C:\Temp\"
    If Right(filePath, 1) <> "\" Then filePath = filePath & "\"
    'If Not filePath Like "C:\*\" Then filePath = "C:\Temp\"
    
    Do While InStr(filePath, "\\") > 0
        filePath = Replace(filePath, "\\", "\")
    Loop
    
    filePath = filePath & fileName
    Set fso = New FileSystemObject
    Set fileStream = fso.CreateTextFile(filePath)
    fileStream.WriteLine data
    fileStream.Close
    
    Exit Function
SaveToFileErr:
    SaveToFile = False
End Function
