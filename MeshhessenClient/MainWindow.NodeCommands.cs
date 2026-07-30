// Node-Farben & Notizen
// Ausgelagert aus MainWindow.xaml.cs (partial class) – reine Umsortierung, keine Logikaenderung.

using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Projections;
using Mapsui.Tiling.Layers;
using Mapsui.Extensions;
using BruTile;
using BruTile.Predefined;
using NetTopologySuite.Geometries;
using Mapsui.Nts;
using LoRaConfig = Meshtastic.Protobufs.LoRaConfig;
using MQTTConfig = Meshtastic.Protobufs.MQTTConfig;

namespace MeshhessenClient;

public partial class MainWindow
{
    // ========== Node Color and Note Management ==========

    private void SetNodeColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is string color && SelectedNodeForMenu is NodeInfo node)
        {
            SetNodeColorInternal(node, color);
        }
    }

    private void RemoveNodeColor_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is NodeInfo node)
        {
            RemoveNodeColorInternal(node);
        }
    }

    private void EditNodeNote_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is NodeInfo node)
        {
            EditNodeNoteInternal(node);
        }
    }

    private void SetNodeColorInternal(NodeInfo node, string color)
    {
        try
        {
            node.ColorHex = color;
            _currentSettings.NodeColors[node.NodeId] = color;
            SettingsService.Save(_currentSettings);

            // Update in _allNodes
            var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
            if (existing != null)
            {
                existing.ColorHex = color;
            }

            // Refresh display
            ApplyNodeSortAndFilterCore();
            UpdateNodePin(node);

            Services.Logger.WriteLine($"Set color {color} for node {node.Name} ({node.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error setting node color: {ex.Message}");
        }
    }

    private void RemoveNodeColorInternal(NodeInfo node)
    {
        try
        {
            node.ColorHex = string.Empty;
            _currentSettings.NodeColors.Remove(node.NodeId);
            SettingsService.Save(_currentSettings);

            // Update in _allNodes
            var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
            if (existing != null)
            {
                existing.ColorHex = string.Empty;
            }

            // Refresh display
            ApplyNodeSortAndFilterCore();
            UpdateNodePin(node);

            Services.Logger.WriteLine($"Removed color from node {node.Name} ({node.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error removing node color: {ex.Message}");
        }
    }

    private void EditNodeNoteInternal(NodeInfo node)
    {
        try
        {
            var dialog = new System.Windows.Window
            {
                Title = string.Format(Loc("StrEditNoteTitle"), node.Name),
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = node.Note,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(textBox, 0);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(buttonPanel, 1);

            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            okButton.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = Loc("StrCancel"), Width = 80, IsCancel = true };
            cancelButton.Click += (s, ev) => { dialog.DialogResult = false; dialog.Close(); };
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonPanel);
            dialog.Content = grid;

            if (dialog.ShowDialog() == true)
            {
                var newNote = textBox.Text.Trim();
                node.Note = newNote;

                if (string.IsNullOrEmpty(newNote))
                {
                    _currentSettings.NodeNotes.Remove(node.NodeId);
                }
                else
                {
                    _currentSettings.NodeNotes[node.NodeId] = newNote;
                }

                SettingsService.Save(_currentSettings);

                // Update in _allNodes
                var existing = _allNodes.FirstOrDefault(n => n.NodeId == node.NodeId);
                if (existing != null)
                {
                    existing.Note = newNote;
                }

                // Refresh display
                ApplyNodeSortAndFilterCore();
                UpdateNodePin(node);

                Services.Logger.WriteLine($"Updated note for node {node.Name} ({node.Id}): {newNote}");
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error editing node note: {ex.Message}");
        }
    }

    private void PlayAlertSound()
    {
        try
        {
            // Play alarm sound in background thread
            Task.Run(() =>
            {
                try
                {
                    // Generate and play alarm WAV sound
                    var wavData = GenerateAlarmSound();
                    using (var ms = new MemoryStream(wavData))
                    {
                        var player = new System.Media.SoundPlayer(ms);
                        player.PlaySync();
                    }
                    Services.Logger.WriteLine("Alert sound played successfully");
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"WAV playback failed: {ex.Message}, trying Console.Beep");
                    try
                    {
                        // Fallback to Console.Beep
                        for (int i = 0; i < 3; i++)
                        {
                            Console.Beep(1200, 150);
                            Thread.Sleep(100);
                        }
                    }
                    catch
                    {
                        // Last fallback: System sound
                        for (int i = 0; i < 5; i++)
                        {
                            System.Media.SystemSounds.Hand.Play();
                            Thread.Sleep(200);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error playing alert sound: {ex.Message}");
        }
    }

    private byte[] GenerateAlarmSound()
    {
        // Generate a simple alarm sound (siren effect) as WAV
        int sampleRate = 8000;
        int durationMs = 2000; // 2 seconds
        int numSamples = (sampleRate * durationMs) / 1000;

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // WAV header
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + numSamples); // File size - 8
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // Format chunk size
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate); // Byte rate
            writer.Write((short)1); // Block align
            writer.Write((short)8); // Bits per sample
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(numSamples);

            // Generate siren sound (alternating frequencies)
            double freq1 = 800.0; // Low frequency
            double freq2 = 1400.0; // High frequency
            double cycleDuration = 0.5; // Half second per cycle
            int cyclesamples = (int)(sampleRate * cycleDuration);

            for (int i = 0; i < numSamples; i++)
            {
                // Alternate between two frequencies
                int cyclePos = i % (cyclesamples * 2);
                double freq = (cyclePos < cyclesamples) ? freq1 : freq2;

                // Generate sine wave
                double angle = 2.0 * Math.PI * freq * i / sampleRate;
                double sample = Math.Sin(angle) * 127 + 128;

                writer.Write((byte)sample);
            }

            return ms.ToArray();
        }
    }

    private void ShowAlertBellAnimation()
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Start blink animation
                var storyboard = new System.Windows.Media.Animation.Storyboard();

                // Create animation for opacity (blink effect)
                var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    AutoReverse = true,
                    RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(6) // 6 blinks (3 seconds)
                };

                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, AlertBellOverlay);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(Border.OpacityProperty));

                storyboard.Children.Add(opacityAnimation);

                // Show overlay and start animation
                AlertBellOverlay.Visibility = Visibility.Visible;

                storyboard.Completed += (s, e) =>
                {
                    AlertBellOverlay.Visibility = Visibility.Collapsed;
                };

                storyboard.Begin();

                // Flash window in taskbar
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                FlashWindow(hwnd, true);
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error showing alert bell animation: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

}
