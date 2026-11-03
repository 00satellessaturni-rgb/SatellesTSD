Attribute VB_Name = "Sheet1"
Attribute VB_Base = "0{00020820-0000-0000-C000-000000000046}"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = True
Attribute VB_TemplateDerived = False
Attribute VB_Customizable = True
Attribute VB_Control = "btnRunMain, 1, 0, MSForms, CommandButton"
Attribute VB_Control = "btnCalculator, 2, 1, MSForms, CommandButton"
Option Explicit

Private Sub btnCalculator_Click()
    formSalCalcV2.Show
End Sub

Private Sub btnRunMain_Click()
    formMain.Show
End Sub

