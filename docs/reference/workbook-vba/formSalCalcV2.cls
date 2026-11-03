Attribute VB_Name = "formSalCalcV2"
Attribute VB_Base = "0{1A4E1CCC-7C84-4DB0-AC2A-378541EA00B4}{A9262B40-43AD-40FD-A1B4-4BE42CD36879}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit

Private oCalc As cCalc2026
Private calcState As Boolean

Private Sub btnCalc_Click()
    With formSalCalcV2
        calcState = True
        oCalc.ResetData
        oCalc.CalculateMonthly
        If .optGross.Value And IsNumeric(.txtGrossSum.Text) Then oCalc.GrossSum = CDbl(.txtGrossSum.Text)
        If .optNet.Value And IsNumeric(.txtNetSum.Text) Then oCalc.NetSum = CDbl(.txtNetSum.Text)
        If IsNumeric(.txtExtraIncome.Text) Then oCalc.ExtraIncomeFlatSum = CDbl(.txtExtraIncome.Text)
        If .chbAnnualIncome.Value And IsNumeric(.txtAnnualIncome.Text) Then
            oCalc.CustomAnnualIncomeGross True, CDbl(.txtAnnualIncome.Text)
        Else
            oCalc.CustomAnnualIncomeGross False
            .chbAnnualIncome.Value = False
            .txtAnnualIncome.Text = vbNullString
        End If
        If .chbTaxFreeCustom.Value And IsNumeric(.txtTaxFree.Text) Then
            oCalc.CustomTaxFree True, CDbl(.txtTaxFree.Text)
        Else
            oCalc.CustomTaxFree False
            .chbTaxFreeCustom.Value = False
            .txtTaxFree.Text = vbNullString
        End If
        oCalc.IsRetired = False
        oCalc.IncomeTaxApplied = True
        oCalc.TaxFreeApplied = .chbTaxFree.Value
        oCalc.SocialTaxUseMin = .chbSocMin.Value
        oCalc.UnEmInsCompApplied = .chbInsComp.Value
        oCalc.UnEmInsPersApplied = .chbInsPers.Value
        oCalc.PensionApplied = .chbPens.Value
        oCalc.CalculateValues
        .txtGrossSum.Text = Format(oCalc.GrossSum, "0.00")
        .txtNetSum.Text = Format(oCalc.NetSum, "0.00")
        .txtIncTax.Text = Format(oCalc.IncomeTaxSum, "0.00")
        .txtInsComp.Text = Format(oCalc.UnEmInsCompSum, "0.00")
        .txtInsPers.Text = Format(oCalc.UnEmInsPersSum, "0.00")
        .txtPens.Text = Format(oCalc.PensionSum, "0.00")
        .txtSocTax.Text = Format(oCalc.SocialTaxSum, "0.00")
        .txtSalComp.Text = Format(oCalc.CompanyExpense, "0.00")
        .txtExtraIncome.Text = Format(oCalc.ExtraIncomeFlatSum, "0.00")
        If Not .chbTaxFreeCustom.Value Then .txtTaxFree.Text = Format(oCalc.TaxFreeSum, "0.00")
        calcState = False
    End With
End Sub

Private Sub btnClose_Click()
    Unload Me
End Sub

Private Sub chbAnnualIncome_Click()
    With formSalCalcV2
        .txtAnnualIncome.Locked = Not .chbAnnualIncome.Value
    End With
End Sub

Private Sub chbTaxFreeCustom_Click()
    With formSalCalcV2
        .txtTaxFree.Locked = Not .chbTaxFreeCustom.Value
    End With
End Sub

Private Sub optGross_Change()
    With formSalCalcV2
        .txtGrossSum.Locked = Not .optGross.Value
        .txtNetSum.Locked = Not .optNet.Value
        .txtAnnualIncome.Text = vbNullString
        .txtGrossSum.Text = vbNullString
        .txtIncTax.Text = vbNullString
        .txtInsComp.Text = vbNullString
        .txtInsPers.Text = vbNullString
        .txtNetSum.Text = vbNullString
        .txtPens.Text = vbNullString
        .txtSalComp.Text = vbNullString
        .txtSocTax.Text = vbNullString
        .txtTaxFree.Text = vbNullString
        .txtExtraIncome.Text = vbNullString
    End With
End Sub

Private Sub txtExtraIncome_Change()
    With formSalCalcV2
        If .optGross.Value And Not calcState Then
            '.txtAnnualIncome.Text = vbNullString
            '.txtGrossSum.Text = vbNullString
            .txtIncTax.Text = vbNullString
            .txtInsComp.Text = vbNullString
            .txtInsPers.Text = vbNullString
            .txtNetSum.Text = vbNullString
            .txtPens.Text = vbNullString
            .txtSalComp.Text = vbNullString
            .txtSocTax.Text = vbNullString
            .txtTaxFree.Text = vbNullString
            '.txtExtraIncome.Text = vbNullString
        End If
        If .optNet.Value And Not calcState Then
            '.txtAnnualIncome.Text = vbNullString
            .txtGrossSum.Text = vbNullString
            .txtIncTax.Text = vbNullString
            .txtInsComp.Text = vbNullString
            .txtInsPers.Text = vbNullString
            '.txtNetSum.Text = vbNullString
            .txtPens.Text = vbNullString
            .txtSalComp.Text = vbNullString
            .txtSocTax.Text = vbNullString
            .txtTaxFree.Text = vbNullString
            '.txtExtraIncome.Text = vbNullString
        End If
    End With
End Sub

Private Sub txtGrossSum_Change()
    With formSalCalcV2
        If .optGross.Value And Len(.txtNetSum) > 0 And Not calcState Then
            .txtAnnualIncome.Text = vbNullString
            '.txtGrossSum.Text = vbNullString
            .txtIncTax.Text = vbNullString
            .txtInsComp.Text = vbNullString
            .txtInsPers.Text = vbNullString
            .txtNetSum.Text = vbNullString
            .txtPens.Text = vbNullString
            .txtSalComp.Text = vbNullString
            .txtSocTax.Text = vbNullString
            .txtTaxFree.Text = vbNullString
            .txtExtraIncome.Text = vbNullString
        End If
    End With
End Sub

Private Sub txtNetSum_Change()
    With formSalCalcV2
        If .optNet.Value And Len(.txtGrossSum) > 0 And Not calcState Then
            .txtAnnualIncome.Text = vbNullString
            .txtGrossSum.Text = vbNullString
            .txtIncTax.Text = vbNullString
            .txtInsComp.Text = vbNullString
            .txtInsPers.Text = vbNullString
            '.txtNetSum.Text = vbNullString
            .txtPens.Text = vbNullString
            .txtSalComp.Text = vbNullString
            .txtSocTax.Text = vbNullString
            .txtTaxFree.Text = vbNullString
            .txtExtraIncome.Text = vbNullString
        End If
    End With
End Sub

'Private Sub optGross_Click()
'    With formSalCalc
'        .txtGrossSum.Locked = Not .optGross.Value
'        .txtNetSum.Locked = Not .optNet.Value
'    End With
'End Sub

'Private Sub optNet_Click()
'    With formSalCalc
'        .txtGrossSum.Locked = Not .optGross.Value
'        .txtNetSum.Locked = Not .optNet.Value
'    End With
'End Sub

'Private Sub txtGrossSum_Change()
'    With formSalCalc
'        .txtAnnualIncome.Text = vbNullString
'        .txtIncTax.Text = vbNullString
'        .txtInsComp.Text = vbNullString
'        .txtInsPers.Text = vbNullString
'        .txtNetSum.Text = vbNullString
'        .txtPens.Text = vbNullString
'        .txtSalComp.Text = vbNullString
'        .txtSocTax.Text = vbNullString
'        .txtTaxFree.Text = vbNullString
'    End With
'End Sub

'Private Sub txtNetSum_Change()
'    With formSalCalc
'        .txtAnnualIncome.Text = vbNullString
'        .txtIncTax.Text = vbNullString
'        .txtInsComp.Text = vbNullString
'        .txtInsPers.Text = vbNullString
'        .txtGrossSum.Text = vbNullString
'        .txtPens.Text = vbNullString
'        .txtSalComp.Text = vbNullString
'        .txtSocTax.Text = vbNullString
'        .txtTaxFree.Text = vbNullString
'    End With
'End Sub

Private Sub UserForm_Initialize()
    Set oCalc = New cCalc2026
    If Not oCalc.InitData(Sheet1) Then MsgBox "Calculator config error!", vbCritical, Title:="Calculator"
End Sub
