$file = 'd:\Programming\Workspace\VRCHOTAS\VRCHOTAS\PreferencesWindow.xaml'
$content = Get-Content $file -Raw
$prefix = '                                <StackPanel x:Name="VrOverlayOptionsPanel">'
$suffix = '                                </StackPanel>'
$start = $content.IndexOf($prefix)
$end = $content.IndexOf($suffix, $start) + $suffix.Length
$oldBlock = $content.Substring($start, $end - $start)
$newBlock = @"
                                <StackPanel x:Name="VrOverlayOptionsPanel">
                                    <GroupBox Header="Toast Settings" Margin="0,0,0,12">
                                        <StackPanel Margin="8">
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Duration (s)" VerticalAlignment="Center" Width="90"/>
                                                <TextBox x:Name="VrOverlayToastDurationBox" Width="60" VerticalContentAlignment="Center"/>
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Text Size" VerticalAlignment="Center" Width="90"/>
                                                <TextBox x:Name="ToastTextSizeBox" Width="60" VerticalContentAlignment="Center"/>
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Background" VerticalAlignment="Center" Width="90"/>
                                                <TextBox x:Name="ToastBgColorBox" Width="100" VerticalContentAlignment="Center" ToolTip="#AARRGGBB format"/>
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                                <TextBlock Text="Opacity" VerticalAlignment="Center" Width="90"/>
                                                <Slider x:Name="ToastOpacitySlider" Minimum="0" Maximum="1" TickFrequency="0.1" IsSnapToTickEnabled="True" Width="150" VerticalAlignment="Center"/>
                                            </StackPanel>
                                        </StackPanel>
                                    </GroupBox>

                                    <GroupBox Header="Marker Settings">
                                        <StackPanel Margin="8">
                                            <CheckBox x:Name="ShowMasterStatusIndicatorCheckBox" Content="Show Marker (Icon) when Master ON" Margin="0,0,0,12" />
                                            
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Icon File" VerticalAlignment="Center" Width="90"/>
                                                <TextBox x:Name="MarkerImagePathBox" HorizontalAlignment="Stretch" MinWidth="150" VerticalContentAlignment="Center" ToolTip="Absolute path to PNG image"/>
                                                <Button Content="..." Width="30" Margin="4,0,0,0" Click="OnBrowseMarkerImage" />
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Size %" VerticalAlignment="Center" Width="90"/>
                                                <Slider x:Name="MarkerSizeSlider" Minimum="10" Maximum="100" Width="150" VerticalAlignment="Center"/>
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Opacity" VerticalAlignment="Center" Width="90"/>
                                                <Slider x:Name="MarkerOpacitySlider" Minimum="0" Maximum="1" TickFrequency="0.1" IsSnapToTickEnabled="True" Width="150" VerticalAlignment="Center"/>
                                            </StackPanel>
                                            <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,0,8">
                                                <TextBlock Text="Position X" VerticalAlignment="Center" Width="90"/>
                                                <TextBox x:Name="MarkerPosXBox" Width="60" VerticalContentAlignment="Center"/>
                                                <TextBlock Text="Y" VerticalAlignment="Center" Margin="12,0,4,0"/>
                                                <TextBox x:Name="MarkerPosYBox" Width="60" VerticalContentAlignment="Center"/>
                                            </StackPanel>
                                        </StackPanel>
                                    </GroupBox>
                                </StackPanel>
