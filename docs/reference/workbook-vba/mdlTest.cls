Attribute VB_Name = "mdlTest"
Option Explicit
Option Compare Text

#If VBA7 Then
    Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As LongPtr)
    'For 64-Bit versions of Excel
#Else
    Private Declare Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)
    'For 32-Bit versions of Excel
#End If

Private Const frmlaStart As String = "IF(LEN('*'!R[2]C)>0,""""""""&'*'!R[2]C&"""""""""
Private Const frmlaEnd As String = ","""""""")"
Private Const frmlaTemp As String = "&"";""&CONFIG!R[2]C[1]&"""""""""

Public Sub TestWhatever()
    
End Sub

Public Sub TestEmployee()
    Dim ws As Worksheet, curRow As Long, maxRow As Long, fRow As Long, fRange As Range, sRange As Range
    Set ws = Sheet3
    curRow = 5
    maxRow = 8
    Do
        curRow = curRow + 1
        If curRow > maxRow Then Exit Sub
        If curRow > 2 Then
            
            If Len(CStr(ws.Cells(curRow, 2).Value)) > 0 Then
                Set sRange = ws.Range(ws.Cells(2, 2), ws.Cells(curRow - 1, 2))
                Set fRange = sRange.Find(What:=CStr(ws.Cells(curRow, 2).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
                If fRange Is Nothing Then Exit Do
            Else
                Set sRange = ws.Range(ws.Cells(2, 3), ws.Cells(curRow - 1, 3))
                Set fRange = sRange.Find(What:=CStr(ws.Cells(curRow, 3).Value), LookIn:=xlFormulas, LookAt:=xlWhole, MatchCase:=False)
                If fRange Is Nothing Then Exit Do
                fRow = fRange.Row
                Do
                    If CStr(ws.Cells(fRange.Row, 4).Value) = CStr(ws.Cells(curRow, 4).Value) Then Exit Do
                    Set fRange = sRange.FindNext(fRange)
                    If fRange Is Nothing Then Exit Do
                Loop Until fRange.Row = fRow
                If fRange Is Nothing Then Exit Do
                If CStr(ws.Cells(fRange.Row, 4).Value) <> CStr(ws.Cells(curRow, 4).Value) Then Exit Do
            End If
        Else
            Exit Do
        End If
        
    Loop While curRow < maxRow
    
End Sub


