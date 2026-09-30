# Helper process of the end-to-end test: shows a plain window or a borderless full-screen window until it is killed.
param(
    [Parameter(Mandatory)][ValidateSet('fullscreen', 'window')][string]$Mode
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = [System.Windows.Forms.Form]::new()
$form.Text = "OrbixE2E-$Mode"
$form.BackColor = [System.Drawing.Color]::FromArgb(40, 44, 52)
$form.ShowInTaskbar = $true

if ($Mode -eq 'fullscreen') {
    # what a game or a video player does: no frame, exactly the size of the monitor
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
}
else {
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $form.Size = [System.Drawing.Size]::new(520, 340)
}

$form.Add_Shown({ $form.Activate(); $form.BringToFront() })
[System.Windows.Forms.Application]::Run($form)
