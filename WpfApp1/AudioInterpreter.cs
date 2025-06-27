using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Base;
using NWaves.Transforms;
using NWaves.Signals;
using NWaves.FeatureExtractors.Options;
using NWaves.Utils;
using MathNet.Numerics;

namespace WpfApp1
{
    public interface IAudioInterpreter
    {
        int Interpret(float[] input, IReadOnlyList<float[]> samples);
    }

    public class DefaultAudioInterpreter : IAudioInterpreter
    {
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            if (input == null || input.Length == 0)
                return 0;

            Complex[] complexSamples = input.Select(s => new Complex(s, 0)).ToArray();
            Fourier.Forward(complexSamples, FourierOptions.Matlab);
            var magnitude = complexSamples.Select(c => c.Magnitude).ToArray();
            if (magnitude.Length == 0)
                return 0;

            var maxMagnitude = magnitude.Max();
            return (int)Math.Ceiling(maxMagnitude % 9) + 1;
        }
    }
}


namespace WpfApp1
{
    public class AverageFftInterpreter : IAudioInterpreter
    {
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            if (samples == null || samples.Count == 0)
                return -1;

            // Gruppiere Samples nach Ziffer (angenommen: 10 Ziffern, 0-9)
            var digitGroups = new Dictionary<int, List<float[]>>();
            for (int i = 0; i < samples.Count; i++)
            {
                // Annahme: Die Reihenfolge der Samples entspricht der Ziffer
                // Passe dies ggf. an deine Sample-Struktur an!
                int digit = i % 10;
                if (!digitGroups.ContainsKey(digit))
                    digitGroups[digit] = new List<float[]>();
                digitGroups[digit].Add(samples[i]);
            }

            // FFT der Aufnahme
            var inputFft = ComputeFft(input);

            // Für jede Ziffer: Durchschnittliches Spektrum berechnen und Distanz messen
            double minDistance = double.MaxValue;
            int bestDigit = -1;

            foreach (var kvp in digitGroups)
            {
                int digit = kvp.Key;
                var group = kvp.Value;

                // Durchschnittliches Spektrum berechnen
                var avgSpectrum = ComputeAverageSpectrum(group);

                // Distanz zur Aufnahme berechnen
                double dist = SpectrumDistance(inputFft, avgSpectrum);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestDigit = digit;
                }
            }

            return bestDigit;
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

        private double[] ComputeAverageSpectrum(List<float[]> group)
        {
            if (group == null || group.Count == 0)
                return Array.Empty<double>();

            // Alle FFTs berechnen und Magnituden aufsummieren
            int fftLen = ComputeFft(group[0]).Length;
            double[] sum = new double[fftLen / 2];

            foreach (var sample in group)
            {
                var fft = ComputeFft(sample);
                for (int i = 0; i < sum.Length; i++)
                    sum[i] += fft[i].Magnitude;
            }

            // Mittelwert berechnen
            for (int i = 0; i < sum.Length; i++)
                sum[i] /= group.Count;

            return sum;
        }

        private double SpectrumDistance(Complex[] inputFft, double[] avgSpectrum)
        {
            int len = Math.Min(inputFft.Length / 2, avgSpectrum.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double diff = inputFft[i].Magnitude - avgSpectrum[i];
                sum += diff * diff;
            }
            return sum;
        }
    }
}

namespace WpfApp1
{
    public class NearestSpectrumInterpreter : IAudioInterpreter
    {
        /// <summary>
        /// Vergleicht das Spektrum der Aufnahme mit jedem Sample und gibt die Ziffer (Ordnername) des ähnlichsten Samples zurück.
        /// </summary>
        /// <param name="input">Aufnahme als float[]</param>
        /// <param name="samples">Liste aller Samples (float[]), wird nicht verwendet</param>
        /// <returns>Ziffer (int) des ähnlichsten Samples, -1 falls kein Treffer</returns>
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            // Suche alle Sample-Dateien in allen Ziffern-Ordnern
            string samplesRoot = "Samples";
            if (!Directory.Exists(samplesRoot))
                return -1;

            var allSampleFiles = Directory.GetDirectories(samplesRoot)
                .SelectMany(dir => Directory.GetFiles(dir, "*.wav")
                    .Select(f => new { FilePath = f, Digit = ParseDigitFromPath(f) }))
                .ToList();

            if (allSampleFiles.Count == 0)
                return -1;

            // FFT der Aufnahme berechnen
            var inputFft = ComputeFft(input);

            double minDistance = double.MaxValue;
            int bestDigit = -1;

            foreach (var sample in allSampleFiles)
            {
                float[] sampleData = LoadWavAsFloatArray(sample.FilePath);
                var sampleFft = ComputeFft(sampleData);

                double dist = SpectrumDistance(inputFft, sampleFft);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestDigit = sample.Digit;
                }
            }

            return bestDigit;
        }

        private static int ParseDigitFromPath(string filePath)
        {
            // Beispiel: Samples\3\sample_20240624_123456.wav -> gibt 3 zurück
            var dir = Path.GetDirectoryName(filePath);
            var digitStr = Path.GetFileName(dir);
            return int.TryParse(digitStr, out int digit) ? digit : -1;
        }

        private static Complex[] ComputeFft(float[] samples)
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

        private static double SpectrumDistance(Complex[] a, Complex[] b)
        {
            int len = Math.Min(a.Length, b.Length) / 2; // Nur positive Frequenzen vergleichen
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double diff = a[i].Magnitude - b[i].Magnitude;
                sum += diff * diff;
            }
            return sum;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new NAudio.Wave.AudioFileReader(filePath);
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

namespace WpfApp1
{
    public class NormalizedNearestSpectrumInterpreter : IAudioInterpreter //Bisher bester weg
    {
        /// <summary>
        /// Vergleicht das normalisierte Spektrum der Aufnahme mit jedem normalisierten Sample-Spektrum
        /// und gibt die Ziffer (Ordnername) des ähnlichsten Samples zurück.
        /// </summary>
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            string samplesRoot = "Samples";
            if (!Directory.Exists(samplesRoot))
                return -1;

            var allSampleFiles = Directory.GetDirectories(samplesRoot)
                .SelectMany(dir => Directory.GetFiles(dir, "*.wav")
                    .Select(f => new { FilePath = f, Digit = ParseDigitFromPath(f) }))
                .ToList();

            if (allSampleFiles.Count == 0)
                return -1;

            // FFT der Aufnahme berechnen und normalisieren
            var inputFft = ComputeFft(input);
            var inputNorm = NormalizeSpectrum(inputFft);

            double minDistance = double.MaxValue;
            int bestDigit = -1;

            foreach (var sample in allSampleFiles)
            {
                float[] sampleData = LoadWavAsFloatArray(sample.FilePath);
                var sampleFft = ComputeFft(sampleData);
                var sampleNorm = NormalizeSpectrum(sampleFft);

                double dist = SpectrumDistance(inputNorm, sampleNorm);

                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestDigit = sample.Digit;
                }
            }

            return bestDigit;
        }

        private static int ParseDigitFromPath(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            var digitStr = Path.GetFileName(dir);
            return int.TryParse(digitStr, out int digit) ? digit : -1;
        }

        private static Complex[] ComputeFft(float[] samples)
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

        private static double[] NormalizeSpectrum(Complex[] fft)
        {
            int len = fft.Length / 2;
            double[] mag = new double[len];
            double max = 0.0;
            for (int i = 0; i < len; i++)
            {
                mag[i] = fft[i].Magnitude;
                if (mag[i] > max) max = mag[i];
            }
            if (max > 0)
            {
                for (int i = 0; i < len; i++)
                    mag[i] /= max;
            }
            return mag;
        }

        private static double SpectrumDistance(double[] a, double[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double diff = a[i] - b[i];
                sum +=  diff * diff;
            }
            return sum;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new NAudio.Wave.AudioFileReader(filePath);
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
namespace WpfApp1
{
    public class SummedNormalizedSpectrumInterpreter : IAudioInterpreter //SCHLECHTE ERKENNUNGSRATE
    {
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            string samplesRoot = "Samples";
            if (!Directory.Exists(samplesRoot))
                return -1;

            // Alle Sample-Dateien mit Ziffernzuordnung laden
            var allSampleFiles = Directory.GetDirectories(samplesRoot)
                .SelectMany(dir => Directory.GetFiles(dir, "*.wav")
                    .Select(f => new { FilePath = f, Digit = ParseDigitFromPath(f) }))
                .ToList();

            if (allSampleFiles.Count == 0)
                return -1;

            // FFT und Normalisierung der Aufnahme
            var inputFft = ComputeFft(input);
            var inputNorm = NormalizeSpectrum(inputFft);

            // Gruppiere Samples nach Ziffer
            var digitGroups = allSampleFiles.GroupBy(f => f.Digit);

            double minAvg = double.MaxValue;
            int bestDigit = -1;

            foreach (var group in digitGroups)
            {
                double sum = 0;
                int count = 0;
                foreach (var sample in group)
                {
                    float[] sampleData = LoadWavAsFloatArray(sample.FilePath);
                    var sampleFft = ComputeFft(sampleData);
                    var sampleNorm = NormalizeSpectrum(sampleFft);

                    sum += SpectrumDistance(inputNorm, sampleNorm);
                    count++;
                }

                if (count > 0)
                {
                    double avg = sum / count;
                    if (avg < minAvg)
                    {
                        minAvg = avg;
                        bestDigit = group.Key;
                    }
                }
            }

            return bestDigit;
        }

        private static int ParseDigitFromPath(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            var digitStr = Path.GetFileName(dir);
            return int.TryParse(digitStr, out int digit) ? digit : -1;
        }

        private static Complex[] ComputeFft(float[] samples)
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

        private static double[] NormalizeSpectrum(Complex[] fft)
        {
            int len = fft.Length / 2;
            double[] mag = new double[len];
            double max = 0.0;
            for (int i = 0; i < len; i++)
            {
                mag[i] = fft[i].Magnitude;
                if (mag[i] > max) max = mag[i];
            }
            if (max > 0)
            {
                for (int i = 0; i < len; i++)
                    mag[i] /= max;
            }
            return mag;
        }

        private static double SpectrumDistance(double[] a, double[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }
            return sum;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new NAudio.Wave.AudioFileReader(filePath);
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



namespace WpfApp1
{
    /// <summary>
    /// Interpretiert Audiodaten mittels MFCC-Features (NWaves) und Dynamic Time Warping.
    /// Erwartet als Input und Samples jeweils Roh-Audiodaten (PCM, float[]).
    /// </summary>
    public class MfccDtwInterpreter : IAudioInterpreter //Funktioniert nicht aber wäre der Beste weg
    {
        private readonly int sampleRate;
        private readonly int windowSize;
        private readonly int hopSize;
        private readonly int numCoefficients;
        private readonly int numMelFilters;
        private readonly double minFrequency;
        private readonly double maxFrequency;
        private readonly double preEmphasisAlpha;

        public MfccDtwInterpreter(
            int sampleRate = 16000,
            int windowSize = 400,
            int hopSize = 160,
            int numCoefficients = 13,
            int numMelFilters = 26,
            double minFrequency = 20,
            double maxFrequency = -1,
            double preEmphasisAlpha = 0.97)
        {
            this.sampleRate = sampleRate;
            this.windowSize = windowSize;
            this.hopSize = hopSize;
            this.numCoefficients = numCoefficients;
            this.numMelFilters = numMelFilters;
            this.minFrequency = minFrequency;
            this.maxFrequency = (maxFrequency == -1) ? sampleRate / 2.0 : maxFrequency;
            this.preEmphasisAlpha = preEmphasisAlpha;
        }

        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            if (input == null || input.Length < windowSize || samples == null || samples.Count == 0)
                return -1;

            var inputMfcc = ComputeMfcc(input);

            // Prüfe, ob Input-MFCCs gültig sind
            if (inputMfcc.Length == 0)
                return -1;

            double minDistance = double.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (sample == null || sample.Length < windowSize)
                    continue;

                var sampleMfcc = ComputeMfcc(sample);
                if (sampleMfcc.Length == 0)
                    continue;

                double distance = DtwDistance(inputMfcc, sampleMfcc);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private double[][] ComputeMfcc(float[] audio)
        {
            if (audio == null || audio.Length < windowSize)
                return Array.Empty<double[]>();

            var signal = new DiscreteSignal(sampleRate, audio);

            var mfccOptions = new MfccOptions
            {
                SamplingRate = sampleRate,
                FeatureCount = numCoefficients,
                FilterBankSize = numMelFilters,
                LowFrequency = minFrequency,
                HighFrequency = maxFrequency,
                FrameDuration = (double)windowSize / sampleRate,
                HopDuration = (double)hopSize / sampleRate,
                PreEmphasis = preEmphasisAlpha
            };

            var extractor = new MfccExtractor(mfccOptions);

            var mfccVectors = extractor.ParallelComputeFrom(signal);

            return mfccVectors.Select(v => v.Select(f => (double)f).ToArray()).ToArray();
        }

        // DTW für double[][] (MFCC-Frames)
        private double DtwDistance(double[][] seq1, double[][] seq2)
        {
            int n = seq1.Length;
            int m = seq2.Length;
            double[,] dtw = new double[n + 1, m + 1];

            for (int i = 0; i <= n; i++) dtw[i, 0] = double.PositiveInfinity;
            for (int j = 0; j <= m; j++) dtw[0, j] = double.PositiveInfinity;
            dtw[0, 0] = 0;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    double cost = Distance.Euclidean(seq1[i - 1], seq2[j - 1]);
                    dtw[i, j] = cost + Math.Min(Math.Min(dtw[i - 1, j], dtw[i, j - 1]), dtw[i - 1, j - 1]);
                }
            }
            return dtw[n, m];
        }
    }
}



namespace WpfApp1
{
    /// <summary>
    /// Singleton für gewichtete Frequenzbereiche pro Ziffer.
    /// </summary>
    public class DigitFrequencyWeighting
    {
        private static DigitFrequencyWeighting? _instance;
        public static DigitFrequencyWeighting Instance => _instance ??= new DigitFrequencyWeighting();

        // Dictionary: Ziffer -> (StartIndex, EndIndex) des wichtigsten Bereichs
        private readonly Dictionary<int, (int Start, int End)> _digitRanges = new();

        private DigitFrequencyWeighting() { }

        public (int Start, int End) GetRangeForDigit(int digit, Func<int, List<float[]>> sampleProvider, int fftLen, double topPercent = 0.2)
        {
            if (_digitRanges.TryGetValue(digit, out var range))
                return range;

            // Hole 3 Samples für die Ziffer
            var samples = sampleProvider(digit).Take(3).ToList();
            if (samples.Count < 1)
                return (0, fftLen / 2);

            // Mittelwertspektrum berechnen
            double[] avg = new double[fftLen / 2];
            foreach (var s in samples)
            {
                var fft = ComputeFft(s, fftLen);
                var norm = NormalizeSpectrum(fft);
                for (int i = 0; i < avg.Length; i++)
                    avg[i] += norm[i];
            }
            for (int i = 0; i < avg.Length; i++)
                avg[i] /= samples.Count;

            // Finde Bereich mit höchsten Mittelwerten (z.B. Top 20%)
            int bandSize = (int)(avg.Length * topPercent);
            int bestStart = 0;
            double bestSum = 0;
            for (int i = 0; i <= avg.Length - bandSize; i++)
            {
                double sum = 0;
                for (int j = 0; j < bandSize; j++)
                    sum += avg[i + j];
                if (sum > bestSum)
                {
                    bestSum = sum;
                    bestStart = i;
                }
            }
            int bestEnd = bestStart + bandSize;
            _digitRanges[digit] = (bestStart, bestEnd);
            return (bestStart, bestEnd);
        }

        private static Complex[] ComputeFft(float[] samples, int fftLen)
        {
            Complex[] data = new Complex[fftLen];
            int n = samples.Length;
            for (int i = 0; i < n; i++)
                data[i] = new Complex(samples[i], 0);
            for (int i = n; i < fftLen; i++)
                data[i] = Complex.Zero;
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(data, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
            return data;
        }

        private static double[] NormalizeSpectrum(Complex[] fft)
        {
            int len = fft.Length / 2;
            double[] mag = new double[len];
            double max = 0.0;
            for (int i = 0; i < len; i++)
            {
                mag[i] = fft[i].Magnitude;
                if (mag[i] > max) max = mag[i];
            }
            if (max > 0)
            {
                for (int i = 0; i < len; i++)
                    mag[i] /= max;
            }
            return mag;
        }
    }

    /// <summary>
    /// Interpreter mit digit-spezifischer Bereichsgewichtung.
    /// </summary>
    public class WeightedBandSpectrumInterpreter : IAudioInterpreter
    {
        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            string samplesRoot = "Samples";
            if (!Directory.Exists(samplesRoot))
                return -1;

            var allSampleFiles = Directory.GetDirectories(samplesRoot)
                .SelectMany(dir => Directory.GetFiles(dir, "*.wav")
                    .Select(f => new { FilePath = f, Digit = ParseDigitFromPath(f) }))
                .ToList();

            if (allSampleFiles.Count == 0)
                return -1;

            // FFT der Aufnahme berechnen und normalisieren
            var inputFft = ComputeFft(input);
            var inputNorm = NormalizeSpectrum(inputFft);

            int fftLen = inputFft.Length;

            double minDistance = double.MaxValue;
            int bestDigit = -1;

            foreach (var group in allSampleFiles.GroupBy(f => f.Digit))
            {
                // Bereich für diese Ziffer bestimmen (Singleton)
                var range = DigitFrequencyWeighting.Instance.GetRangeForDigit(
                    group.Key,
                    digit => group.Where(g => g.Digit == digit).Select(g => LoadWavAsFloatArray(g.FilePath)).ToList(),
                    fftLen);

                foreach (var sample in group)
                {
                    float[] sampleData = LoadWavAsFloatArray(sample.FilePath);
                    var sampleFft = ComputeFft(sampleData);
                    var sampleNorm = NormalizeSpectrum(sampleFft);

                    double dist = WeightedSpectrumDistance(inputNorm, sampleNorm, range);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestDigit = group.Key;
                    }
                }
            }

            return bestDigit;
        }

        private static int ParseDigitFromPath(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            var digitStr = Path.GetFileName(dir);
            return int.TryParse(digitStr, out int digit) ? digit : -1;
        }

        private static Complex[] ComputeFft(float[] samples)
        {
            int n = samples.Length;
            int fftLength = 1;
            while (fftLength < n) fftLength <<= 1;
            Complex[] data = new Complex[fftLength];
            for (int i = 0; i < n; i++)
                data[i] = new Complex(samples[i], 0);
            for (int i = n; i < fftLength; i++)
                data[i] = Complex.Zero;
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(data, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
            return data;
        }

        private static double[] NormalizeSpectrum(Complex[] fft)
        {
            int len = fft.Length / 2;
            double[] mag = new double[len];
            double max = 0.0;
            for (int i = 0; i < len; i++)
            {
                mag[i] = fft[i].Magnitude;
                if (mag[i] > max) max = mag[i];
            }
            if (max > 0)
            {
                for (int i = 0; i < len; i++)
                    mag[i] /= max;
            }
            return mag;
        }

        private static double WeightedSpectrumDistance(double[] a, double[] b, (int Start, int End) range)
        {
            int len = Math.Min(a.Length, b.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double weight = (i >= range.Start && i < range.End) ? 2.0 : 1.0; // Bereich doppelt gewichten
                double diff = a[i] - b[i];
                sum += weight * diff * diff;
            }
            return sum;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new NAudio.Wave.AudioFileReader(filePath);
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


namespace WpfApp1
{
    public abstract class AbstractWeightedBandSpectrumInterpreter : IAudioInterpreter
    {
        // Caching: (Digit, SampleCount, FftLen) -> (Start, End)
        private static readonly Dictionary<(int Digit, int SampleCount, int FftLen), (int Start, int End)> _digitRanges = new();

        private readonly int _sampleCount;

        protected AbstractWeightedBandSpectrumInterpreter(int sampleCount)
        {
            _sampleCount = sampleCount;
        }

        public static void RecalculateAllBandWeightings()
        {
            _digitRanges.Clear();
        }


        public int Interpret(float[] input, IReadOnlyList<float[]> samples)
        {
            string samplesRoot = "Samples";
            if (!Directory.Exists(samplesRoot))
                return -1;

            var allSampleFiles = Directory.GetDirectories(samplesRoot)
                .SelectMany(dir => Directory.GetFiles(dir, "*.wav")
                    .Select(f => new { FilePath = f, Digit = ParseDigitFromPath(f) }))
                .ToList();

            if (allSampleFiles.Count == 0)
                return -1;

            var inputFft = ComputeFft(input);
            var inputNorm = NormalizeSpectrum(inputFft);
            int fftLen = inputFft.Length;

            double minDistance = double.MaxValue;
            int bestDigit = -1;

            foreach (var group in allSampleFiles.GroupBy(f => f.Digit))
            {
                var range = GetOrComputeRangeForDigit(group.Key, _sampleCount, fftLen);

                foreach (var sample in group)
                {
                    float[] sampleData = LoadWavAsFloatArray(sample.FilePath);
                    var sampleFft = ComputeFft(sampleData);
                    var sampleNorm = NormalizeSpectrum(sampleFft);

                    double dist = ComputeDistance(inputNorm, sampleNorm, range);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestDigit = group.Key;
                    }
                }
            }

            return bestDigit;
        }

        /// <summary>
        /// Muss von abgeleiteten Klassen implementiert werden.
        /// </summary>
        protected abstract double ComputeDistance(double[] inputNorm, double[] sampleNorm, (int Start, int End) range);

        /// <summary>
        /// Bestimmt und cached für jede Ziffer den wichtigsten Frequenzbereich anhand der neuesten n Samples.
        /// </summary>
        private static (int Start, int End) GetOrComputeRangeForDigit(int digit, int sampleCount, int fftLen, double topPercent = 0.2)
        {
            var key = (digit, sampleCount, fftLen);
            if (_digitRanges.TryGetValue(key, out var range))
                return range;

            string samplesRoot = "Samples";
            string digitDir = Path.Combine(samplesRoot, digit.ToString());
            if (!Directory.Exists(digitDir))
                return (0, fftLen / 2);

            // Neueste n Dateien nach Änderungsdatum
            var newestFiles = Directory.GetFiles(digitDir, "*.wav")
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(sampleCount)
                .ToList();

            var samples = newestFiles.Select(LoadWavAsFloatArray).ToList();
            if (samples.Count < 1)
                return (0, fftLen / 2);

            double[] avg = new double[fftLen / 2];
            foreach (var s in samples)
            {
                var fft = ComputeFft(s, fftLen);
                var norm = NormalizeSpectrum(fft);
                for (int i = 0; i < avg.Length; i++)
                    avg[i] += norm[i];
            }
            for (int i = 0; i < avg.Length; i++)
                avg[i] /= samples.Count;

            int bandSize = (int)(avg.Length * topPercent);
            int bestStart = 0;
            double bestSum = 0;
            for (int i = 0; i <= avg.Length - bandSize; i++)
            {
                double sum = 0;
                for (int j = 0; j < bandSize; j++)
                    sum += avg[i + j];
                if (sum > bestSum)
                {
                    bestSum = sum;
                    bestStart = i;
                }
            }
            int bestEnd = bestStart + bandSize;
            _digitRanges[key] = (bestStart, bestEnd);
            return (bestStart, bestEnd);
        }

        // Hilfsmethoden
        private static int ParseDigitFromPath(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            var digitStr = Path.GetFileName(dir);
            return int.TryParse(digitStr, out int digit) ? digit : -1;
        }

        private static Complex[] ComputeFft(float[] samples, int? fftLenOverride = null)
        {
            int n = samples.Length;
            int fftLength = fftLenOverride ?? 1;
            if (fftLenOverride == null)
            {
                while (fftLength < n) fftLength <<= 1;
            }
            Complex[] data = new Complex[fftLength];
            for (int i = 0; i < n; i++)
                data[i] = new Complex(samples[i], 0);
            for (int i = n; i < fftLength; i++)
                data[i] = Complex.Zero;
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(data, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
            return data;
        }

        private static double[] NormalizeSpectrum(Complex[] fft)
        {
            int len = fft.Length / 2;
            double[] mag = new double[len];
            double max = 0.0;
            for (int i = 0; i < len; i++)
            {
                mag[i] = fft[i].Magnitude;
                if (mag[i] > max) max = mag[i];
            }
            if (max > 0)
            {
                for (int i = 0; i < len; i++)
                    mag[i] /= max;
            }
            return mag;
        }

        private static float[] LoadWavAsFloatArray(string filePath)
        {
            using var audioFile = new NAudio.Wave.AudioFileReader(filePath);
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

namespace WpfApp1
{
    /// <summary>
    /// Nutzt 10 Samples pro Ziffer zur Bereichsbestimmung, Bereich wird doppelt gewichtet.
    /// </summary>
    public class TenSampleWeightedBandSpectrumInterpreter : AbstractWeightedBandSpectrumInterpreter
    {
        public TenSampleWeightedBandSpectrumInterpreter() : base(10) { }

        protected override double ComputeDistance(double[] inputNorm, double[] sampleNorm, (int Start, int End) range)
        {
            int len = Math.Min(inputNorm.Length, sampleNorm.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double weight = (i >= range.Start && i < range.End) ? 2.0 : 1.0;
                double diff = inputNorm[i] - sampleNorm[i];
                sum += weight * diff * diff;
            }
            return sum;
        }
    }

    /// <summary>
    /// Nutzt 30 Samples pro Ziffer zur Bereichsbestimmung, Bereich wird doppelt gewichtet.
    /// </summary>
    public class ThirtySampleWeightedBandSpectrumInterpreter : AbstractWeightedBandSpectrumInterpreter
    {
        public ThirtySampleWeightedBandSpectrumInterpreter() : base(30) { }

        protected override double ComputeDistance(double[] inputNorm, double[] sampleNorm, (int Start, int End) range)
        {
            int len = Math.Min(inputNorm.Length, sampleNorm.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double weight = (i >= range.Start && i < range.End) ? 2.0 : 1.0;
                double diff = inputNorm[i] - sampleNorm[i];
                sum += weight * diff * diff;
            }
            return sum;
        }
    }
}
namespace WpfApp1
{
    /// <summary>
    /// Nutzt 3 Samples pro Ziffer zur Bereichsbestimmung, Bereich wird doppelt gewichtet.
    /// </summary>
    public class ThreeSampleWeightedBandSpectrumInterpreter : AbstractWeightedBandSpectrumInterpreter
    {
        public ThreeSampleWeightedBandSpectrumInterpreter() : base(3) { }

        protected override double ComputeDistance(double[] inputNorm, double[] sampleNorm, (int Start, int End) range)
        {
            int len = Math.Min(inputNorm.Length, sampleNorm.Length);
            double sum = 0;
            for (int i = 0; i < len; i++)
            {
                double weight = (i >= range.Start && i < range.End) ? 2.0 : 1.0;
                double diff = inputNorm[i] - sampleNorm[i];
                sum += weight * diff * diff;
            }
            return sum;
        }
    }
}

