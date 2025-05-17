# WaveLogic - Prototype Speech Recognition

WaveLogic is a solution for speech recognition of simple commands (digits 1-9). The Program is implemented in C#


## How It Works

1. The user creates audio samples, each assigned to a specific digit (1–9).
2. The user records a .wav file containing a spoken digit.
3. The audio signal is processed using a Fourier transform.
4. Dominant frequency features are extracted from the signal.
5. These features are compared with the existing samples.
6. The recognized digit is returned to the C# application.
