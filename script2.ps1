$file = 'd:\Programming\Workspace\VRCHOTAS\VRCHOTAS\PreferencesWindow.xaml.cs'
$content = Get-Content $file -Raw
$block = "
        private void OnBrowseMarkerImage(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Select Marker Image"
            };
            if (dialog.ShowDialog() == true)
            {
                MarkerImagePathBox.Text = dialog.FileName;
            }
        }
"
if ($content -notmatch "OnBrowseMarkerImage") {
    $content = $content.Replace("private void OnOkClick", "$block
        private void OnOkClick")
    Set-Content $file -Value $content
}
