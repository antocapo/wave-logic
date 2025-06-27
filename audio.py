import pyttsx3
import os




engine = pyttsx3.init()
voices = engine.getProperty('voices')

for idx, voice in enumerate(voices):
    print(f"{idx}: {voice.name} - {voice.id}")

# Set up German mapping
numbers_de = {
    1: "eins",
    2: "zwei",
    3: "drei",
    4: "vier",
    5: "fünf",
    6: "sechs",
    7: "sieben",
    8: "acht",
    9: "neun"
}

# Initialize TTS engine
engine = pyttsx3.init()
engine.setProperty('rate', 130)  # Speech speed

# Optional: choose a German voice (if installed)
for voice in engine.getProperty('voices'):
    if "german" in voice.name.lower() or "de" in voice.id.lower():
        engine.setProperty('voice', voice.id)
        break


# Generate and save audio files
for num, word in numbers_de.items():
    filename = f"{num}-1.wav"
    engine.save_to_file(word, filename)
    print(f"Saved: {filename}")

engine.runAndWait()
