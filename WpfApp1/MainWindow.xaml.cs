using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OxyPlot;
using OxyPlot.Series;
using MathNet.Numerics.IntegralTransforms;
using System.Collections.Generic;
using System.Windows.Threading;
using System.ComponentModel;

namespace WpfApp1
{
    public class DigitSampleInfo
    {
        public int Digit { get; set; }
        public int SampleCount { get; set; }
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        private readonly AudioManager _audioManager;
        private IAudioInterpreter _audioInterpreter;
        private int? _selectedDigit = null;
        private bool _isSampleRecordingMode = false;

        private string[] _currentSamplePaths = Array.Empty<string>();
        private Complex[][] _currentFftSpectra = Array.Empty<Complex[]>();

        private Key _recordHotkey = Key.Enter;
        private bool _isSelectingHotkey = false;

        // Fine Tuning State
        private bool _isFineTuningMode = false;
        private bool _isNewPersonFineTuning = false;
        private int _fineTuningCurrentDigit = 1;
        private const int _fineTuningMaxDigit = 9;
        private int _fineTuningCorrectCount = 0;

        // Aufnahme-Countdown
        private DispatcherTimer _recordingCountdownTimer;
        private double _recordingTimeLeft = 2.0;
        private const double _recordingDuration = 2.0; // Sekunden

        // Für Fortschrittskreis
        private DoubleCollection _recordingCountdownDashArray = new DoubleCollection() { 0, 1000 };
        public DoubleCollection RecordingCountdownDashArray
        {
            get => _recordingCountdownDashArray;
            set
            {
                _recordingCountdownDashArray = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecordingCountdownDashArray)));
            }
        }

        // Strategie-Auswahl
        private readonly List<(string Name, IAudioInterpreter Interpreter)> _strategies =
            new()
            {
                //("fast Spec Match", new NearestSpectrumInterpreter()),
                // Hier können weitere Strategien ergänzt werden:
                ("Intense Spec Match", new NormalizedNearestSpectrumInterpreter()),
                ("Intense Sum Spec Match", new SummedNormalizedSpectrumInterpreter()),
                ("First 3 Sample Weighed FFT", new WeightedBandSpectrumInterpreter()),
                ("Newest 3 Sample Weighed FFT", new ThreeSampleWeightedBandSpectrumInterpreter()),
                ("10 Sample Weighed FFT", new TenSampleWeightedBandSpectrumInterpreter()),
                ("30 Sample Weighed FFT", new ThirtySampleWeightedBandSpectrumInterpreter()),
                ("Mels Algo", new MfccDtwInterpreter()),
            };

        public List<string> StrategyNames => _strategies.Select(s => s.Name).ToList();

        private int _selectedStrategyIndex;
        public int SelectedStrategyIndex
        {
            get => _selectedStrategyIndex;
            set
            {
                if (_selectedStrategyIndex != value)
                {
                    _selectedStrategyIndex = value;
                    _audioInterpreter = _strategies[value].Interpreter;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedStrategyIndex)));

                    // NEU: Automatisch neu berechnen, wenn nötig
                    if (_audioInterpreter is AbstractWeightedBandSpectrumInterpreter)
                    {
                        AbstractWeightedBandSpectrumInterpreter.RecalculateAllBandWeightings();
                    }
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _audioManager = new AudioManager();
            _audioManager.RecordingStopped += AudioManager_RecordingStopped;
            _audioInterpreter = _strategies[0].Interpreter;
            _selectedStrategyIndex = 0;

            SetStatus(false);
            UpdateInfoText();

            SampleListBox.SelectionChanged += SampleListBox_SelectionChanged;
            SampleActionPanel.Visibility = Visibility.Collapsed;
            BackToPredictionButton.Visibility = Visibility.Collapsed;
        }

        // Aufnahme-Animation & Countdown starten
        private void StartRecordingAnimation()
        {
            Console.WriteLine("Aktuelle Strategie: " + _audioInterpreter.GetType().Name);

            RecordingIndicator.Opacity = 1;
            var pulse = (Storyboard)FindResource("RecordingPulseStoryboard");
            pulse.Begin(this, true);

            // Countdown Overlay starten
            _recordingTimeLeft = _recordingDuration;
            RecordingCountdownOverlay.Visibility = Visibility.Visible;
            UpdateRecordingCountdownVisuals();

            if (_recordingCountdownTimer == null)
            {
                _recordingCountdownTimer = new DispatcherTimer();
                _recordingCountdownTimer.Interval = TimeSpan.FromMilliseconds(50);
                _recordingCountdownTimer.Tick += RecordingCountdownTimer_Tick;
            }
            _recordingCountdownTimer.Start();
        }

        // Aufnahme-Animation & Countdown stoppen
        private void StopRecordingAnimation()
        {
            var pulse = (Storyboard)FindResource("RecordingPulseStoryboard");
            pulse.Stop(this);
            var fade = (Storyboard)FindResource("RecordingFadeOutStoryboard");
            fade.Begin(this, true);

            // Countdown Overlay ausblenden
            RecordingCountdownOverlay.Visibility = Visibility.Collapsed;
            _recordingCountdownTimer?.Stop();
        }

        private void RecordingCountdownTimer_Tick(object? sender, EventArgs e)
        {
            _recordingTimeLeft -= 0.05;
            if (_recordingTimeLeft <= 0)
            {
                _recordingTimeLeft = 0;
                UpdateRecordingCountdownVisuals();
                RecordingCountdownOverlay.Visibility = Visibility.Collapsed;
                _recordingCountdownTimer.Stop();
            }
            else
            {
                UpdateRecordingCountdownVisuals();
            }
        }

        private void UpdateRecordingCountdownVisuals()
        {
            // Fortschrittskreis: 0 = leer, 1 = voll (Kreisumfang ca. 377)
            double percent = 1.0 - (_recordingTimeLeft / _recordingDuration);
            double circleLength = 2 * Math.PI * 54; // 2πr, r=60-6=54
            double dash = percent * circleLength;
            double gap = circleLength - dash;
            RecordingCountdownDashArray = new DoubleCollection() { dash, gap };

            if (RecordingCountdownText != null)
                RecordingCountdownText.Text = _recordingTimeLeft.ToString("0.0");
        }

        // Fine Tuning Auswahl anzeigen
        private void FineTuningButton_Click(object sender, RoutedEventArgs e)
        {
            FineTuningSelectionPopup.Visibility = Visibility.Visible;
        }

        // Vorhandene Person: Standard Fine Tuning
        private void ExistingPersonButton_Click(object sender, RoutedEventArgs e)
        {
            FineTuningSelectionPopup.Visibility = Visibility.Collapsed;
            _isFineTuningMode = true;
            _isNewPersonFineTuning = false;
            _fineTuningCurrentDigit = 1;
            InfoTextBlock.Text = $"Fine Tuning: Please say '{_fineTuningCurrentDigit}'";
            SetStatus(true);
            DigitPanel.Visibility = Visibility.Collapsed;
            SampleListBox.Visibility = Visibility.Collapsed;
            SpectrumPlot.Visibility = Visibility.Collapsed;
            LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
            SampleActionPanel.Visibility = Visibility.Collapsed;
            BackToPredictionButton.Visibility = Visibility.Collapsed;
            _audioManager.StartRecording();
        }

        // Neue Person: Angepasstes Fine Tuning
        private void NewPersonButton_Click(object sender, RoutedEventArgs e)
        {
            FineTuningSelectionPopup.Visibility = Visibility.Collapsed;
            _isFineTuningMode = true;
            _isNewPersonFineTuning = true;
            _fineTuningCurrentDigit = 1;
            _fineTuningCorrectCount = 0;
            InfoTextBlock.Text = $"Neue Person: Bitte sage '{_fineTuningCurrentDigit}' (0/3)";
            SetStatus(true);
            DigitPanel.Visibility = Visibility.Collapsed;
            SampleListBox.Visibility = Visibility.Collapsed;
            SpectrumPlot.Visibility = Visibility.Collapsed;
            LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
            SampleActionPanel.Visibility = Visibility.Collapsed;
            BackToPredictionButton.Visibility = Visibility.Collapsed;
            _audioManager.StartRecording();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isSelectingHotkey)
            {
                _recordHotkey = e.Key;
                _isSelectingHotkey = false;
                UpdateInfoText();
                InfoTextBlock.Text = $"Hotkey set to '{_recordHotkey}'. " + InfoTextBlock.Text;
                e.Handled = true;
                return;
            }

            if (e.Key == _recordHotkey && !_isFineTuningMode)
            {
                SetStatus(true);
                _audioManager.StartRecording();
            }
        }

        private void AudioManager_RecordingStopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                SetStatus(false);
                var samples = _audioManager.GetLastRecordingSamples();

                if (_isFineTuningMode)
                {
                    if (_isNewPersonFineTuning)
                        HandleFineTuningNewPerson(samples);
                    else
                        HandleFineTuningExistingPerson(samples);
                    return;
                }

                if (_selectedDigit.HasValue && _isSampleRecordingMode)
                {
                    string filePath = _audioManager.SaveSampleToDigit(samples, _selectedDigit.Value);
                    InfoTextBlock.Text = $"Sample for {_selectedDigit.Value} saved!";
                    ShowLastRecordingSpectrum(filePath);

                    var updatedSamples = AudioManager.GetSamples(_selectedDigit.Value);
                    SampleListBox.ItemsSource = updatedSamples.Select(Path.GetFileName).ToList();
                    _currentSamplePaths = updatedSamples;
                    _currentFftSpectra = AudioManager.GetSamples_inFTSpec(updatedSamples);

                    UpdateDigitPanelSampleCounts();
                }
                else
                {
                    var interpretedNumber = _audioInterpreter.Interpret(samples, _audioManager.GetSampleLibrary());
                    InterpretedNumberTextBlock.Text = interpretedNumber.ToString();
                    ShowLastRecordingSpectrumFromSamples(samples);
                }
                UpdateInfoText();
            });
        }

        // Standard Fine Tuning (vorhandene Person)
        private void HandleFineTuningExistingPerson(float[] samples)
        {
            int interpreted = _audioInterpreter.Interpret(samples, _audioManager.GetSampleLibrary());
            int sampleCount = AudioManager.GetSamples(_fineTuningCurrentDigit).Length;

            if (interpreted != _fineTuningCurrentDigit || sampleCount < 3)
            {
                _audioManager.SaveSampleToDigit(samples, _fineTuningCurrentDigit);
                InfoTextBlock.Text = $"Fine Tuning: Please say '{_fineTuningCurrentDigit}' (Samples: {sampleCount + 1})";
                SetStatus(true);
                _audioManager.StartRecording();
            }
            else
            {
                if (_fineTuningCurrentDigit < _fineTuningMaxDigit)
                {
                    _fineTuningCurrentDigit++;
                    InfoTextBlock.Text = $"Fine Tuning: Please say '{_fineTuningCurrentDigit}'";
                    SetStatus(true);
                    _audioManager.StartRecording();
                }
                else
                {
                    _isFineTuningMode = false;
                    _fineTuningCurrentDigit = 1;
                    InfoTextBlock.Text = $"Fine Tuning complete. Back to prediction mode. Hit {_recordHotkey} while saying your voice command.";
                    SetStatus(false);
                }
            }
        }

        // Angepasstes Fine Tuning (neue Person)
        private void HandleFineTuningNewPerson(float[] samples)
        {
            int interpreted = _audioInterpreter.Interpret(samples, _audioManager.GetSampleLibrary());

            if (interpreted == _fineTuningCurrentDigit)
            {
                _fineTuningCorrectCount++;
                _audioManager.SaveSampleToDigit(samples, _fineTuningCurrentDigit);
            }
            else
            {
                _audioManager.SaveSampleToDigit(samples, _fineTuningCurrentDigit);
            }

            if (_fineTuningCorrectCount < 3)
            {
                InfoTextBlock.Text = $"Neue Person: Bitte sage '{_fineTuningCurrentDigit}' ({_fineTuningCorrectCount}/3)";
                SetStatus(true);
                _audioManager.StartRecording();
            }
            else
            {
                if (_fineTuningCurrentDigit < _fineTuningMaxDigit)
                {
                    _fineTuningCurrentDigit++;
                    _fineTuningCorrectCount = 0;
                    InfoTextBlock.Text = $"Neue Person: Bitte sage '{_fineTuningCurrentDigit}' (0/3)";
                    SetStatus(true);
                    _audioManager.StartRecording();
                }
                else
                {
                    _isFineTuningMode = false;
                    _isNewPersonFineTuning = false;
                    _fineTuningCurrentDigit = 1;
                    _fineTuningCorrectCount = 0;
                    InfoTextBlock.Text = $"Fine Tuning complete. Back to prediction mode. Hit {_recordHotkey} while saying your voice command.";
                    SetStatus(false);
                }
            }
        }

        private void ShowLastRecordingSpectrum(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
                return;
            }

            Complex[] fft = AudioManager.GetFftFromWavFile(filePath);

            PlotSpectrum(fft, "Last Recording Spectrum", LastRecordingSpectrumPlot, OxyColors.OrangeRed);
        }

        private void ShowLastRecordingSpectrumFromSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
                return;
            }

            Complex[] fft = ComputeFft(samples);

            PlotSpectrum(fft, "Last Recording Spectrum", LastRecordingSpectrumPlot, OxyColors.OrangeRed);
        }

        private Complex[] ComputeFft(float[] samples)
        {
            int n = samples.Length;
            int fftLength = 1;
            while (fftLength < n) fftLength <<= 1;
            Complex[] data = new Complex[fftLength];
            for (int i = 0; i < n; i++)
                data[i] = new Complex(samples[i], 0);
            for (int i = n; i < fftLength; i++)
                data[i] = Complex.Zero;

            Fourier.Forward(data, FourierOptions.Matlab);
            return data;
        }

        private void PlotSpectrum(Complex[] fft, string title, OxyPlot.Wpf.PlotView plotView, OxyColor color)
        {
            int len = Math.Min(fft.Length / 2, 2048);
            var model = new PlotModel { Title = title };
            var series = new LineSeries { Color = color, StrokeThickness = 2 };

            for (int i = 0; i < len; i++)
            {
                double magnitude = fft[i].Magnitude;
                series.Points.Add(new DataPoint(i, magnitude));
            }

            model.Series.Add(series);
            plotView.Model = model;
            plotView.Visibility = Visibility.Visible;
        }

        private void SaveSampleButton_Click(object sender, RoutedEventArgs e)
        {
            var digitInfos = Enumerable.Range(1, 9)
                .Select(d => new DigitSampleInfo
                {
                    Digit = d,
                    SampleCount = AudioManager.GetSamples(d).Length
                })
                .ToList();

            DigitPanel.ItemsSource = digitInfos;
            DigitPanel.Visibility = Visibility.Visible;
            SampleListBox.Visibility = Visibility.Collapsed;
            SpectrumPlot.Visibility = Visibility.Collapsed;
            LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
            InfoTextBlock.Text = "Select a digit to save a sample for.";
            SampleActionPanel.Visibility = Visibility.Collapsed;
            BackToPredictionButton.Visibility = Visibility.Collapsed;
        }

        private void UpdateDigitPanelSampleCounts()
        {
            if (DigitPanel.Visibility == Visibility.Visible && DigitPanel.ItemsSource is IEnumerable<DigitSampleInfo>)
            {
                var digitInfos = Enumerable.Range(1, 9)
                    .Select(d => new DigitSampleInfo
                    {
                        Digit = d,
                        SampleCount = AudioManager.GetSamples(d).Length
                    })
                    .ToList();
                DigitPanel.ItemsSource = digitInfos;
            }
        }

        private void DigitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int digit))
            {
                _selectedDigit = digit;
                _isSampleRecordingMode = true;
                DigitPanel.Visibility = Visibility.Collapsed;
                InfoTextBlock.Text = $"Sample mode for {digit}. Hit {_recordHotkey} to record samples.";
                BackToPredictionButton.Visibility = Visibility.Visible;

                var samples = AudioManager.GetSamples(digit);
                SampleListBox.ItemsSource = samples.Select(Path.GetFileName).ToList();
                SampleListBox.Visibility = Visibility.Visible;
                _currentSamplePaths = samples;
                _currentFftSpectra = AudioManager.GetSamples_inFTSpec(samples);
                SpectrumPlot.Visibility = Visibility.Collapsed;
            }
        }

        private void BackToPredictionButton_Click(object sender, RoutedEventArgs e)
        {
            _isSampleRecordingMode = false;
            _selectedDigit = null;
            BackToPredictionButton.Visibility = Visibility.Collapsed;
            DigitPanel.Visibility = Visibility.Collapsed;
            InfoTextBlock.Text = $"Back to prediction mode. Hit {_recordHotkey} while saying your voice command.";
            SampleListBox.Visibility = Visibility.Collapsed;
            SpectrumPlot.Visibility = Visibility.Collapsed;
            LastRecordingSpectrumPlot.Visibility = Visibility.Collapsed;
        }

        private void SampleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SampleListBox.SelectedIndex >= 0 && _currentFftSpectra.Length > SampleListBox.SelectedIndex)
            {
                var fft = _currentFftSpectra[SampleListBox.SelectedIndex];
                PlotSpectrum(fft, "Fourier Spectrum", SpectrumPlot, OxyColors.SteelBlue);
                SampleActionPanel.Visibility = Visibility.Visible;
            }
            else
            {
                SpectrumPlot.Visibility = Visibility.Collapsed;
                SampleActionPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SampleListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedDigit.HasValue && SampleListBox.SelectedItem is string fileName)
            {
                var filePath = _audioManager.GetSampleFilePath(_selectedDigit.Value, fileName);
                _audioManager.PlaySample(filePath);
            }
        }

        private void PlaySampleButton_Click(object sender, RoutedEventArgs e)
        {
            if (SampleListBox.SelectedItem is string fileName && _selectedDigit.HasValue)
            {
                var filePath = _audioManager.GetSampleFilePath(_selectedDigit.Value, fileName);
                if (File.Exists(filePath))
                {
                    try
                    {
                        var player = new System.Media.SoundPlayer(filePath);
                        player.Play();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Fehler beim Abspielen: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void DeleteSampleButton_Click(object sender, RoutedEventArgs e)
        {
            if (SampleListBox.SelectedItem is string fileName && _selectedDigit.HasValue)
            {
                var filePath = _audioManager.GetSampleFilePath(_selectedDigit.Value, fileName);
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        var items = SampleListBox.ItemsSource.Cast<string>().ToList();
                        items.Remove(fileName);
                        SampleListBox.ItemsSource = items;
                        SpectrumPlot.Visibility = Visibility.Collapsed;
                        SampleActionPanel.Visibility = Visibility.Collapsed;

                        UpdateDigitPanelSampleCounts();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Fehler beim Löschen: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SelectHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _isSelectingHotkey = true;
            InfoTextBlock.Text = "Press any key to set as new hotkey for recording...";
        }

        private void UpdateInfoText()
        {
            if (_isFineTuningMode)
            {
                if (_isNewPersonFineTuning)
                    InfoTextBlock.Text = $"Neue Person: Bitte sage '{_fineTuningCurrentDigit}' ({_fineTuningCorrectCount}/3)";
                else
                    InfoTextBlock.Text = $"Fine Tuning: Please say '{_fineTuningCurrentDigit}'";
            }
            else if (_isSampleRecordingMode && _selectedDigit.HasValue)
            {
                InfoTextBlock.Text = $"Sample mode for {_selectedDigit.Value}. Hit {_recordHotkey} to record samples.";
            }
            else
            {
                InfoTextBlock.Text = $"Hit {_recordHotkey} while saying your voice command.";
            }
        }

        private string GetHotkeyDisplayName()
        {
            return _recordHotkey.ToString();
        }

        private void SetStatus(bool isRecording)
        {
            if (StatusTextBlock == null)
                return;

            if (isRecording)
            {
                StatusTextBlock.Text = "Recording";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                StartRecordingAnimation();
            }
            else
            {
                StatusTextBlock.Text = "Stand by";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                StopRecordingAnimation();
            }
        }
    }
}
