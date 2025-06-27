using System;
using System.Collections.Generic;
using System.IO;
using WpfApp1;
using Xunit;
using Xunit.Abstractions;

namespace WaveLogic_Tests
{
    public class AudioInterpreterTests
    {
        private readonly ITestOutputHelper _output;
        private const string TestSamplesRoot = "Samples";
        private const string TrainingSamplesRoot = "TrainingSamples";

        public AudioInterpreterTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> Strategies()
        {
            yield return new object[] { "Default", new DefaultAudioInterpreter() };
            //yield return new object[] { "Average FFT", new AverageFftInterpreter() };
            //yield return new object[] { "Nearest Spectrum", new NearestSpectrumInterpreter() };
            //yield return new object[] { "Normalized Nearest Spectrum", new NormalizedNearestSpectrumInterpreter() };
            //yield return new object[] { "Summed Normalized Spectrum", new SummedNormalizedSpectrumInterpreter() };
            yield return new object[] { "Intense Spec Match", new NormalizedNearestSpectrumInterpreter() };
            yield return new object[] { "Intense Sum Spec Match", new SummedNormalizedSpectrumInterpreter() };
            yield return new object[] { "Weighed FFT", new WeightedBandSpectrumInterpreter() };
            yield return new object[] { "10 Sample Weighed FFT", new TenSampleWeightedBandSpectrumInterpreter() };
            yield return new object[] { "30 Sample Weighed FFT", new ThirtySampleWeightedBandSpectrumInterpreter() };
            yield return new object[] { "Mels Algo", new MfccDtwInterpreter() };

        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void Interpreter_Accuracy_IsReported(string strategyName, IAudioInterpreter interpreter)
        {
            int total = 0;
            int correct = 0;

            for (int digit = 1; digit <= 9; digit++)
            {
                string digitFolder = Path.Combine(TestSamplesRoot, digit.ToString());
                if (!Directory.Exists(digitFolder))
                {
                    _output.WriteLine($"No test samples for digit {digit} in {digitFolder}");
                    continue;
                }

                var sampleFiles = Directory.GetFiles(digitFolder, "*.wav");
                if (sampleFiles.Length == 0)
                {
                    _output.WriteLine($"No WAV files for digit {digit} in {digitFolder}");
                    continue;
                }

                foreach (var file in sampleFiles)
                {
                    float[] input;
                    try
                    {
                        input = LoadWavAsFloatArrayViaAudioManager(file);
                        if (input.Length == 0)
                        {
                            _output.WriteLine($"Leeres oder ungültiges Testsample übersprungen: {file}");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"Fehler beim Laden von Testsample {file}: {ex.Message}");
                        continue;
                    }

                    // Trainingsdaten: alle WAVs aus TrainingSamples/{digit}
                    var trainingSamples = new List<float[]>();
                    for (int d = 1; d <= 9; d++)
                    {
                        string trainFolder = Path.Combine(TrainingSamplesRoot, d.ToString());
                        if (!Directory.Exists(trainFolder)) continue;
                        foreach (var trainFile in Directory.GetFiles(trainFolder, "*.wav"))
                        {
                            try
                            {
                                var data = LoadWavAsFloatArrayViaAudioManager(trainFile);
                                if (data.Length > 0)
                                    trainingSamples.Add(data);
                            }
                            catch (Exception ex)
                            {
                                _output.WriteLine($"Fehler beim Laden von Trainingssample {trainFile}: {ex.Message}");
                            }
                        }
                    }

                    int predicted = interpreter.Interpret(input, trainingSamples);
                    if (predicted == digit)
                        correct++;
                    total++;
                }
            }

            double accuracy = total > 0 ? (double)correct / total * 100.0 : 0.0;
            _output.WriteLine($"Strategy: {strategyName} - Accuracy: {accuracy:0.0}% ({correct}/{total})");
        }

        // Nutze die interne Methode von AudioManager via Reflection
        private static float[] LoadWavAsFloatArrayViaAudioManager(string filePath)
        {
            var method = typeof(AudioManager).GetMethod("LoadWavAsFloatArray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("LoadWavAsFloatArray not found in AudioManager.");
            return (float[])method.Invoke(null, new object[] { filePath });
        }
    }
}
