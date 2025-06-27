using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using NAudio.Wave;
using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace WpfApp1
{
    public class AudioManager
    {
        private WaveInEvent _waveIn;
        private MemoryStream _memoryStream;
        private List<float[]> _sampleLibrary = new();
        private System.Timers.Timer _recordTimer;
        private const string SamplesRoot = "Samples";

        public event EventHandler RecordingStopped;

        public void StartRecording()
        {
            _waveIn = new WaveInEvent();
            _waveIn.WaveFormat = new WaveFormat(44100, 16, 1); // 16 Bit PCM, Mono
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _memoryStream = new MemoryStream();
            _waveIn.StartRecording();

            _recordTimer = new System.Timers.Timer(2000);
            _recordTimer.Elapsed += (s, e) =>
            {
                _recordTimer.Stop();
                StopRecording();
            };
            _recordTimer.AutoReset = false;
            _recordTimer.Start();
        }

        public void StopRecording()
        {
            _waveIn?.StopRecording();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            _memoryStream?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            _waveIn.Dispose();
            _waveIn = null;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public float[] GetLastRecordingSamples()
        {
            if (_memoryStream == null) return Array.Empty<float>();
            _memoryStream.Position = 0;
            var buffer = _memoryStream.ToArray();
            int bytesPerSample = 2; // 16 Bit PCM
            int sampleCount = buffer.Length / bytesPerSample;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample16 = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = sample16 / 32768f; // Normierung auf [-1, 1]
            }
            return samples;
        }

        public void SaveSample(float[] sample)
        {
            _sampleLibrary.Add(sample);
        }

        public IReadOnlyList<float[]> GetSampleLibrary() => _sampleLibrary.AsReadOnly();

        // --- Sample-Management für Ziffern ---

        /// <summary>
        /// Speichert das Sample und gibt den Pfad der gespeicherten Datei zurück.
        /// </summary>
        public string SaveSampleToDigit(float[] samples, int digit)
        {
            Directory.CreateDirectory(SamplesRoot);
            string digitDir = Path.Combine(SamplesRoot, digit.ToString());
            Directory.CreateDirectory(digitDir);

            string fileName = $"sample_{DateTime.Now:yyyyMMdd_HHmmssfff}.wav";
            string filePath = Path.Combine(digitDir, fileName);

            SaveSamplesAsWav(samples, filePath);
            return filePath;
        }

        public List<string> GetSampleFilesForDigit(int digit)
        {
            string digitDir = Path.Combine(SamplesRoot, digit.ToString());
            if (!Directory.Exists(digitDir))
                return new List<string>();
            return new List<string>(Directory.GetFiles(digitDir, "*.wav"));
        }

        public string GetSampleFilePath(int digit, string fileName)
        {
            return Path.Combine(SamplesRoot, digit.ToString(), fileName);
        }

        public void PlaySample(string filePath)
        {
            var audioFile = new AudioFileReader(filePath);
            var outputDevice = new WaveOutEvent();
            outputDevice.Init(audioFile);
            outputDevice.Play();
            outputDevice.PlaybackStopped += (s, e) =>
            {
                outputDevice.Dispose();
                audioFile.Dispose();
            };
        }

        private void SaveSamplesAsWav(float[] samples, string filePath)
        {
            var waveFormat = new WaveFormat(44100, 1);
            using var writer = new WaveFileWriter(filePath, waveFormat);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        // --- Neue statische Methoden ---

        public static string[] GetSamples(int ziffer)
        {
            string digitDir = Path.Combine(SamplesRoot, ziffer.ToString());
            if (!Directory.Exists(digitDir))
                return Array.Empty<string>();
            return Directory.GetFiles(digitDir, "*.wav");
        }

        public static Complex[][] GetSamples_inFTSpec(string[] samplePaths)
        {
            var result = new List<Complex[]>();
            foreach (var path in samplePaths)
            {
                float[] samples = LoadWavAsFloatArray(path);
                Complex[] complexSamples = samples.Select(s => new Complex(s, 0)).ToArray();
                Fourier.Forward(complexSamples, FourierOptions.Matlab);
                result.Add(complexSamples);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Gibt das FFT-Spektrum eines WAV-Files zurück.
        /// </summary>
        public static Complex[] GetFftFromWavFile(string filePath)
        {
            float[] samples = LoadWavAsFloatArray(filePath);
            Complex[] fft = samples.Select(s => new Complex(s, 0)).ToArray();
            Fourier.Forward(fft, FourierOptions.Matlab);
            return fft;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new AudioFileReader(filePath);
            var samples = new float[audioFile.Length / 4];
            int read = audioFile.Read(samples, 0, samples.Length);
            if (read < samples.Length)
            {
                Array.Resize(ref samples, read);
            }
            return samples;
        }
    }
}
