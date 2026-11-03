Attribute VB_Name = "cCalc2026"
Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = False
Option Explicit

Private Const taxCfgKey As String = "{taxes_config}"

Private Const valCalcYearly As Integer = 0
Private Const valCalcMonthly As Integer = 1
Private Const valCalcHourly As Integer = 2
Private Const strCalcYearly As String = "Yearly"
Private Const strCalcMonthly As String = "Monthly"
Private Const strCalcHourly As String = "Hourly"

Private Const defHoursPerMonth As Integer = 160

Private Const colIncTaxRate As Long = 2
Private Const colPensRate As Long = 7
Private Const colUnEmInsPersRate As Long = 3
Private Const colUnEmInsCompRate As Long = 4
Private Const colTaxFreeYearCommonSum As Long = 8
Private Const colTaxFreeYearRetiredSum As Long = 12
Private Const colTaxFreeYearTier1 As Long = 9
Private Const colTaxFreeYearTier2 As Long = 10
Private Const colTaxFreeT2Divider As Long = 11
Private Const colSocialTaxRate As Long = 5
Private Const colSocialMinMonthSum As Long = 6

'tax values
Private curIncTaxRate As Double
Private curPensRate As Double
Private curUnEmInsPersRate As Double
Private curUnEmInsCompRate As Double
Private curTaxFreeYearCommonSum As Double
Private curTaxFreeYearRetiredSum As Double
Private curTaxFreeYearTier1 As Double
Private curTaxFreeYearTier2 As Double
Private curTaxFreeT2Divider As Double
Private curSocialTaxRate As Double
Private curSocialMinMonthSum As Double

'inputs
Private curCalcPeriod As Integer
Private curHoursPerMonth As Integer
Private curIncTax As Boolean
Private curSocTax As Boolean
Private curUseSocTaxMin As Boolean
Private curPension As Boolean
Private curCustomPensRate As Double
Private curRetired As Boolean
Private curUnEmInsPers As Boolean
Private curUnEmInsComp As Boolean
Private curCalcNetToGross As Boolean
Private curTaxFreeApplied As Boolean
Private curCustomTaxFree As Boolean
Private curCustomTaxFreeSum As Double
Private curCustomAnnIncGross As Boolean
Private curCustomAnnIncGrossSum As Double

Private curExtraIncomeFlatSum As Double

'calculated values
Private curSalaryCompSum As Double
Private curSocTaxAppliedToSum As Double
Private curSocTaxSum As Double
Private curSocTaxComplimSum As Double
Private curPensionSum As Double
Private curUnEmInsPersSum As Double
Private curUnEmInsCompSum As Double
Private curIncTaxAppliedToSum As Double
Private curIncTaxSum As Double
Private curTaxFreeMaxSum As Double

'multipurpose inputs/calculated values
Private curNetSum As Double
Private curGrossSum As Double

'process vars
Private curCalcState As Boolean
Private initTrue As Boolean
Private rowTaxCfg As Long
Private ws As Worksheet
Private cfgWs As Worksheet

Public Function InitData(ByRef wsCfg As Worksheet) As Boolean
    Dim fRange As Range
    Dim i As Long, val As Double
    
    If wsCfg Is Nothing Then Exit Function
    
    Set cfgWs = wsCfg
    
    Set fRange = cfgWs.Cells(1, 1).EntireColumn.Find(What:=taxCfgKey, LookIn:=xlFormulas, LookAt:=xlPart, MatchCase:=False)
    If fRange Is Nothing Then Exit Function Else rowTaxCfg = fRange.Row + 2
    
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colIncTaxRate).Value), val) Then Exit Function
    curIncTaxRate = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colPensRate).Value), val) Then Exit Function
    curPensRate = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colUnEmInsPersRate).Value), val) Then Exit Function
    curUnEmInsPersRate = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colUnEmInsCompRate).Value), val) Then Exit Function
    curUnEmInsCompRate = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colTaxFreeYearCommonSum).Value), val) Then Exit Function
    curTaxFreeYearCommonSum = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colTaxFreeYearRetiredSum).Value), val) Then Exit Function
    curTaxFreeYearRetiredSum = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colTaxFreeYearTier1).Value), val) Then Exit Function
    curTaxFreeYearTier1 = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colTaxFreeYearTier2).Value), val) Then Exit Function
    curTaxFreeYearTier2 = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colTaxFreeT2Divider).Value), val) Then Exit Function
    curTaxFreeT2Divider = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colSocialTaxRate).Value), val) Then Exit Function
    curSocialTaxRate = Abs(val)
    If Not mdlFunc.DoubleFromString(CStr(cfgWs.Cells(rowTaxCfg, colSocialMinMonthSum).Value), val) Then Exit Function
    curSocialMinMonthSum = Abs(val)
    
    ResetData
    
    initTrue = True
    InitData = True
End Function

Property Get ResultReady() As Boolean
    ResultReady = curCalcState
End Property

Property Get NetSum() As Double
    If curCalcState Then NetSum = curNetSum Else NetSum = 0
End Property

Property Let NetSum(ByVal data As Double)
    curNetSum = Abs(data)
    curCalcNetToGross = True
    curCalcState = False
End Property

Property Get GrossSum() As Double
    If curCalcState Then GrossSum = curGrossSum Else GrossSum = 0
End Property

Property Let GrossSum(ByVal data As Double)
    curGrossSum = Abs(data)
    curCalcNetToGross = False
    curCalcState = False
End Property

Property Get CalculationPeriod() As String
    Select Case curCalcPeriod
    Case valCalcYearly
        CalculationPeriod = strCalcYearly
    Case valCalcMonthly
        CalculationPeriod = strCalcMonthly
    Case valCalcHourly
        CalculationPeriod = strCalcHourly
    Case Else
        CalculationPeriod = vbNullString
    End Select
End Property

Property Get CalculationYearly() As String
    CalculationYearly = strCalcYearly
End Property

Property Get CalculationMonthly() As String
    CalculationMonthly = strCalcMonthly
End Property

Property Get CalculationHourly() As String
    CalculationHourly = strCalcHourly
End Property

Public Sub CalculateYearly()
    curCalcPeriod = valCalcYearly
    curCalcState = False
End Sub

Public Sub CalculateMonthly()
    curCalcPeriod = valCalcMonthly
    curCalcState = False
End Sub

Public Sub CalculateHourly(Optional ByVal hourCount As Integer)
    curCalcPeriod = valCalcHourly
    If hourCount = 0 Then curHoursPerMonth = defHoursPerMonth Else curHoursPerMonth = Abs(hourCount)
    curCalcState = False
End Sub

Property Get HourCountPerMonth() As Integer
    HourCountPerMonth = curHoursPerMonth
End Property

Property Let HourCountPerMonth(ByVal data As Integer)
    curHoursPerMonth = Abs(data)
    If curCalcPeriod = valCalcHourly Then curCalcState = False
End Property

Public Sub CustomTaxFree(ByVal data As Boolean, Optional ByVal val As Double)
    curCustomTaxFree = data
    curCustomTaxFreeSum = Abs(val)
    curCalcState = False
End Sub

Public Sub CustomAnnualIncomeGross(ByVal data As Boolean, Optional ByVal val As Double)
    curCustomAnnIncGross = data
    curCustomAnnIncGrossSum = Abs(val)
    curCalcState = False
End Sub

Property Let ExtraIncomeFlatSum(ByVal val As Double)
    curExtraIncomeFlatSum = Abs(val)
    curCalcState = False
End Property

Property Let PensionApplied(ByVal data As Boolean)
    curPension = data
    curCalcState = False
End Property

Property Let PensionRate(ByVal data As Double)
    curCustomPensRate = Abs(data)
    curCalcState = False
End Property

Property Let IsRetired(ByVal data As Boolean)
    curRetired = data
    curCalcState = False
End Property

Property Let TaxFreeApplied(ByVal data As Boolean)
    curTaxFreeApplied = data
    curCalcState = False
End Property

Property Let IncomeTaxApplied(ByVal data As Boolean)
    curIncTax = data
    curCalcState = False
End Property

Property Let SocialTaxApplied(ByVal data As Boolean)
    curSocTax = data
    curCalcState = False
End Property

Property Let SocialTaxUseMin(ByVal data As Boolean)
    curUseSocTaxMin = data
    curCalcState = False
End Property

Property Let UnEmInsApplied(ByVal data As Boolean)
    curUnEmInsPers = data
    curUnEmInsComp = data
    curCalcState = False
End Property

Property Let UnEmInsCompApplied(ByVal data As Boolean)
    curUnEmInsComp = data
    curCalcState = False
End Property

Property Let UnEmInsPersApplied(ByVal data As Boolean)
    curUnEmInsPers = data
    curCalcState = False
End Property

Property Get ExtraIncomeFlatSum() As Double
    ExtraIncomeFlatSum = curExtraIncomeFlatSum
End Property

Property Get CompanyExpense() As Double
    If curCalcState Then CompanyExpense = curSalaryCompSum Else CompanyExpense = 0
End Property

Property Get SocialTaxAppliedToSum() As Double
    If curCalcState Then SocialTaxAppliedToSum = curSocTaxAppliedToSum Else SocialTaxAppliedToSum = 0
End Property

Property Get SocialTaxSum() As Double
    If curCalcState Then SocialTaxSum = curSocTaxSum Else SocialTaxSum = 0
End Property

Property Get SocialTaxComplimSum() As Double
    If curCalcState Then SocialTaxComplimSum = curSocTaxComplimSum Else SocialTaxComplimSum = 0
End Property

Property Get PensionSum() As Double
    If curCalcState Then PensionSum = curPensionSum Else PensionSum = 0
End Property

Property Get UnEmInsPersSum() As Double
    If curCalcState Then UnEmInsPersSum = curUnEmInsPersSum Else UnEmInsPersSum = 0
End Property

Property Get UnEmInsCompSum() As Double
    If curCalcState Then UnEmInsCompSum = curUnEmInsCompSum Else UnEmInsCompSum = 0
End Property

Property Get IncomeTaxRate() As Double
    IncomeTaxRate = curIncTaxRate
End Property

Property Get IncomeTaxAppliedToSum() As Double
    If curCalcState Then IncomeTaxAppliedToSum = curIncTaxAppliedToSum Else IncomeTaxAppliedToSum = 0
End Property

Property Get IncomeTaxSum() As Double
    If curCalcState Then IncomeTaxSum = curIncTaxSum Else IncomeTaxSum = 0
End Property

Property Get TaxFreeSum() As Double
    If curCalcState Then TaxFreeSum = curTaxFreeMaxSum Else TaxFreeSum = 0
End Property

Public Function CalculateValues() As Boolean
    Dim tempSum As Double, tempValue As Double, tier2Flag As Boolean, tier2tfSum As Double
    Dim calcSocTaxMinSum As Double
    Dim calcTaxFreeTier1 As Double
    Dim calcTaxFreeTier2 As Double
    Dim calcPensRate As Double
    Dim calcUnEmPersRate As Double
    Dim calcUnEmCompRate As Double
    Dim calcTaxFreeMaxSum As Double
    Dim calcExtraIncomeFlatSum As Double
    
    curCalcState = False
    
    If curTaxFreeT2Divider = 0 Or curIncTaxRate = 0 Or (curIncTaxRate + calcPensRate + calcUnEmPersRate) >= 1 Then Exit Function
    
    If curPension Then
        calcPensRate = curPensRate
        If curCustomPensRate > 0 Then calcPensRate = curCustomPensRate
    Else
        calcPensRate = 0
    End If
    If curUnEmInsPers Then calcUnEmPersRate = curUnEmInsPersRate Else calcUnEmPersRate = 0
    If curUnEmInsComp Then calcUnEmCompRate = curUnEmInsCompRate Else calcUnEmCompRate = 0
    If curCalcPeriod = valCalcYearly Then calcSocTaxMinSum = curSocialMinMonthSum * 12 Else calcSocTaxMinSum = curSocialMinMonthSum
    If curCalcPeriod = valCalcHourly Then
        curNetSum = curNetSum * curHoursPerMonth
        curGrossSum = curGrossSum * curHoursPerMonth
    End If
    
    If Not curIncTax Then GoTo CalculateValuesTier0
    If Not curTaxFreeApplied Then GoTo CalculateValuesTier3
    
    If curCustomTaxFree Then
        curTaxFreeMaxSum = curCustomTaxFreeSum
    Else
        If curRetired Then
            curTaxFreeMaxSum = curTaxFreeYearRetiredSum
        Else
            curTaxFreeMaxSum = curTaxFreeYearCommonSum
        End If
        If curCalcPeriod = valCalcMonthly Or curCalcPeriod = valCalcHourly Then
            curTaxFreeMaxSum = curTaxFreeMaxSum / 12
            calcTaxFreeTier1 = curTaxFreeYearTier1 / 12
            calcTaxFreeTier2 = curTaxFreeYearTier2 / 12
        End If
        If curCalcPeriod = valCalcYearly Then
            calcTaxFreeTier1 = curTaxFreeYearTier1
            calcTaxFreeTier2 = curTaxFreeYearTier2
        End If
    End If
    
    'If curCustomAnnIncGross And Not curCustomTaxFree And Not curRetired Then
    '    If curCustomAnnIncGrossSum >= curTaxFreeYearTier2 Then GoTo CalculateValuesTier3
    '    If curCustomAnnIncGrossSum > curTaxFreeYearTier1 Then
    '        curTaxFreeMaxSum = curTaxFreeYearCommonSum * (1 - (curCustomAnnIncGrossSum / curTaxFreeT2Divider) + (curTaxFreeYearTier1 / curTaxFreeT2Divider))
    '        If curCalcPeriod = valCalcMonthly Or curCalcPeriod = valCalcHourly Then curTaxFreeMaxSum = curTaxFreeMaxSum / 12
    '    End If
    'End If
    
    If curCalcNetToGross Then
        tempSum = curNetSum
        calcExtraIncomeFlatSum = curExtraIncomeFlatSum / (1 - curIncTaxRate)
    Else
        tempSum = curGrossSum
        calcExtraIncomeFlatSum = curExtraIncomeFlatSum
    End If
    tempSum = tempSum + curExtraIncomeFlatSum
    
    tempValue = curTaxFreeMaxSum
    If tempSum <= tempValue Then GoTo CalculateValuesTier0
    
    GoTo CalculateValuesTier1 'according to 2026 fixed tax free sum
    
    'If curRetired Or curCustomTaxFree Or curCustomAnnIncGross Then GoTo CalculateValuesTier1
    
    'If curCalcNetToGross Then tempValue = (calcTaxFreeTier1 - calcExtraIncomeFlatSum) * (1 - calcPensRate - calcUnEmPersRate + curIncTaxRate * (calcPensRate + calcUnEmPersRate - 1)) + curIncTaxRate * curTaxFreeMaxSum + curExtraIncomeFlatSum Else tempValue = calcTaxFreeTier1
    'If tempSum <= tempValue Then GoTo CalculateValuesTier1
    
    'If curCalcNetToGross Then tempValue = (calcTaxFreeTier2 - calcExtraIncomeFlatSum) * (1 - calcPensRate - calcUnEmPersRate + curIncTaxRate * (calcPensRate + calcUnEmPersRate - 1)) + curExtraIncomeFlatSum Else tempValue = calcTaxFreeTier2
    'If tempSum >= tempValue Then GoTo CalculateValuesTier3
    
    'GoTo CalculateValuesTier2

CalculateValuesTier0:
    If curCalcNetToGross Then
        curGrossSum = curNetSum / (1 - calcPensRate - calcUnEmPersRate)
    End If
    
    GoTo CalculateValuesSummary
    
CalculateValuesTier1:
    If curCalcNetToGross Then
        curGrossSum = (curNetSum - curIncTaxRate * curTaxFreeMaxSum) / (1 - calcPensRate - calcUnEmPersRate + curIncTaxRate * (calcPensRate + calcUnEmPersRate - 1))
    End If
    
    GoTo CalculateValuesSummary
    
CalculateValuesTier2:
    'If curCalcNetToGross Then
    '    tier2Flag = True
    '    tier2tfSum = curTaxFreeMaxSum
    '    curGrossSum = (curNetSum + ((curIncTaxRate * curTaxFreeYearCommonSum * calcExtraIncomeFlatSum) / curTaxFreeT2Divider) - curIncTaxRate * curTaxFreeMaxSum * (1 + (curTaxFreeYearTier1 / curTaxFreeT2Divider))) / (1 - calcPensRate - calcUnEmPersRate + curIncTaxRate * (calcPensRate + calcUnEmPersRate - 1 - (curTaxFreeYearCommonSum / curTaxFreeT2Divider)))
    'End If
    '
    'curTaxFreeMaxSum = curTaxFreeMaxSum * (1 + (curTaxFreeYearTier1 / curTaxFreeT2Divider)) - (((curGrossSum + calcExtraIncomeFlatSum) * curTaxFreeYearCommonSum) / curTaxFreeT2Divider)
    '
    'GoTo CalculateValuesSummary
    
CalculateValuesTier3:
    If curCalcNetToGross Then
        curGrossSum = (curNetSum) / (1 - calcPensRate - calcUnEmPersRate + curIncTaxRate * (calcPensRate + calcUnEmPersRate - 1))
    End If
    
    curTaxFreeMaxSum = 0
    
    GoTo CalculateValuesSummary
    
CalculateValuesSummary:
    curTaxFreeMaxSum = Round(curTaxFreeMaxSum, 2)
    curGrossSum = Round(curGrossSum, 2)
    curPensionSum = Round(curGrossSum * calcPensRate, 2)
    curUnEmInsPersSum = Round(curGrossSum * calcUnEmPersRate, 2)
    curUnEmInsCompSum = Round(curGrossSum * calcUnEmCompRate, 2)
    curIncTaxAppliedToSum = curGrossSum - curPensionSum - curUnEmInsPersSum - curTaxFreeMaxSum
    If curIncTaxAppliedToSum < 0 Or (Not curIncTax) Then curIncTaxAppliedToSum = 0
    curIncTaxSum = Round(curIncTaxAppliedToSum * curIncTaxRate, 2)
    If curCalcNetToGross Then
        curGrossSum = curNetSum + curPensionSum + curUnEmInsPersSum + curIncTaxSum
        If tier2Flag Then
            curTaxFreeMaxSum = Round(tier2tfSum * (1 + (curTaxFreeYearTier1 / curTaxFreeT2Divider)) - (((curGrossSum + calcExtraIncomeFlatSum) * curTaxFreeYearCommonSum) / curTaxFreeT2Divider), 2)
        End If
        curPensionSum = Round(curGrossSum * calcPensRate, 2)
        curUnEmInsPersSum = Round(curGrossSum * calcUnEmPersRate, 2)
        curUnEmInsCompSum = Round(curGrossSum * calcUnEmCompRate, 2)
        curIncTaxAppliedToSum = curGrossSum - curPensionSum - curUnEmInsPersSum - curTaxFreeMaxSum
        If curIncTaxAppliedToSum < 0 Or (Not curIncTax) Then curIncTaxAppliedToSum = 0
        curIncTaxSum = Round(curIncTaxAppliedToSum * curIncTaxRate, 2)
        curNetSum = curGrossSum - curPensionSum - curUnEmInsPersSum - curIncTaxSum
    Else
        curNetSum = curGrossSum - curPensionSum - curUnEmInsPersSum - curIncTaxSum
    End If
    If curTaxFreeMaxSum > curNetSum Then curTaxFreeMaxSum = curNetSum
    curSocTaxComplimSum = 0
    If curSocTax Then
        If curUseSocTaxMin Then If curGrossSum < calcSocTaxMinSum Then curSocTaxComplimSum = calcSocTaxMinSum - curGrossSum
        curSocTaxAppliedToSum = curGrossSum + curSocTaxComplimSum
        curSocTaxSum = Round(curSocTaxAppliedToSum * curSocialTaxRate, 2)
    Else
        curSocTaxAppliedToSum = 0
        curSocTaxSum = 0
    End If
    curSalaryCompSum = curGrossSum + curUnEmInsCompSum + curSocTaxSum
    
    curCalcState = True
    CalculateValues = True

End Function

Public Sub ResetData()

    'inputs
    curCalcPeriod = valCalcMonthly
    curHoursPerMonth = defHoursPerMonth
    curIncTax = True
    curSocTax = True
    curUseSocTaxMin = True
    curPension = True
    curRetired = False
    curUnEmInsPers = True
    curUnEmInsComp = True
    curCalcNetToGross = False
    curTaxFreeApplied = True
    curCustomTaxFree = False
    curCustomTaxFreeSum = 0
    curCustomAnnIncGross = False
    curCustomAnnIncGrossSum = 0
    curExtraIncomeFlatSum = 0
    
    'calculated values
    curSalaryCompSum = 0
    curSocTaxAppliedToSum = 0
    curSocTaxSum = 0
    curSocTaxComplimSum = 0
    curPensionSum = 0
    curUnEmInsPersSum = 0
    curUnEmInsCompSum = 0
    curIncTaxSum = 0
    curIncTaxAppliedToSum = 0
    curTaxFreeMaxSum = 0
    
    'multipurpose inputs/calculated values
    curNetSum = 0
    curGrossSum = 0
    
    'process vars
    curCalcState = False

End Sub


